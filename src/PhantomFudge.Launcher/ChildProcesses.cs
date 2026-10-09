using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace PhantomFudge.Launcher;

internal sealed record ChildResult(int? ExitCode, bool TimedOut, int OutputCharacters, int ErrorCharacters)
{
    internal bool Succeeded => !TimedOut && ExitCode == 0;
}

internal sealed class ApplicationHandoff(Func<bool> childHasExited, Func<bool> appIsRunning)
{
    internal Task<bool> Completion { get; } = WaitAsync(childHasExited, appIsRunning);
    internal bool Pending => !Completion.IsCompletedSuccessfully;

    private static async Task<bool> WaitAsync(Func<bool> childHasExited, Func<bool> appIsRunning)
    {
        while (true)
        {
            if (Confirmed(childHasExited))
                return false;
            if (Confirmed(appIsRunning))
                return true;
            await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
        }
    }

    private static bool Confirmed(Func<bool> probe)
    {
        try
        {
            return probe();
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or
            UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Uncertain liveness never permits an installer to replace an initializing child.
            return false;
        }
    }
}

internal static class ChildProcesses
{
    internal const int MaximumObservedCharacters = 8192;

    internal static ProcessStartInfo Manager(InstallPaths paths, string verb)
    {
        if (verb != "start")
            throw new ArgumentException("Unsupported runtime operation.", nameof(verb));
        var start = NewStart(paths.Python, paths.Root);
        foreach (var argument in new[] { "-I", "-B", "-X", "utf8" })
            start.ArgumentList.Add(argument);
        start.ArgumentList.Add(paths.Manager);
        start.ArgumentList.Add(verb);
        ApplyInstalledEnvironment(start, paths);
        start.RedirectStandardInput = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        return start;
    }

    internal static ProcessStartInfo Application(InstallPaths paths, string? token)
    {
        var start = NewStart(paths.App, Path.GetDirectoryName(paths.App)!);
        ApplyInstalledEnvironment(start, paths);
        start.Environment["PHANTOM_TRANSCRIPTION_URI"] = LauncherContract.ServiceUri;
        if (token is not null)
            start.Environment["PHANTOM_TRANSCRIPTION_TOKEN"] = token;
        return start;
    }

    internal static ProcessStartInfo NewStart(string executable, string workingDirectory) => new(executable)
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        WorkingDirectory = workingDirectory
    };

    internal static void ApplyInstalledEnvironment(ProcessStartInfo start, InstallPaths paths)
    {
        foreach (var key in start.Environment.Keys.ToArray())
        {
            if (key.StartsWith("PHANTOM_TRANSCRIPTION_", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("PYTHON", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("HF_TOKEN", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("HUGGING_FACE_HUB_TOKEN", StringComparison.OrdinalIgnoreCase))
                start.Environment.Remove(key);
        }
        start.Environment["PHANTOM_TRANSCRIPTION_WORKSPACE"] = paths.DataRoot;
        start.Environment["PHANTOM_TRANSCRIPTION_MODELS_ROOT"] = paths.Models;
        start.Environment["PHANTOM_TRANSCRIPTION_TOKEN_FILE"] = paths.Token;
        start.Environment["PYTHONNOUSERSITE"] = "1";
        start.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
        start.Environment["PYTHONUTF8"] = "1";
        start.Environment["HF_HUB_OFFLINE"] = "1";
        start.Environment["TRANSFORMERS_OFFLINE"] = "1";
        start.Environment["HF_HUB_DISABLE_TELEMETRY"] = "1";
        start.Environment["HF_HUB_DISABLE_IMPLICIT_TOKEN"] = "1";
        start.Environment["HF_HOME"] = Path.Combine(paths.Local, "huggingface");
        start.Environment["HF_HUB_CACHE"] = Path.Combine(paths.Local, "huggingface", "hub");
        start.Environment["OPENBLAS_NUM_THREADS"] = "1";
        start.Environment["OMP_NUM_THREADS"] = "1";
    }

    internal static async Task<ChildResult> RunAsync(ProcessStartInfo start, TimeSpan timeout)
    {
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The child process did not start.");
        if (start.RedirectStandardInput)
            process.StandardInput.Close();
        var output = DiscardAsync(process.StandardOutput);
        var errors = DiscardAsync(process.StandardError);
        try
        {
            await Task.WhenAll(process.WaitForExitAsync(), output, errors).WaitAsync(timeout).ConfigureAwait(false);
            return new(process.ExitCode, false, output.Result, errors.Result);
        }
        catch (TimeoutException)
        {
            // Never kill a possibly initializing runtime, or delete its ownership receipt.
            return new(null, true, 0, 0);
        }
    }

    private static async Task<int> DiscardAsync(StreamReader reader)
    {
        var buffer = new char[1024];
        var count = 0;
        try
        {
            int read;
            while ((read = await reader.ReadAsync(buffer).ConfigureAwait(false)) != 0)
                count = Math.Min(MaximumObservedCharacters, count + read);
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException)
        {
            // A timed-out child loses only our pipe handles. No child output is retained.
        }
        return count;
    }
}
