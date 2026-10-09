using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net.Http;
using System.Text.Json;
using System.Windows;

namespace PhantomFudge.Launcher;

internal static class Program
{
    private static readonly TimeSpan ManagerTimeout = TimeSpan.FromSeconds(180);

    [STAThread]
    private static int Main(string[] arguments)
    {
        if (arguments is ["--self-test-child"])
            return RunSelfTestChild();
        var mode = LauncherContract.ParseArguments(arguments);
        if (mode == LaunchMode.Invalid)
            return Finish(LauncherCode.InvalidArguments);
        if (mode == LaunchMode.SelfTest)
            return SelfTestAsync().GetAwaiter().GetResult();
        if (mode == LaunchMode.ValidateInstall)
        {
            try
            {
                var paths = Paths(arguments[1]);
                paths.ValidateOwnershipMarker();
                paths.Validate(requireSpeech: true);
                LauncherStatus.Write(LauncherCode.Success, checks: new
                {
                    installedLayoutValidated = true,
                    noAppOrServiceStarted = true,
                    noAudioOrModelRequest = true
                });
                return (int)LauncherCode.Success;
            }
            catch (Exception error) when (Expected(error))
            {
                return Finish(LauncherCode.InstallationInvalid);
            }
        }
        try
        {
            if (InstallerMaintenanceGate.Blocks(mode))
                return Finish(LauncherCode.OperationBusy);
            using var coordination = new InstanceCoordination();
            if (mode == LaunchMode.Maintenance)
            {
                if (coordination.AppIsRunning())
                    return Finish(LauncherCode.AppRunning);
                if (!coordination.TryEnter())
                    return Finish(LauncherCode.OperationBusy);
                if (coordination.AppIsRunning())
                    return Finish(LauncherCode.AppRunning);
                return Finish(MaintenanceAsync(arguments.Length == 2 ? arguments[1] : null).GetAwaiter().GetResult());
            }

            // Recovery happens before install validation, token creation, or any runtime operation.
            if (coordination.AppIsRunning())
            {
                try
                {
                    coordination.RevealAsync().GetAwaiter().GetResult();
                    return Finish(LauncherCode.Success);
                }
                catch (Exception error) when (Expected(error))
                {
                    return ShowFailure(LauncherCode.RecoveryUnavailable);
                }
            }
            if (!coordination.TryEnter())
                return ShowFailure(LauncherCode.OperationBusy);
            if (InstallerMaintenanceGate.Blocks(mode))
                return Finish(LauncherCode.OperationBusy);
            return RunInteractive(mode, coordination);
        }
        catch (Exception error) when (Expected(error))
        {
            return mode == LaunchMode.Maintenance
                ? Finish(LauncherCode.MaintenanceBlocked)
                : ShowFailure(LauncherCode.LocalDataUnavailable);
        }
    }

