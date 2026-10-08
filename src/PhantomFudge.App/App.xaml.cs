using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using PhantomFudge.App.Native;

namespace PhantomFudge.App;

public partial class App : Application
{
    private SingleInstance? instance;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            if (e.Args is ["--capture-proof", var outputDirectory])
            {
                var code = await Diagnostics.CaptureProof.RunAsync(outputDirectory, transparent: true);
                Shutdown(code);
                return;
            }
            if (e.Args is ["--ui-smoke", var uiDirectory])
            {
                var preview = new MainWindow();
                MainWindow = preview;
                Shutdown(await preview.RunUiSmokeAsync(uiDirectory));
                return;
            }

            instance = new SingleInstance();
            if (!instance.IsOwner)
            {
                await instance.RevealExistingAsync();
                Shutdown();
                return;
            }

            var window = new MainWindow();
            MainWindow = window;
            instance.Listen(
                () => Dispatcher.BeginInvoke(window.RevealAndReset),
                message => Dispatcher.BeginInvoke(() => window.ReportStatus(message)));
            window.ShowProtected();
        }
        catch (Exception error) when (error is Win32Exception or IOException or UnauthorizedAccessException
            or InvalidOperationException or PlatformNotSupportedException or OperationCanceledException or COMException)
        {
            LocalFailureNotice.Record(WindowFailure.Startup);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        instance?.Dispose();
        base.OnExit(e);
    }
}
