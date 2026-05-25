namespace InotifyRelay.Web.Services;

/// <summary>
/// Reads the kernel's per-user inotify limits from <c>/proc/sys/fs/inotify/</c>.
/// These are the values you bump with <c>sysctl fs.inotify.max_user_watches=...</c>
/// on the host — they're per-user, not per-process, and can't be changed from
/// inside an unprivileged container. Returns null fields on non-Linux or when
/// the procfs entries aren't readable.
/// </summary>
public sealed record InotifySystemLimits(int? MaxUserWatches, int? MaxUserInstances, int? MaxQueuedEvents)
{
    public static InotifySystemLimits Read()
    {
        if (!OperatingSystem.IsLinux())
            return new InotifySystemLimits(null, null, null);

        return new InotifySystemLimits(
            TryReadInt("/proc/sys/fs/inotify/max_user_watches"),
            TryReadInt("/proc/sys/fs/inotify/max_user_instances"),
            TryReadInt("/proc/sys/fs/inotify/max_queued_events"));
    }

    private static int? TryReadInt(string path)
    {
        try
        {
            return File.Exists(path) && int.TryParse(File.ReadAllText(path).Trim(), out var v) ? v : null;
        }
        catch { return null; }
    }
}
