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
    private readonly Dictionary<string, bool> _activeWatches = new(StringComparer.Ordinal);
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

            // Plan once across ALL rules — overlapping recursive sources collapse to
            // their common ancestor, non-recursive sources sitting under a recursive
            // ancestor are dropped (the ancestor's watch already delivers their events).
            // Per-rule attribution happens at dispatch time in RuleMatcher.
            var plan = WatchPlanner.Plan(rules);
            // key the watch by its absolute path — natural and stable across re-sync
            var desired = plan.ToDictionary(p => p.Path, p => p.Recursive, StringComparer.Ordinal);

            foreach (var key in _activeWatches.Keys.Except(desired.Keys).ToArray())
            {
                await watcher.RemoveWatchAsync(key, ct);
                _activeWatches.Remove(key);
            }
            foreach (var (path, recursive) in desired)
            {
                // We listen for every event type and let RuleMatcher do the per-rule
                // mask filtering — a single watch may serve rules with different masks.
                await watcher.AddOrUpdateWatchAsync(
                    new WatchDefinition(path, path, recursive, FileEventType.All), ct);
                _activeWatches[path] = recursive;
            }
            logger.LogInformation(
                "Watcher sync complete. {Watches} consolidated watches from {Sources} rule sources.",
                _activeWatches.Count,
                rules.Where(r => r.Enabled).Sum(r => r.Sources.Count));
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
