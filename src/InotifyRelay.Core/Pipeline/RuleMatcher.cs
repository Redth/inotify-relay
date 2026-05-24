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
                if (!PathStartsWith(change.Path, src.Path)) continue;
                if (!string.IsNullOrEmpty(src.GlobPattern) && !GlobMatches(src.Path, src.GlobPattern, change.Path)) continue;
                yield return new RuleMatch { Rule = rule, Source = src, Change = change };
                break; // one source per rule is enough
            }
        }
    }

    private static bool PathStartsWith(string fullPath, string root)
    {
        var r = root.TrimEnd('/', '\\');
        return fullPath.Equals(r, StringComparison.Ordinal)
            || fullPath.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || fullPath.StartsWith(r + '/', StringComparison.Ordinal);
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