    private static InstallPaths Paths() => InstallPaths.FromLauncherDirectory(AppContext.BaseDirectory,
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    private static InstallPaths Paths(string root) => InstallPaths.FromInstallRoot(root,
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    private static async Task<LauncherCode> MaintenanceAsync(string? root)
    {
        var paths = root is null ? Paths() : Paths(root);
        paths.ValidateOwnershipMarker();
        using var service = new OwnedServiceClient();
        var success = await MaintenanceOperation.RunAsync(paths, service);
        return success ? LauncherCode.Success : LauncherCode.MaintenanceBlocked;
    }

    private static int RunInteractive(LaunchMode mode, InstanceCoordination coordination)
    {
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new LauncherWindow();
        var exitCode = LauncherCode.AppStartFailed;
        Process? startingApp = null;
        ApplicationHandoff? handoff = null;
        window.Closing += (_, eventArgs) =>
        {
            if (startingApp is not null && (handoff?.Pending ?? true))
            {
                eventArgs.Cancel = true;
                window.Hide();
            }
        };
        window.Closed += (_, _) => application.Shutdown((int)exitCode);
        application.DispatcherUnhandledException += (_, eventArgs) =>
        {
            eventArgs.Handled = true;
            exitCode = LauncherCode.AppStartFailed;
            LauncherStatus.Write(exitCode);
            window.Failure(exitCode, allowNotes: false);
        };

        async Task OpenAsync(bool notesOnly)
        {
            InstallPaths paths;
            var recovering = false;
            try
            {
                recovering = coordination.AppIsRunning();
                if (recovering)
                {
                    await coordination.RevealAsync();
                    exitCode = LauncherCode.Success;
                    LauncherStatus.Write(exitCode);
                    window.Complete();
                    return;
                }
                paths = Paths();
                paths.Validate(requireSpeech: false);
            }
            catch (Exception error) when (Expected(error))
            {
                exitCode = recovering ? LauncherCode.RecoveryUnavailable : LauncherCode.InstallationInvalid;
                LauncherStatus.Write(exitCode);
                window.Failure(exitCode, allowNotes: false);
                return;
            }

            string? token = null;
            if (!notesOnly)
            {
                window.Progress("Preparing local transcription. Audio stays off until you choose Start in the notes window.");
                try
                {
                    paths.Validate(requireSpeech: true);
                    token = LocalToken.ReadOrCreate(paths);
                    var result = await ChildProcesses.RunAsync(ChildProcesses.Manager(paths, "start"), ManagerTimeout);
                    if (!result.Succeeded)
                    {
                        exitCode = result.TimedOut ? LauncherCode.RuntimeTimedOut : LauncherCode.RuntimeUnavailable;
                        LauncherStatus.Write(exitCode);
                        window.Failure(exitCode, allowNotes: true);
                        return;
                    }
                }
                catch (Exception error) when (Expected(error))
                {
                    exitCode = error is UnauthorizedAccessException ? LauncherCode.LocalDataUnavailable : LauncherCode.RuntimeUnavailable;
                    LauncherStatus.Write(exitCode);
                    window.Failure(exitCode, allowNotes: true);
                    return;
                }
            }

            window.Progress(notesOnly ? "Opening notes without starting transcription." : "Opening the notes window. Audio remains off.");
            try
            {
                if (coordination.AppIsRunning())
                    await coordination.RevealAsync();
                else
                {
                    startingApp = Process.Start(ChildProcesses.Application(paths, token)) ??
                        throw new InvalidOperationException("The notes process did not start.");
                    handoff = new ApplicationHandoff(() => startingApp.HasExited, coordination.AppIsRunning);
                    if (await Task.WhenAny(handoff.Completion, Task.Delay(TimeSpan.FromSeconds(30))) != handoff.Completion)
                    {
                        exitCode = LauncherCode.AppStartPending;
                        LauncherStatus.Write(exitCode);
                        window.Failure(exitCode, allowNotes: false);
                    }
                    if (!await handoff.Completion)
                        throw new InvalidOperationException("The notes process exited before instance coordination.");
                }
                exitCode = LauncherCode.Success;
                LauncherStatus.Write(exitCode);
                window.Complete();
            }
            catch (Exception error) when (Expected(error))
            {
                exitCode = LauncherCode.AppStartFailed;
                LauncherStatus.Write(exitCode);
                window.Failure(exitCode, allowNotes: false);
                if (!window.IsVisible)
                    window.Complete();
            }
        }

        window.NotesOnly = () => OpenAsync(notesOnly: true);
        window.Loaded += async (_, _) => await OpenAsync(mode == LaunchMode.NotesOnly);
        try
        {
            return application.Run(window);
        }
        finally
        {
            if (startingApp is not null)
            {
                // Even dispatcher shutdown must not release coordination before child handoff.
                if (handoff?.Pending ?? true)
                    new ApplicationHandoff(() => startingApp.HasExited, coordination.AppIsRunning).Completion.GetAwaiter().GetResult();
                startingApp.Dispose();
            }
        }
    }

    private static int ShowFailure(LauncherCode code)
    {
        LauncherStatus.Write(code);
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new LauncherWindow();
        window.Failure(code, allowNotes: false);
        window.Closed += (_, _) => application.Shutdown((int)code);
        return application.Run(window);
    }

    private static int Finish(LauncherCode code)
    {
        LauncherStatus.Write(code);
        return (int)code;
    }

    private static bool Expected(Exception error) => error is IOException or InvalidDataException or
        UnauthorizedAccessException or InvalidOperationException or Win32Exception or JsonException or
        OperationCanceledException or PlatformNotSupportedException or ArgumentException or System.Security.SecurityException or HttpRequestException or TimeoutException;

    private static int RunSelfTestChild()
    {
        if (Environment.GetEnvironmentVariable("PHANTOM_LAUNCHER_SELF_TEST") != "synthetic-child-only")
            return (int)LauncherCode.InvalidArguments;
        var suffix = Environment.GetEnvironmentVariable("PHANTOM_LAUNCHER_SELF_TEST_READY_SUFFIX");
        if (suffix is not null)
        {
            if (!suffix.StartsWith("SelfTest-", StringComparison.Ordinal) ||
                !Guid.TryParseExact(suffix["SelfTest-".Length..], "N", out _))
                return (int)LauncherCode.InvalidArguments;
            Thread.Sleep(TimeSpan.FromMilliseconds(2500));
            using var appMutex = new Mutex(false, @"Local\PhantomFudge-" + suffix);
            Thread.Sleep(TimeSpan.FromSeconds(1));
            return 0;
        }
        Console.Out.Write(new string('x', 24576));
        Console.Error.Write(new string('y', 24576));
        return Environment.GetEnvironmentVariable("PHANTOM_LAUNCHER_SELF_TEST_FAILURE") == "expected-failure" ? 23 : 0;
    }

    private static async Task<int> SelfTestAsync()
    {
        try
        {
            var token = LauncherContract.CreateToken();
            var tokenFormat = token.Length == 64 && LauncherContract.IsValidToken(token);
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "synthetic-install"));
            var paths = InstallPaths.FromLauncherDirectory(Path.Combine(root, "launcher"), root);
            var manager = ChildProcesses.Manager(paths, "start");
            var fixedArguments = manager.ArgumentList.SequenceEqual(["-I", "-B", "-X", "utf8", paths.Manager, "start"]);
            var environmentIsolated = manager.Environment["PHANTOM_TRANSCRIPTION_WORKSPACE"] == paths.DataRoot &&
                manager.Environment["PHANTOM_TRANSCRIPTION_MODELS_ROOT"] == paths.Models &&
                manager.Environment["HF_HUB_OFFLINE"] == "1" &&
                !manager.Environment.ContainsKey("PHANTOM_TRANSCRIPTION_TOKEN");
            var notes = ChildProcesses.Application(paths, null);
            var notesHaveNoToken = !notes.Environment.ContainsKey("PHANTOM_TRANSCRIPTION_TOKEN") && notes.ArgumentList.Count == 0;
            var child = ChildProcesses.NewStart(Environment.ProcessPath ??
                throw new InvalidOperationException("No process path."), AppContext.BaseDirectory);
            child.ArgumentList.Add("--self-test-child");
            child.Environment["PHANTOM_LAUNCHER_SELF_TEST"] = "synthetic-child-only";
            child.Environment.Remove("PHANTOM_LAUNCHER_SELF_TEST_READY_SUFFIX");
            child.RedirectStandardInput = true;
            child.RedirectStandardOutput = true;
            child.RedirectStandardError = true;
            var result = await ChildProcesses.RunAsync(child, TimeSpan.FromSeconds(20));
            var childLifecycle = result.Succeeded &&
                result.OutputCharacters == ChildProcesses.MaximumObservedCharacters &&
                result.ErrorCharacters == ChildProcesses.MaximumObservedCharacters;
            child.Environment["PHANTOM_LAUNCHER_SELF_TEST_FAILURE"] = "expected-failure";
            var failedChild = await ChildProcesses.RunAsync(child, TimeSpan.FromSeconds(20));
            var childFailureDetected = !failedChild.Succeeded && !failedChild.TimedOut && failedChild.ExitCode == 23;
            var instanceCoordination = await TestCoordinationAsync();
            var delayedApplicationHandoff = await Task.Run(TestDelayedApplicationHandoff);
            var passed = tokenFormat && fixedArguments && environmentIsolated && notesHaveNoToken &&
                childLifecycle && childFailureDetected && instanceCoordination && delayedApplicationHandoff;
            var code = passed ? LauncherCode.Success : LauncherCode.SelfTestFailed;
            LauncherStatus.Write(code, selfTest: true, checks: new
            {
                tokenFormat, fixedArguments, environmentIsolated, notesHaveNoToken, childLifecycle, childFailureDetected,
                instanceCoordination, delayedApplicationHandoff,
                noAppOrServiceStarted = true, noAudioOrModelRequest = true
            });
            return (int)code;
        }
        catch (Exception error) when (Expected(error))
        {
            LauncherStatus.Write(LauncherCode.SelfTestFailed, selfTest: true);
            return (int)LauncherCode.SelfTestFailed;
        }
    }

