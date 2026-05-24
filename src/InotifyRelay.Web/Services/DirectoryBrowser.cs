namespace InotifyRelay.Web.Services;

/// <summary>Shared logic for the source-path folder picker.</summary>
public sealed class DirectoryBrowser
{
    public sealed record Entry(string Name, string Path);
    public sealed record Result(string? Path, string? Parent, IReadOnlyList<Entry> Entries, IReadOnlyList<string>? Roots, string? Error);

    public Result Browse(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            if (OperatingSystem.IsWindows())
            {
                var roots = DriveInfo.GetDrives()
                    .Where(d => d.IsReady)
                    .Select(d => d.RootDirectory.FullName)
                    .ToList();
                return new Result(null, null, Array.Empty<Entry>(), roots, null);
            }
            path = "/";
        }

        try
        {
            if (!Directory.Exists(path))
                return new Result(path, null, Array.Empty<Entry>(), null, $"Path does not exist: {path}");

            var entries = new List<Entry>();
            foreach (var dir in Directory.EnumerateDirectories(path))
            {
                try
                {
                    var info = new DirectoryInfo(dir);
                    if ((info.Attributes & FileAttributes.Hidden) != 0) continue;
                    entries.Add(new Entry(info.Name, info.FullName));
                }
                catch { /* skip entries we can't stat */ }
            }
            entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            var parent = Directory.GetParent(path)?.FullName;
            return new Result(path, parent, entries, null, null);
        }
        catch (UnauthorizedAccessException)
        {
            return new Result(path, null, Array.Empty<Entry>(), null, "Permission denied");
        }
        catch (Exception ex)
        {
            return new Result(path, null, Array.Empty<Entry>(), null, ex.Message);
        }
    }
}
