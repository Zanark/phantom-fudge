using System.IO;
using System.Text.Json;

namespace PhantomFudge.Launcher;

internal enum LauncherCode
{
    Success = 0,
    InvalidArguments = 64,
    InstallationInvalid = 65,
    RuntimeUnavailable = 66,
    AppStartFailed = 67,
    RecoveryUnavailable = 68,
    AppRunning = 69,
    OperationBusy = 70,
    MaintenanceBlocked = 71,
    SelfTestFailed = 72,
    LocalDataUnavailable = 74,
    RuntimeTimedOut = 75,
    AppStartPending = 76
}

internal static class LauncherStatus
{
    internal static string Message(LauncherCode code) => code switch
    {
        LauncherCode.Success => "Phantom Fudge operation completed.",
        LauncherCode.InvalidArguments => "Use no arguments, --notes-only, --maintenance [installation-root], --validate-install installation-root, or --self-test.",
        LauncherCode.InstallationInvalid => "Installation files are missing or invalid. Run the installer again to repair this installation.",
        LauncherCode.RuntimeUnavailable => "Local transcription could not start. Close other speech-service launches, then retry. If a retained runtime receipt is reported, repair it using the documented recovery steps. No process was forcibly stopped.",
        LauncherCode.AppStartFailed => "The notes window could not start. Check Windows support and repair the installation. Existing notes were not closed.",
        LauncherCode.RecoveryUnavailable => "A notes window is already open, but reveal did not complete. Use Ctrl+Alt+Space, or save and close that window before retrying.",
        LauncherCode.AppRunning => "Save your notes and close Phantom Fudge with its X button before updating or uninstalling. Hiding the window is not enough.",
        LauncherCode.OperationBusy => "Another Phantom Fudge startup or maintenance operation is running. Wait for it to finish, then retry.",
        LauncherCode.MaintenanceBlocked => "The installed speech service could not be safely stopped. Stop transcription, close Phantom Fudge, and retry. An unreachable or uncertain ownership receipt requires repair; no unrelated process was stopped.",
        LauncherCode.SelfTestFailed => "Launcher diagnostics failed. Repair the installation and retry --self-test.",
        LauncherCode.LocalDataUnavailable => "Local runtime data or its private token could not be opened. Check your Windows profile permissions and the documented repair steps. Existing files were preserved.",
        LauncherCode.RuntimeTimedOut => "Local transcription startup did not finish in time. Wait before retrying; a retained runtime receipt may require repair. No runtime process was forcibly stopped.",
        LauncherCode.AppStartPending => "The notes process is still starting. Updates remain blocked until it establishes its instance or exits. Close hides this progress window while the launcher keeps watching; no process is forcibly stopped.",
        _ => "Phantom Fudge could not complete this operation. Repair the installation and retry."
    };

    internal static void Write(LauncherCode code, bool selfTest = false, object? checks = null)
    {
        var payload = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            status = code.ToString(),
            exitCode = (int)code,
            message = Message(code),
            utc = DateTimeOffset.UtcNow,
            checks
        }, new JsonSerializerOptions { WriteIndented = true });
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhantomFudge");
            InstallPaths.RejectReparsePoints(directory);
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, selfTest ? "launcher-self-test.json" : "launcher-status.json");
            InstallPaths.RejectReparsePoints(path);
            File.WriteAllText(path, payload);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // Diagnostics never replace the operation's exit code or expose exception details.
        }
        try
        {
            Console.Out.WriteLine(payload);
        }
        catch (IOException)
        {
            // A closed caller pipe must not turn a completed operation into a startup failure.
        }
    }
}
