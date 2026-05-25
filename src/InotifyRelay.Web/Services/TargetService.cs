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
        if (await db.Targets.AnyAsync(t => t.Name == name, ct))
            throw new DuplicateNameException($"A target named '{name}' already exists.");
        var defaultCfg = JsonFormat.Serialize(provider.CreateDefaultConfig());
        var t = new TargetEntity
        {
            Name = name,
            ProviderType = providerType,
            ProviderConfigJson = defaultCfg,
            CoalesceMs = CoalesceRecommender.Recommend(providerType, defaultCfg),
        };
        db.Targets.Add(t);
        await db.SaveChangesAsync(ct);
        return t;
    }

    public async Task SaveAsync(TargetEntity target, CancellationToken ct = default)
    {
        if (await db.Targets.AnyAsync(t => t.Name == target.Name && t.Id != target.Id, ct))
            throw new DuplicateNameException($"A target named '{target.Name}' already exists.");
        target.ProviderConfigJson = JsonFormat.Pretty(target.ProviderConfigJson);
        target.PathMappingsJson  = JsonFormat.Pretty(target.PathMappingsJson);
        target.DefaultTemplateJson = JsonFormat.Pretty(target.DefaultTemplateJson);
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
        var mappings = ParseMappings(t.PathMappingsJson);
        return await provider.SendAsync(
            new RelayContext(fake, "test-rule", t.Name, t.ProviderConfigJson, null, mappings), ct);
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private static IReadOnlyList<Core.Pipeline.PathMapping> ParseMappings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<Core.Pipeline.PathMapping>();
        try { return JsonSerializer.Deserialize<List<Core.Pipeline.PathMapping>>(json, JsonOpts) ?? new(); }
        catch { return Array.Empty<Core.Pipeline.PathMapping>(); }
    }
}
