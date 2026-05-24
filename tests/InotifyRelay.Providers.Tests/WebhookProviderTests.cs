using System.Net;
using System.Text.Json;
using InotifyRelay.Core.Events;
using InotifyRelay.Core.Pipeline;
using InotifyRelay.Core.Providers;
using InotifyRelay.Core.Templating;
using InotifyRelay.Providers.Tests.TestHelpers;
using InotifyRelay.Providers.Webhook;
using Microsoft.Extensions.Logging.Abstractions;

namespace InotifyRelay.Providers.Tests;

public class WebhookProviderTests
{
    private static (FakeHttpClientFactory, WebhookProvider, FakeHttpHandler) Build()
    {
        var f = new FakeHttpClientFactory();
        var p = new WebhookProvider(f, TemplateFilterRegistry.CreateDefault(), NullLogger<WebhookProvider>.Instance);
        return (f, p, f.Handler);
    }

    private static FileSystemChange Change(string path = "/watch/movies/x.mkv") =>
        new(path, FileEventType.ClosedWrite, false, DateTimeOffset.UtcNow, "/watch/movies");

    private static RelayContext Ctx(string cfgJson, FileSystemChange? change = null,
        IReadOnlyList<PathMapping>? mappings = null)
        => new(change ?? Change(), "rule", "target", cfgJson, null, mappings);

    [Fact]
    public async Task Posts_default_body_and_sets_url_method_content_type()
    {
        var (_, p, h) = Build();
        var cfg = JsonSerializer.Serialize(new WebhookConfig
        {
            UrlTemplate = "https://hook.test/{event}",
            BodyTemplate = "{\"path\":\"{path|jsonescape}\"}",
        });

        var result = await p.SendAsync(Ctx(cfg), default);

        Assert.True(result.Success);
        Assert.Equal("POST", h.Last.Method);
        Assert.Equal("https://hook.test/ClosedWrite", h.Last.Url.ToString());
        Assert.Equal("application/json", h.Last.ContentType);
        Assert.Equal("{\"path\":\"/watch/movies/x.mkv\"}", h.Last.Body);
    }

    [Fact]
    public async Task Method_GET_does_not_send_body()
    {
        var (_, p, h) = Build();
        var cfg = JsonSerializer.Serialize(new WebhookConfig
        {
            Method = "GET",
            UrlTemplate = "https://hook.test/scan",
            BodyTemplate = "ignored",
        });
        await p.SendAsync(Ctx(cfg), default);
        Assert.Equal("GET", h.Last.Method);
        Assert.Null(h.Last.Body);
    }

    [Fact]
    public async Task Adds_bearer_auth_header()
    {
        var (_, p, h) = Build();
        var cfg = JsonSerializer.Serialize(new WebhookConfig
        {
            UrlTemplate = "https://hook.test/",
            Auth = new WebhookAuth { Type = "bearer", Token = "abc.def" },
        });
        await p.SendAsync(Ctx(cfg), default);
        Assert.Equal("Bearer abc.def", h.Last.Headers["Authorization"]);
    }

    [Fact]
    public async Task Adds_basic_auth_header()
    {
        var (_, p, h) = Build();
        var cfg = JsonSerializer.Serialize(new WebhookConfig
        {
            UrlTemplate = "https://hook.test/",
            Auth = new WebhookAuth { Type = "basic", Username = "user", Password = "pw" },
        });
        await p.SendAsync(Ctx(cfg), default);
        // base64("user:pw") = "dXNlcjpwdw=="
        Assert.Equal("Basic dXNlcjpwdw==", h.Last.Headers["Authorization"]);
    }

    [Fact]
    public async Task Custom_headers_are_templated()
    {
        var (_, p, h) = Build();
        var cfg = JsonSerializer.Serialize(new WebhookConfig
        {
            UrlTemplate = "https://hook.test/",
            Headers = new Dictionary<string, string> { ["X-Event"] = "{event}", ["X-Path"] = "{path}" },
        });
        await p.SendAsync(Ctx(cfg), default);
        Assert.Equal("ClosedWrite", h.Last.Headers["X-Event"]);
        Assert.Equal("/watch/movies/x.mkv", h.Last.Headers["X-Path"]);
    }

    [Fact]
    public async Task Non_2xx_returns_failure_with_status()
    {
        var (_, p, h) = Build();
        h.Responder = (_, _) => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("nope")
        };
        var cfg = JsonSerializer.Serialize(new WebhookConfig { UrlTemplate = "https://hook.test/" });
        var result = await p.SendAsync(Ctx(cfg), default);
        Assert.False(result.Success);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("HTTP 400", result.Error);
    }

    [Fact]
    public async Task Network_exception_becomes_failure_result_not_throw()
    {
        var (_, p, h) = Build();
        h.Responder = (_, _) => throw new HttpRequestException("dns broke");
        var cfg = JsonSerializer.Serialize(new WebhookConfig { UrlTemplate = "https://hook.test/" });
        var result = await p.SendAsync(Ctx(cfg), default);
        Assert.False(result.Success);
        Assert.Equal(0, result.StatusCode);
        Assert.Contains("dns broke", result.Error);
    }

    [Fact]
    public async Task Path_mappings_rewrite_url_and_body_via_path_template_var()
    {
        var (_, p, h) = Build();
        var cfg = JsonSerializer.Serialize(new WebhookConfig
        {
            UrlTemplate = "https://hook.test/scan",
            BodyTemplate = "{\"path\":\"{path|jsonescape}\"}",
        });
        var mappings = new[] { new PathMapping("/watch/movies", "/data/movies") };
        await p.SendAsync(Ctx(cfg, mappings: mappings), default);
        Assert.Equal("{\"path\":\"/data/movies/x.mkv\"}", h.Last.Body);
    }
}
