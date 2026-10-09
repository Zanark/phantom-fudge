using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PhantomFudge.App.Native;

public sealed record ProtectionResult(bool ReadbackAvailable, uint? ReadbackAffinity, int ReadbackError, bool PeekPreventionRequested);

internal static class WindowProtection
{
    private const int ExtendedStyle = -20;
    internal const long ToolWindow = 0x80;
    internal const long AppWindow = 0x40000;
    private const long NoActivate = 0x08000000;
    private const uint ExcludeFromCapture = 0x11;
    private const int DisallowPeek = 11;

    public static ProtectionResult Configure(Window window, bool nonActivating)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            throw new PlatformNotSupportedException("The preview requires Windows 11.");

        var handle = new WindowInteropHelper(window).EnsureHandle();
        var hr = DwmIsCompositionEnabled(out var composing);
        Marshal.ThrowExceptionForHR(hr);
        if (!composing)
            throw new InvalidOperationException("Desktop composition is unavailable.");

        var styles = ReadStyles(handle);
        styles = (styles | ToolWindow) & ~AppWindow;
        styles = nonActivating ? styles | NoActivate : styles & ~NoActivate;
        WriteStyles(handle, styles);
        if (!SetWindowPos(handle, nint.Zero, 0, 0, 0, 0, 0x1 | 0x2 | 0x4 | 0x10 | 0x20))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        if (!SetWindowDisplayAffinity(handle, ExcludeFromCapture))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        var preventPeek = 1;
        Marshal.ThrowExceptionForHR(DwmSetWindowAttribute(handle, DisallowPeek, ref preventPeek, sizeof(int)));

        var readback = GetWindowDisplayAffinity(handle, out var affinity);
        var readbackError = readback ? 0 : Marshal.GetLastPInvokeError();
        if (readback && affinity != ExcludeFromCapture)
            throw new InvalidOperationException("Capture-affinity readback contradicts the requested setting.");

        // Flush submitted composition work; recipient capture still requires independent qualification.
        FlushComposition();
        return new ProtectionResult(readback, readback ? affinity : null, readbackError, PeekPreventionRequested: true);
    }

    public static long ReadStyles(nint handle)
    {
        Marshal.SetLastPInvokeError(0);
        var value = Environment.Is64BitProcess ? GetWindowLongPtr(handle, ExtendedStyle) :
            new nint(GetWindowLong(handle, ExtendedStyle));
        var error = Marshal.GetLastPInvokeError();
        if (value == 0 && error != 0)
            throw new Win32Exception(error);
        return value.ToInt64();
    }

    private static void WriteStyles(nint handle, long styles)
    {
        Marshal.SetLastPInvokeError(0);
        var previous = Environment.Is64BitProcess
            ? SetWindowLongPtr(handle, ExtendedStyle, new nint(styles))
            : new nint(SetWindowLong(handle, ExtendedStyle, checked((int)styles)));
        var error = Marshal.GetLastPInvokeError();
        if (previous == 0 && error != 0)
            throw new Win32Exception(error);
    }

    public static void FlushComposition() => Marshal.ThrowExceptionForHR(DwmFlush());

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint handle, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong(nint handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint handle, int index, nint value);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong(nint handle, int index, int value);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(nint handle, uint affinity);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowDisplayAffinity(nint handle, out uint affinity);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint handle, nint insertAfter, int x, int y,
        int width, int height, uint flags);
    [DllImport("dwmapi.dll")]
    private static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);
    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
