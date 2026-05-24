namespace InotifyRelay.Core.Pipeline;

/// <summary>
/// Reduces a collection of paths to the smallest set that "covers" all of them.
/// A path is dropped when another path in the set is its ancestor — the ancestor
/// already covers the descendant. An empty/whitespace path is treated as a
/// full-scan marker and subsumes everything else.
/// </summary>
public static class PathSubsumption
{
    /// <summary>
    /// Pick the broadest-coverage subset of <paramref name="items"/>, choosing
    /// the path of each item via <paramref name="pathSelector"/>. Items that
    /// share an exact path are deduped (first occurrence wins).
    /// </summary>
    public static IReadOnlyList<T> Subsume<T>(IEnumerable<T> items, Func<T, string> pathSelector)
    {
        // Materialize and pre-normalize.
        var entries = items.Select(it => (Item: it, Path: Normalize(pathSelector(it)))).ToList();

        // Full-scan wins outright: if any item has an empty path, keep the first such item only.
        var fullIdx = entries.FindIndex(e => e.Path.Length == 0);
        if (fullIdx >= 0)
            return new[] { entries[fullIdx].Item };

        // Sort by path length ascending. Shorter paths (potential ancestors) come first.
        entries.Sort((a, b) => a.Path.Length.CompareTo(b.Path.Length));

        var survivors = new List<(T Item, string Path)>();
        foreach (var entry in entries)
        {
            var subsumed = false;
            foreach (var s in survivors)
            {
                if (IsAncestorOrEqual(s.Path, entry.Path)) { subsumed = true; break; }
            }
            if (!subsumed) survivors.Add(entry);
        }
        return survivors.Select(s => s.Item).ToList();
    }

    /// <summary>Returns true when <paramref name="a"/> is an ancestor of (or equal to) <paramref name="b"/>.</summary>
    public static bool IsAncestorOrEqual(string a, string b)
    {
        a = Normalize(a);
        b = Normalize(b);
        if (a.Length == 0) return true;
        if (a.Length > b.Length) return false;
        if (!b.StartsWith(a, StringComparison.Ordinal)) return false;
        return a.Length == b.Length || b[a.Length] == '/' || b[a.Length] == '\\';
    }

    private static string Normalize(string p)
    {
        if (string.IsNullOrWhiteSpace(p)) return string.Empty;
        // Strip trailing separators so "/x/" and "/x" compare equal.
        var end = p.Length;
        while (end > 1 && (p[end - 1] == '/' || p[end - 1] == '\\')) end--;
        return end == p.Length ? p : p.Substring(0, end);
    }
}
