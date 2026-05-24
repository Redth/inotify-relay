using InotifyRelay.Core.Pipeline;

namespace InotifyRelay.Core.Tests;

public class DebouncerTests
{
    [Fact]
    public void Emits_once_within_window_then_again_after_quiet_period()
    {
        var time = new FakeTime();
        var d = new Debouncer<string>(time);

        Assert.True(d.ShouldEmit("k", TimeSpan.FromMilliseconds(1000)));
        time.Advance(TimeSpan.FromMilliseconds(500));
        Assert.False(d.ShouldEmit("k", TimeSpan.FromMilliseconds(1000)));
        time.Advance(TimeSpan.FromMilliseconds(1500));
        Assert.True(d.ShouldEmit("k", TimeSpan.FromMilliseconds(1000)));
    }

    [Fact]
    public void Different_keys_do_not_interfere()
    {
        var d = new Debouncer<string>();
        Assert.True(d.ShouldEmit("a", TimeSpan.FromSeconds(1)));
        Assert.True(d.ShouldEmit("b", TimeSpan.FromSeconds(1)));
    }

    private sealed class FakeTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan t) => _now += t;
    }
}
