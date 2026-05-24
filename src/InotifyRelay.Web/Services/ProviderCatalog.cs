using InotifyRelay.Core.Providers;

namespace InotifyRelay.Web.Services;

public sealed class ProviderCatalog
{
    private readonly Dictionary<string, IRelayProvider> _providers;

    public ProviderCatalog(IEnumerable<IRelayProvider> providers)
    {
        _providers = providers.ToDictionary(p => p.TypeKey, StringComparer.OrdinalIgnoreCase);
    }

    public IRelayProvider? Get(string typeKey)
        => _providers.TryGetValue(typeKey, out var p) ? p : null;

    public IReadOnlyCollection<IRelayProvider> All => _providers.Values;
}
