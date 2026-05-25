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

                    // Rewrite SourceRoot from the consolidated watch root to the
                    // matched rule's source path. Template variables like
                    // {relativePath} stay relative to the rule, not the watch.
                    var ruleChange = change with { SourceRoot = match.Source.Path };

                    var log = new EventLogEntity
                    {
                        RuleId = match.Rule.Id,
                        RuleName = match.Rule.Name,
                        Path = ruleChange.Path,
                        OldPath = ruleChange.OldPath,
                        EventType = ruleChange.EventType.ToString(),
                        IsDirectory = ruleChange.IsDirectory,
                        SourceRoot = ruleChange.SourceRoot,
                        Timestamp = ruleChange.Timestamp.UtcDateTime,
                    };
                    db.EventLogs.Add(log);
                    await db.SaveChangesAsync(stoppingToken);

                    foreach (var binding in match.Rule.TargetBindings)
                    {
                        if (!binding.Enabled) continue;
                        await queue.Writer.WriteAsync(new DeliveryWork(log.Id, ruleChange, match.Rule, binding), stoppingToken);
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
