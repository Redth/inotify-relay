using System.Runtime.InteropServices;

namespace InotifyRelay.Watcher.Linux;

internal static partial class NativeMethods
{
    // inotify_init1 flags
    public const int IN_CLOEXEC  = 0x80000;
    public const int IN_NONBLOCK = 0x800;

    // event masks
    public const uint IN_ACCESS        = 0x00000001;
    public const uint IN_MODIFY        = 0x00000002;
    public const uint IN_ATTRIB        = 0x00000004;
    public const uint IN_CLOSE_WRITE   = 0x00000008;
    public const uint IN_CLOSE_NOWRITE = 0x00000010;
    public const uint IN_OPEN          = 0x00000020;
    public const uint IN_MOVED_FROM    = 0x00000040;
    public const uint IN_MOVED_TO      = 0x00000080;
    public const uint IN_CREATE        = 0x00000100;
    public const uint IN_DELETE        = 0x00000200;
    public const uint IN_DELETE_SELF   = 0x00000400;
    public const uint IN_MOVE_SELF     = 0x00000800;

    public const uint IN_UNMOUNT       = 0x00002000;
    public const uint IN_Q_OVERFLOW    = 0x00004000;
    public const uint IN_IGNORED       = 0x00008000;

    public const uint IN_ONLYDIR       = 0x01000000;
    public const uint IN_DONT_FOLLOW   = 0x02000000;
    public const uint IN_EXCL_UNLINK   = 0x04000000;
    public const uint IN_ISDIR         = 0x40000000;

    // poll
    public const short POLLIN = 0x0001;

    [LibraryImport("libc", SetLastError = true)]
    public static partial int inotify_init1(int flags);

    [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int inotify_add_watch(int fd, string pathname, uint mask);

    [LibraryImport("libc", SetLastError = true)]
    public static partial int inotify_rm_watch(int fd, int wd);

    [LibraryImport("libc", SetLastError = true, EntryPoint = "close")]
    public static partial int close_fd(int fd);

    // read(int fd, void *buf, size_t count); returns ssize_t.
    [LibraryImport("libc", SetLastError = true, EntryPoint = "read")]
    public static unsafe partial nint read(int fd, byte* buf, nuint count);

    [StructLayout(LayoutKind.Sequential)]
    public struct PollFd
    {
        public int fd;
        public short events;
        public short revents;
    }

    [LibraryImport("libc", SetLastError = true)]
    public static unsafe partial int poll(PollFd* fds, nuint nfds, int timeout);
}
