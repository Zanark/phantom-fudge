using System.Diagnostics;
using PhantomFudge.Core.Answers;

namespace PhantomFudge.Core.Tests;

public sealed class AgencyCliAnswerProviderTests : IDisposable
{
    private readonly string root = Path.Combine(Environment.CurrentDirectory, "AgencyTests-" + Guid.NewGuid().ToString("N"));

    public AgencyCliAnswerProviderTests() => Directory.CreateDirectory(root);

    [Theory]
    [InlineData("/help")]
    [InlineData("@C:\\synthetic-only\\never-read.txt")]
    [InlineData("Question\n !shell")]
    [InlineData("Question\n&delegate")]
    [InlineData("Question\r/help")]
    public async Task FileOrCommandSyntaxIsRejectedBeforeStartingAnyProcess(string question)
    {
        var provider = CreateProvider(_ => throw new InvalidOperationException("Must not start."));
        var error = await Error(provider, question);
        Assert.Equal("agency_literal_input", error.Code);
        Assert.DoesNotContain(question, error.Message);
    }

    [Fact]
    public async Task AlreadyCancelledRequestDoesNotRunAnything()
    {
        var provider = CreateProvider(_ => throw new InvalidOperationException("Must not start."));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var unused in provider.StreamAsync(new(Guid.NewGuid(), "Synthetic"), cancellation.Token)) { }
        });
    }

    [Fact]
    public void ModeAndEndpointCannotSelectAnotherProvider()
    {
        Assert.Throws<ArgumentException>(() => new AgencyCliAnswerProvider(new(AnswerProviderMode.Copilot)));
        Assert.Throws<ArgumentException>(() => new AgencyCliAnswerProvider(new(AnswerProviderMode.LocalModel,
            Model: "synthetic", LocalEndpoint: "http://127.0.0.1:12345/v1")));
        Assert.Throws<ArgumentException>(() => new AgencyCliAnswerProvider(new(AnswerProviderMode.Agency,
            LocalEndpoint: "http://127.0.0.1:12345/v1")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyQuestionsAreRejectedWithoutSideEffects(string question) =>
        Assert.Equal("agency_question", (await Error(CreateProvider(_ => throw new InvalidOperationException("Must not start.")), question)).Code);

    [Theory]
    [InlineData("agency.exe")]
    [InlineData("agency copilot --allow-all")]
    [InlineData("C:\\missing\\agency.exe")]
    [InlineData("C:\\installed\\agency.cmd")]
    [InlineData("C:\\installed\\agency.ps1")]
    public void ResolutionDoesNotEvaluateShellStringsOrWrappers(string path) =>
        Assert.Equal("agency_not_found", Assert.Throws<AnswerProviderException>(() =>
            AgencyCliAnswerProvider.ResolveExecutable(path)).Code);

    [Fact]
    public void AbsoluteExecutablePathsRemainSinglePaths()
    {
        var path = Path.Combine(root, "agency.exe");
        File.WriteAllText(path, "synthetic; never executed");
        Assert.Equal(path, AgencyCliAnswerProvider.ResolveExecutable(path));
    }

    [Fact]
    public void LaunchMatchesProbeAndHasNoQuestionInArgumentsOrProviderOverrides()
    {
        var settings = new AnswerSettings(AnswerProviderMode.Agency, Model: "synthetic");
        var info = AgencyCliLaunch.Create(Path.Combine(root, "agency.exe"), settings,
            Path.Combine(root, "work"), Path.Combine(root, "home"), Path.Combine(root, "profile"), Path.Combine(root, "logs"));
        Assert.False(info.UseShellExecute);
        Assert.True(info.CreateNoWindow && info.RedirectStandardInput && info.RedirectStandardOutput && info.RedirectStandardError);
        Assert.Equal(Path.Combine(root, "work"), info.WorkingDirectory);
        Assert.Equal(["--verbosity", "error", "--log-dir", Path.Combine(root, "logs"),
            .. AgencyAnswerProfile.CandidateArguments, "--model", "synthetic"], info.ArgumentList);
        foreach (var required in new[] { "--profile-only", "--no-default-mcps", "--no-config-plugins",
            "--no-aec", "--no-input-processing", "--no-custom-instructions", "--disable-builtin-mcps",
            "--available-tools=phantom_fudge_no_tools", "--deny-tool=read", "--no-remote-export" })
            Assert.Contains(required, info.ArgumentList);
        Assert.DoesNotContain(info.ArgumentList, arg => arg is "--allow-all" or "--allow-all-tools" or
            "--allow-all-paths" or "--allow-all-urls" or "--yolo" or "--prompt" or "-p" or "--plugin" or "--mcp" or "login");
    }

    [Fact]
    public void EnvironmentRetainsOnlyProbeOsRootsAndNeverCopiesTokensOrCustomRouting()
    {
        var info = new ProcessStartInfo();
        info.Environment["APPDATA"] = Path.Combine(root, "original-roaming");
        info.Environment["LOCALAPPDATA"] = Path.Combine(root, "original-local");
        string[] forbidden = ["AGENCY_CONTEXT", "AGENCY_ASA_TOKEN", "AGENCY_ADO_TOKEN", "AGENCY_PROVIDER_URI",
            "AGENCY_AEC_ENABLED", "COPILOT_PROVIDER_BASE_URL", "COPILOT_PROVIDER_API_KEY", "COPILOT_OFFLINE",
            "GITHUB_TOKEN", "GH_TOKEN", "NODE_OPTIONS", "OPENAI_API_KEY", "HTTP_PROXY", "HTTPS_PROXY",
            "COPILOT_CUSTOM_INSTRUCTIONS_DIRS", "CLAUDE_CONFIG_DIR", "OTEL_EXPORTER_OTLP_ENDPOINT"];
        foreach (var name in forbidden) info.Environment[name] = "synthetic-parent-only";
        info.Environment["PATH"] = "." + Path.PathSeparator + root + Path.PathSeparator + "relative";
        AgencyCliLaunch.SanitizeEnvironment(info, Path.Combine(root, "home"), Path.Combine(root, "profile"));
        foreach (var name in forbidden) Assert.False(info.Environment.ContainsKey(name));
        Assert.Equal(Path.Combine(root, "original-roaming"), info.Environment["APPDATA"]);
        Assert.Equal(Path.Combine(root, "original-local"), info.Environment["LOCALAPPDATA"]);
        Assert.Equal(Path.Combine(root, "home"), info.Environment["HOME"]);
        Assert.Equal(Path.Combine(root, "home"), info.Environment["USERPROFILE"]);
        Assert.Equal(Path.Combine(root, "profile"), info.Environment["COPILOT_HOME"]);
        Assert.Equal(root, info.Environment["PATH"]);
    }

    [Fact]
    public async Task FullProviderStreamsSanitizedRecordedShapeThroughFakeProcessesOnly()
    {
        if (!OperatingSystem.IsWindows()) return;
        var launches = 0;
        var provider = CreateProvider(info =>
        {
            launches++;
            Assert.DoesNotContain("Synthetic question", string.Join(" ", info.ArgumentList));
            Assert.Equal(AgencyAnswerProfile.Toml, File.ReadAllText(Path.Combine(info.WorkingDirectory, "agency.toml")));
            Assert.Equal(CopilotCliProfile.SettingsJson, File.ReadAllText(Path.Combine(info.Environment["COPILOT_HOME"]!, "settings.json")));
            return Fake(info, IsVersion(info)
                ? $"[Console]::Out.WriteLine('{AgencyCliLaunch.VersionPrefix} (target: synthetic)')"
                : "[Console]::Error.WriteLine('Resolving synthetic test'); $question=[Console]::In.ReadToEnd(); if($question -ne 'Synthetic question'){exit 5}; " +
                    Emit(AgencyProtocolTests.Replay));
        });
        var request = new AnswerRequest(Guid.NewGuid(), "Synthetic question");
        var events = new List<AnswerEvent>();
        await foreach (var item in provider.StreamAsync(request)) events.Add(item);
        Assert.Equal("PHANTOM_OK", string.Concat(events.Select(e => e.Text)));
        Assert.Equal([AnswerEventKind.TextDelta, AnswerEventKind.TextDelta, AnswerEventKind.Completed], events.Select(e => e.Kind));
        Assert.All(events, item => Assert.Equal(request.RequestId, item.RequestId));
        Assert.Equal([1L, 2L, 3L], events.Select(e => e.Sequence));
        Assert.Equal(2, launches); // Version probe + one request; never retries.
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(root, "requests")));
    }

    [Fact]
    public async Task UnsupportedVersionNeverReceivesTheQuestionOrStartsASecondProcess()
    {
        if (!OperatingSystem.IsWindows()) return;
        var launches = 0;
        var provider = CreateProvider(info =>
        {
            launches++;
            Assert.True(IsVersion(info));
            return Fake(info, "[Console]::Out.WriteLine('agency unverified')");
        });
        Assert.Equal("agency_version", (await Error(provider)).Code);
        Assert.Equal(1, launches);
    }

    [Fact]
    public async Task AuthenticationNoticeStopsFakeProcessWithoutInvokingLogin()
    {
        if (!OperatingSystem.IsWindows()) return;
        var provider = ScriptProvider("[Console]::Error.Write('opening your browser synthetic-private'); [Console]::Error.Flush(); Start-Sleep -Seconds 90");
        var error = await Error(provider);
        Assert.Equal("agency_auth_required", error.Code);
        Assert.DoesNotContain("synthetic-private", error.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(root, "requests")));
    }

    [Fact]
    public async Task PartialAnswerSurvivesCancellationAndWorkspaceIsReaped()
    {
        if (!OperatingSystem.IsWindows()) return;
        var prefix = string.Join('\n', AgencyProtocolTests.Replay.Split('\n').Take(9));
        var provider = ScriptProvider(Emit(prefix) + "; [Console]::Out.Flush(); Start-Sleep -Seconds 90");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using (var stream = provider.StreamAsync(new(Guid.NewGuid(), "Synthetic question"), cancellation.Token).GetAsyncEnumerator())
        {
            Assert.True(await stream.MoveNextAsync());
            Assert.Equal("PHANTOM_O", stream.Current.Text);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await stream.MoveNextAsync());
        }
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(root, "requests")));
    }

    [Fact]
    public async Task MissingTerminalAndOversizedOutputNeverComplete()
    {
        if (!OperatingSystem.IsWindows()) return;
        var prefix = string.Join('\n', AgencyProtocolTests.Replay.Split('\n').Take(9));
        Assert.Equal("copilot_incomplete", (await Error(ScriptProvider(Emit(prefix)))).Code);
        Assert.Equal("copilot_limit", (await Error(ScriptProvider("[Console]::Out.Write(('x' * 270000)); [Console]::Out.Flush()"))).Code);
    }

    [Fact]
    public async Task TerminalJsonWithFailingProcessExitIsNotSuccess()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Equal("agency_failed", (await Error(ScriptProvider(Emit(AgencyProtocolTests.Replay) + "; exit 7"))).Code);
    }

    [Fact]
    public async Task RequestDeadlineStopsFakeProcessesAndRemovesOwnedWorkspace()
    {
        if (!OperatingSystem.IsWindows()) return;
        var provider = CreateProvider(info => Fake(info, IsVersion(info)
            ? $"[Console]::Out.WriteLine('{AgencyCliLaunch.VersionPrefix} (target: synthetic)')"
            : "Start-Sleep -Seconds 90"), TimeSpan.FromSeconds(2));
        Assert.Equal("agency_timeout", (await Error(provider)).Code);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(root, "requests")));
    }

    private AgencyCliAnswerProvider ScriptProvider(string script) => CreateProvider(info =>
        Fake(info, IsVersion(info) ? $"[Console]::Out.WriteLine('{AgencyCliLaunch.VersionPrefix} (target: synthetic)')" : script));

    private AgencyCliAnswerProvider CreateProvider(Func<ProcessStartInfo, ProcessStartInfo> transform, TimeSpan? timeout = null)
    {
        var executable = Path.Combine(root, "agency.exe");
        File.WriteAllText(executable, "synthetic nonexecutable sentinel; never launched");
        return new(new(AnswerProviderMode.Agency, AgencyPath: executable), Path.Combine(root, "requests"),
            timeout ?? TimeSpan.FromSeconds(20), transform);
    }

    private static bool IsVersion(ProcessStartInfo info) => info.ArgumentList.SequenceEqual(["--version"]);

    private static ProcessStartInfo Fake(ProcessStartInfo info, string script)
    {
        info.FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        info.ArgumentList.Clear();
        foreach (var value in new[] { "-NoProfile", "-NoLogo", "-NonInteractive", "-Command", script }) info.ArgumentList.Add(value);
        return info;
    }

    private static string Emit(string text) => string.Join("; ", text.Split('\n').Select(line =>
        "[Console]::Out.WriteLine('" + line.TrimEnd('\r').Replace("'", "''") + "')"));

    private static async Task<AnswerProviderException> Error(AgencyCliAnswerProvider provider, string question = "Synthetic question") =>
        await Assert.ThrowsAsync<AnswerProviderException>(async () =>
        {
            await foreach (var unused in provider.StreamAsync(new(Guid.NewGuid(), question))) { }
        });

    public void Dispose() => Directory.Delete(root, recursive: true);
}
