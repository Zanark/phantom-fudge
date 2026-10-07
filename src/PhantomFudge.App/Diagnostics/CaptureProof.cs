using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PhantomFudge.App.Native;

namespace PhantomFudge.App.Diagnostics;

internal static class CaptureProof
{
    public static async Task<int> RunAsync(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var backdrop = MakeWindow("Synthetic capture backdrop", 80, 80, 480, 330,
            Color.FromRgb(35, 150, 90), "");
        var overlay = MakeWindow("Synthetic capture control", 110, 110, 400, 250,
            Color.FromRgb(170, 45, 45), "SYNTHETIC NOTE\nPHANTOM CANARY 7319");
        try
        {
            backdrop.Show();
            overlay.Show();
            await SettleAsync();
            var handle = new WindowInteropHelper(overlay).Handle;
            if (!GetWindowRect(handle, out var bounds))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            var positive = Grab(bounds);
            SaveImage(outputDirectory, "positive-control.png", bounds, positive);
            overlay.Hide();
            await SettleAsync();
            var baseline = Grab(bounds);
            SaveImage(outputDirectory, "underlying-baseline.png", bounds, baseline);
            var controlDifference = CountDifferences(baseline, positive);

            var configuration = WindowProtection.Configure(overlay, nonActivating: false);
            var frames = new List<object>();
            var maximumDifference = 0;
            for (var cycle = 0; cycle < 4; cycle++)
            {
                overlay.Show();
                for (var sample = 0; sample < 4; sample++)
                {
                    var pixels = Grab(bounds);
                    var difference = CountDifferences(baseline, pixels);
                    maximumDifference = Math.Max(maximumDifference, difference);
                    var name = $"protected-{cycle}-{sample}.png";
                    SaveImage(outputDirectory, name, bounds, pixels);
                    frames.Add(new { cycle, sample, difference, image = name, utc = DateTimeOffset.UtcNow });
                    await Dispatcher.Yield(DispatcherPriority.Render);
                    await Task.Delay(30);
                }
                overlay.Hide();
                await SettleAsync();
                WindowProtection.Configure(overlay, nonActivating: false);
            }

            var styles = WindowProtection.ReadStyles(handle);
            var assemblyPath = typeof(CaptureProof).Assembly.Location;
            var passed = controlDifference > baseline.Length / 4 / 5 && maximumDifference == 0
                && (styles & WindowProtection.ToolWindow) != 0 && (styles & WindowProtection.AppWindow) == 0;
            var result = new
            {
                outcome = passed ? "PASS_LOCAL_GDI_SAMPLES_ONLY" : "FAIL_LOCAL_PROOF",
                utc = DateTimeOffset.UtcNow,
                os = Environment.OSVersion.VersionString,
                processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
                assemblySha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(assemblyPath))),
                configuration,
                toolWindow = (styles & WindowProtection.ToolWindow) != 0,
                appWindow = (styles & WindowProtection.AppWindow) != 0,
                controlDifferencePixels = controlDifference,
                maximumProtectedDifferencePixels = maximumDifference,
                sampledFrames = frames,
                limitation = "Synthetic cropped GDI BitBlt observations only. Not Teams, Zoom, Meet, OBS, " +
                    "WGC, first-frame completeness, actual app UX, or a universal privacy guarantee."
            };
            await File.WriteAllTextAsync(Path.Combine(outputDirectory, "result.json"),
                JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 2;
        }
        finally
        {
            overlay.Close();
            backdrop.Close();
        }
    }

    private static Window MakeWindow(string title, int left, int top, int width, int height,
        Color background, string text) => new()
    {
        Title = title, Left = left, Top = top, Width = width, Height = height,
        WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
        ShowInTaskbar = false, AllowsTransparency = false, Topmost = true,
        Background = new SolidColorBrush(background),
        Content = new TextBlock
        {
            Text = text, FontSize = 26, Foreground = Brushes.White, Margin = new Thickness(22),
            TextWrapping = TextWrapping.Wrap
        }
    };

    private static async Task SettleAsync()
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        WindowProtection.FlushComposition();
        // Fixture settling is not the application's privacy barrier.
        await Task.Delay(150);
    }

    private static int CountDifferences(byte[] baseline, byte[] candidate)
    {
        var count = 0;
        for (var offset = 0; offset < baseline.Length; offset += 4)
            if (baseline[offset] != candidate[offset] || baseline[offset + 1] != candidate[offset + 1] ||
                baseline[offset + 2] != candidate[offset + 2])
                count++;
        return count;
    }

    private static void SaveImage(string directory, string name, NativeRect rect, byte[] pixels)
    {
        var image = BitmapSource.Create(rect.Right - rect.Left, rect.Bottom - rect.Top, 96, 96,
            PixelFormats.Bgr32, null, pixels, (rect.Right - rect.Left) * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(Path.Combine(directory, name));
        encoder.Save(file);
    }

    private static byte[] Grab(NativeRect rect)
    {
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0 || width > 4096 || height > 4096)
            throw new InvalidOperationException("Invalid synthetic capture bounds.");
        var screen = GetDC(nint.Zero);
        if (screen == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        var memory = nint.Zero;
        var bitmap = nint.Zero;
        var previous = nint.Zero;
        try
        {
            memory = CreateCompatibleDC(screen);
            if (memory == 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            var info = new BitmapInfo
            {
                Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32
            };
            bitmap = CreateDIBSection(screen, ref info, 0, out var pixels, nint.Zero, 0);
            if (bitmap == 0)
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            previous = SelectObject(memory, bitmap);
            if (previous == 0 || previous == -1)
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            if (!BitBlt(memory, 0, 0, width, height, screen, rect.Left, rect.Top, 0x00CC0020 | 0x40000000))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            var result = new byte[checked(width * height * 4)];
            Marshal.Copy(pixels, result, 0, result.Length);
            return result;
        }
        finally
        {
            if (previous != 0 && previous != -1)
                SelectObject(memory, previous);
            if (bitmap != 0)
                DeleteObject(bitmap);
            if (memory != 0)
                DeleteDC(memory);
            ReleaseDC(nint.Zero, screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ClrUsed, ClrImportant, Colors;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect rect);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetDC(nint window);
    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage,
        out nint pixels, nint section, uint offset);
    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(nint destination, int x, int y, int width, int height,
        nint source, int sourceX, int sourceY, uint operation);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(nint dc);
}
