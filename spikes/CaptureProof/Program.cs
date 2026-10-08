using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace PhantomFudge.CaptureProof;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length is < 1 or > 2 || (args.Length == 2 && args[1] != "--transparent"))
        {
            Console.Error.WriteLine("Usage: CaptureProof <synthetic-evidence-directory> [--transparent]");
            return 64;
        }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            try
            {
                app.Shutdown(await App.Diagnostics.CaptureProof.RunAsync(args[0], args.Length == 2));
            }
            catch (Exception error) when (error is Win32Exception or IOException or COMException
                or UnauthorizedAccessException or InvalidOperationException or PlatformNotSupportedException)
            {
                Console.Error.WriteLine($"Synthetic capture proof failed: {error.GetType().Name}; " +
                    $"nativeError={(error as Win32Exception)?.NativeErrorCode}.");
                app.Shutdown(1);
            }
        };
        return app.Run();
    }
}
