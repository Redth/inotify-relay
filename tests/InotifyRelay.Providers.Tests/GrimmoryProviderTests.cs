using System.Net;
using System.Text;
using System.Text.Json;
using InotifyRelay.Core.Events;
using InotifyRelay.Core.Providers;
using InotifyRelay.Providers.Grimmory;
using InotifyRelay.Providers.Tests.TestHelpers;

namespace InotifyRelay.Providers.Tests;

public class GrimmoryProviderTests
{
    private static (FakeHttpClientFactory, GrimmoryProvider, FakeHttpHandler) Build()
    {
        var f = new FakeHttpClientFactory();
        var p = new GrimmoryProvider(f);
        return (f, p, f.Handler);
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage NoContent() => new(HttpStatusCode.NoContent);

    // Pin a unique base URL per test so the static token cache in the provider
    // doesn't carry credentials across tests.
    private static GrimmoryConfig FreshConfig(string libraryId = "1", string? user = null) => new()
    {
        BaseUrl = $"http://grimmory-{Guid.NewGuid():N}:6060",
        Username = user ?? $"user-{Guid.NewGuid():N}",
        Password = "pw",
        LibraryId = libraryId,
    };

    private static FileSystemChange Change() =>
        new("/watch/books/a.epub", FileEventType.ClosedWrite, false, DateTimeOffset.UtcNow, "/watch/books");

    [Fact]
    public async Task Send_logs_in_then_PUTs_refresh_with_bearer()
    {
        var (_, p, h) = Build();
        h.Responder = (req, n) => req.RequestUri!.AbsolutePath switch
        {
            "/api/v1/auth/login" => Json("{\"accessToken\":\"jwt-abc\",\"expires\":253402300799000}"),
            _ => NoContent(),
        };
        var cfg = FreshConfig(libraryId: "42");
        var result = await p.SendAsync(new RelayContext(Change(), "r", "t",
            JsonSerializer.Serialize(cfg), null), default);

        Assert.True(result.Success);
        Assert.Equal(2, h.Requests.Count);

        // login
        Assert.Equal("POST", h.Requests[0].Method);
        Assert.Equal("/api/v1/auth/login", h.Requests[0].Url.AbsolutePath);
        using var loginBody = JsonDocument.Parse(h.Requests[0].Body!);
        Assert.Equal(cfg.Username, loginBody.RootElement.GetProperty("username").GetString());
        Assert.Equal("pw", loginBody.RootElement.GetProperty("password").GetString());

        // refresh
        Assert.Equal("PUT", h.Requests[1].Method);
        Assert.EndsWith("/api/v1/libraries/42/refresh", h.Requests[1].Url.AbsolutePath);
        Assert.Equal("Bearer jwt-abc", h.Requests[1].Headers["Authorization"]);
    }

    [Fact]
    public async Task Token_is_cached_so_two_consecutive_calls_only_log_in_once()
    {
        var (_, p, h) = Build();
        h.Responder = (req, n) => req.RequestUri!.AbsolutePath switch
        {
            "/api/v1/auth/login" => Json("{\"accessToken\":\"jwt-1\",\"expires\":253402300799000}"),
            _ => NoContent(),
        };
        var cfg = FreshConfig();
        var json = JsonSerializer.Serialize(cfg);

        await p.SendAsync(new RelayContext(Change(), "r", "t", json, null), default);
        await p.SendAsync(new RelayContext(Change(), "r", "t", json, null), default);

        Assert.Equal(1, h.Requests.Count(r => r.Url.AbsolutePath == "/api/v1/auth/login"));
        Assert.Equal(2, h.Requests.Count(r => r.Url.AbsolutePath.EndsWith("/refresh")));
    }

    [Fact]
    public async Task Failed_login_returns_failure_result_not_throw()
    {
        var (_, p, h) = Build();
        h.Responder = (_, _) => Json("{\"error\":\"bad creds\"}", HttpStatusCode.Unauthorized);
        var cfg = FreshConfig();
        var result = await p.SendAsync(new RelayContext(Change(), "r", "t",
            JsonSerializer.Serialize(cfg), null), default);
        Assert.False(result.Success);
        Assert.Contains("401", result.Error);
    }

    [Fact]
    public async Task Missing_library_id_short_circuits_before_login()
    {
        var (_, p, h) = Build();
        var cfg = FreshConfig(libraryId: "");
        var result = await p.SendAsync(new RelayContext(Change(), "r", "t",
            JsonSerializer.Serialize(cfg), null), default);
        Assert.False(result.Success);
        Assert.Contains("LibraryId", result.Error);
        Assert.Empty(h.Requests);
    }

    [Fact]
    public async Task ListLibraries_logs_in_then_GETs_libraries_and_maps_id_name()
    {
        var (_, p, h) = Build();
        h.Responder = (req, n) => req.RequestUri!.AbsolutePath switch
        {
            "/api/v1/auth/login" => Json("{\"accessToken\":\"jwt-x\",\"expires\":253402300799000}"),
            "/api/v1/libraries"  => Json("[{\"id\":1,\"name\":\"Books\"},{\"id\":2,\"name\":\"Comics\"}]"),
            _ => Json("{}", HttpStatusCode.NotFound),
        };
        var cfg = FreshConfig();
        var libs = (await p.ListLibrariesAsync(JsonSerializer.Serialize(cfg), default)).ToList();

        Assert.Equal(2, libs.Count);
        Assert.Equal(new LibraryInfo("1", "Books"), libs[0]);
        Assert.Equal(new LibraryInfo("2", "Comics"), libs[1]);
        Assert.Equal("Bearer jwt-x", h.Requests.Last().Headers["Authorization"]);
    }

    [Fact]
    public async Task ListLibraries_surfaces_http_error_on_libraries_endpoint()
    {
        var (_, p, h) = Build();
        h.Responder = (req, n) => req.RequestUri!.AbsolutePath switch
        {
            "/api/v1/auth/login" => Json("{\"accessToken\":\"jwt-y\",\"expires\":253402300799000}"),
            _ => Json("nope", HttpStatusCode.Forbidden),
        };
        var cfg = FreshConfig();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await p.ListLibrariesAsync(JsonSerializer.Serialize(cfg), default));
        Assert.Contains("403", ex.Message);
    }
}
