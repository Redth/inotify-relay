using InotifyRelay.Core.Events;
using Microsoft.Extensions.FileSystemGlobbing;

namespace InotifyRelay.Core.Pipeline;

public sealed class RuleMatch
{
    public required RuleSnapshot Rule { get; init; }
    public required SourceSnapshot Source { get; init; }
    public required FileSystemChange Change { get; init; }
}

public static class RuleMatcher
{
    public static IEnumerable<RuleMatch> Match(FileSystemChange change, IReadOnlyList<RuleSnapshot> rules)
    {
        foreach (var rule in rules)
        {
            if (!rule.Enabled) continue;
            if ((rule.EventMask & change.EventType) == 0) continue;

            foreach (var src in rule.Sources)
            {
                if (!PathMatchesSource(change.Path, src)) continue;
                if (!string.IsNullOrEmpty(src.GlobPattern) && !GlobMatches(src.Path, src.GlobPattern, change.Path)) continue;
                yield return new RuleMatch { Rule = rule, Source = src, Change = change };
                break; // one source per rule is enough
            }
        }
    }

    /// <summary>
    /// A path matches a source when:
    ///   - recursive source: the path equals the source root or is anywhere under it
    ///   - non-recursive source: the path is the source root itself or a direct child of it
    /// (so a non-recursive <c>/movies</c> matches <c>/movies/a.mkv</c> but NOT
    /// <c>/movies/sub/a.mkv</c>). Important now that the watch plan may install a
    /// recursive watch on an ancestor that delivers deep events the non-recursive
    /// rule shouldn't care about.
    /// </summary>
    public static bool PathMatchesSource(string fullPath, SourceSnapshot src)
    {
        var root = src.Path.TrimEnd('/', '\\');
        if (fullPath.Equals(root, StringComparison.Ordinal)) return true;

        var hasChildSep =
            fullPath.Length > root.Length &&
            fullPath.StartsWith(root, StringComparison.Ordinal) &&
            (fullPath[root.Length] == '/' || fullPath[root.Length] == '\\');
        if (!hasChildSep) return false;
        if (src.Recursive) return true;

        // Non-recursive: only events on direct children of the source root.
        var parent = Path.GetDirectoryName(fullPath)?.TrimEnd('/', '\\') ?? "";
        return parent.Equals(root, StringComparison.Ordinal);
    }

    private static bool GlobMatches(string root, string pattern, string fullPath)
    {
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        matcher.AddInclude(pattern);
        var r = root.TrimEnd('/', '\\');
        var rel = fullPath.StartsWith(r, StringComparison.Ordinal)
            ? fullPath[r.Length..].TrimStart('/', '\\')
            : fullPath;
        var result = matcher.Match(rel);
        return result.HasMatches;
    }
}
