using System.Runtime.InteropServices;
using Xunit;

namespace InotifyRelay.Watcher.Tests.TestHelpers;

/// <summary>Skips when not running on Linux.</summary>
public sealed class LinuxFactAttribute : FactAttribute
{
    public LinuxFactAttribute()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            Skip = "Linux-only (uses inotify directly).";
    }
}

/// <summary>Skips when running on Linux (we exercise the inotify path there instead).</summary>
public sealed class NonLinuxFactAttribute : FactAttribute
{
    public NonLinuxFactAttribute()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            Skip = "Non-Linux only (covers the managed FileSystemWatcher fallback).";
    }
}
