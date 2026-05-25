namespace InotifyRelay.Core.Pipeline;

/// <summary>One physical filesystem watch we should set up to satisfy the rules.</summary>
public sealed record WatchPlanEntry(string Path, bool Recursive);

/// <summary>
/// Reduces the set of (path, recursive) pairs declared across all rule sources
/// into the minimum set of physical filesystem watches that covers them. Two
/// recursive sources where one is an ancestor of the other collapse to the
/// ancestor; a non-recursive source sitting under a recursive root is dropped
/// because the recursive root's watch already delivers events for it (the
/// rule-level <see cref="RuleMatcher"/> still handles the "non-recursive only
/// matches direct children" semantics at dispatch time).
/// </summary>
public static class WatchPlanner
{
    public static IReadOnlyList<WatchPlanEntry> Plan(IReadOnlyList<RuleSnapshot> rules)
    {
        var sources = rules
            .Where(r => r.Enabled)
            .SelectMany(r => r.Sources)
            .Where(s => !string.IsNullOrWhiteSpace(s.Path))
            .ToList();

        var recursivePaths = sources
            .Where(s => s.Recursive)
            .Select(s => Normalize(s.Path))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Minimum cover of recursive paths.
        var recursiveCover = PathSubsumption.Subsume(recursivePaths, p => p)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Non-recursive paths that aren't already covered by a recursive root.
        var nonRecursive = sources
            .Where(s => !s.Recursive)
            .Select(s => Normalize(s.Path))
            .Distinct(StringComparer.Ordinal)
            .Where(p => !recursiveCover.Any(r => PathSubsumption.IsAncestorOrEqual(r, p)))
            .ToList();

        var plan = new List<WatchPlanEntry>(recursiveCover.Count + nonRecursive.Count);
        foreach (var p in recursiveCover) plan.Add(new WatchPlanEntry(p, true));
        foreach (var p in nonRecursive) plan.Add(new WatchPlanEntry(p, false));
        return plan;
    }

    private static string Normalize(string p)
    {
        if (string.IsNullOrWhiteSpace(p)) return string.Empty;
        var end = p.Length;
        while (end > 1 && (p[end - 1] == '/' || p[end - 1] == '\\')) end--;
        return end == p.Length ? p : p.Substring(0, end);
    }
}
