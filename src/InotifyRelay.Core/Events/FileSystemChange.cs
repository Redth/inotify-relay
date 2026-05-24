namespace InotifyRelay.Core.Events;

public sealed record FileSystemChange(
    string Path,
    FileEventType EventType,
    bool IsDirectory,
    DateTimeOffset Timestamp,
    string SourceRoot,
    string? OldPath = null);
