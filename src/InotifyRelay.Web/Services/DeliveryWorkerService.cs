using InotifyRelay.Core.Pipeline;
using InotifyRelay.Core.Providers;
using InotifyRelay.Data;
using InotifyRelay.Data.Entities;
using Microsoft.Extensions.Hosting;

namespace InotifyRelay.Web.Services;

public sealed class DeliveryWorkerService(
    DeliveryQueue queue,
    IServiceScopeFactory scopes,
    ProviderCatalog providers,
    ILogger<DeliveryWorkerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // simple single-worker pull; scale by spawning more workers in future
        try
        {
            await foreach (var work in queue.Reader.ReadAllAsync(stoppingToken))
                _ = Task.Run(() => DeliverAsync(work, stoppingToken), stoppingToken);
        }
        catch (OperationCanceledException) { }
    }

    private async Task DeliverAsync(DeliveryWork work, CancellationToken ct)
    {
        try
        {
            if (work.Binding.DelayMs > 0)
                await Task.Delay(work.Binding.DelayMs, ct);

            using var scope = scopes.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IConfigStore>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var target = await store.GetTargetAsync(work.Binding.TargetId, ct);
            if (target is null) return;
            var provider = providers.Get(target.ProviderType);
            if (provider is null)
            {
                logger.LogWarning("No provider registered for type {Type}", target.ProviderType);
                return;
            }

            var ctx = new RelayContext(work.Change, work.Rule.Name, target.Name,
                target.ProviderConfigJson, work.Binding.TemplateOverrideJson);

            var attempts = 0;
            var delay = target.RetryInitialBackoffMs;
            RelayResult? final = null;
            while (attempts < Math.Max(1, target.RetryMaxAttempts))
            {
                attempts++;
                final = await provider.SendAsync(ctx, ct);
                if (final.Success) break;
                if (attempts >= target.RetryMaxAttempts) break;
                await Task.Delay(Math.Min(delay, target.RetryMaxBackoffMs), ct);
                delay = (int)Math.Min(delay * target.RetryBackoffMultiplier, target.RetryMaxBackoffMs);
            }

            if (final is not null)
            {
                db.DeliveryLogs.Add(new DeliveryLogEntity
                {
                    EventLogId = work.EventLogId,
                    TargetId = target.Id,
                    TargetName = target.Name,
                    ProviderType = target.ProviderType,
                    Success = final.Success,
                    StatusCode = final.StatusCode,
                    Attempts = attempts,
                    ElapsedMs = (int)final.Elapsed.TotalMilliseconds,
                    Error = final.Error,
                    ResponseSnippet = final.ResponseSnippet,
                });
                await db.SaveChangesAsync(ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "Delivery failed for binding {BindingId}", work.Binding.Id);
        }
    }
}
