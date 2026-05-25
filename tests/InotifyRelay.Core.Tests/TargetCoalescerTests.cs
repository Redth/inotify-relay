using InotifyRelay.Core.Events;
using InotifyRelay.Core.Pipeline;

namespace InotifyRelay.Core.Tests;

public class TargetCoalescerTests
{
    private static DeliveryWork Work(Guid targetId, string path, FileEventType ev = FileEventType.ClosedWrite)
    {
        var change = new FileSystemChange(path, ev, false, DateTimeOffset.UtcNow, "/watch");
        var binding = new TargetBindingSnapshot(Guid.NewGuid(), targetId, true, 0, null, 0);
        var rule = new RuleSnapshot(Guid.NewGuid(), "r", true, FileEventType.All, 0, 0,
            Array.Empty<SourceSnapshot>(), new[] { binding });
        return new DeliveryWork(EventLogId: null, change, rule, binding);
    }

    [Fact]
    public async Task Passes_through_when_coalesce_ms_is_zero()
    {
        var c = new TargetCoalescer();
        var delivered = new List<DeliveryWork>();
        var done = new TaskCompletionSource();

        c.Enqueue(Work(Guid.NewGuid(), "/x/a"), 0, (w, _) =>
        {
            delivered.Add(w);
            done.SetResult();
            return Task.CompletedTask;
        }, CancellationToken.None);

        await done.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Single(delivered);
    }

    [Fact]
    public async Task Buffers_then_flushes_after_window()
    {
        var c = new TargetCoalescer();
        var delivered = new List<string>();
        var done = new TaskCompletionSource();
        var targetId = Guid.NewGuid();

        TargetCoalescer.DeliverFn deliver = (w, _) =>
        {
            lock (delivered) delivered.Add(w.Change.Path);
            return Task.CompletedTask;
        };

        c.Enqueue(Work(targetId, "/movies/a.mkv"), 150, deliver, CancellationToken.None);
        c.Enqueue(Work(targetId, "/movies/b.mkv"), 150, deliver, CancellationToken.None);
        c.Enqueue(Work(targetId, "/movies/c.mkv"), 150, deliver, CancellationToken.None);

        // Wait a hair longer than the window to be safe.
        await Task.Delay(400);

        // All three independent paths survive (no ancestor relation).
        Assert.Equal(3, delivered.Count);
        Assert.Contains("/movies/a.mkv", delivered);
        Assert.Contains("/movies/b.mkv", delivered);
        Assert.Contains("/movies/c.mkv", delivered);
    }

    [Fact]
    public async Task Path_subsumption_keeps_only_ancestors()
    {
        var c = new TargetCoalescer();
        var delivered = new List<string>();
        var targetId = Guid.NewGuid();

        TargetCoalescer.DeliverFn deliver = (w, _) =>
        {
            lock (delivered) delivered.Add(w.Change.Path);
            return Task.CompletedTask;
        };

        // Three descendants + the parent. Only the parent should survive.
        c.Enqueue(Work(targetId, "/movies/Inception/a.mkv"), 150, deliver, CancellationToken.None);
        c.Enqueue(Work(targetId, "/movies/Inception/b.mkv"), 150, deliver, CancellationToken.None);
        c.Enqueue(Work(targetId, "/movies/Inception/c.mkv"), 150, deliver, CancellationToken.None);
        c.Enqueue(Work(targetId, "/movies/Inception"),       150, deliver, CancellationToken.None);

        await Task.Delay(400);
        Assert.Single(delivered);
        Assert.Equal("/movies/Inception", delivered[0]);
    }

    [Fact]
    public async Task Multiple_targets_do_not_share_a_window()
    {
        var c = new TargetCoalescer();
        var delivered = new List<(Guid, string)>();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        TargetCoalescer.DeliverFn deliver = (w, _) =>
        {
            lock (delivered) delivered.Add((w.Binding.TargetId, w.Change.Path));
            return Task.CompletedTask;
        };

        // Two distinct targets, identical paths. Both batches should fire.
        c.Enqueue(Work(a, "/x/foo"), 150, deliver, CancellationToken.None);
        c.Enqueue(Work(b, "/x/foo"), 150, deliver, CancellationToken.None);
        await Task.Delay(400);

        Assert.Equal(2, delivered.Count);
        Assert.Contains((a, "/x/foo"), delivered);
        Assert.Contains((b, "/x/foo"), delivered);
    }

    [Fact]
    public async Task Adding_more_work_resets_the_window()
    {
        var c = new TargetCoalescer();
        var delivered = new List<string>();
        var target = Guid.NewGuid();

        TargetCoalescer.DeliverFn deliver = (w, _) =>
        {
            lock (delivered) delivered.Add(w.Change.Path);
            return Task.CompletedTask;
        };

        // 500ms window with 200ms between enqueues. Each enqueue must arrive before
        // the previous window elapses so the timer resets; final wait clears the
        // window. Generous tolerances so a noisy CI runner doesn't trip us up.
        const int WindowMs = 500;
        c.Enqueue(Work(target, "/p/1"), WindowMs, deliver, CancellationToken.None);
        await Task.Delay(200);
        c.Enqueue(Work(target, "/p/2"), WindowMs, deliver, CancellationToken.None);
        await Task.Delay(200);
        c.Enqueue(Work(target, "/p/3"), WindowMs, deliver, CancellationToken.None);

        // At ~t=400ms — if window hadn't reset, the t=0 enqueue would have
        // flushed already (at t=500). Nothing should have been delivered yet.
        Assert.Empty(delivered);

        // Wait through the post-reset window plus headroom.
        await Task.Delay(WindowMs + 300);
        Assert.Equal(3, delivered.Count);
    }

    [Fact]
    public async Task Empty_path_full_scan_subsumes_all_other_paths()
    {
        var c = new TargetCoalescer();
        var delivered = new List<string>();
        var target = Guid.NewGuid();

        TargetCoalescer.DeliverFn deliver = (w, _) =>
        {
            lock (delivered) delivered.Add(w.Change.Path);
            return Task.CompletedTask;
        };

        c.Enqueue(Work(target, "/movies/a.mkv"), 150, deliver, CancellationToken.None);
        c.Enqueue(Work(target, ""),              150, deliver, CancellationToken.None);
        c.Enqueue(Work(target, "/tv/b.mkv"),     150, deliver, CancellationToken.None);

        await Task.Delay(400);
        Assert.Single(delivered);
        Assert.Equal("", delivered[0]);
    }

    [Fact]
    public async Task Disposing_flushes_pending_work_synchronously()
    {
        var c = new TargetCoalescer();
        var delivered = new List<string>();
        var target = Guid.NewGuid();

        TargetCoalescer.DeliverFn deliver = (w, _) =>
        {
            lock (delivered) delivered.Add(w.Change.Path);
            return Task.CompletedTask;
        };

        c.Enqueue(Work(target, "/p/a"), 5000, deliver, CancellationToken.None); // long window
        c.Enqueue(Work(target, "/p/b"), 5000, deliver, CancellationToken.None);
        Assert.Empty(delivered);

        await c.DisposeAsync();
        Assert.Equal(2, delivered.Count);
    }

}
