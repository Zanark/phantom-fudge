using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using PhantomFudge.Launcher;

namespace PhantomFudge.Core.Tests;

public sealed class InstalledLauncherTests
{
    [Fact]
    public void OnlyExactSupportedModesAreAccepted()
    {
        Assert.Equal(LaunchMode.Normal, LauncherContract.ParseArguments([]));
        Assert.Equal(LaunchMode.NotesOnly, LauncherContract.ParseArguments(["--notes-only"]));
        Assert.Equal(LaunchMode.Maintenance, LauncherContract.ParseArguments(["--maintenance"]));
        Assert.Equal(LaunchMode.Maintenance, LauncherContract.ParseArguments(["--maintenance", @"C:\synthetic root"]));
        Assert.Equal(LaunchMode.ValidateInstall, LauncherContract.ParseArguments(["--validate-install", @"C:\synthetic root"]));
        Assert.Equal(LaunchMode.SelfTest, LauncherContract.ParseArguments(["--self-test"]));
        Assert.Equal(LaunchMode.Invalid, LauncherContract.ParseArguments(["--notes-only", "extra"]));
        Assert.Equal(LaunchMode.Invalid, LauncherContract.ParseArguments(["--python", "anything.exe"]));
        Assert.Equal(LaunchMode.Invalid, LauncherContract.ParseArguments(["--documentation-screenshots"]));
        Assert.Equal(LaunchMode.Invalid, LauncherContract.ParseArguments(["--validate-install"]));
        Assert.Equal(LaunchMode.Invalid, LauncherContract.ParseArguments(["--maintenance", ""]));
    }

