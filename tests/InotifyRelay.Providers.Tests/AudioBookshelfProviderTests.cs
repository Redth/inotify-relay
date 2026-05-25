using System.Net;
using System.Text.Json;
using InotifyRelay.Core.Events;
using InotifyRelay.Core.Providers;
using InotifyRelay.Providers.AudioBookshelf;
using InotifyRelay.Providers.Tests.TestHelpers;

namespace InotifyRelay.Providers.Tests;

public class AudioBookshelfProviderTests
{
    private static (FakeHttpClientFactory, AudioBookshelfProvider, FakeHttpHandler) Build()
    {
        var f = new FakeHttpClientFactory();
        var p = new AudioBookshelfProvider(f);
        return (f, p, f.Handler);
    }

    private static FileSystemChange Change() =>
        new("/watch/audiobooks/Author/Title/01.mp3", FileEventType.ClosedWrite, false,
            DateTimeOffset.UtcNow, "/watch/audiobooks");

    [Fact]
    public async Task Scans_library_with_POST_and_bearer_auth()
    {
        var (_, p, h) = Build();
        var cfg = JsonSerializer.Serialize(new AudioBookshelfConfig
        {
            BaseUrl = "http://abs:13378",
            ApiToken = "secret-token",
            LibraryId = "lib-abc",
        });
        var result = await p.SendAsync(new RelayContext(Change(), "r", "t", cfg, null), default);

        Assert.True(result.Success);
        Assert.Equal("POST", h.Last.Method);
        Assert.Equal("http://abs:13378/api/libraries/lib-abc/scan", h.Last.Url.ToString());
        Assert.Equal("Bearer secret-token", h.Last.Headers["Authorization"]);
    }

    [Fact]
    public async Task Force_appends_query_param()
    {
        var (_, p, h) = Build();
        var cfg = JsonSerializer.Serialize(new AudioBookshelfConfig
        {
            BaseUrl = "http://abs:13378",
            ApiToken = "tok",
            LibraryId = "lib-1",
            Force = true,
        });
        await p.SendAsync(new RelayContext(Change(), "r", "t", cfg, null), default);
        Assert.EndsWith("/api/libraries/lib-1/scan?force=1", h.Last.Url.ToString());
    }

    [Fact]
    public async Task Trailing_slash_on_base_url_does_not_duplicate()
    {
        var (_, p, h) = Build();
        var cfg = JsonSerializer.Serialize(new AudioBookshelfConfig
        {
            BaseUrl = "http://abs:13378/",
            ApiToken = "tok",
            LibraryId = "lib-1",
        });
        await p.SendAsync(new RelayContext(Change(), "r", "t", cfg, null), default);
        Assert.Equal("http://abs:13378/api/libraries/lib-1/scan", h.Last.Url.ToString());
    }

    [Fact]
    public async Task Library_id_with_slash_is_url_encoded()
    {
        var (_, p, h) = Build();
        var cfg = JsonSerializer.Serialize(new AudioBookshelfConfig
        {
            BaseUrl = "http://abs:13378",
            ApiToken = "tok",
            LibraryId = "lib/sub",
        });
        await p.SendAsync(new RelayContext(Change(), "r", "t", cfg, null), default);
        // A literal '/' in the id must be %2F-escaped — not interpreted as a path segment.
        Assert.Contains("/api/libraries/lib%2Fsub/scan", h.Last.Url.ToString());
    }

    [Fact]
    public async Task Missing_library_id_returns_failure_without_calling_server()
    {
        var (_, p, h) = Build();
        var cfg = JsonSerializer.Serialize(new AudioBookshelfConfig
        {
            BaseUrl = "http://abs:13378",
            ApiToken = "tok",
            LibraryId = "",
        });
        var result = await p.SendAsync(new RelayContext(Change(), "r", "t", cfg, null), default);
        Assert.False(result.Success);
        Assert.Contains("LibraryId", result.Error);
        Assert.Empty(h.Requests);
    }

    [Fact]
    public async Task Non_2xx_returns_failure_with_status()
    {
        var (_, p, h) = Build();
        h.Responder = (_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("bad token")
        };
        var cfg = JsonSerializer.Serialize(new AudioBookshelfConfig
        {
            BaseUrl = "http://abs:13378",
            ApiToken = "wrong",
            LibraryId = "lib-1",
        });
        var result = await p.SendAsync(new RelayContext(Change(), "r", "t", cfg, null), default);
        Assert.False(result.Success);
        Assert.Equal(401, result.StatusCode);
    }
}
