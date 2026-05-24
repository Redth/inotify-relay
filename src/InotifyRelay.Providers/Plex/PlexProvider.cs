using System.Diagnostics;
using System.Text.Json;
using InotifyRelay.Core.Providers;
using InotifyRelay.Core.Templating;

namespace InotifyRelay.Providers.Plex;

public sealed class PlexProvider(IHttpClientFactory http, ITemplateFilterRegistry filters) : IRelayProvider
{
    public string TypeKey => "plex";
    public string DisplayName => "Plex";
    public Type ConfigType => typeof(PlexConfig);

    public object CreateDefaultConfig() => new PlexConfig();

    public async Task<RelayResult> SendAsync(RelayContext ctx, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var cfg = JsonSerializer.Deserialize<PlexConfig>(ctx.ProviderConfigJson) ?? new PlexConfig();
        var tctx = EventTemplateContext.Build(ctx.Change, ctx.RuleName, ctx.Change.SourceRoot, ctx.PathMappings);

        var client = http.CreateClient("relay");
        var baseUrl = cfg.BaseUrl.TrimEnd('/');
        string url;
        if (cfg.Action.Equals("refresh-path", StringComparison.OrdinalIgnoreCase))
        {
            var path = Template.Parse(cfg.PathTemplate).Render(tctx, filters);
            url = $"{baseUrl}/library/sections/{cfg.SectionId}/refresh?path={Uri.EscapeDataString(path)}&X-Plex-Token={Uri.EscapeDataString(cfg.Token)}";
        }
        else
        {
            url = $"{baseUrl}/library/sections/{cfg.SectionId}/refresh?X-Plex-Token={Uri.EscapeDataString(cfg.Token)}";
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("Accept", "application/json");
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
