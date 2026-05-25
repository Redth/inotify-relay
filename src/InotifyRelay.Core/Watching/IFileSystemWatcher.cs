using InotifyRelay.Core.Events;

namespace InotifyRelay.Core.Watching;

public sealed record WatchDefinition(
    string Id,
    string Path,
    bool Recursive,
    FileEventType EventMask);

/// <summary>Live numbers about how much watcher resource we're consuming.</summary>
public sealed record WatcherStats(
    /// <summary>Implementation type, e.g. "linux-inotify" or "managed".</summary>
    string Implementation,
    /// <summary>Whether the watcher is currently running (StartAsync has succeeded).</summary>
    bool IsRunning,
    /// <summary>Number of distinct directory watch descriptors held by the kernel.
    /// On Linux this is the count that contributes to <c>fs.inotify.max_user_watches</c>.</summary>
    int ActiveWatchDescriptors,
    /// <summary>Number of logical watch "roots" registered by the sync service.</summary>
    int ActiveRoots);

public interface IFileSystemWatcher
{
    ValueTask StartAsync(CancellationToken ct);
    ValueTask StopAsync(CancellationToken ct);
    ValueTask AddOrUpdateWatchAsync(WatchDefinition definition, CancellationToken ct);
    ValueTask RemoveWatchAsync(string id, CancellationToken ct);
    IAsyncEnumerable<FileSystemChange> ReadAllAsync(CancellationToken ct);
    WatcherStats GetStats();
}
