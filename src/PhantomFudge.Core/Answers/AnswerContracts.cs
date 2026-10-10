using System.Text;

namespace PhantomFudge.Core.Answers;

public enum AnswerEventKind { TextDelta, Completed }

public sealed record AnswerRequest(Guid RequestId, string ExactText, AnswerClarification? Clarification = null,
    string? PreviousAnswer = null)
{
    internal static void ValidatePreviousAnswer(string? previousAnswer)
    {
        if (previousAnswer is null)
            return;
        if (string.IsNullOrWhiteSpace(previousAnswer))
            throw new AnswerProviderException("previous_answer_text", "The previous answer cannot be empty or blank.");
        if (previousAnswer.Length > AnswerRun.MaximumCharacters)
            throw new AnswerProviderException("previous_answer_limit", "The previous answer must be at most 32768 characters. Turn off previous-answer context to ask without it.");
        try { _ = new UTF8Encoding(false, true).GetByteCount(previousAnswer); }
        catch (EncoderFallbackException)
        {
            throw new AnswerProviderException("previous_answer_unicode", "The previous answer contains invalid Unicode. Turn off previous-answer context to ask without it.");
        }
    }
}

public sealed record AnswerEvent(Guid RequestId, long Sequence, AnswerEventKind Kind, string Text = "");

public interface IAnswerProvider
{
    IAsyncEnumerable<AnswerEvent> StreamAsync(AnswerRequest request, CancellationToken cancellationToken = default);
}

public enum AnswerProviderMode { SnapshotOnly, Copilot, LocalModel, Agency }

public sealed record AnswerSettings(
    AnswerProviderMode Mode,
    string? CliPath = null,
    string? Model = null,
    string? LocalEndpoint = null,
    string? AgencyPath = null)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Mode))
            throw new ArgumentException("Unknown answer provider.");
        if (Mode == AnswerProviderMode.SnapshotOnly)
            return;
        if (AgencyPath is not null && (AgencyPath.Length > 1024 || !Path.IsPathFullyQualified(AgencyPath) ||
            !string.Equals(Path.GetExtension(AgencyPath), ".exe", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Choose an absolute path to agency.exe, not a shell command.");
        if (CliPath is not null && (CliPath.Length > 1024 || !Path.IsPathFullyQualified(CliPath) ||
            !string.Equals(Path.GetExtension(CliPath), ".exe", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Choose an absolute path to the installed copilot.exe, not a shell command.");
        if (Model is not null && (Model.Length is < 1 or > 128 ||
            Model.Any(character => !(char.IsAsciiLetterOrDigit(character) || "-_.:/".Contains(character)))))
            throw new ArgumentException("Use a model identifier, not command arguments.");
        if (Mode is AnswerProviderMode.Copilot or AnswerProviderMode.Agency)
        {
            if (LocalEndpoint is not null)
                throw new ArgumentException("A local endpoint cannot be attached to the selected hosted provider.");
            return;
        }
        if (string.IsNullOrWhiteSpace(Model))
            throw new ArgumentException("The local model needs an explicit model identifier.");
        ValidateLocalEndpoint(LocalEndpoint);
    }

    public static Uri ValidateLocalEndpoint(string? value)
    {
        if (value is null || !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttp || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            uri.AbsolutePath is not ("/v1" or "/v1/") ||
            uri.Host is not ("127.0.0.1" or "[::1]") ||
            value.Contains('%') || value.Contains('\\') ||
            !value.StartsWith("http://" + uri.Host, StringComparison.Ordinal))
            throw new ArgumentException("Local models require http://127.0.0.1:PORT/v1 or http://[::1]:PORT/v1.");
        return uri;
    }
}

public sealed class AnswerProviderException(string code, string safeMessage) : Exception(safeMessage)
{
    public string Code { get; } = code;
}
