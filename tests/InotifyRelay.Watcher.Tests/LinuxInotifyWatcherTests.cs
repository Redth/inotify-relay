using InotifyRelay.Core.Events;
using InotifyRelay.Watcher.Linux;
using InotifyRelay.Watcher.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace InotifyRelay.Watcher.Tests;

/// <summary>
/// End-to-end tests against a real Linux inotify backend. Each test owns a
/// fresh tmpdir and a fresh watcher instance to isolate kernel state. Events
/// are awaited with generous timeouts because inotify delivery isn't bounded.
/// </summary>
public class LinuxInotifyWatcherTests
{
    private static async Task<WatcherTestHarness> NewAsync(FileEventType mask = FileEventType.All, bool recursive = true)
    {
        var h = new WatcherTestHarness(new LinuxInotifyWatcher(NullLogger<LinuxInotifyWatcher>.Instance));
        await h.StartAsync(recursive, mask);
        return h;
    }

    [LinuxFact]
    public async Task File_create_emits_Created()
    {
        await using var h = await NewAsync();
        var path = Path.Combine(h.TempDir, "new.txt");
        File.WriteAllText(path, "");
        var ev = await h.AwaitEventAsync(c => c.EventType == FileEventType.Created && c.Path == path);
        Assert.False(ev.IsDirectory);
    }

    [LinuxFact]
    public async Task File_write_emits_Modified_and_ClosedWrite()
    {
        await using var h = await NewAsync();
        var path = Path.Combine(h.TempDir, "x.txt");
        File.WriteAllText(path, "hello world");

        var modified = await h.AwaitEventAsync(c => c.EventType == FileEventType.Modified && c.Path == path);
        var closed   = await h.AwaitEventAsync(c => c.EventType == FileEventType.ClosedWrite && c.Path == path);
        Assert.NotNull(modified);
        Assert.NotNull(closed);
    }

    [LinuxFact]
    public async Task File_delete_emits_Deleted()
    {
        await using var h = await NewAsync();
        var path = Path.Combine(h.TempDir, "doomed.txt");
        File.WriteAllText(path, "bye");
        File.Delete(path);
        var ev = await h.AwaitEventAsync(c => c.EventType == FileEventType.Deleted && c.Path == path);
        Assert.False(ev.IsDirectory);
    }

    [LinuxFact]
    public async Task Rename_within_watched_dir_emits_Renamed_with_OldPath()
    {
        await using var h = await NewAsync();
        var src = Path.Combine(h.TempDir, "before.txt");
        var dst = Path.Combine(h.TempDir, "after.txt");
        File.WriteAllText(src, "x");
        File.Move(src, dst);

        var ev = await h.AwaitEventAsync(c => c.EventType == FileEventType.Renamed && c.Path == dst);
        Assert.Equal(src, ev.OldPath);
    }

    [LinuxFact]
    public async Task Move_in_from_outside_emits_MovedTo_standalone()
    {
        await using var h = await NewAsync();
        // Create file outside the watched tree, then move it in. Because the
        // matching IN_MOVED_FROM is never seen, the watcher emits a standalone
        // MovedTo.
        var outside = Path.Combine(Path.GetTempPath(), $"ir-outside-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(outside, "x");
            var dst = Path.Combine(h.TempDir, "incoming.txt");
            File.Move(outside, dst);
            var ev = await h.AwaitEventAsync(c => c.EventType == FileEventType.MovedTo && c.Path == dst);
            Assert.NotNull(ev);
        }
        finally
        {
            try { File.Delete(outside); } catch { }
        }
    }

    [LinuxFact]
    public async Task Subdirectory_create_emits_DirCreated()
    {
        await using var h = await NewAsync();
        var dir = Path.Combine(h.TempDir, "sub");
        Directory.CreateDirectory(dir);
        var ev = await h.AwaitEventAsync(c => c.EventType == FileEventType.DirCreated && c.Path == dir);
        Assert.True(ev.IsDirectory);
    }

    [LinuxFact]
    public async Task Files_in_newly_created_subdir_are_watched_when_recursive()
    {
        await using var h = await NewAsync();
        var sub = Path.Combine(h.TempDir, "auto");
        Directory.CreateDirectory(sub);
        // small gap so the dynamic add_watch settles before we touch a file
        await Task.Delay(50);
        var file = Path.Combine(sub, "f.txt");
        File.WriteAllText(file, "hi");
        var ev = await h.AwaitEventAsync(c => c.EventType == FileEventType.Created && c.Path == file);
        Assert.NotNull(ev);
    }

    [LinuxFact]
    public async Task EventMask_filters_out_unwanted_event_types()
    {
        // ClosedWrite only — Modified for the same file should be filtered.
        await using var h = await NewAsync(mask: FileEventType.ClosedWrite);
        var path = Path.Combine(h.TempDir, "x.txt");
        File.WriteAllText(path, "data");

        var closed = await h.AwaitEventAsync(c => c.EventType == FileEventType.ClosedWrite && c.Path == path);
        Assert.NotNull(closed);

        // Now collect events for a short window; no Modified should arrive.
        File.AppendAllText(path, " more");
        var bucket = await h.CollectWithinAsync(TimeSpan.FromMilliseconds(300));
        Assert.DoesNotContain(bucket, c => c.EventType == FileEventType.Modified);
    }

    [LinuxFact]
    public async Task RemoveWatchAsync_stops_further_events()
    {
        await using var h = await NewAsync();
        var first = Path.Combine(h.TempDir, "first.txt");
        File.WriteAllText(first, "x");
        await h.AwaitEventAsync(c => c.Path == first && c.EventType == FileEventType.Created);

        // Drain any in-flight events for the first file — a single File.WriteAllText
        // produces Created + Modified + ClosedWrite, but we've only consumed Created.
        await h.CollectWithinAsync(TimeSpan.FromMilliseconds(100));

        await h.Watcher.RemoveWatchAsync("root", CancellationToken.None);
        await Task.Delay(50);
        var second = Path.Combine(h.TempDir, "second.txt");
        File.WriteAllText(second, "y");

        var bucket = await h.CollectWithinAsync(TimeSpan.FromMilliseconds(300));
        // No events for the second file should have arrived — the watch is gone.
        Assert.DoesNotContain(bucket, c => c.Path == second);
    }

    [LinuxFact]
    public async Task Single_inotify_instance_serves_many_watched_directories()
    {
        // The whole reason this project exists: don't bleed inotify instances.
        // Measure DELTA — CI runners may have unrelated inotify fds (test host,
        // .NET runtime, etc.), so the absolute count varies. The property we
        // care about is that OUR watcher adds exactly one regardless of how
        // many directories it ends up watching.
        var before = CountInotifyFds();

        await using var h = await NewAsync();
        for (var i = 0; i < 50; i++)
            Directory.CreateDirectory(Path.Combine(h.TempDir, $"sub-{i:D3}"));
        await Task.Delay(200);

        var after = CountInotifyFds();
        Assert.Equal(1, after - before);
    }

    private static int CountInotifyFds()
    {
        var pid = Environment.ProcessId;
        var count = 0;
        foreach (var fd in Directory.EnumerateFiles($"/proc/{pid}/fd"))
        {
            try
            {
                var link = new FileInfo(fd).LinkTarget;
                if (link is not null && link.Contains("inotify", StringComparison.Ordinal))
                    count++;
            }
            catch { /* fd may have closed mid-walk */ }
        }
        return count;
    }
}
