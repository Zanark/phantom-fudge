using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using PhantomFudge.Core.Diagnostics;

namespace PhantomFudge.Core.Answers;

public sealed class AgencyCliAnswerProvider : IAnswerProvider, IAsyncDisposable
{
    public const bool IsBackgroundExecutionQualified = true;
    public static string DefaultModel => AgencyAnswerProfile.DefaultModel;
    public static string ReasoningEffort => AgencyAnswerProfile.ReasoningEffort;
    public string EffectiveModel => settings.Model ?? DefaultModel;
    private readonly AnswerSettings settings;
    private readonly string requestRoot;
    private readonly TimeSpan deadline;
    private readonly Func<ProcessStartInfo, ProcessStartInfo>? transformStartInfo;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object stopLock = new();
    private CancellationTokenSource stopping = new();
    private CancellationTokenSource? engineLifetime;
    private AgencyAcpConnection? connection;
    private string? owned;
    private string? work;
    private ExecutableStamp? verifiedVersion;
    private bool disposed;
    private readonly bool prepareNextAutomatically;
    private Task? preparation;
    internal Action<DiagnosticEvent, Guid?, long, string>? StageObserver { get; set; }

    public AgencyCliAnswerProvider(AnswerSettings settings)
        : this(settings, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PhantomFudge", "agency-requests"), TimeSpan.FromSeconds(120))
    {
        for (var directory = new DirectoryInfo(requestRoot); directory is not null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) || File.Exists(Path.Combine(directory.FullName, ".git")))
                throw new AnswerProviderException("agency_workspace", "The Agency request directory must be outside every Git repository.");
    }

