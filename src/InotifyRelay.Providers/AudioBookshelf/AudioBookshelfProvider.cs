using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using InotifyRelay.Core.Providers;

namespace InotifyRelay.Providers.AudioBookshelf;

public sealed class AudioBookshelfProvider(IHttpClientFactory http)
    : IRelayProvider, IRelayProviderWithLibraries
{
    public string TypeKey => "audiobookshelf";
    public string DisplayName => "Audiobookshelf";
    public Type ConfigType => typeof(AudioBookshelfConfig);

    public object CreateDefaultConfig() => new AudioBookshelfConfig();

    public async Task<IReadOnlyList<LibraryInfo>> ListLibrariesAsync(string configJson, CancellationToken ct)
    {
        var cfg = JsonSerializer.Deserialize<AudioBookshelfConfig>(configJson) ?? new AudioBookshelfConfig();
        var baseUrl = cfg.BaseUrl.TrimEnd('/');
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/libraries");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.ApiToken);
        req.Headers.TryAddWithoutValidation("Accept", "application/json");

        var client = http.CreateClient("relay");
        using var resp = await client.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Audiobookshelf returned HTTP {(int)resp.StatusCode}");

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        // ABS responds either with a bare array OR { libraries: [...] } depending on version.
        var arr = doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement
            : doc.RootElement.TryGetProperty("libraries", out var l) ? l : default;
        var libs = new List<LibraryInfo>();
        if (arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arr.EnumerateArray())
            {
                var id = item.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                var name = item.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
                var kind = item.TryGetProperty("mediaType", out var k) ? k.GetString() : null;
                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                    libs.Add(new LibraryInfo(id, name, kind));
            }
        }
        return libs;
    }

    public async Task<RelayResult> SendAsync(RelayContext ctx, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var cfg = JsonSerializer.Deserialize<AudioBookshelfConfig>(ctx.ProviderConfigJson) ?? new AudioBookshelfConfig();

        if (string.IsNullOrWhiteSpace(cfg.LibraryId))
        {
            sw.Stop();
            return RelayResult.Fail(0, null, "LibraryId is required for Audiobookshelf.", sw.Elapsed);
        }

        var baseUrl = cfg.BaseUrl.TrimEnd('/');
        var url = $"{baseUrl}/api/libraries/{Uri.EscapeDataString(cfg.LibraryId)}/scan";
        if (cfg.Force) url += "?force=1";

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.ApiToken);

            var client = http.CreateClient("relay");
            var resp = await client.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            sw.Stop();
            return resp.IsSuccessStatusCode
                ? RelayResult.Ok((int)resp.StatusCode, body, sw.Elapsed)
                : RelayResult.Fail((int)resp.StatusCode, body, $"HTTP {(int)resp.StatusCode}", sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return RelayResult.Fail(0, null, ex.Message, sw.Elapsed);
        }
    }
}
