using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using InotifyRelay.Core.Events;
using InotifyRelay.Core.Watching;
using Microsoft.Extensions.Logging;
using static InotifyRelay.Watcher.Linux.NativeMethods;

namespace InotifyRelay.Watcher.Linux;

/// <summary>
/// Single-fd inotify-based watcher. One inotify instance covers all watched directories,
/// which avoids the per-directory-instance pattern of <c>System.IO.FileSystemWatcher</c>
/// that blows past <c>fs.inotify.max_user_instances</c> quickly.
/// </summary>
public sealed class LinuxInotifyWatcher : IFileSystemWatcher, IAsyncDisposable
{
    private readonly ILogger<LinuxInotifyWatcher> _logger;
    private readonly Channel<FileSystemChange> _channel =
        Channel.CreateUnbounded<FileSystemChange>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

    private int _fd = -1;
    private Thread? _reader;
    private CancellationTokenSource? _cts;

    // wd -> watched dir info
    private readonly ConcurrentDictionary<int, WatchedDir> _byWd = new();
    // absolute path -> wd
    private readonly ConcurrentDictionary<string, int> _byPath = new(StringComparer.Ordinal);
    // root id -> root info
    private readonly ConcurrentDictionary<string, RootInfo> _roots = new(StringComparer.Ordinal);
    // pending IN_MOVED_FROM by cookie -> (path, sourceRoot, expiresUtcMs)
    private readonly ConcurrentDictionary<uint, PendingMove> _pendingMoves = new();

    public LinuxInotifyWatcher(ILogger<LinuxInotifyWatcher> logger) => _logger = logger;

    private sealed class WatchedDir
    {
        public required int Wd { get; init; }
        public required string Path { get; init; }
        public HashSet<string> RootIds { get; } = new(StringComparer.Ordinal);
    }

    private sealed class RootInfo
    {
        public required string Id { get; init; }
        public required string Path { get; init; }
        public required bool Recursive { get; init; }
        public required FileEventType EventMask { get; set; }
    }

    private sealed record PendingMove(string Path, string SourceRoot, long ExpiresUtcMs, bool IsDirectory);

