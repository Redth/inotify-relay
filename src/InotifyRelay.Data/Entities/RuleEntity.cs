using InotifyRelay.Core.Events;

namespace InotifyRelay.Data.Entities;

public class RuleEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool Enabled { get; set; } = true;

    public FileEventType EventMask { get; set; } = FileEventType.Created | FileEventType.ClosedWrite | FileEventType.MovedTo | FileEventType.Deleted;
    public int DebounceMs { get; set; } = 1000;
    public int StabilizationMs { get; set; } = 0;

    public List<SourceEntity> Sources { get; set; } = new();
    public List<TargetBindingEntity> TargetBindings { get; set; } = new();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class SourceEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RuleId { get; set; }
    public RuleEntity Rule { get; set; } = null!;

    public string Path { get; set; } = "";
    public string? GlobPattern { get; set; }
    public bool Recursive { get; set; } = true;
}
