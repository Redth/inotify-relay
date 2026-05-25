using InotifyRelay.Core.Events;
using InotifyRelay.Core.Pipeline;
using InotifyRelay.Data;
using InotifyRelay.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace InotifyRelay.Web.Services;

public sealed class RuleService(AppDbContext db, IConfigChangeNotifier notifier)
{
    public Task<List<RuleEntity>> ListAsync(CancellationToken ct = default)
        => db.Rules.AsNoTracking()
            .Include(r => r.Sources)
            .Include(r => r.TargetBindings)
            .OrderBy(r => r.Name)
            .ToListAsync(ct);

    /// <summary>
    /// Returns a detached entity graph. The editor mutates this freely; on save,
    /// <see cref="SaveAsync"/> diffs it against the actual tracked rule. We do NOT
    /// return the tracked instance because Blazor Server's long-lived per-circuit
    /// DbContext + EF's nav-collection auto-tracking mis-classifies brand-new
    /// children with non-default Guid PKs as Modified instead of Added, causing
    /// UPDATE-against-nothing on save.
    /// </summary>
    public Task<RuleEntity?> GetAsync(Guid id, CancellationToken ct = default)
        => db.Rules.AsNoTracking()
            .Include(r => r.Sources)
            .Include(r => r.TargetBindings)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<RuleEntity> CreateAsync(string name, CancellationToken ct = default)
    {
        if (await db.Rules.AnyAsync(x => x.Name == name, ct))
            throw new DuplicateNameException($"A rule named '{name}' already exists.");
        var r = new RuleEntity { Name = name, EventMask = FileEventType.Created | FileEventType.ClosedWrite | FileEventType.MovedTo | FileEventType.Deleted };
        db.Rules.Add(r);
        await db.SaveChangesAsync(ct);
        notifier.RaiseRulesChanged();
        return r;
    }

    public async Task SaveAsync(RuleEntity input, CancellationToken ct = default)
    {
        if (await db.Rules.AnyAsync(x => x.Name == input.Name && x.Id != input.Id, ct))
            throw new DuplicateNameException($"A rule named '{input.Name}' already exists.");

        var tracked = await db.Rules
            .Include(r => r.Sources)
            .Include(r => r.TargetBindings)
            .FirstOrDefaultAsync(r => r.Id == input.Id, ct);

        if (tracked is null)
            throw new InvalidOperationException($"Rule {input.Id} not found");

        // Scalar fields
        tracked.Name            = input.Name;
        tracked.Description     = input.Description;
        tracked.Enabled         = input.Enabled;
        tracked.EventMask       = input.EventMask;
        tracked.DebounceMs      = input.DebounceMs;
        tracked.StabilizationMs = input.StabilizationMs;
        tracked.UpdatedAt       = DateTimeOffset.UtcNow;

        DiffChildren(
            tracked.Sources, input.Sources,
            keep: s => s.Id,
            apply: (dst, src) => { dst.Path = src.Path; dst.GlobPattern = src.GlobPattern; dst.Recursive = src.Recursive; },
            adopt: s => { s.RuleId = tracked.Id; db.Sources.Add(s); },
            evict: s => db.Sources.Remove(s));

        DiffChildren(
            tracked.TargetBindings, input.TargetBindings,
            keep: b => b.Id,
            apply: (dst, src) => { dst.TargetId = src.TargetId; dst.Enabled = src.Enabled; dst.DelayMs = src.DelayMs; dst.TemplateOverrideJson = src.TemplateOverrideJson; dst.Order = src.Order; },
            adopt: b => { b.RuleId = tracked.Id; db.TargetBindings.Add(b); },
            evict: b => db.TargetBindings.Remove(b));

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

    /// <summary>
    /// Three-way diff of a child collection by Id: items in both sides get
    /// <paramref name="apply"/>'d; items only on the input side get
    /// <paramref name="adopt"/>'d (typically <c>db.Add</c>); items only on the
    /// tracked side get <paramref name="evict"/>'d (typically <c>db.Remove</c>).
    /// </summary>
    private static void DiffChildren<T>(
        ICollection<T> tracked,
        ICollection<T> input,
        Func<T, Guid> keep,
        Action<T, T> apply,
        Action<T> adopt,
        Action<T> evict)
    {
        var trackedMap = tracked.ToDictionary(keep);
        var inputIds = input.Select(keep).ToHashSet();

        foreach (var existing in tracked.ToList())
            if (!inputIds.Contains(keep(existing)))
                evict(existing);

        foreach (var item in input)
        {
            if (trackedMap.TryGetValue(keep(item), out var dst))
                apply(dst, item);
            else
                adopt(item);
        }
    }
}
