using InotifyRelay.Core.Events;

namespace InotifyRelay.Core.Watching;

public sealed record WatchDefinition(
    string Id,
    string Path,
    bool Recursive,
    FileEventType EventMask);

public interface IFileSystemWatcher
{
    ValueTask StartAsync(CancellationToken ct);
    ValueTask StopAsync(CancellationToken ct);
    ValueTask AddOrUpdateWatchAsync(WatchDefinition definition, CancellationToken ct);
    ValueTask RemoveWatchAsync(string id, CancellationToken ct);
    IAsyncEnumerable<FileSystemChange> ReadAllAsync(CancellationToken ct);
}
