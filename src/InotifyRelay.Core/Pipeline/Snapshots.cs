using InotifyRelay.Core.Events;

namespace InotifyRelay.Core.Pipeline;

public sealed record SourceSnapshot(string Path, string? GlobPattern, bool Recursive);

public sealed record TargetBindingSnapshot(
    Guid Id,
    Guid TargetId,
    bool Enabled,
    int DelayMs,
    string? TemplateOverrideJson,
    int Order);

public sealed record RuleSnapshot(
    Guid Id,
    string Name,
    bool Enabled,
    FileEventType EventMask,
    int DebounceMs,
    int StabilizationMs,
    IReadOnlyList<SourceSnapshot> Sources,
    IReadOnlyList<TargetBindingSnapshot> TargetBindings);

public sealed record TargetSnapshot(
    Guid Id,
    string Name,
    string ProviderType,
    string ProviderConfigJson,
    string DefaultTemplateJson,
    int RetryMaxAttempts,
    int RetryInitialBackoffMs,
    double RetryBackoffMultiplier,
    int RetryMaxBackoffMs,
    int CoalesceMs);
