namespace InotifyRelay.Core.Events;

[Flags]
public enum FileEventType
{
    None        = 0,
    Created     = 1 << 0,
    Modified    = 1 << 1,
    ClosedWrite = 1 << 2,
    Deleted     = 1 << 3,
    Renamed     = 1 << 4,
    MovedFrom   = 1 << 5,
    MovedTo     = 1 << 6,
    Attrib      = 1 << 7,
    DirCreated  = 1 << 8,
    DirDeleted  = 1 << 9,
    Overflow    = 1 << 10,
    All         = (1 << 11) - 1,
}
