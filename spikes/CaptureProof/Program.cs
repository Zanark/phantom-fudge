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
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: CaptureProof <synthetic-evidence-directory>");
            return 64;
        }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            try
            {
                app.Shutdown(await App.Diagnostics.CaptureProof.RunAsync(args[0]));
            }
            catch (Exception error) when (error is Win32Exception or IOException or COMException
                or UnauthorizedAccessException or InvalidOperationException or PlatformNotSupportedException)
            {
                Console.Error.WriteLine($"Synthetic capture proof failed: {error.GetType().Name}.");
                app.Shutdown(1);
            }
        };
        return app.Run();
    }
}
