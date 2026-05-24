using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using InotifyRelay.Core.Providers;
using InotifyRelay.Core.Templating;
using Microsoft.Extensions.Logging;

namespace InotifyRelay.Providers.Webhook;

public sealed class WebhookProvider(IHttpClientFactory http, ITemplateFilterRegistry filters, ILogger<WebhookProvider> logger)
    : IRelayProvider
{
    public string TypeKey => "webhook";
    public string DisplayName => "Webhook";
    public Type ConfigType => typeof(WebhookConfig);

    public object CreateDefaultConfig() => new WebhookConfig();

    public async Task<RelayResult> SendAsync(RelayContext ctx, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var cfg = JsonSerializer.Deserialize<WebhookConfig>(ctx.ProviderConfigJson) ?? new WebhookConfig();
        var tctx = EventTemplateContext.Build(ctx.Change, ctx.RuleName, ctx.Change.SourceRoot);

        var url = Template.Parse(cfg.UrlTemplate).Render(tctx, filters);
        var client = http.CreateClient("relay");
        using var req = new HttpRequestMessage(new HttpMethod(cfg.Method.ToUpperInvariant()), url);

        if (!string.IsNullOrEmpty(cfg.BodyTemplate) && cfg.Method.ToUpperInvariant() is not "GET" and not "DELETE")
        {
            var body = Template.Parse(cfg.BodyTemplate).Render(tctx, filters);
            req.Content = new StringContent(body, Encoding.UTF8, cfg.ContentType);
        }

        foreach (var (k, v) in cfg.Headers)
        {
            try
            {
                var rendered = Template.Parse(v).Render(tctx, filters);
                req.Headers.TryAddWithoutValidation(k, rendered);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to set header {Key}", k);
            }
        }

        switch (cfg.Auth.Type?.ToLowerInvariant())
        {
            case "bearer" when !string.IsNullOrEmpty(cfg.Auth.Token):
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.Auth.Token);
                break;
            case "basic" when cfg.Auth.Username is not null && cfg.Auth.Password is not null:
                var creds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{cfg.Auth.Username}:{cfg.Auth.Password}"));
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", creds);
                break;
        }

        try
        {
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
