using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace PhantomFudge.Core.Answers;

public sealed class AgencyCliAnswerProvider : IAnswerProvider
{
    public const bool IsBackgroundExecutionQualified = true;
    private readonly AnswerSettings settings;
    private readonly string requestRoot;
    private readonly TimeSpan deadline;
    private readonly Func<ProcessStartInfo, ProcessStartInfo>? transformStartInfo;

    public AgencyCliAnswerProvider(AnswerSettings settings)
        : this(settings, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PhantomFudge", "agency-requests"), TimeSpan.FromSeconds(120))
    {
        for (var directory = new DirectoryInfo(requestRoot); directory is not null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) || File.Exists(Path.Combine(directory.FullName, ".git")))
                throw new AnswerProviderException("agency_workspace", "The Agency request directory must be outside every Git repository.");
    }

    internal AgencyCliAnswerProvider(AnswerSettings settings, string requestRoot, TimeSpan deadline,
        Func<ProcessStartInfo, ProcessStartInfo>? transformStartInfo = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        if (settings.Mode != AnswerProviderMode.Agency)
            throw new ArgumentException("The Agency provider requires an explicit Agency selection.", nameof(settings));
        this.settings = settings;
        this.requestRoot = CopilotCliProfile.CheckPath(requestRoot);
        this.deadline = deadline;
        this.transformStartInfo = transformStartInfo;
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
        if (request.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(request.ExactText) || request.ExactText.Length > 8192)
            throw new AnswerProviderException("agency_question", "Provide a nonempty question of at most 8192 characters.");
        ValidateLiteralQuestion(request.ExactText);
        CopilotCliProfile.CheckPath(requestRoot);
        var executable = ResolveExecutable(settings.AgencyPath);
        var owned = Path.Combine(requestRoot, Guid.NewGuid().ToString("N"));
        var work = Path.Combine(owned, "work");
        var home = Path.Combine(owned, "home");
        var profile = Path.Combine(owned, "profile");
        var logs = Path.Combine(owned, "logs");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(deadline);
        var token = timeout.Token;
        try
        {
            Prepare(work, home, profile);
            await VerifyVersionAsync(executable, work, home, profile, logs, token, cancellationToken).ConfigureAwait(false);
            var guard = new AgencyAuthenticationGuard();
            await using var process = new CopilotOwnedProcess(
                Transform(AgencyCliLaunch.Create(executable, settings, work, home, profile, logs)),
                token, guard.Inspect);
            try { await process.SendAsync(request.ExactText, token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw TimedOut(); }
            var parser = new AgencyJsonProtocol();
            long sequence = 0;
            await using var records = CopilotJsonProtocol.ReadRecordsAsync(process.Process.StandardOutput, token,
                allowEmptyLines: true).GetAsyncEnumerator(token);
            while (await ReadNextAsync(records, token, cancellationToken).ConfigureAwait(false))
            {
                process.CheckErrorLimit();
                var delta = parser.Accept(records.Current);
                if (delta is not null) yield return new(request.RequestId, ++sequence, AnswerEventKind.TextDelta, delta);
            }
            process.CheckErrorLimit();
            int exit;
            try { exit = await process.WaitAsync(token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw TimedOut(); }
            parser.Complete(exit);
            cancellationToken.ThrowIfCancellationRequested();
            yield return new(request.RequestId, ++sequence, AnswerEventKind.Completed);
        }
        finally
        {
            // Remove only this application's owned request tree, never Agency's shared cache.
            if (Directory.Exists(owned))
            {
                try { Directory.Delete(owned, recursive: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new AnswerProviderException("agency_cleanup", "The owned Agency request directory could not be removed.");
                }
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
            File.WriteAllText(Path.Combine(work, "agency.toml"), AgencyAnswerProfile.Toml, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(profile, "settings.json"), CopilotCliProfile.SettingsJson, new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new AnswerProviderException("agency_workspace", "The isolated Agency request workspace could not be prepared.");
        }
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
                !output.ToString().Trim().StartsWith(AgencyCliLaunch.VersionPrefix + " ", StringComparison.Ordinal))
                throw new AnswerProviderException("agency_version",
                    "This adapter requires the verified Agency 2026.10.7.4 build. No prompt was sent to an unverified version.");
        }
        catch (OperationCanceledException) when (!caller.IsCancellationRequested) { throw TimedOut(); }
        catch (Exception ex) when (ex is IOException or DecoderFallbackException)
        {
            throw new AnswerProviderException("agency_version", "The installed Agency version could not be verified safely.");
        }
    }

    private static async Task<bool> ReadNextAsync(IAsyncEnumerator<string> reader, CancellationToken token, CancellationToken caller)
    {
        try { return await reader.MoveNextAsync().ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested && !caller.IsCancellationRequested) { throw TimedOut(); }
        catch (IOException) { throw new AnswerProviderException("agency_io", "Agency's output stream could not be read."); }
    }

    internal static void ValidateLiteralQuestion(string text)
    {
        // Agency's successful probe qualified plain stdin, not attachment/command expansion.
        if (text.Contains('@') || text.Split(['\r', '\n']).Any(line =>
            line.TrimStart().StartsWith('/') || line.TrimStart().StartsWith('!') || line.TrimStart().StartsWith('&')))
            throw new AnswerProviderException("agency_literal_input",
                "Agency questions cannot contain @ references or lines beginning with /, !, or &. Edit the question to remove file or command syntax.");
    }

    private static AnswerProviderException TimedOut() => new("agency_timeout", "Agency exceeded the answer deadline.");

    private static bool IsExecutable(string path) =>
        Path.IsPathFullyQualified(path) &&
        Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
        File.Exists(path);

    private static AnswerProviderException Missing() => new("agency_not_found",
        "Choose an absolute path to the installed agency.exe. Shell commands, .cmd, and .ps1 wrappers are not accepted.");
}
