using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using InotifyRelay.Core.Providers;
using InotifyRelay.Core.Templating;

namespace InotifyRelay.Providers.Jellyfin;

public sealed class JellyfinProvider(IHttpClientFactory http, ITemplateFilterRegistry filters)
    : IRelayProvider, IRelayProviderWithLibraries
{
    public string TypeKey => "jellyfin";
    public string DisplayName => "Jellyfin";
    public Type ConfigType => typeof(JellyfinConfig);

    public object CreateDefaultConfig() => new JellyfinConfig();

    public async Task<IReadOnlyList<LibraryInfo>> ListLibrariesAsync(string configJson, CancellationToken ct)
    {
        var cfg = JsonSerializer.Deserialize<JellyfinConfig>(configJson) ?? new JellyfinConfig();
        var baseUrl = cfg.BaseUrl.TrimEnd('/');
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/Library/VirtualFolders");
        req.Headers.Add("X-Emby-Token", cfg.ApiKey);
        req.Headers.Add("Authorization", $"MediaBrowser Token=\"{cfg.ApiKey}\"");
        req.Headers.TryAddWithoutValidation("Accept", "application/json");

        var client = http.CreateClient("relay");
        using var resp = await client.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Jellyfin returned HTTP {(int)resp.StatusCode}");

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var libs = new List<LibraryInfo>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var id = item.TryGetProperty("ItemId", out var idEl) ? idEl.GetString() : null;
            var name = item.TryGetProperty("Name", out var nameEl) ? nameEl.GetString() : null;
            var kind = item.TryGetProperty("CollectionType", out var kindEl) ? kindEl.GetString() : null;
            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                libs.Add(new LibraryInfo(id, name, kind));
        }
        return libs;
    }

    public async Task<RelayResult> SendAsync(RelayContext ctx, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var cfg = JsonSerializer.Deserialize<JellyfinConfig>(ctx.ProviderConfigJson) ?? new JellyfinConfig();
        var tctx = EventTemplateContext.Build(ctx.Change, ctx.RuleName, ctx.Change.SourceRoot, ctx.PathMappings);

        var client = http.CreateClient("relay");
        var baseUrl = cfg.BaseUrl.TrimEnd('/');

        try
        {
            HttpResponseMessage resp;
            if (cfg.Action.Equals("report-path", StringComparison.OrdinalIgnoreCase))
            {
                var path = Template.Parse(cfg.PathTemplate).Render(tctx, filters);
                var body = JsonSerializer.Serialize(new
                {
                    Updates = new[] { new { Path = path, UpdateType = cfg.UpdateType } }
                });
                using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/Library/Media/Updated")
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
                req.Headers.Add("X-Emby-Token", cfg.ApiKey);
                req.Headers.Add("Authorization", $"MediaBrowser Token=\"{cfg.ApiKey}\"");
                resp = await client.SendAsync(req, ct);
            }
            else // refresh-library
            {
                var url = string.IsNullOrWhiteSpace(cfg.LibraryId)
                    ? $"{baseUrl}/Library/Refresh"
                    : $"{baseUrl}/Items/{cfg.LibraryId}/Refresh?Recursive=true&ImageRefreshMode=Default&MetadataRefreshMode=Default";
                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Headers.Add("X-Emby-Token", cfg.ApiKey);
                req.Headers.Add("Authorization", $"MediaBrowser Token=\"{cfg.ApiKey}\"");
                resp = await client.SendAsync(req, ct);
            }

            var body2 = await resp.Content.ReadAsStringAsync(ct);
            sw.Stop();
            return resp.IsSuccessStatusCode
                ? RelayResult.Ok((int)resp.StatusCode, body2, sw.Elapsed)
                : RelayResult.Fail((int)resp.StatusCode, body2, $"HTTP {(int)resp.StatusCode}", sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return RelayResult.Fail(0, null, ex.Message, sw.Elapsed);
        }
    }
}
