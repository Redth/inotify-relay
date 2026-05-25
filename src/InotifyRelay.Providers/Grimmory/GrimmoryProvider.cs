using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using InotifyRelay.Core.Providers;

namespace InotifyRelay.Providers.Grimmory;

/// <summary>
/// Provider for <a href="https://grimmory.org/">Grimmory</a> (formerly BookLore).
/// Grimmory uses short-lived JWT auth — there is no static API token mechanism,
/// so this provider does a username+password login per call and caches the
/// returned access token in-memory until shortly before it expires.
/// </summary>
public sealed class GrimmoryProvider(IHttpClientFactory http)
    : IRelayProvider, IRelayProviderWithLibraries
{
    public string TypeKey => "grimmory";
    public string DisplayName => "Grimmory";
    public Type ConfigType => typeof(GrimmoryConfig);

    public object CreateDefaultConfig() => new GrimmoryConfig();

    private sealed record CachedToken(string Token, DateTimeOffset ExpiresAt);
    private static readonly ConcurrentDictionary<string, CachedToken> _tokenCache = new();
    private static readonly TimeSpan TokenSafetyMargin = TimeSpan.FromSeconds(30);

    public async Task<RelayResult> SendAsync(RelayContext ctx, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var cfg = JsonSerializer.Deserialize<GrimmoryConfig>(ctx.ProviderConfigJson) ?? new GrimmoryConfig();
        if (string.IsNullOrWhiteSpace(cfg.LibraryId))
        {
            sw.Stop();
            return RelayResult.Fail(0, null, "LibraryId is required for Grimmory.", sw.Elapsed);
        }

        try
        {
            var client = http.CreateClient("relay");
            var token = await GetTokenAsync(client, cfg, ct);
            var baseUrl = cfg.BaseUrl.TrimEnd('/');
            using var req = new HttpRequestMessage(HttpMethod.Put,
                $"{baseUrl}/api/v1/libraries/{Uri.EscapeDataString(cfg.LibraryId)}/refresh");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await client.SendAsync(req, ct);
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

    public async Task<IReadOnlyList<LibraryInfo>> ListLibrariesAsync(string configJson, CancellationToken ct)
    {
        var cfg = JsonSerializer.Deserialize<GrimmoryConfig>(configJson) ?? new GrimmoryConfig();
        var client = http.CreateClient("relay");
        var token = await GetTokenAsync(client, cfg, ct);
        var baseUrl = cfg.BaseUrl.TrimEnd('/');
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/v1/libraries");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.TryAddWithoutValidation("Accept", "application/json");

        using var resp = await client.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Grimmory returned HTTP {(int)resp.StatusCode}");

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var libs = new List<LibraryInfo>();
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                string? id = null;
                if (item.TryGetProperty("id", out var idEl))
                {
                    id = idEl.ValueKind switch
                    {
                        JsonValueKind.Number => idEl.GetRawText(),
                        JsonValueKind.String => idEl.GetString(),
                        _ => null,
                    };
                }
                var name = item.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                    libs.Add(new LibraryInfo(id, name));
            }
        }
        return libs;
    }

    private static async Task<string> GetTokenAsync(HttpClient client, GrimmoryConfig cfg, CancellationToken ct)
    {
        var key = $"{cfg.BaseUrl.TrimEnd('/')}|{cfg.Username}";
        if (_tokenCache.TryGetValue(key, out var cached) &&
            cached.ExpiresAt - TokenSafetyMargin > DateTimeOffset.UtcNow)
        {
            return cached.Token;
        }

        var baseUrl = cfg.BaseUrl.TrimEnd('/');
        var loginBody = JsonSerializer.Serialize(new { username = cfg.Username, password = cfg.Password });
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/v1/auth/login")
        {
            Content = new StringContent(loginBody, Encoding.UTF8, "application/json")
        };
        using var resp = await client.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Grimmory login failed: HTTP {(int)resp.StatusCode}");

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("accessToken", out var tokEl) || tokEl.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("Grimmory login response missing 'accessToken'.");
        var token = tokEl.GetString()!;

        // expires is a unix-ms timestamp; if absent, default to 5 minutes.
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        if (doc.RootElement.TryGetProperty("expires", out var expEl) && expEl.TryGetInt64(out var ms))
            expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(ms);

        _tokenCache[key] = new CachedToken(token, expiresAt);
        return token;
    }
}
