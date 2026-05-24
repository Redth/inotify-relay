using InotifyRelay.Core.Events;
using InotifyRelay.Core.Pipeline;
using InotifyRelay.Core.Watching;
using Microsoft.Extensions.Hosting;

namespace InotifyRelay.Web.Services;

/// <summary>Synchronises rule definitions into watch descriptors on the active watcher.</summary>
public sealed class WatcherSyncService(
    IFileSystemWatcher watcher,
    IServiceScopeFactory scopes,
    IConfigChangeNotifier notifier,
    ILogger<WatcherSyncService> logger) : BackgroundService
{
    private readonly Dictionary<string, FileEventType> _activeWatches = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await watcher.StartAsync(stoppingToken);
        notifier.RulesChanged += OnRulesChanged;
        await SyncAsync(stoppingToken);

        // periodic re-sync to catch external DB edits or directories that come online late
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(2));
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
            await SyncAsync(stoppingToken);

        notifier.RulesChanged -= OnRulesChanged;
        await watcher.StopAsync(CancellationToken.None);
    }

    private void OnRulesChanged() => _ = Task.Run(() => SyncAsync(CancellationToken.None));

    private async Task SyncAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            using var scope = scopes.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IConfigStore>();
            var rules = await store.GetRulesAsync(ct);

            // desired: rule.Id+":"+sourceIndex -> (path, recursive, mask)
            var desired = new Dictionary<string, (string Path, bool Recursive, FileEventType Mask)>();
            foreach (var r in rules)
            {
                if (!r.Enabled) continue;
                for (var i = 0; i < r.Sources.Count; i++)
                {
                    var key = $"{r.Id}:{i}";
                    desired[key] = (r.Sources[i].Path, r.Sources[i].Recursive, r.EventMask);
                }
            }

            // remove gone
            foreach (var key in _activeWatches.Keys.Except(desired.Keys).ToArray())
            {
                await watcher.RemoveWatchAsync(key, ct);
                _activeWatches.Remove(key);
            }
            // add/update
            foreach (var (key, def) in desired)
            {
                await watcher.AddOrUpdateWatchAsync(new WatchDefinition(key, def.Path, def.Recursive, def.Mask), ct);
                _activeWatches[key] = def.Mask;
            }
            logger.LogInformation("Watcher sync complete. Active watches: {Count}", _activeWatches.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Watcher sync failed");
        }
        finally
        {
            _gate.Release();
        }
    }
}
