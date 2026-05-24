using System.Text.Json;
using InotifyRelay.Core.Events;
using InotifyRelay.Core.Pipeline;
using InotifyRelay.Core.Providers;
using InotifyRelay.Core.Templating;
using InotifyRelay.Providers.Jellyfin;
using InotifyRelay.Providers.Tests.TestHelpers;

namespace InotifyRelay.Providers.Tests;

public class JellyfinProviderTests
{
    private static (FakeHttpClientFactory, JellyfinProvider, FakeHttpHandler) Build()
    {
        var f = new FakeHttpClientFactory();
        var p = new JellyfinProvider(f, TemplateFilterRegistry.CreateDefault());
        return (f, p, f.Handler);
    }

    private static FileSystemChange Change() =>
        new("/watch/movies/Inception/a.mkv", FileEventType.ClosedWrite, false,
            DateTimeOffset.UtcNow, "/watch/movies");

    [Fact]
    public async Task Refresh_library_with_no_id_hits_Library_Refresh()
    {
        var (_, p, h) = Build();
        var cfg = JsonSerializer.Serialize(new JellyfinConfig
        {
            BaseUrl = "http://jelly:8096",
            ApiKey = "key123",
            Action = "refresh-library",
            LibraryId = null,
        });
        var result = await p.SendAsync(new RelayContext(Change(), "r", "t", cfg, null), default);

        Assert.True(result.Success);
        Assert.Equal("POST", h.Last.Method);
        Assert.Equal("http://jelly:8096/Library/Refresh", h.Last.Url.ToString());
        Assert.Equal("key123", h.Last.Headers["X-Emby-Token"]);
        Assert.Contains("MediaBrowser Token=\"key123\"", h.Last.Headers["Authorization"]);
    }

    [Fact]
    public async Task Refresh_library_with_id_hits_Items_id_Refresh()
    {
        var (_, p, h) = Build();
        var cfg = JsonSerializer.Serialize(new JellyfinConfig
        {
            BaseUrl = "http://jelly:8096",
            ApiKey = "key123",
            Action = "refresh-library",
            LibraryId = "abc-123",
        });
        await p.SendAsync(new RelayContext(Change(), "r", "t", cfg, null), default);

        Assert.Contains("/Items/abc-123/Refresh", h.Last.Url.ToString());
        Assert.Contains("Recursive=true", h.Last.Url.Query);
    }

    [Fact]
    public async Task Report_path_posts_updates_array_with_templated_path()
    {
        var (_, p, h) = Build();
        var cfg = JsonSerializer.Serialize(new JellyfinConfig
        {
            BaseUrl = "http://jelly:8096",
            ApiKey = "key123",
            Action = "report-path",
            PathTemplate = "{directory}",
            UpdateType = "Modified",
        });
        await p.SendAsync(new RelayContext(Change(), "r", "t", cfg, null), default);

        Assert.EndsWith("/Library/Media/Updated", h.Last.Url.ToString());
        Assert.Equal("application/json", h.Last.ContentType);

        using var doc = JsonDocument.Parse(h.Last.Body!);
        var first = doc.RootElement.GetProperty("Updates")[0];
        Assert.Equal("/watch/movies/Inception", first.GetProperty("Path").GetString());
        Assert.Equal("Modified", first.GetProperty("UpdateType").GetString());
    }

    [Fact]
    public async Task Path_mappings_rewrite_report_path_body()
    {
        var (_, p, h) = Build();
        var cfg = JsonSerializer.Serialize(new JellyfinConfig
        {
            BaseUrl = "http://jelly:8096",
            ApiKey = "key123",
            Action = "report-path",
            PathTemplate = "{path}",
            UpdateType = "Modified",
        });
        var mappings = new[] { new PathMapping("/watch/movies", "/data/movies") };
        await p.SendAsync(new RelayContext(Change(), "r", "t", cfg, null, mappings), default);

        using var doc = JsonDocument.Parse(h.Last.Body!);
        var first = doc.RootElement.GetProperty("Updates")[0];
        Assert.Equal("/data/movies/Inception/a.mkv", first.GetProperty("Path").GetString());
    }

    [Fact]
    public async Task Trailing_slash_on_base_url_does_not_duplicate()
    {
        var (_, p, h) = Build();
        var cfg = JsonSerializer.Serialize(new JellyfinConfig
        {
            BaseUrl = "http://jelly:8096/",
            ApiKey = "k",
            Action = "refresh-library",
        });
        await p.SendAsync(new RelayContext(Change(), "r", "t", cfg, null), default);
        Assert.Equal("http://jelly:8096/Library/Refresh", h.Last.Url.ToString());
    }
}
