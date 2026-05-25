namespace InotifyRelay.Core.Pipeline;

/// <summary>
/// Cross-platform path manipulation that preserves the input's separator style.
/// <see cref="System.IO.Path.GetDirectoryName"/> on Windows always rewrites to
/// backslashes regardless of input (so <c>/watch/movies/x.mkv</c> becomes
/// <c>\watch\movies</c>), which silently corrupts templated Linux paths when
/// inotify-relay is run from Windows or has tests executed on Windows runners.
/// </summary>
public static class PathHelpers
{
    /// <summary>Return the parent directory portion of <paramref name="path"/>,
    /// preserving whichever separator style the input uses.</summary>
    public static string GetDirectory(string? path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        var idx = path.LastIndexOfAny(_seps);
        if (idx < 0) return string.Empty;
        // Root-only case: parent of "/foo" is "/" (and "\foo" is "\").
        return idx == 0 ? path[..1] : path[..idx];
    }

    private static readonly char[] _seps = ['/', '\\'];
}
