namespace PhantomFudge.Core.Answers;

internal sealed class AgencyJsonProtocol
{
    private readonly CopilotJsonProtocol protocol = new();
    private bool hasJson;
    private bool resolving;
    private bool resolved;

    internal string? Accept(string record)
    {
        if (!hasJson && record == "📦 Resolving Copilot CLI..." && !resolving && !resolved)
        {
            resolving = true;
            return null;
        }
        const string prefix = "✅ Copilot CLI resolved (in ";
        const string suffix = " ms)";
        if (!hasJson && !resolved && record.StartsWith(prefix, StringComparison.Ordinal) &&
            record.EndsWith(suffix, StringComparison.Ordinal))
        {
            var number = record.AsSpan(prefix.Length, record.Length - prefix.Length - suffix.Length);
            if (number.Length is > 0 and <= 6 && int.TryParse(number, out var milliseconds) &&
                milliseconds >= 0 && number.ToArray().All(char.IsAsciiDigit))
            {
                resolved = true;
                return null;
            }
        }
        if (!record.StartsWith('{'))
        {
            if (AgencyAuthenticationGuard.IsAuthenticationNotice(record)) throw AgencyAuthenticationGuard.Required();
            throw CopilotJsonProtocol.Protocol();
        }
        hasJson = true;
        try { return protocol.Accept(record); }
        catch (AnswerProviderException error) when (error.Code == "copilot_failed") { throw Failed(); }
    }

    internal void Complete(int exitCode)
    {
        try { protocol.Complete(exitCode); }
        catch (AnswerProviderException error) when (error.Code == "copilot_failed") { throw Failed(); }
    }

    private static AnswerProviderException Failed() => new("agency_failed",
        "Agency did not complete the answer. No provider fallback or sign-in command was launched by Phantom Fudge.");
}

internal sealed class AgencyAuthenticationGuard
{
    private string tail = "";

    internal AnswerProviderException? Inspect(string text)
    {
        var current = tail + text;
        tail = current.Length <= 128 ? current : current[^128..];
        return IsAuthenticationNotice(current) ? Required() : null;
    }

    internal static bool IsAuthenticationNotice(string text) =>
        new[] { "opening your browser", "waiting for authorization", "device code",
            "authentication is required", "please sign in", "interactionrequired" }
        .Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));

    internal static AnswerProviderException Required() => new("agency_auth_required",
        "Agency requested authentication. This request was stopped; Phantom Fudge did not launch a sign-in command. " +
        "Agency's cached authentication may have expired.");
}