    public ValueTask StartAsync(CancellationToken ct)
    {
        if (_fd >= 0) return ValueTask.CompletedTask;
        var fd = inotify_init1(IN_NONBLOCK | IN_CLOEXEC);
        if (fd < 0)
            throw new InvalidOperationException($"inotify_init1 failed: errno {Marshal.GetLastWin32Error()}");
        _fd = fd;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "inotify-reader" };
        _reader.Start(_cts.Token);
        _logger.LogInformation("Started inotify watcher (fd={Fd})", _fd);
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken ct)
    {
        _cts?.Cancel();
        if (_fd >= 0)
        {
            close_fd(_fd);
            _fd = -1;
        }
        _channel.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    public async ValueTask AddOrUpdateWatchAsync(WatchDefinition definition, CancellationToken ct)
    {
        if (_fd < 0) throw new InvalidOperationException("Watcher not started");

        var root = new RootInfo
        {
            Id = definition.Id,
            Path = definition.Path,
            Recursive = definition.Recursive,
            EventMask = definition.EventMask,
        };
        _roots[root.Id] = root;

        if (!Directory.Exists(definition.Path))
        {
            _logger.LogWarning("Watch path does not exist: {Path}", definition.Path);
            return;
        }

        AddDirectoryWatch(definition.Path, root.Id);
        if (definition.Recursive)
        {
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(definition.Path, "*", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                }))
                {
                    AddDirectoryWatch(dir, root.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Recursive add failed under {Path}", definition.Path);
            }
        }
        await ValueTask.CompletedTask;
    }

    public ValueTask RemoveWatchAsync(string id, CancellationToken ct)
    {
        if (!_roots.TryRemove(id, out _)) return ValueTask.CompletedTask;
        // Remove watch descriptors that are no longer referenced by any root
        foreach (var kvp in _byWd.ToArray())
        {
            kvp.Value.RootIds.Remove(id);
            if (kvp.Value.RootIds.Count == 0)
            {
                if (_fd >= 0) inotify_rm_watch(_fd, kvp.Key);
                _byWd.TryRemove(kvp.Key, out _);
                _byPath.TryRemove(kvp.Value.Path, out _);
            }
        }
        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<FileSystemChange> ReadAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var c in _channel.Reader.ReadAllAsync(ct))
            yield return c;
    }

    public WatcherStats GetStats() => new(
        Implementation: "linux-inotify",
        IsRunning: _fd >= 0,
        ActiveWatchDescriptors: _byWd.Count,
        ActiveRoots: _roots.Count);

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        if (_reader is not null && _reader.IsAlive)
            _reader.Join(TimeSpan.FromSeconds(2));
    }

    private void AddDirectoryWatch(string path, string rootId)
    {
        if (_byPath.TryGetValue(path, out var existingWd))
        {
            if (_byWd.TryGetValue(existingWd, out var existing))
                existing.RootIds.Add(rootId);
            return;
        }

        const uint mask = IN_CREATE | IN_DELETE | IN_MODIFY | IN_CLOSE_WRITE
                       | IN_MOVED_FROM | IN_MOVED_TO | IN_ATTRIB | IN_EXCL_UNLINK | IN_ONLYDIR;
        var wd = inotify_add_watch(_fd, path, mask);
        if (wd < 0)
        {
            var err = Marshal.GetLastWin32Error();
            _logger.LogWarning("inotify_add_watch failed for {Path}: errno {Errno}", path, err);
            return;
        }
        var entry = new WatchedDir { Wd = wd, Path = path };
        entry.RootIds.Add(rootId);
        _byWd[wd] = entry;
        _byPath[path] = wd;
    }

    private unsafe void ReadLoop(object? state)
    {
        var token = (CancellationToken)state!;
        var buf = new byte[64 * 1024];
        fixed (byte* p = buf)
        {
            while (!token.IsCancellationRequested && _fd >= 0)
            {
                var pfd = new PollFd { fd = _fd, events = POLLIN };
                var pr = poll(&pfd, 1, 250);
                if (pr < 0)
                {
                    var err = Marshal.GetLastWin32Error();
                    if (err == 4 /* EINTR */) continue;
                    _logger.LogError("poll failed: errno {Errno}", err);
                    break;
                }
                if (pr == 0)
                {
                    FlushExpiredMoves();
                    continue;
                }
                var n = read(_fd, p, (nuint)buf.Length);
                if (n < 0)
                {
                    var err = Marshal.GetLastWin32Error();
                    if (err == 11 /* EAGAIN */) continue;
                    _logger.LogError("read failed: errno {Errno}", err);
                    break;
                }
                ParseEvents(buf, (int)n);
                FlushExpiredMoves();
            }
        }
    }

    private void ParseEvents(byte[] buf, int len)
    {
        var i = 0;
        // struct inotify_event { int wd; uint mask; uint cookie; uint len; char name[]; }
        while (i + 16 <= len)
        {
            var wd = BitConverter.ToInt32(buf, i);
            var mask = BitConverter.ToUInt32(buf, i + 4);
            var cookie = BitConverter.ToUInt32(buf, i + 8);
            var nameLen = (int)BitConverter.ToUInt32(buf, i + 12);
            string name = "";
            if (nameLen > 0)
            {
                var end = Array.IndexOf<byte>(buf, 0, i + 16, nameLen);
                var strLen = end < 0 ? nameLen : end - (i + 16);
                name = System.Text.Encoding.UTF8.GetString(buf, i + 16, strLen);
            }
            i += 16 + nameLen;

            if ((mask & IN_Q_OVERFLOW) != 0)
            {
                _channel.Writer.TryWrite(new FileSystemChange(
                    "", FileEventType.Overflow, false, DateTimeOffset.UtcNow, ""));
                continue;
            }

            if ((mask & IN_IGNORED) != 0)
            {
                if (_byWd.TryRemove(wd, out var ignored))
                    _byPath.TryRemove(ignored.Path, out _);
                continue;
            }

            if (!_byWd.TryGetValue(wd, out var watched)) continue;

            var fullPath = nameLen > 0 ? Path.Combine(watched.Path, name) : watched.Path;
            var isDir = (mask & IN_ISDIR) != 0;

            // dispatch to each root that's interested
            foreach (var rootId in watched.RootIds.ToArray())
            {
                if (!_roots.TryGetValue(rootId, out var root)) continue;
                DispatchEvent(root, fullPath, mask, cookie, isDir);
            }

            // dynamic recursion: a new subdir was created -> add watch
            if (isDir && (mask & IN_CREATE) != 0)
            {
                foreach (var rootId in watched.RootIds.ToArray())
                {
                    if (_roots.TryGetValue(rootId, out var root) && root.Recursive)
                        AddDirectoryWatch(fullPath, root.Id);
                }
            }
        }
    }

    private void DispatchEvent(RootInfo root, string fullPath, uint mask, uint cookie, bool isDir)
    {
        var t = DateTimeOffset.UtcNow;
        if ((mask & IN_CREATE) != 0)
            Emit(isDir ? FileEventType.DirCreated : FileEventType.Created);
        if ((mask & IN_MODIFY) != 0 && !isDir)
            Emit(FileEventType.Modified);
        if ((mask & IN_CLOSE_WRITE) != 0 && !isDir)
            Emit(FileEventType.ClosedWrite);
        if ((mask & IN_ATTRIB) != 0)
            Emit(FileEventType.Attrib);
        if ((mask & IN_DELETE) != 0)
            Emit(isDir ? FileEventType.DirDeleted : FileEventType.Deleted);

        if ((mask & IN_MOVED_FROM) != 0)
        {
            var expires = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 200;
            _pendingMoves[cookie] = new PendingMove(fullPath, root.Path, expires, isDir);
            // also emit MovedFrom in case there's no pair
        }
        if ((mask & IN_MOVED_TO) != 0)
        {
            if (_pendingMoves.TryRemove(cookie, out var pending))
            {
                _channel.Writer.TryWrite(new FileSystemChange(
                    fullPath, FileEventType.Renamed, isDir, t, root.Path, OldPath: pending.Path));
            }
            else
            {
                Emit(FileEventType.MovedTo);
            }
        }

        void Emit(FileEventType e)
        {
            if ((root.EventMask & e) == 0) return;
            _channel.Writer.TryWrite(new FileSystemChange(fullPath, e, isDir, t, root.Path));
        }
    }

    private void FlushExpiredMoves()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var kvp in _pendingMoves.ToArray())
        {
            if (kvp.Value.ExpiresUtcMs <= now && _pendingMoves.TryRemove(kvp.Key, out var m))
            {
                _channel.Writer.TryWrite(new FileSystemChange(
                    m.Path, FileEventType.MovedFrom, m.IsDirectory,
                    DateTimeOffset.UtcNow, m.SourceRoot));
            }
        }
    }
}
