using System.Collections.Concurrent;
using System.Threading.Channels;
using InotifyRelay.Core.Events;
using InotifyRelay.Core.Watching;
using Microsoft.Extensions.Logging;

namespace InotifyRelay.Watcher.Managed;

/// <summary>
/// Fallback watcher built on <see cref="System.IO.FileSystemWatcher"/>. Used on Windows
/// and as a last-resort on non-Linux Unix platforms. NOT recommended for Linux container
/// workloads because of the inotify-instance-per-directory issue.
/// </summary>
public sealed class ManagedFsWatcher : IFileSystemWatcher, IDisposable
{
    private readonly ILogger<ManagedFsWatcher> _logger;
    private readonly Channel<FileSystemChange> _channel =
        Channel.CreateUnbounded<FileSystemChange>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly ConcurrentDictionary<string, RootEntry> _roots = new();

    public ManagedFsWatcher(ILogger<ManagedFsWatcher> logger) => _logger = logger;

    private sealed class RootEntry
    {
        public required FileSystemWatcher Watcher { get; init; }
        public required string Path { get; init; }
        public required FileEventType Mask { get; init; }
    }

    public ValueTask StartAsync(CancellationToken ct) => ValueTask.CompletedTask;

    public ValueTask StopAsync(CancellationToken ct)
    {
        foreach (var r in _roots.Values) r.Watcher.Dispose();
        _roots.Clear();
        _channel.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    public ValueTask AddOrUpdateWatchAsync(WatchDefinition definition, CancellationToken ct)
    {
        if (_roots.TryRemove(definition.Id, out var existing))
            existing.Watcher.Dispose();

        if (!Directory.Exists(definition.Path))
        {
            _logger.LogWarning("Watch path missing: {Path}", definition.Path);
            return ValueTask.CompletedTask;
        }

        var fsw = new FileSystemWatcher(definition.Path)
        {
            IncludeSubdirectories = definition.Recursive,
            EnableRaisingEvents = false,
            InternalBufferSize = 64 * 1024,
        };
        fsw.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                         | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.Attributes;

        var rootPath = definition.Path;
        fsw.Created += (_, e) => Push(rootPath, definition.EventMask,
            new FileSystemChange(e.FullPath, IsDir(e.FullPath) ? FileEventType.DirCreated : FileEventType.Created,
                IsDir(e.FullPath), DateTimeOffset.UtcNow, rootPath));
        fsw.Changed += (_, e) => Push(rootPath, definition.EventMask,
            new FileSystemChange(e.FullPath, FileEventType.Modified, IsDir(e.FullPath),
                DateTimeOffset.UtcNow, rootPath));
        fsw.Deleted += (_, e) => Push(rootPath, definition.EventMask,
            new FileSystemChange(e.FullPath, FileEventType.Deleted, false,
                DateTimeOffset.UtcNow, rootPath));
        fsw.Renamed += (_, e) => Push(rootPath, definition.EventMask,
            new FileSystemChange(e.FullPath, FileEventType.Renamed, IsDir(e.FullPath),
                DateTimeOffset.UtcNow, rootPath, OldPath: e.OldFullPath));
        fsw.Error += (_, e) => _logger.LogWarning(e.GetException(), "FileSystemWatcher error on {Path}", rootPath);

        fsw.EnableRaisingEvents = true;
        _roots[definition.Id] = new RootEntry { Watcher = fsw, Path = definition.Path, Mask = definition.EventMask };
        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveWatchAsync(string id, CancellationToken ct)
    {
        if (_roots.TryRemove(id, out var r)) r.Watcher.Dispose();
        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<FileSystemChange> ReadAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var c in _channel.Reader.ReadAllAsync(ct)) yield return c;
    }

    public WatcherStats GetStats() => new(
        Implementation: "managed",
        IsRunning: !_channel.Reader.Completion.IsCompleted,
        ActiveWatchDescriptors: _roots.Count,
        ActiveRoots: _roots.Count);

    private void Push(string rootPath, FileEventType mask, FileSystemChange c)
    {
        if ((mask & c.EventType) == 0) return;
        _channel.Writer.TryWrite(c);
    }

    private static bool IsDir(string path)
    {
        try { return Directory.Exists(path); } catch { return false; }
    }

    public void Dispose() => StopAsync(CancellationToken.None).AsTask().Wait();
}
