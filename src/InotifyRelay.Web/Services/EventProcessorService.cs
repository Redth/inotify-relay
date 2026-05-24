using InotifyRelay.Core.Pipeline;
using InotifyRelay.Core.Watching;
using InotifyRelay.Data;
using InotifyRelay.Data.Entities;
using Microsoft.Extensions.Hosting;

namespace InotifyRelay.Web.Services;

public sealed class EventProcessorService(
    IFileSystemWatcher watcher,
    IServiceScopeFactory scopes,
    DeliveryQueue queue,
    ILogger<EventProcessorService> logger) : BackgroundService
{
    private readonly Debouncer<string> _debouncer = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var change in watcher.ReadAllAsync(stoppingToken))
            {
                using var scope = scopes.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<IConfigStore>();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var rules = await store.GetRulesAsync(stoppingToken);
                foreach (var match in RuleMatcher.Match(change, rules))
                {
                    var dbKey = $"{match.Rule.Id}|{change.Path}|{change.EventType}";
                    if (match.Rule.DebounceMs > 0 && !_debouncer.ShouldEmit(dbKey, TimeSpan.FromMilliseconds(match.Rule.DebounceMs)))
                        continue;

                    var log = new EventLogEntity
                    {
                        RuleId = match.Rule.Id,
                        RuleName = match.Rule.Name,
                        Path = change.Path,
                        OldPath = change.OldPath,
                        EventType = change.EventType.ToString(),
                        IsDirectory = change.IsDirectory,
                        SourceRoot = change.SourceRoot,
                        Timestamp = change.Timestamp.UtcDateTime,
                    };
                    db.EventLogs.Add(log);
                    await db.SaveChangesAsync(stoppingToken);

                    foreach (var binding in match.Rule.TargetBindings)
                    {
                        if (!binding.Enabled) continue;
                        await queue.Writer.WriteAsync(new DeliveryWork(log.Id, change, match.Rule, binding), stoppingToken);
                    }
                }
                if ((DateTime.UtcNow.Second & 31) == 0)
                    _debouncer.Cleanup(TimeSpan.FromMinutes(5));
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex)
        {
            logger.LogError(ex, "Event processor crashed");
        }
    }
}
