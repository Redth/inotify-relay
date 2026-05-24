using System.Text.Json;
using System.Web;
using InotifyRelay.Core.Events;
using InotifyRelay.Core.Pipeline;
using InotifyRelay.Core.Providers;
using InotifyRelay.Core.Templating;
using InotifyRelay.Providers.Plex;
using InotifyRelay.Providers.Tests.TestHelpers;

namespace InotifyRelay.Providers.Tests;

public class PlexProviderTests
{
    private static (FakeHttpClientFactory, PlexProvider, FakeHttpHandler) Build()
    {
        var f = new FakeHttpClientFactory();
        var p = new PlexProvider(f, TemplateFilterRegistry.CreateDefault());
        return (f, p, f.Handler);
    }

    private static FileSystemChange Change() =>
        new("/watch/movies/Inception/a.mkv", FileEventType.ClosedWrite, false,
            DateTimeOffset.UtcNow, "/watch/movies");

    [Fact]
    public async Task Refresh_section_uses_GET_with_section_id_and_token_in_query()
    {
        var (_, p, h) = Build();
        var cfg = JsonSerializer.Serialize(new PlexConfig
        {
            BaseUrl = "http://plex:32400",
            Token = "tok-abc",
            Action = "refresh-section",
            SectionId = "1",
        });
        var result = await p.SendAsync(new RelayContext(Change(), "r", "t", cfg, null), default);
        Assert.True(result.Success);
        Assert.Equal("GET", h.Last.Method);
        Assert.StartsWith("http://plex:32400/library/sections/1/refresh", h.Last.Url.ToString());
        Assert.Equal("tok-abc", HttpUtility.ParseQueryString(h.Last.Url.Query)["X-Plex-Token"]);
    }

    [Fact]
    public async Task Refresh_path_includes_url_encoded_path_query()
    {
        var (_, p, h) = Build();
        var cfg = JsonSerializer.Serialize(new PlexConfig
        {
            BaseUrl = "http://plex:32400",
            Token = "tok",
            Action = "refresh-path",
            SectionId = "2",
            PathTemplate = "{directory}",
        });
        await p.SendAsync(new RelayContext(Change(), "r", "t", cfg, null), default);

        var qs = HttpUtility.ParseQueryString(h.Last.Url.Query);
        Assert.Equal("/watch/movies/Inception", qs["path"]);
        Assert.Equal("tok", qs["X-Plex-Token"]);
    }

    [Fact]
    public async Task Path_mappings_rewrite_refresh_path_query()
    {
        var (_, p, h) = Build();
        var cfg = JsonSerializer.Serialize(new PlexConfig
        {
            BaseUrl = "http://plex:32400",
            Token = "tok",
            Action = "refresh-path",
            SectionId = "2",
            PathTemplate = "{directory}",
        });
        var mappings = new[] { new PathMapping("/watch/movies", "/data/movies") };
        await p.SendAsync(new RelayContext(Change(), "r", "t", cfg, null, mappings), default);

        var qs = HttpUtility.ParseQueryString(h.Last.Url.Query);
        Assert.Equal("/data/movies/Inception", qs["path"]);
    }

    [Fact]
    public async Task Trailing_slash_on_base_url_does_not_duplicate()
    {
        var (_, p, h) = Build();
        var cfg = JsonSerializer.Serialize(new PlexConfig
        {
            BaseUrl = "http://plex:32400/",
            Token = "tok",
            Action = "refresh-section",
            SectionId = "1",
        });
        await p.SendAsync(new RelayContext(Change(), "r", "t", cfg, null), default);
        Assert.StartsWith("http://plex:32400/library/sections/1/refresh", h.Last.Url.ToString());
    }
}
