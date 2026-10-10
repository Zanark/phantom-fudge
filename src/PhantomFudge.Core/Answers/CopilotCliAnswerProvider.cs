using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace PhantomFudge.Core.Answers;

public sealed class CopilotCliAnswerProvider : IAnswerProvider
{
    private readonly AnswerSettings settings;
    private readonly string profileDirectory;
    private readonly string requestRoot;
    private readonly TimeSpan deadline;
    private readonly Func<ProcessStartInfo, ProcessStartInfo>? transformStartInfo;

    public static string ResolveExecutable(string? cliPath = null) => CopilotCliLaunch.ResolveExecutable(cliPath);

    public static void InitializeProfile(string profileDirectory) => CopilotCliProfile.Initialize(profileDirectory);

    public CopilotCliAnswerProvider(AnswerSettings settings, string profileDirectory)
        : this(settings, profileDirectory, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhantomFudge", "copilot-requests"),
            TimeSpan.FromSeconds(120))
    {
        for (var directory = new DirectoryInfo(requestRoot); directory is not null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) || File.Exists(Path.Combine(directory.FullName, ".git")))
                throw new AnswerProviderException("copilot_workspace", "The isolated Copilot request directory must be outside every Git repository.");
    }

    internal CopilotCliAnswerProvider(AnswerSettings settings, string profileDirectory, string requestRoot, TimeSpan deadline,
        Func<ProcessStartInfo, ProcessStartInfo>? transformStartInfo = null)
    {
        settings.Validate();
        if (settings.Mode is not (AnswerProviderMode.Copilot or AnswerProviderMode.LocalModel))
            throw new ArgumentException("Copilot CLI requires an explicit Copilot or local-model provider.");
        this.settings = settings;
        this.profileDirectory = CopilotCliProfile.CheckPath(profileDirectory);
        this.requestRoot = CopilotCliProfile.CheckPath(requestRoot);
        this.deadline = deadline;
        this.transformStartInfo = transformStartInfo;
    }

    public async IAsyncEnumerable<AnswerEvent> StreamAsync(AnswerRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Clarification?.ValidateFor(request.ExactText);
        if (request.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(request.ExactText) || request.ExactText.Length > 8192)
            throw new AnswerProviderException("copilot_question", "Provide a nonempty question of at most 8192 characters.");
        var contextualPrompt = request.Clarification is null && request.PreviousAnswer is null
            ? null : AgencyAnswerPrompt.Create(request);
        cancellationToken.ThrowIfCancellationRequested();
        CopilotCliProfile.CheckPath(requestRoot);
        var executable = CopilotCliLaunch.ResolveExecutable(settings.CliPath);
        if (settings.Mode == AnswerProviderMode.Copilot) CopilotCliProfile.Validate(profileDirectory);
        var owned = Path.Combine(requestRoot, Guid.NewGuid().ToString("N"));
        var work = Path.Combine(owned, "work");
        var home = Path.Combine(owned, "home");
        var localProfile = Path.Combine(owned, "profile");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(deadline);
        var token = timeout.Token;
        try
        {
            CreateDirectory(work);
            CreateDirectory(home);
            CopilotCliProfile.Initialize(localProfile);
            await VerifyVersionAsync(executable, localProfile, work, home, token, cancellationToken).ConfigureAwait(false);
            var profile = settings.Mode == AnswerProviderMode.Copilot ? profileDirectory : localProfile;
            await using var process = new CopilotOwnedProcess(
                Transform(CopilotCliLaunch.Create(executable, settings, profile, work, home)), token);
            var parser = new CopilotJsonProtocol();
            long sequence = 0;
            try
            {
                if (contextualPrompt is null)
                    await process.SendAsync(request.ExactText, token).ConfigureAwait(false);
                else
                    await process.SendAsync(contextualPrompt, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw TimedOut(); }
            await using var records = CopilotJsonProtocol.ReadRecordsAsync(process.Process.StandardOutput, token).GetAsyncEnumerator(token);
            while (await MoveNextAsync(records, token, cancellationToken).ConfigureAwait(false))
            {
                process.CheckErrorLimit();
                var delta = parser.Accept(records.Current);
                if (delta is not null) yield return new(request.RequestId, ++sequence, AnswerEventKind.TextDelta, delta);
            }
            process.CheckErrorLimit();
            int exit;
            try { exit = await process.WaitAsync(token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw TimedOut();
            }
            parser.Complete(exit);
            cancellationToken.ThrowIfCancellationRequested();
            yield return new(request.RequestId, ++sequence, AnswerEventKind.Completed);
        }
        finally
        {
            // No prompt files are created; local CLI history and the owned working tree are removed.
            if (Directory.Exists(owned))
            {
                try { Directory.Delete(owned, recursive: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new AnswerProviderException("copilot_cleanup", "The isolated Copilot request directory could not be removed.");
                }
            }
        }
    }

    private async Task VerifyVersionAsync(string executable, string profile, string work, string home,
        CancellationToken token, CancellationToken caller)
    {
        try
        {
            await using var process = new CopilotOwnedProcess(Transform(CopilotCliLaunch.Create(executable, settings, profile, work, home, true)), token);
            process.Process.StandardInput.Close();
            var output = new StringBuilder();
            var buffer = new char[1024];
            int count;
            while ((count = await process.Process.StandardOutput.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) != 0)
            {
                if (output.Length + count > 4096) throw CopilotJsonProtocol.Limit();
                output.Append(buffer, 0, count);
            }
            if (await process.WaitAsync(token).ConfigureAwait(false) != 0 ||
                !CopilotCliLaunch.SupportedVersions.Contains(output.ToString().Split('\n')[0].TrimEnd('\r'), StringComparer.Ordinal))
                throw new AnswerProviderException("copilot_version",
                    "This adapter requires offline-verified Copilot CLI 1.0.81-0 or 1.0.91. The installed version was not used; safety flags are never removed as a fallback.");
        }
        catch (OperationCanceledException) when (!caller.IsCancellationRequested) { throw TimedOut(); }
        catch (Exception ex) when (ex is IOException or DecoderFallbackException)
        {
            throw new AnswerProviderException("copilot_version", "The installed Copilot version could not be verified safely.");
        }
    }

    private static async Task<bool> MoveNextAsync(IAsyncEnumerator<string> records, CancellationToken token, CancellationToken caller)
    {
        try { return await records.MoveNextAsync().ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested && !caller.IsCancellationRequested) { throw TimedOut(); }
        catch (IOException) { throw new AnswerProviderException("copilot_io", "Copilot's output stream could not be read."); }
    }

    private static void CreateDirectory(string path)
    {
        try { Directory.CreateDirectory(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { throw CopilotCliProfile.Invalid(); }
    }

    private ProcessStartInfo Transform(ProcessStartInfo info) => transformStartInfo?.Invoke(info) ?? info;

    private static AnswerProviderException TimedOut() => new("copilot_timeout", "Copilot exceeded the answer deadline.");
}