    internal AgencyCliAnswerProvider(AnswerSettings settings, string requestRoot, TimeSpan deadline,
        Func<ProcessStartInfo, ProcessStartInfo>? transformStartInfo = null, bool prepareNextAutomatically = true)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        if (settings.Mode != AnswerProviderMode.Agency)
            throw new ArgumentException("The Agency provider requires an explicit Agency selection.", nameof(settings));
        this.settings = settings;
        this.requestRoot = CopilotCliProfile.CheckPath(requestRoot);
        this.deadline = deadline;
        this.transformStartInfo = transformStartInfo;
        this.prepareNextAutomatically = prepareNextAutomatically;
    }

    public static string ResolveExecutable(string? path = null)
    {
        if (path is not null)
        {
            if (IsExecutable(path)) return Path.GetFullPath(path);
            throw Missing();
        }

        var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "agency", "CurrentVersion", "agency.exe");
        if (IsExecutable(installed)) return installed;
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (!Path.IsPathFullyQualified(directory) ||
                string.Equals(Path.TrimEndingDirectorySeparator(directory), Environment.CurrentDirectory, StringComparison.OrdinalIgnoreCase))
                continue;
            var candidate = Path.Combine(directory, "agency.exe");
            if (IsExecutable(candidate)) return candidate;
        }
        throw Missing();
    }

    public async IAsyncEnumerable<AnswerEvent> StreamAsync(AnswerRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        request.Clarification?.ValidateFor(request.ExactText);
        if (request.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(request.ExactText) || request.ExactText.Length > 8192)
            throw new AnswerProviderException("agency_question", "Provide a nonempty question of at most 8192 characters.");
        var prompt = AgencyAnswerPrompt.Create(request);
        var watch = Stopwatch.StartNew();
        using var timeout = CreateOperation(cancellationToken);
        timeout.CancelAfter(deadline);
        var token = timeout.Token;
        await EnterAsync(token, cancellationToken).ConfigureAwait(false);
        var completed = false;
        try
        {
            await ReadyAsync(request.RequestId, watch, token, cancellationToken).ConfigureAwait(false);
            using var stopEngine = token.Register(() => engineLifetime?.Cancel());
            await PrepareSessionAsync(token, cancellationToken).ConfigureAwait(false);
            Record(DiagnosticEvent.ProviderReady, request.RequestId, watch, "session_ready");
            Record(DiagnosticEvent.ProviderReady, request.RequestId, watch, EffectiveModel switch
            {
                "gpt-5.6-sol-fast" => "gpt-5.6-sol-fast.low",
                "claude-haiku-5.5" => "claude-haiku-5.5.low",
                "gpt-5.4-mini" => "gpt-5.4-mini.low",
                _ => "explicit_model.low"
            });
            long sequence = 0;
            var first = true;
            await using var deltas = connection!.PromptAsync(prompt,
                () => Record(DiagnosticEvent.ModelStarted, request.RequestId, watch, "acp_prompt_submitted"),
                token, allowClarification: request.Clarification is { Confirmed: false }).GetAsyncEnumerator(token);
            while (await ReadNextAsync(deltas, token, cancellationToken).ConfigureAwait(false))
            {
                if (first)
                {
                    Record(DiagnosticEvent.FirstAnswerText, request.RequestId, watch, "provider_delta");
                    first = false;
                }
                yield return new(request.RequestId, ++sequence, AnswerEventKind.TextDelta, deltas.Current);
            }
            await StepAsync(() => connection.CloseSessionAsync(token), token, cancellationToken).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            completed = true;
            Record(DiagnosticEvent.AnswerCompleted, request.RequestId, watch, "acp_end_turn_closed");
            yield return new(request.RequestId, ++sequence, AnswerEventKind.Completed);
        }
        finally
        {
            try
            {
                if (!completed) await ResetEngineAsync().ConfigureAwait(false);
                Record(DiagnosticEvent.RequestCleanup, request.RequestId, watch, completed ? "session_closed" : "engine_stopped");
            }
            finally
            {
                gate.Release();
                if (completed && prepareNextAutomatically) BeginPreparation(token);
            }
        }
    }

    // Starts/reuses the transport without requesting a session or submitting a model prompt.
    public Task WarmUpTransportAsync(CancellationToken cancellationToken = default) =>
        WarmUpCoreAsync(prepareSession: false, cancellationToken);

    // Prepares one empty conversation, but never submits a model prompt or infers user input.
    public Task WarmUpAsync(CancellationToken cancellationToken = default) =>
        WarmUpCoreAsync(prepareSession: true, cancellationToken);

    private async Task WarmUpCoreAsync(bool prepareSession, CancellationToken cancellationToken)
    {
        using var timeout = CreateOperation(cancellationToken);
        timeout.CancelAfter(deadline);
        await EnterAsync(timeout.Token, cancellationToken).ConfigureAwait(false);
        var ready = false;
        try
        {
            var watch = Stopwatch.StartNew();
            await ReadyAsync(null, watch, timeout.Token, cancellationToken).ConfigureAwait(false);
            using var stopEngine = timeout.Token.Register(() => engineLifetime?.Cancel());
            if (prepareSession)
                await PrepareSessionAsync(timeout.Token, cancellationToken).ConfigureAwait(false);
            Record(DiagnosticEvent.ProviderReady, null, watch, prepareSession ? "empty_session_ready" : "transport_ready");
            ready = true;
        }
        finally
        {
            try { if (!ready) await ResetEngineAsync().ConfigureAwait(false); }
            finally { gate.Release(); }
        }
    }

    public async Task StopAsync()
    {
        lock (stopLock) stopping.Cancel();
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await ResetEngineAsync().ConfigureAwait(false);
            lock (stopLock)
            {
                stopping.Dispose();
                stopping = new CancellationTokenSource();
                if (disposed) stopping.Cancel();
            }
        }
        finally { gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        lock (stopLock)
        {
            if (disposed) return;
            disposed = true;
        }
        await StopAsync().ConfigureAwait(false);
        if (preparation is not null) await preparation.ConfigureAwait(false);
    }

    private Task PrepareSessionAsync(CancellationToken token, CancellationToken caller) =>
        connection!.HasPreparedSession ? Task.CompletedTask :
            StepAsync(() => connection.NewSessionAsync(work!, EffectiveModel, token), token, caller);

    private void BeginPreparation(CancellationToken operation)
    {
        lock (stopLock)
        {
            if (disposed || stopping.IsCancellationRequested || operation.IsCancellationRequested) return;
            preparation = PrepareNextAsync();
        }
    }

    private async Task PrepareNextAsync()
    {
        try { await WarmUpAsync().ConfigureAwait(false); }
        catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException) { }
        catch (AnswerProviderException)
        {
            LocalDiagnostics.Record(DiagnosticEvent.ProviderReady, code: "background_prepare_failed", provider: "Agency");
        }
    }

    private CancellationTokenSource CreateOperation(CancellationToken caller)
    {
        lock (stopLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return CancellationTokenSource.CreateLinkedTokenSource(caller, stopping.Token);
        }
    }

    private async Task EnterAsync(CancellationToken token, CancellationToken caller)
    {
        try { await gate.WaitAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!caller.IsCancellationRequested && !IsStopping) { throw TimedOut(); }
    }

    private bool IsStopping { get { lock (stopLock) return stopping.IsCancellationRequested; } }

    private async Task StepAsync(Func<Task> action, CancellationToken token, CancellationToken caller)
    {
        try { await action().ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested && !caller.IsCancellationRequested && !IsStopping)
        { throw TimedOut(); }
    }

    private async Task ReadyAsync(Guid? id, Stopwatch watch, CancellationToken token, CancellationToken caller)
    {
        CopilotCliProfile.CheckPath(requestRoot);
        var executable = ResolveExecutable(settings.AgencyPath);
        var stamp = ExecutableStamp.Read(executable);
        if (connection is not null && connection.CanReuse && verifiedVersion == stamp)
        {
            ValidateOwnedProfile();
            Record(DiagnosticEvent.ProviderReady, id, watch, "warm_engine");
            return;
        }
        await ResetEngineAsync().ConfigureAwait(false);
        owned = Path.Combine(requestRoot, Guid.NewGuid().ToString("N"));
        work = Path.Combine(owned, "work");
        var home = Path.Combine(owned, "home");
        var profile = Path.Combine(owned, "profile");
        var logs = Path.Combine(owned, "logs");
        Prepare(work, home, profile);
        if (verifiedVersion != stamp)
        {
            await VerifyVersionAsync(executable, work, home, profile, logs, token, caller).ConfigureAwait(false);
            if (ExecutableStamp.Read(executable) != stamp)
                throw new AnswerProviderException("agency_version", "Agency changed during version verification. No question was sent.");
            verifiedVersion = stamp;
            Record(DiagnosticEvent.ProviderVersion, id, watch, "identity_checked");
        }
        else Record(DiagnosticEvent.ProviderVersion, id, watch, "cached_unchanged");
        engineLifetime = new CancellationTokenSource();
        using var stopEngine = token.Register(() => engineLifetime?.Cancel());
        var guard = new AgencyAuthenticationGuard();
        var process = new CopilotOwnedProcess(Transform(AgencyCliLaunch.Create(executable, settings, work, home, profile, logs)),
            engineLifetime.Token, guard.Inspect);
        connection = new(process, engineLifetime.Token);
        Record(DiagnosticEvent.ProviderLaunch, id, watch, "acp");
        await StepAsync(() => connection.InitializeAsync(token), token, caller).ConfigureAwait(false);
        Record(DiagnosticEvent.ProviderReady, id, watch, "cold_engine");
    }

    private async Task ResetEngineAsync()
    {
        engineLifetime?.Cancel();
        try
        {
            if (connection is not null) await connection.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            connection = null;
            engineLifetime?.Dispose();
            engineLifetime = null;
            if (owned is not null && Directory.Exists(owned))
            {
                try { Directory.Delete(owned, recursive: true); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                { throw new AnswerProviderException("agency_cleanup", "The isolated Agency workspace could not be removed."); }
            }
            owned = work = null;
        }
    }

    private void Record(DiagnosticEvent kind, Guid? id, Stopwatch watch, string code)
    {
        var elapsed = watch.ElapsedMilliseconds;
        LocalDiagnostics.Record(kind, id, elapsed, code: code, provider: "Agency");
        StageObserver?.Invoke(kind, id, elapsed, code);
    }

    private sealed record ExecutableStamp(string Path, long Length, long Modified, long Created)
    {
        internal static ExecutableStamp Read(string path)
        {
            try
            {
                var file = new FileInfo(path);
                return new(path, file.Length, file.LastWriteTimeUtc.Ticks, file.CreationTimeUtc.Ticks);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                throw new AnswerProviderException("agency_version", "The installed Agency executable could not be verified.");
            }
        }
    }

    private ProcessStartInfo Transform(ProcessStartInfo info) => transformStartInfo?.Invoke(info) ?? info;

    private static void Prepare(string work, string home, string profile)
    {
        try
        {
            Directory.CreateDirectory(work);
            Directory.CreateDirectory(home);
            Directory.CreateDirectory(profile);
            Directory.CreateDirectory(Path.Combine(profile, "agents"));
            File.WriteAllText(Path.Combine(work, "agency.toml"), AgencyAnswerProfile.Toml, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(profile, "settings.json"), CopilotCliProfile.SettingsJson, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(profile, "agents", AgencyAnswerProfile.AgentFileName),
                AgencyAnswerProfile.AgentDefinition, new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new AnswerProviderException("agency_workspace", "The isolated Agency request workspace could not be prepared.");
        }
    }

    private void ValidateOwnedProfile()
    {
        try
        {
            var profile = Path.Combine(owned!, "profile");
            var home = Path.Combine(owned!, "home");
            foreach (var directory in new[] { owned!, work!, profile, home }) CopilotCliProfile.CheckPath(directory);
            foreach (var (path, expected) in new[]
            {
                (Path.Combine(work!, "agency.toml"), AgencyAnswerProfile.Toml),
                (Path.Combine(profile, "settings.json"), CopilotCliProfile.SettingsJson),
                (Path.Combine(profile, "agents", AgencyAnswerProfile.AgentFileName), AgencyAnswerProfile.AgentDefinition)
            })
            {
                var file = new FileInfo(path);
                if (!file.Exists || file.Length > 4096 || file.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
                    (file.Name == "settings.json"
                        ? !ValidGeneratedSettings(File.ReadAllText(path, Encoding.UTF8))
                        : File.ReadAllText(path, Encoding.UTF8) != expected)) throw ProfileChanged();
            }
            foreach (var root in new[] { profile, Path.Combine(home, ".copilot"), Path.Combine(home, ".claude"),
                         Path.Combine(home, ".agents"), Path.Combine(work!, ".github"), Path.Combine(work!, ".agents") })
                foreach (var name in new[] { "agents", "skills", "hooks", "installed-plugins", "plugins" })
                {
                    var path = Path.Combine(root, name);
                    if (root == profile && name == "agents")
                    {
                        CopilotCliProfile.CheckPath(path);
                        if (Directory.EnumerateFileSystemEntries(path).Any(entry =>
                            Path.GetFileName(entry) != AgencyAnswerProfile.AgentFileName)) throw ProfileChanged();
                        continue;
                    }
                    if (Directory.Exists(path) && (new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint) ||
                        Directory.EnumerateFileSystemEntries(path).Any())) throw ProfileChanged();
                }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw ProfileChanged();
        }
        catch (AnswerProviderException error) when (error.Code == "copilot_profile")
        {
            throw ProfileChanged();
        }
    }

    private static AnswerProviderException ProfileChanged() => new("agency_profile",
        "The isolated answer profile changed unexpectedly. The engine was stopped without submitting a question.");

    internal static bool ValidGeneratedSettings(string text)
    {
        if (text.Length > 4096) return false;
        try
        {
            using var expected = JsonDocument.Parse(CopilotCliProfile.SettingsJson);
            using var actual = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 8 });
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in actual.RootElement.EnumerateObject())
            {
                if (!seen.Add(property.Name)) return false;
                // The qualified Agency wrapper adds cosmetic launch announcements to settings.json.
                if (property.Name == "companyAnnouncements")
                {
                    if (property.Value.ValueKind != JsonValueKind.Array || property.Value.GetArrayLength() > 16 ||
                        property.Value.EnumerateArray().Any(value => value.ValueKind != JsonValueKind.String ||
                            value.GetString()!.Length > 512)) return false;
                }
                else if (!expected.RootElement.TryGetProperty(property.Name, out var value) ||
                    !JsonElement.DeepEquals(value, property.Value)) return false;
            }
            return expected.RootElement.EnumerateObject().All(property => seen.Contains(property.Name));
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException) { return false; }
    }

    private async Task VerifyVersionAsync(string executable, string work, string home, string profile, string logs,
        CancellationToken token, CancellationToken caller)
    {
        try
        {
            var guard = new AgencyAuthenticationGuard();
            await using var process = new CopilotOwnedProcess(
                Transform(AgencyCliLaunch.Create(executable, settings, work, home, profile, logs, versionOnly: true)),
                token, guard.Inspect);
            process.Process.StandardInput.Close();
            var output = new StringBuilder();
            var buffer = new char[1024];
            int count;
            while ((count = await process.Process.StandardOutput.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) != 0)
            {
                if (output.Length + count > 4096) throw CopilotJsonProtocol.Limit();
                output.Append(buffer, 0, count);
            }
            process.CheckErrorLimit();
            if (await process.WaitAsync(token).ConfigureAwait(false) != 0 ||
                !AgencyCliLaunch.HasVersionIdentity(output.ToString()))
                throw new AnswerProviderException("agency_version",
                    "The installed executable did not identify itself as Agency. Check the configured Agency path, then Reload setup; no question was sent.");
        }
        catch (OperationCanceledException) when (!caller.IsCancellationRequested && !IsStopping) { throw TimedOut(); }
        catch (Exception ex) when (ex is IOException or DecoderFallbackException)
        {
            throw new AnswerProviderException("agency_version", "The installed Agency version could not be verified safely.");
        }
    }

    private async Task<bool> ReadNextAsync(IAsyncEnumerator<string> reader, CancellationToken token, CancellationToken caller)
    {
        try { return await reader.MoveNextAsync().ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested && !caller.IsCancellationRequested && !IsStopping) { throw TimedOut(); }
        catch (IOException) { throw new AnswerProviderException("agency_io", "Agency's output stream could not be read."); }
    }

    private static AnswerProviderException TimedOut() => new("agency_timeout", "Agency exceeded the answer deadline.");

    private static bool IsExecutable(string path) =>
        Path.IsPathFullyQualified(path) &&
        Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
        File.Exists(path);

    private static AnswerProviderException Missing() => new("agency_not_found",
        "Choose an absolute path to the installed agency.exe. Shell commands, .cmd, and .ps1 wrappers are not accepted.");
}
