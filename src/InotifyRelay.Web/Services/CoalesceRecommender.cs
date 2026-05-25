using System.Text.Json;

namespace InotifyRelay.Web.Services;

/// <summary>
/// Suggests a coalesce window for a target based on its provider + the currently
/// selected provider action. Full-library scans get a long window (a torrent
/// finish that drops 50 files → 1 scan). Path-specific reports get 0 (each call
/// is distinct; coalescing mostly just adds latency).
/// </summary>
public static class CoalesceRecommender
{
    public const int FullScanDefault = 5000;
    public const int PathSpecificDefault = 0;

    public static int Recommend(string providerType, string? providerConfigJson)
    {
        var key = (providerType ?? "").ToLowerInvariant();
        var action = ReadAction(providerConfigJson);
        return (key, action) switch
        {
            ("jellyfin", "report-path")          => PathSpecificDefault,
            ("plex",     "refresh-path")         => PathSpecificDefault,
            ("webhook",  _)                      => PathSpecificDefault,
            ("jellyfin", _)                      => FullScanDefault,
            ("plex",     _)                      => FullScanDefault,
            ("audiobookshelf", _)                => FullScanDefault,
            ("grimmory", _)                      => FullScanDefault,
            _                                    => PathSpecificDefault,
        };
    }

    public static string? Explain(string providerType, string? providerConfigJson)
    {
        var ms = Recommend(providerType, providerConfigJson);
        var key = (providerType ?? "").ToLowerInvariant();
        var action = ReadAction(providerConfigJson);
        return (key, action) switch
        {
            ("jellyfin", "report-path") =>
                "Each report-path call carries a distinct file path — coalescing mostly adds latency. Recommended: 0 ms.",
            ("plex", "refresh-path") =>
                "Each refresh-path call carries a distinct folder — coalescing mostly adds latency. Recommended: 0 ms.",
            ("jellyfin", _) or ("plex", _) or ("audiobookshelf", _) or ("grimmory", _) =>
                $"Full-library scans benefit from coalescing — many events collapse to one scan. Recommended: {ms} ms.",
            _ => null,
        };
    }

    private static string ReadAction(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "";
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("Action", out var a) && a.ValueKind == JsonValueKind.String
                ? a.GetString() ?? ""
                : "";
        }
        catch { return ""; }
    }
}
