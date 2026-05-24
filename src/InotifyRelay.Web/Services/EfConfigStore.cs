using InotifyRelay.Core.Pipeline;
using InotifyRelay.Data;
using Microsoft.EntityFrameworkCore;

namespace InotifyRelay.Web.Services;

public sealed class EfConfigStore(AppDbContext db) : IConfigStore
{
    public async Task<IReadOnlyList<RuleSnapshot>> GetRulesAsync(CancellationToken ct)
    {
        var rules = await db.Rules
            .Include(r => r.Sources)
            .Include(r => r.TargetBindings)
            .AsNoTracking()
            .ToListAsync(ct);

        return rules.Select(r => new RuleSnapshot(
            r.Id, r.Name, r.Enabled, r.EventMask, r.DebounceMs, r.StabilizationMs,
            r.Sources.Select(s => new SourceSnapshot(s.Path, s.GlobPattern, s.Recursive)).ToList(),
            r.TargetBindings
                .OrderBy(b => b.Order)
                .Select(b => new TargetBindingSnapshot(b.Id, b.TargetId, b.Enabled, b.DelayMs, b.TemplateOverrideJson, b.Order))
                .ToList()
        )).ToList();
    }

    public async Task<TargetSnapshot?> GetTargetAsync(Guid id, CancellationToken ct)
    {
        var t = await db.Targets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return t is null ? null : Map(t);
    }

    public async Task<IReadOnlyList<TargetSnapshot>> GetTargetsAsync(CancellationToken ct)
    {
        var t = await db.Targets.AsNoTracking().ToListAsync(ct);
        return t.Select(Map).ToList();
    }

    private static TargetSnapshot Map(Data.Entities.TargetEntity t) => new(
        t.Id, t.Name, t.ProviderType, t.ProviderConfigJson, t.DefaultTemplateJson,
        t.RetryMaxAttempts, t.RetryInitialBackoffMs, t.RetryBackoffMultiplier, t.RetryMaxBackoffMs,
        t.CoalesceMs);
}
