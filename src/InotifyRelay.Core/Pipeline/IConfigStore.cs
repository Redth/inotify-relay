namespace InotifyRelay.Core.Pipeline;

public interface IConfigStore
{
    Task<IReadOnlyList<RuleSnapshot>> GetRulesAsync(CancellationToken ct);
    Task<TargetSnapshot?> GetTargetAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<TargetSnapshot>> GetTargetsAsync(CancellationToken ct);
}

public interface IConfigChangeNotifier
{
    event Action? RulesChanged;
    void RaiseRulesChanged();
}

public sealed class ConfigChangeNotifier : IConfigChangeNotifier
{
    public event Action? RulesChanged;
    public void RaiseRulesChanged() => RulesChanged?.Invoke();
}
