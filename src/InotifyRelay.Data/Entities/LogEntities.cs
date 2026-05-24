namespace InotifyRelay.Data.Entities;

public class EventLogEntity
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Guid? RuleId { get; set; }
    public string? RuleName { get; set; }
    public string EventType { get; set; } = "";
    public string Path { get; set; } = "";
    public string? OldPath { get; set; }
    public bool IsDirectory { get; set; }
    public string SourceRoot { get; set; } = "";
}

public class DeliveryLogEntity
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public long? EventLogId { get; set; }
    public Guid? TargetId { get; set; }
    public string? TargetName { get; set; }
    public string ProviderType { get; set; } = "";
    public bool Success { get; set; }
    public int StatusCode { get; set; }
    public int Attempts { get; set; }
    public int ElapsedMs { get; set; }
    public string? Error { get; set; }
    public string? ResponseSnippet { get; set; }
}
