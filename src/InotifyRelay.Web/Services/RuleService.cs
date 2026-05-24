using InotifyRelay.Core.Events;
using InotifyRelay.Core.Pipeline;
using InotifyRelay.Data;
using InotifyRelay.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace InotifyRelay.Web.Services;

public sealed class RuleService(AppDbContext db, IConfigChangeNotifier notifier)
{
    public Task<List<RuleEntity>> ListAsync(CancellationToken ct = default)
        => db.Rules.Include(r => r.Sources).Include(r => r.TargetBindings).OrderBy(r => r.Name).ToListAsync(ct);

    public Task<RuleEntity?> GetAsync(Guid id, CancellationToken ct = default)
        => db.Rules.Include(r => r.Sources).Include(r => r.TargetBindings).FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<RuleEntity> CreateAsync(string name, CancellationToken ct = default)
    {
        var r = new RuleEntity { Name = name, EventMask = FileEventType.Created | FileEventType.ClosedWrite | FileEventType.MovedTo | FileEventType.Deleted };
        db.Rules.Add(r);
        await db.SaveChangesAsync(ct);
        notifier.RaiseRulesChanged();
        return r;
    }

    public async Task SaveAsync(RuleEntity rule, CancellationToken ct = default)
    {
        rule.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        notifier.RaiseRulesChanged();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var r = await db.Rules.FindAsync([id], ct);
        if (r is null) return;
        db.Rules.Remove(r);
        await db.SaveChangesAsync(ct);
        notifier.RaiseRulesChanged();
    }

    public async Task SetEnabledAsync(Guid id, bool enabled, CancellationToken ct = default)
    {
        var r = await db.Rules.FindAsync([id], ct);
        if (r is null) return;
        r.Enabled = enabled;
        r.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        notifier.RaiseRulesChanged();
    }
}
