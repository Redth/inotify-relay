using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using InotifyRelay.Core.Providers;

namespace InotifyRelay.Providers.AudioBookshelf;

public sealed class AudioBookshelfProvider(IHttpClientFactory http) : IRelayProvider
{
    public string TypeKey => "audiobookshelf";
    public string DisplayName => "Audiobookshelf";
    public Type ConfigType => typeof(AudioBookshelfConfig);

    public object CreateDefaultConfig() => new AudioBookshelfConfig();

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