    private static bool TestDelayedApplicationHandoff()
    {
        var suffix = "SelfTest-" + Guid.NewGuid().ToString("N");
        using var coordination = new InstanceCoordination(suffix);
        if (!coordination.TryEnter())
            return false;
        var start = ChildProcesses.NewStart(Environment.ProcessPath ??
            throw new InvalidOperationException("No process path."), AppContext.BaseDirectory);
        start.ArgumentList.Add("--self-test-child");
        start.Environment["PHANTOM_LAUNCHER_SELF_TEST"] = "synthetic-child-only";
        start.Environment["PHANTOM_LAUNCHER_SELF_TEST_READY_SUFFIX"] = suffix;
        using var child = Process.Start(start) ?? throw new InvalidOperationException("No synthetic child.");
        var handoff = new ApplicationHandoff(() => child.HasExited, coordination.AppIsRunning);
        Thread.Sleep(TimeSpan.FromMilliseconds(2100));
        var pendingBeyondTwoSeconds = handoff.Pending;
        var operationBlocked = false;
        var contenderThread = new Thread(() =>
        {
            try
            {
                using var contender = new InstanceCoordination(suffix);
                operationBlocked = !contender.TryEnter();
            }
            catch (Exception error) when (Expected(error))
            {
                operationBlocked = false;
            }
        }) { IsBackground = true };
        contenderThread.Start();
        var contenderFinished = contenderThread.Join(TimeSpan.FromSeconds(10));
        var ready = handoff.Completion.WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();
        var childExited = child.WaitForExit(20_000);
        return pendingBeyondTwoSeconds && contenderFinished && operationBlocked && ready && childExited && child.ExitCode == 0;
    }

