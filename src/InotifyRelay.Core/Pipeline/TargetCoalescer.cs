using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace InotifyRelay.Core.Pipeline;

/// <summary>
/// Per-target sliding-window coalescer. Buffers <see cref="DeliveryWork"/> for each
/// target; once a target goes quiet for its configured <c>CoalesceMs</c>, the buffer
/// is reduced via <see cref="PathSubsumption"/> and each survivor is delivered via
/// the injected sink.
/// </summary>
public sealed class TargetCoalescer : IAsyncDisposable
{
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<Guid, Batch> _batches = new();

    public TargetCoalescer(ILogger<TargetCoalescer>? logger = null)
        => _logger = logger ?? NullLogger<TargetCoalescer>.Instance;

    public delegate Task DeliverFn(DeliveryWork work, CancellationToken ct);

    /// <summary>
    /// Hand <paramref name="work"/> to the coalescer. When <paramref name="coalesceMs"/> is
    /// non-positive, the work is dispatched immediately.
    /// </summary>
    public void Enqueue(DeliveryWork work, int coalesceMs, DeliverFn deliver, CancellationToken ct)
    {
        if (coalesceMs <= 0)
        {
            _ = SafeDeliverAsync(deliver, work, ct);
            return;
        }
        var batch = _batches.GetOrAdd(work.Binding.TargetId, _ => new Batch(this, deliver));
        batch.Add(work, coalesceMs, ct);
    }

    private async Task SafeDeliverAsync(DeliverFn deliver, DeliveryWork work, CancellationToken ct)
    {
        try { await deliver(work, ct); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _logger.LogError(ex, "Coalescer delivery threw"); }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var b in _batches.Values) await b.FlushNowAsync();
        _batches.Clear();
    }

    private sealed class Batch
    {
        private readonly TargetCoalescer _owner;
        private readonly DeliverFn _deliver;
        private readonly object _gate = new();
        private readonly List<DeliveryWork> _items = new();
        private CancellationTokenSource? _timerCts;
        private CancellationToken _ct;

        public Batch(TargetCoalescer owner, DeliverFn deliver)
        {
            _owner = owner;
            _deliver = deliver;
        }

        public void Add(DeliveryWork work, int coalesceMs, CancellationToken ct)
        {
            lock (_gate)
            {
                _items.Add(work);
                _ct = ct;
                _timerCts?.Cancel();
                _timerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var token = _timerCts.Token;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(coalesceMs, token);
                        await FlushNowAsync();
                    }
                    catch (OperationCanceledException) { /* superseded by a newer add */ }
                });
            }
        }

        public async Task FlushNowAsync()
        {
            List<DeliveryWork> snapshot;
            lock (_gate)
            {
                if (_items.Count == 0) return;
                snapshot = new List<DeliveryWork>(_items);
                _items.Clear();
            }
            if (snapshot.Count > 0)
                _owner._batches.TryRemove(snapshot[0].Binding.TargetId, out _);

            var survivors = PathSubsumption.Subsume(snapshot, w => w.Change.Path);
            _owner._logger.LogInformation(
                "Coalesced {Before} → {After} delivery items for target {TargetId}",
                snapshot.Count, survivors.Count, snapshot[0].Binding.TargetId);

            foreach (var item in survivors)
            {
                try { await _deliver(item, _ct); }
                catch (OperationCanceledException) { }
                catch (Exception ex) { _owner._logger.LogError(ex, "Delivery threw inside coalescer"); }
            }
        }
    }
}
