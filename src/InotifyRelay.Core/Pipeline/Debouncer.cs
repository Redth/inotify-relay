using System.Collections.Concurrent;

namespace InotifyRelay.Core.Pipeline;

/// <summary>
/// Collapses repeated occurrences of the same key within a window. <c>Submit</c> returns
/// true only when the key has been quiet for at least <paramref name="window"/>.
/// </summary>
public sealed class Debouncer<TKey> where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, long> _last = new();
    private readonly TimeProvider _time;

    public Debouncer(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
    }

    public bool ShouldEmit(TKey key, TimeSpan window)
    {
        var now = _time.GetUtcNow().ToUnixTimeMilliseconds();
        if (_last.TryGetValue(key, out var last) && (now - last) < window.TotalMilliseconds)
        {
            _last[key] = now;
            return false;
        }
        _last[key] = now;
        return true;
    }

    public void Cleanup(TimeSpan keepFor)
    {
        var cutoff = _time.GetUtcNow().ToUnixTimeMilliseconds() - (long)keepFor.TotalMilliseconds;
        foreach (var kvp in _last)
            if (kvp.Value < cutoff)
                _last.TryRemove(kvp.Key, out _);
    }
}
