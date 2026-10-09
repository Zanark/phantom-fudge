using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PhantomFudge.App.Native;

internal sealed class WindowHotkeys(nint handle) : IDisposable
{
    public const int ToggleVisibility = 1;
    public const int ToggleEditing = 2;
    public const int KeyboardControl = 3;
    private readonly List<int> registered = [];

    public void Register(int id, uint virtualKey)
    {
        const uint controlAltNoRepeat = 0x2 | 0x1 | 0x4000;
        if (!RegisterHotKey(handle, id, controlAltNoRepeat, virtualKey))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        registered.Add(id);
    }

    public void Dispose()
    {
        foreach (var id in registered)
            if (!UnregisterHotKey(handle, id))
                System.Diagnostics.Trace.TraceWarning("A window hotkey could not be unregistered.");
        registered.Clear();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint handle, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint handle, int id);
}
