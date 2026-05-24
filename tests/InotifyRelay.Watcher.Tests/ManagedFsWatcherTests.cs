using InotifyRelay.Core.Events;
using InotifyRelay.Watcher.Managed;
using InotifyRelay.Watcher.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace InotifyRelay.Watcher.Tests;

/// <summary>
/// Tests for the managed <see cref="System.IO.FileSystemWatcher"/> fallback used
/// on Windows / macOS. Skipped on Linux where we exercise the inotify path.
/// </summary>
public class ManagedFsWatcherTests
{
    private static async Task<WatcherTestHarness> NewAsync()
    {
        var h = new WatcherTestHarness(new ManagedFsWatcher(NullLogger<ManagedFsWatcher>.Instance), prefix: "managed");
        await h.StartAsync(recursive: true, mask: FileEventType.All);
        return h;
    }

    [NonLinuxFact]
    public async Task File_create_emits_Created()
    {
        await using var h = await NewAsync();
        var path = Path.Combine(h.TempDir, "new.txt");
        File.WriteAllText(path, "");
        var ev = await h.AwaitEventAsync(c => c.EventType == FileEventType.Created && c.Path == path,
            TimeSpan.FromSeconds(8));
        Assert.False(ev.IsDirectory);
    }

    [NonLinuxFact]
    public async Task File_delete_emits_Deleted()
    {
        await using var h = await NewAsync();
        var path = Path.Combine(h.TempDir, "doomed.txt");
        File.WriteAllText(path, "x");
        await h.AwaitEventAsync(c => c.EventType == FileEventType.Created && c.Path == path,
            TimeSpan.FromSeconds(8));
        File.Delete(path);
        await h.AwaitEventAsync(c => c.EventType == FileEventType.Deleted && c.Path == path,
            TimeSpan.FromSeconds(8));
    }

    [NonLinuxFact]
    public async Task File_rename_emits_Renamed_with_OldPath()
    {
        await using var h = await NewAsync();
        var src = Path.Combine(h.TempDir, "before.txt");
        var dst = Path.Combine(h.TempDir, "after.txt");
        File.WriteAllText(src, "x");
        await h.AwaitEventAsync(c => c.EventType == FileEventType.Created && c.Path == src,
            TimeSpan.FromSeconds(8));
        File.Move(src, dst);
        var ev = await h.AwaitEventAsync(c => c.EventType == FileEventType.Renamed && c.Path == dst,
            TimeSpan.FromSeconds(8));
        Assert.Equal(src, ev.OldPath);
    }
}
