namespace InotifyRelay.Data.Entities;

public class TargetEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string ProviderType { get; set; } = "";

    /// <summary>JSON-serialized provider-specific config.</summary>
    public string ProviderConfigJson { get; set; } = "{}";

    /// <summary>JSON-serialized default template (per-provider shape).</summary>
    public string DefaultTemplateJson { get; set; } = "{}";

    public int RetryMaxAttempts { get; set; } = 3;
    public int RetryInitialBackoffMs { get; set; } = 500;
    public double RetryBackoffMultiplier { get; set; } = 2.0;
    public int RetryMaxBackoffMs { get; set; } = 30_000;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class TargetBindingEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RuleId { get; set; }
    public RuleEntity Rule { get; set; } = null!;

    public Guid TargetId { get; set; }
    public TargetEntity Target { get; set; } = null!;

    public bool Enabled { get; set; } = true;
    public int DelayMs { get; set; } = 0;
    public string? TemplateOverrideJson { get; set; }
    public int Order { get; set; } = 0;
}
