using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace PhantomFudge.Launcher;

[SupportedOSPlatform("windows")]
internal static class ProcessIdentity
{
    internal static string? Read(uint pid)
    {
        using var process = OpenProcess(0x1000, false, pid);
        if (process.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            if (error == 87)
                return null;
            throw new Win32Exception(error);
        }
        if (!GetProcessTimes(process, out var created, out var exited, out _, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return exited == 0 ? created.ToString(CultureInfo.InvariantCulture) : null;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(SafeProcessHandle process, out ulong created, out ulong exited,
        out ulong kernel, out ulong user);
}
