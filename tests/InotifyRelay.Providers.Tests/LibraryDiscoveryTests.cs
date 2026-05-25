using System.Net;
using System.Text;
using System.Text.Json;
using InotifyRelay.Core.Providers;
using InotifyRelay.Core.Templating;
using InotifyRelay.Providers.AudioBookshelf;
using InotifyRelay.Providers.Jellyfin;
using InotifyRelay.Providers.Plex;
using InotifyRelay.Providers.Tests.TestHelpers;

namespace InotifyRelay.Providers.Tests;

public class LibraryDiscoveryTests
{
    private static (FakeHttpClientFactory f, FakeHttpHandler h) Server(string responseJson, HttpStatusCode code = HttpStatusCode.OK)
    {
        var f = new FakeHttpClientFactory();
        f.Handler.Responder = (_, _) => new HttpResponseMessage(code)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
        };
        return (f, f.Handler);
    }

    // ---- Jellyfin ----

    [Fact]
    public async Task Jellyfin_lists_libraries_from_VirtualFolders()
    {
        var (f, h) = Server(@"[
            {""Name"":""Movies"",""CollectionType"":""movies"",""ItemId"":""abc-1""},
            {""Name"":""TV Shows"",""CollectionType"":""tvshows"",""ItemId"":""abc-2""}
        ]");
        var p = new JellyfinProvider(f, TemplateFilterRegistry.CreateDefault());
        var cfg = JsonSerializer.Serialize(new JellyfinConfig { BaseUrl = "http://jelly:8096", ApiKey = "k" });

        var libs = (await p.ListLibrariesAsync(cfg, default)).ToList();

        Assert.Equal("GET", h.Last.Method);
        Assert.EndsWith("/Library/VirtualFolders", h.Last.Url.ToString());
        Assert.Equal("k", h.Last.Headers["X-Emby-Token"]);
        Assert.Equal(2, libs.Count);
        Assert.Equal(new LibraryInfo("abc-1", "Movies", "movies"), libs[0]);
    }

    [Fact]
    public async Task Jellyfin_library_listing_surfaces_http_error()
    {
        var (f, _) = Server("unauthorized", HttpStatusCode.Unauthorized);
        var p = new JellyfinProvider(f, TemplateFilterRegistry.CreateDefault());
        var cfg = JsonSerializer.Serialize(new JellyfinConfig { BaseUrl = "http://jelly:8096", ApiKey = "wrong" });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await p.ListLibrariesAsync(cfg, default));
        Assert.Contains("401", ex.Message);
    }

    // ---- Plex ----

    [Fact]
    public async Task Plex_lists_sections_from_MediaContainer()
    {
        var (f, h) = Server(@"{
            ""MediaContainer"": {
                ""Directory"": [
                    {""key"":""1"",""title"":""Movies"",""type"":""movie""},
                    {""key"":""2"",""title"":""TV"",""type"":""show""}
                ]
            }
        }");
        var p = new PlexProvider(f, TemplateFilterRegistry.CreateDefault());
        var cfg = JsonSerializer.Serialize(new PlexConfig { BaseUrl = "http://plex:32400", Token = "tok" });

        var libs = (await p.ListLibrariesAsync(cfg, default)).ToList();

        Assert.EndsWith("X-Plex-Token=tok", h.Last.Url.ToString());
        Assert.Equal(2, libs.Count);
        Assert.Equal(new LibraryInfo("1", "Movies", "movie"), libs[0]);
        Assert.Equal(new LibraryInfo("2", "TV", "show"), libs[1]);
    }

    [Fact]
    public async Task Plex_handles_empty_directory_array()
    {
        var (f, _) = Server(@"{""MediaContainer"":{""Directory"":[]}}");
        var p = new PlexProvider(f, TemplateFilterRegistry.CreateDefault());
        var cfg = JsonSerializer.Serialize(new PlexConfig { BaseUrl = "http://plex:32400", Token = "tok" });

        var libs = await p.ListLibrariesAsync(cfg, default);
        Assert.Empty(libs);
    }

    // ---- Audiobookshelf ----

    [Fact]
    public async Task Audiobookshelf_lists_libraries_keyed_array()
    {
        var (f, h) = Server(@"{
            ""libraries"":[
                {""id"":""lib-1"",""name"":""Audiobooks"",""mediaType"":""book""},
                {""id"":""lib-2"",""name"":""Podcasts"",""mediaType"":""podcast""}
            ]
        }");
        var p = new AudioBookshelfProvider(f);
        var cfg = JsonSerializer.Serialize(new AudioBookshelfConfig { BaseUrl = "http://abs:13378", ApiToken = "t" });

        var libs = (await p.ListLibrariesAsync(cfg, default)).ToList();

        Assert.EndsWith("/api/libraries", h.Last.Url.ToString());
        Assert.Equal("Bearer t", h.Last.Headers["Authorization"]);
        Assert.Equal(2, libs.Count);
        Assert.Equal(new LibraryInfo("lib-1", "Audiobooks", "book"), libs[0]);
    }

    [Fact]
    public async Task Audiobookshelf_accepts_bare_array_response()
    {
        // Some ABS versions return [...] directly instead of { libraries: [...] }.
        var (f, _) = Server(@"[
            {""id"":""x"",""name"":""Books"",""mediaType"":""book""}
        ]");
        var p = new AudioBookshelfProvider(f);
        var cfg = JsonSerializer.Serialize(new AudioBookshelfConfig { BaseUrl = "http://abs:13378", ApiToken = "t" });

        var libs = (await p.ListLibrariesAsync(cfg, default)).ToList();
        Assert.Single(libs);
        Assert.Equal("Books", libs[0].Name);
    }
}