    [Fact]
    public void InstallationPathsAreFixedAndDataIsOutsideTheInstallation()
    {
        var root = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ".local", "installed example"));
        var data = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ".local", "profile example"));
        var paths = InstallPaths.FromLauncherDirectory(Path.Combine(root, "launcher"), data);
        Assert.Equal(Path.Combine(root, "app", "PhantomFudge.exe"), paths.App);
        Assert.Equal(Path.Combine(root, "runtime", "python", "python.exe"), paths.Python);
        Assert.Equal(Path.Combine(data, "PhantomFudge", "InstalledRuntime", ".local", "transcription.token"), paths.Token);
        Assert.Equal(Path.Combine(paths.Local, "transcription-runtime.json"), paths.Receipt);
        Assert.Throws<InvalidDataException>(() => InstallPaths.FromLauncherDirectory(root, data));
        Assert.Throws<InvalidDataException>(() => InstallPaths.FromLauncherDirectory("launcher", data));
    }

    [Theory]
    [InlineData("0.1.0")]
    [InlineData("0.1.0-preview.1+20261009")]
    public void ManifestReadsOnlyVersion(string version)
    {
        var json = JsonSerializer.Serialize(new { version, python = "untrusted.exe", arguments = "--untrusted" });
        Assert.Equal(version, LauncherContract.ReadVersion(json));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"version\":5}")]
    [InlineData("{\"version\":\"\"}")]
    [InlineData("{\"version\":\"a\\nb\"}")]
    [InlineData("{\"version\":\"../escape\"}")]
    public void InvalidManifestCannotChangeLaunchParameters(string json) =>
        Assert.Throws<InvalidDataException>(() => LauncherContract.ReadVersion(json));

    [Fact]
    public void LargeManifestIsRejectedBeforeParsing() =>
        Assert.Throws<InvalidDataException>(() => LauncherContract.ReadVersion(new string('x', 16385)));

    [Fact]
    public void TokensAreCryptographicUrlSafeAndBounded()
    {
        var tokens = Enumerable.Range(0, 100).Select(_ => LauncherContract.CreateToken()).ToArray();
        Assert.Equal(100, tokens.Distinct(StringComparer.Ordinal).Count());
        Assert.All(tokens, token =>
        {
            Assert.Matches("^[0-9a-f]{64}$", token);
            Assert.True(LauncherContract.IsValidToken(token));
        });
        Assert.False(LauncherContract.IsValidToken(new string('a', 31)));
        Assert.False(LauncherContract.IsValidToken(new string('a', 513)));
        Assert.False(LauncherContract.IsValidToken(new string('a', 32) + "\n"));
        Assert.False(LauncherContract.IsValidToken(new string('a', 32) + "\u00E9"));
    }

    [Fact]
    public void MutexSuffixMatchesExistingApplicationContract()
    {
        Assert.Equal("A477763EFFDE40D0D1B77AE8", LauncherContract.UserSuffix("S-1-5-21-test"));
        Assert.NotEqual(LauncherContract.UserSuffix("S-1-5-21-one"), LauncherContract.UserSuffix("S-1-5-21-two"));
    }

    [Fact]
    public void ManagerUsesBundledExecutableAndArgumentListWithoutShell()
    {
        var paths = SyntheticPaths();
        var start = ChildProcesses.Manager(paths, "start");
        Assert.Equal(paths.Python, start.FileName);
        Assert.Equal(new[] { "-I", "-B", "-X", "utf8", paths.Manager, "start" }, start.ArgumentList);
        Assert.False(start.UseShellExecute);
        Assert.True(start.CreateNoWindow);
        Assert.True(start.RedirectStandardInput && start.RedirectStandardOutput && start.RedirectStandardError);
        Assert.Equal(paths.DataRoot, start.Environment["PHANTOM_TRANSCRIPTION_WORKSPACE"]);
        Assert.False(start.Environment.ContainsKey("PHANTOM_TRANSCRIPTION_MODELS"));
        Assert.Equal(paths.Models, start.Environment["PHANTOM_TRANSCRIPTION_MODELS_ROOT"]);
        Assert.Throws<ArgumentException>(() => ChildProcesses.Manager(paths, "start --anything"));
    }

    [Fact]
    public void EnvironmentRemovesAmbientSpeechConfigurationAndCredentials()
    {
        var paths = SyntheticPaths();
        var start = new ProcessStartInfo();
        start.Environment["PHANTOM_TRANSCRIPTION_TOKEN"] = "not-a-real-token";
        start.Environment["PHANTOM_TRANSCRIPTION_MODEL_PATH"] = "untrusted-model";
        start.Environment["PHANTOM_TRANSCRIPTION_HOST"] = "external.invalid";
        start.Environment["PYTHONPATH"] = "untrusted-module-path";
        start.Environment["HF_TOKEN"] = "not-a-real-secret";
        ChildProcesses.ApplyInstalledEnvironment(start, paths);
        Assert.False(start.Environment.ContainsKey("PHANTOM_TRANSCRIPTION_TOKEN"));
        Assert.False(start.Environment.ContainsKey("PHANTOM_TRANSCRIPTION_MODEL_PATH"));
        Assert.False(start.Environment.ContainsKey("PHANTOM_TRANSCRIPTION_HOST"));
        Assert.False(start.Environment.ContainsKey("PYTHONPATH"));
        Assert.False(start.Environment.ContainsKey("HF_TOKEN"));
        Assert.Equal("1", start.Environment["HF_HUB_OFFLINE"]);
        Assert.Equal("1", start.Environment["TRANSFORMERS_OFFLINE"]);
        Assert.Equal(paths.Token, start.Environment["PHANTOM_TRANSCRIPTION_TOKEN_FILE"]);
    }

    [Fact]
    public void NotesOnlyDoesNotPassTokenOrArgumentsAndNormalLaunchUsesOnlyLocalToken()
    {
        var paths = SyntheticPaths();
        var notes = ChildProcesses.Application(paths, null);
        Assert.False(notes.Environment.ContainsKey("PHANTOM_TRANSCRIPTION_TOKEN"));
        Assert.Empty(notes.ArgumentList);
        var normal = ChildProcesses.Application(paths, new string('x', 64));
        Assert.Equal(new string('x', 64), normal.Environment["PHANTOM_TRANSCRIPTION_TOKEN"]);
        Assert.Equal(LauncherContract.ServiceUri, normal.Environment["PHANTOM_TRANSCRIPTION_URI"]);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void TokenCreationIsPrivateAndPreservesExistingContent()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var paths = SyntheticPaths();
        try
        {
            var first = LocalToken.ReadOrCreate(paths);
            var original = File.ReadAllBytes(paths.Token);
            Assert.Equal(first, LocalToken.ReadOrCreate(paths));
            Assert.Equal(original, File.ReadAllBytes(paths.Token));
            LocalToken.VerifyPrivateAcl(paths.Token);
            var existingUrlSafe = new string('z', 62) + "_-";
            File.WriteAllText(paths.Token, existingUrlSafe);
            Assert.Equal(existingUrlSafe, LocalToken.ReadOrCreate(paths));
            Assert.Equal(existingUrlSafe, File.ReadAllText(paths.Token));
            File.WriteAllText(paths.Token, "invalid");
            Assert.Throws<InvalidDataException>(() => LocalToken.ReadOrCreate(paths));
            Assert.Equal("invalid", File.ReadAllText(paths.Token));
        }
        finally
        {
            DeleteFixture(paths);
        }
    }

    private static void DeleteFixture(InstallPaths paths)
    {
        var fixture = Path.GetDirectoryName(paths.DataRoot)!;
        if (Directory.Exists(fixture))
            Directory.Delete(fixture, recursive: true);
    }

    private static InstallPaths SyntheticPaths()
    {
        var root = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ".local", "installer-release", "launcher-tests-" + Guid.NewGuid().ToString("N")));
        return new(Path.Combine(root, "install with spaces"), Path.Combine(root, "data"));
    }

    [Fact]
    public void InstallerMutexPresenceBlocksOnlyNormalAndNotesOnlyUntilDisposed()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Equal(@"Local\PhantomFudge-Installer-Maintenance", InstallerMaintenanceGate.Name);
        var name = @"Local\PhantomFudge-Installer-Test-" + Guid.NewGuid().ToString("N");
        Assert.False(InstallerMaintenanceGate.Blocks(LaunchMode.Normal, name));
        Assert.False(InstallerMaintenanceGate.Blocks(LaunchMode.NotesOnly, name));
        using (var gate = new Mutex(false, name))
        {
            Assert.True(InstallerMaintenanceGate.Blocks(LaunchMode.Normal, name));
            Assert.True(InstallerMaintenanceGate.Blocks(LaunchMode.NotesOnly, name));
            Assert.False(InstallerMaintenanceGate.Blocks(LaunchMode.Maintenance, name));
            Assert.False(InstallerMaintenanceGate.Blocks(LaunchMode.ValidateInstall, name));
            Assert.False(InstallerMaintenanceGate.Blocks(LaunchMode.SelfTest, name));
            Assert.False(InstallerMaintenanceGate.Blocks(LaunchMode.Invalid, name));
        }
        Assert.False(InstallerMaintenanceGate.Blocks(LaunchMode.Normal, name));
        Assert.False(InstallerMaintenanceGate.Blocks(LaunchMode.NotesOnly, name));
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task ApplicationHandoffRemainsPendingBeyondTwoSecondsUntilItsMutexAppears()
    {
        if (!OperatingSystem.IsWindows()) return;
        var name = @"Local\PhantomFudge-Delayed-App-Test-" + Guid.NewGuid().ToString("N");
        var exited = 0;
        var handoff = new ApplicationHandoff(() => Volatile.Read(ref exited) != 0,
            () => InstallerMaintenanceGate.Blocks(LaunchMode.Normal, name));
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(2300));
            Assert.True(handoff.Pending);
            using var appMutex = new Mutex(false, name);
            Assert.True(await handoff.Completion.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.False(handoff.Pending);
        }
        finally
        {
            Volatile.Write(ref exited, 1);
            await handoff.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task ApplicationHandoffReleasesOnlyAfterConfirmedExitWithoutReportingReadiness()
    {
        var exited = 0;
        var handoff = new ApplicationHandoff(() => Volatile.Read(ref exited) != 0, () => false);
        try
        {
            Assert.True(handoff.Pending);
            Volatile.Write(ref exited, 1);
            Assert.False(await handoff.Completion.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.False(handoff.Pending);
        }
        finally
        {
            Volatile.Write(ref exited, 1);
            await handoff.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task ApplicationHandoffKeepsTheCloseGuardDuringUncertainLiveness()
    {
        var exited = 0;
        var handoff = new ApplicationHandoff(
            () => Volatile.Read(ref exited) != 0 ? true : throw new InvalidOperationException("Synthetic inspection failure."),
            () => throw new UnauthorizedAccessException("Synthetic mutex access failure."));
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250));
            Assert.True(handoff.Pending);
            Volatile.Write(ref exited, 1);
            Assert.False(await handoff.Completion.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.False(handoff.Pending);
        }
        finally
        {
            Volatile.Write(ref exited, 1);
            await handoff.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task ApplicationHandoffDoesNotClaimReadinessForAnAlreadyExitedChild()
    {
        var handoff = new ApplicationHandoff(() => true, () => true);
        Assert.False(await handoff.Completion);
        Assert.False(handoff.Pending);
    }
}
