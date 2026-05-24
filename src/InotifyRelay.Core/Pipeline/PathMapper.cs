namespace InotifyRelay.Core.Pipeline;

public sealed record PathMapping(string From, string To);

/// <summary>
/// Rewrites paths using a prefix-mapping list — the same pattern Sonarr/Radarr
/// call "Remote Path Mappings". Used so a watcher path like
/// <c>/watch/movies/Inception/a.mkv</c> (what inotify-relay sees) is translated
/// to <c>/data/movies/Inception/a.mkv</c> (what Jellyfin/Plex sees) before being
/// sent on the wire. Longest matching prefix wins; segment-boundary aware so
/// <c>/movies</c> doesn't accidentally match <c>/moviestars</c>.
/// </summary>
public static class PathMapper
{
    public static string Apply(string path, IReadOnlyList<PathMapping>? mappings)
    {
        if (mappings is null || mappings.Count == 0 || string.IsNullOrEmpty(path))
            return path;

        PathMapping? best = null;
        var bestLen = -1;
        foreach (var m in mappings)
        {
            if (string.IsNullOrEmpty(m.From)) continue;
            var fromNorm = TrimEnd(m.From);
            if (!IsPrefix(fromNorm, path)) continue;
            if (fromNorm.Length > bestLen)
            {
                best = m;
                bestLen = fromNorm.Length;
            }
        }
        if (best is null) return path;

        var fromN = TrimEnd(best.From);
        var toN = TrimEnd(best.To);
        if (path.Length == fromN.Length) return best.To;
        return toN + path.Substring(fromN.Length);
    }

    private static bool IsPrefix(string prefixTrimmed, string full)
    {
        if (prefixTrimmed.Length > full.Length) return false;
        if (!full.StartsWith(prefixTrimmed, StringComparison.Ordinal)) return false;
        return full.Length == prefixTrimmed.Length
            || full[prefixTrimmed.Length] == '/'
            || full[prefixTrimmed.Length] == '\\';
    }

    private static string TrimEnd(string s)
    {
        var end = s.Length;
        while (end > 1 && (s[end - 1] == '/' || s[end - 1] == '\\')) end--;
        return end == s.Length ? s : s.Substring(0, end);
    }
}
