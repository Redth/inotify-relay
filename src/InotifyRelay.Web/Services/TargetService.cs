using System.Text.Json;
using InotifyRelay.Core.Providers;
using InotifyRelay.Data;
using InotifyRelay.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace InotifyRelay.Web.Services;

public sealed class TargetService(AppDbContext db, ProviderCatalog catalog)
{
    public Task<List<TargetEntity>> ListAsync(CancellationToken ct = default)
        => db.Targets.OrderBy(t => t.Name).ToListAsync(ct);

    public Task<TargetEntity?> GetAsync(Guid id, CancellationToken ct = default)
        => db.Targets.FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<TargetEntity> CreateAsync(string name, string providerType, CancellationToken ct = default)
    {
        var provider = catalog.Get(providerType) ?? throw new InvalidOperationException($"Unknown provider {providerType}");
        var t = new TargetEntity
        {
            Name = name,
            ProviderType = providerType,
            ProviderConfigJson = JsonSerializer.Serialize(provider.CreateDefaultConfig()),
            CoalesceMs = DefaultCoalesceMs(providerType),
        };
        db.Targets.Add(t);
        await db.SaveChangesAsync(ct);
        return t;
    }

    /// <summary>
    /// Sensible default coalescing window per provider. Webhooks fire one HTTP request
    /// per event by default; media-server rescans batch — a torrent finishing or rsync
    /// landing dozens of files inside a folder should produce one scan, not dozens.
    /// </summary>
    private static int DefaultCoalesceMs(string providerType) => providerType.ToLowerInvariant() switch
    {
        "jellyfin" => 5000,
        "plex"     => 5000,
        _          => 0,
    };

    public async Task SaveAsync(TargetEntity target, CancellationToken ct = default)
    {
        target.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var t = await db.Targets.FindAsync([id], ct);
        if (t is null) return;
        db.Targets.Remove(t);
        await db.SaveChangesAsync(ct);
    }

    public IRelayProvider? GetProvider(string typeKey) => catalog.Get(typeKey);

    public async Task<RelayResult> TestAsync(Guid id, CancellationToken ct = default)
    {
        var t = await GetAsync(id, ct) ?? throw new InvalidOperationException("Target not found");
        var provider = catalog.Get(t.ProviderType) ?? throw new InvalidOperationException("Provider missing");
        var fake = new Core.Events.FileSystemChange(
            "/watch/test/sample.mkv", Core.Events.FileEventType.ClosedWrite, false,
            DateTimeOffset.UtcNow, "/watch/test");
        return await provider.SendAsync(new RelayContext(fake, "test-rule", t.Name, t.ProviderConfigJson, null), ct);
    }
}
