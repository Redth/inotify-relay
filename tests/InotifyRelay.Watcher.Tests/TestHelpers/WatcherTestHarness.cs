using System.Threading.Channels;
using InotifyRelay.Core.Events;
using InotifyRelay.Core.Watching;

namespace InotifyRelay.Watcher.Tests.TestHelpers;

/// <summary>
/// Wires an <see cref="IFileSystemWatcher"/> up to a temp directory and gives
/// tests an easy way to await events matching a predicate.
/// </summary>
public sealed class WatcherTestHarness : IAsyncDisposable
{
    public string TempDir { get; }
    public IFileSystemWatcher Watcher { get; }
    private readonly CancellationTokenSource _cts = new();
    private readonly Channel<FileSystemChange> _collected =
        Channel.CreateUnbounded<FileSystemChange>(new UnboundedChannelOptions { SingleReader = false });
    private readonly Task _pump;

    public WatcherTestHarness(IFileSystemWatcher watcher, string? prefix = null)
    {
        Watcher = watcher;
        TempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
            $"ir-test-{prefix ?? "x"}-{Guid.NewGuid():N}")).FullName;
        _pump = Task.Run(async () =>
        {
            try
            {
                await foreach (var c in watcher.ReadAllAsync(_cts.Token))
                    await _collected.Writer.WriteAsync(c, _cts.Token);
            }
            catch (OperationCanceledException) { }
            _collected.Writer.TryComplete();
        });
    }

    public async Task StartAsync(bool recursive = true, FileEventType mask = FileEventType.All)
    {
        await Watcher.StartAsync(_cts.Token);
        await Watcher.AddOrUpdateWatchAsync(
            new WatchDefinition("root", TempDir, recursive, mask), _cts.Token);
    }

    /// <summary>Wait for any event satisfying <paramref name="predicate"/> within <paramref name="timeout"/>.</summary>
    public async Task<FileSystemChange> AwaitEventAsync(
        Func<FileSystemChange, bool> predicate,
        TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromSeconds(5);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        cts.CancelAfter(timeout.Value);

        try
        {
            await foreach (var c in _collected.Reader.ReadAllAsync(cts.Token))
            {
                if (predicate(c)) return c;
            }
        }
        catch (OperationCanceledException)
        {
            // fall through to throw a nicer message
        }
        throw new TimeoutException(
            $"Timed out after {timeout.Value.TotalMilliseconds:F0}ms waiting for matching event.");
    }

    /// <summary>Collect every event arriving within a fixed window. Useful for negative assertions.</summary>
    public async Task<List<FileSystemChange>> CollectWithinAsync(TimeSpan window)
    {
        var bucket = new List<FileSystemChange>();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        cts.CancelAfter(window);
        try
        {
            await foreach (var c in _collected.Reader.ReadAllAsync(cts.Token))
                bucket.Add(c);
        }
        catch (OperationCanceledException) { }
        return bucket;
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        await Watcher.StopAsync(CancellationToken.None);
        try { await _pump; } catch { }
        try { Directory.Delete(TempDir, recursive: true); } catch { }
        if (Watcher is IAsyncDisposable ad) await ad.DisposeAsync();
        else if (Watcher is IDisposable d) d.Dispose();
        _cts.Dispose();
    }
}
