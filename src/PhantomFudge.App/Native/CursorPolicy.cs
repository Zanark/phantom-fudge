using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PhantomFudge.App.Native;

internal static class CursorPolicy
{
    private static readonly nint Arrow = LoadArrow();

    private static nint LoadArrow()
    {
        var cursor = LoadCursor(nint.Zero, new nint(32512));
        return cursor != 0 ? cursor : throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    public static void SetArrow() => SetCursor(Arrow);
    public static bool IsArrow => GetCursor() == Arrow;

    [DllImport("user32.dll", EntryPoint = "LoadCursorW", SetLastError = true)]
    private static extern nint LoadCursor(nint instance, nint resource);
    [DllImport("user32.dll")]
    private static extern nint SetCursor(nint cursor);
    [DllImport("user32.dll")]
    private static extern nint GetCursor();
}
