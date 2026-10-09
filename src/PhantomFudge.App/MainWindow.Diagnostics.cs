using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using PhantomFudge.Core.Diagnostics;

namespace PhantomFudge.App;

public partial class MainWindow
{
    private readonly Dictionary<Core.AudioSource, long> lastAudioDiagnostic = [];
    private Guid? diagnosticCaptureId;
    private long captureStartedAt;
    private long answerStartedAt;
    private bool firstAnswerLogged;
    private bool firstAnswerVisibleLogged;
    private readonly System.Windows.Threading.DispatcherTimer diagnosticHealthTimer = new()
    {
        Interval = TimeSpan.FromSeconds(2)
    };

    private void InitializeDiagnostics()
    {
        try
        {
            LocalDiagnostics.ReloadSettings();
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            LocalDiagnostics.Record(DiagnosticEvent.ConfigurationFailed, code: "diagnostic_settings", provider: "Application");
            ReportStatus("Diagnostic settings are invalid; metadata logging remains enabled.");
        }
        string? build = null;
        try
        {
            using var sourceFile = File.OpenRead(typeof(MainWindow).Assembly.Location);
            build = Convert.ToHexString(SHA256.HashData(sourceFile))[..16];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ReportStatus("Build identity could not be recorded in local diagnostics.");
        }
        LocalDiagnostics.Record(DiagnosticEvent.ApplicationStarted, count: diagnosticRendering ? 1 : 0,
            code: build, provider: "Application");
        diagnosticHealthTimer.Tick += (_, _) =>
        {
            if (LocalDiagnostics.FailureCode is not null)
                ReportStatus("Local diagnostics could not be written. Check the Diagnostics folder permissions.");
            DiagnosticsButton.Content = LocalDiagnostics.DroppedRecords > 0
                ? $"Diagnostics ({LocalDiagnostics.DroppedRecords} dropped)" : "Diagnostics";
        };
        diagnosticHealthTimer.Start();
    }

    private void Diagnostics_Click(object sender, RoutedEventArgs args)
    {
        try
        {
            LocalDiagnostics.ReloadSettings();
            Directory.CreateDirectory(LocalDiagnostics.DirectoryPath);
            Process.Start(new ProcessStartInfo
            {
                FileName = LocalDiagnostics.DirectoryPath,
                UseShellExecute = true
            });
            ReportStatus(LocalDiagnostics.IncludeText
                ? "Local diagnostics: text logging is explicitly enabled. Folder opened outside capture protection."
                : "Local diagnostics: timing/error metadata only; no audio or text bodies. Folder opened outside capture protection.");
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
            JsonException or System.ComponentModel.Win32Exception)
        {
            ReportStatus("Diagnostics could not be opened or settings are invalid; no text logging was enabled.");
        }
    }
}