    private static async Task<bool> TestCoordinationAsync()
    {
        var suffix = "SelfTest-" + Guid.NewGuid().ToString("N");
        var operationBlocked = false;
        using (var owner = new InstanceCoordination(suffix))
        {
            if (!owner.TryEnter())
                return false;
            // Mutex ownership is thread-affine: hold/release synchronously on this thread.
            var contenderThread = new Thread(() =>
            {
                try
                {
                    using var contender = new InstanceCoordination(suffix);
                    operationBlocked = !contender.TryEnter();
                }
                catch (Exception error) when (Expected(error))
                {
                    operationBlocked = false;
                }
            }) { IsBackground = true };
            contenderThread.Start();
            if (!contenderThread.Join(TimeSpan.FromSeconds(10)))
                return false;
        }
        using var appMutex = new Mutex(false, @"Local\PhantomFudge-" + suffix);
        using var probe = new InstanceCoordination(suffix);
        using var server = new NamedPipeServerStream("PhantomFudge-" + suffix, PipeDirection.In, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var connection = server.WaitForConnectionAsync(timeout.Token);
        var reveal = probe.RevealAsync();
        await connection;
        var request = new byte[6];
        await server.ReadExactlyAsync(request, timeout.Token);
        await reveal;
        return operationBlocked && probe.AppIsRunning() && request.AsSpan().SequenceEqual("REVEAL"u8);
    }
}
