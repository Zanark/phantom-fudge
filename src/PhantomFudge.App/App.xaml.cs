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
            if (e.Args is ["--prepare-agency", var agencyPath])
            {
                var executable = Core.Answers.AgencyCliAnswerProvider.ResolveExecutable(
                    agencyPath == "auto" ? null : agencyPath);
                Directory.CreateDirectory(Core.Answers.AnswerConfiguration.DirectoryPath);
                await File.WriteAllTextAsync(
                    Path.Combine(Core.Answers.AnswerConfiguration.DirectoryPath, "answer-setup.json"),
                    System.Text.Json.JsonSerializer.Serialize(new { agencyPath = executable }));
                Shutdown(0);
                return;
            }
            if (e.Args is ["--prepare-answer-profile", var cliPath])
            {
                var executable = Core.Answers.CopilotCliAnswerProvider.ResolveExecutable(
                    cliPath == "auto" ? null : cliPath);
                Core.Answers.CopilotCliAnswerProvider.InitializeProfile(Core.Answers.AnswerConfiguration.CopilotProfilePath);
                Directory.CreateDirectory(Core.Answers.AnswerConfiguration.DirectoryPath);
                await File.WriteAllTextAsync(
                    Path.Combine(Core.Answers.AnswerConfiguration.DirectoryPath, "answer-setup.json"),
                    System.Text.Json.JsonSerializer.Serialize(new { cliPath = executable }));
                Shutdown(0);
                return;
            }
            if (e.Args is ["--capture-proof", var outputDirectory])
            {
                var code = await Diagnostics.CaptureProof.RunAsync(outputDirectory, transparent: true);
                Shutdown(code);
                return;
            }
            if (e.Args is ["--ui-smoke", var uiDirectory])
            {
                var preview = new MainWindow(diagnosticMode: true);
                MainWindow = preview;
                Shutdown(await preview.RunUiSmokeAsync(uiDirectory));
                return;
            }
            if (e.Args is ["--documentation-screenshots", var documentationDirectory])
            {
                var preview = new MainWindow(diagnosticMode: true);
                MainWindow = preview;
                Shutdown(await preview.RunUiSmokeAsync(documentationDirectory, documentationFixture: true));
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
            or InvalidOperationException or PlatformNotSupportedException or OperationCanceledException or COMException
            or Core.Answers.AnswerProviderException or NullReferenceException or System.Windows.Markup.XamlParseException)
        {
            Core.Diagnostics.LocalDiagnostics.RecordException(Core.Diagnostics.DiagnosticEvent.ConfigurationFailed,
                error, provider: "Application");
            if (e.Args is ["--documentation-screenshots" or "--ui-smoke", var diagnosticDirectory])
            {
                Directory.CreateDirectory(diagnosticDirectory);
                await File.WriteAllTextAsync(Path.Combine(diagnosticDirectory, "result.json"),
                    System.Text.Json.JsonSerializer.Serialize(new
                    {
                        passed = false,
                        startupError = error.GetType().Name,
                        diagnosticDetails = error.Message.StartsWith("Synthetic local question assertions failed:", StringComparison.Ordinal) ||
                            error.Message.StartsWith("Synthetic answer lifecycle failed:", StringComparison.Ordinal)
                                ? error.Message : null
                    }));
            }
            LocalFailureNotice.Record(WindowFailure.Startup);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Core.Diagnostics.LocalDiagnostics.Record(Core.Diagnostics.DiagnosticEvent.ApplicationStopped,
            provider: "Application");
        Core.Diagnostics.LocalDiagnostics.FlushAndStop();
        instance?.Dispose();
        base.OnExit(e);
    }
}
