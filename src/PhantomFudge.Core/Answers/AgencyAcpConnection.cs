using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace PhantomFudge.Core.Answers;

// ACP is a persistent NDJSON transport, not the one-shot CLI event stream.
// Only these qualified methods are emitted; incoming permission/tool requests fail closed.
internal sealed class AgencyAcpConnection : IAsyncDisposable
{
    internal const string EngineVersion = "1.0.92";
    private readonly CopilotOwnedProcess process;
    private readonly IAsyncEnumerator<string> records;
    private long serial;
    private string? sessionId;
    private readonly HashSet<string> usedSessionIds = new(StringComparer.Ordinal);
    private bool sawResolving;
    private bool sawResolved;
    private bool hasJson;
    private bool disabledNotice;
    private bool unknownNotice;
    private bool answerStarted;
    private int answerCharacters;
    private int sessionCount;
    private bool promptSent;
    private int outputCharacters;
    private int outputRecords;
    private string? requestedModel;
    private bool requireSelectedAgent;
    private readonly StringBuilder answerText = new(1024);

    internal AgencyAcpConnection(CopilotOwnedProcess process, CancellationToken lifetime)
    {
        this.process = process;
        records = CopilotJsonProtocol.ReadRecordsAsync(process.Process.StandardOutput, lifetime, allowEmptyLines: true,
                maximumOutputCharacters: 33 * CopilotJsonProtocol.MaximumOutputCharacters,
                maximumRecords: 33 * CopilotJsonProtocol.MaximumRecords)
            .GetAsyncEnumerator(lifetime);
    }

    internal bool HasPreparedSession => sessionId is not null && !promptSent;
    internal bool CanReuse => !process.Process.HasExited && (HasPreparedSession || sessionCount < 32);

    internal async Task InitializeAsync(CancellationToken token)
    {
        var result = await CallAsync("initialize", new { protocolVersion = 1, clientCapabilities = new { } }, token).ConfigureAwait(false);
        try
        {
            if (result.GetProperty("protocolVersion").GetInt32() != 1 ||
                result.GetProperty("agentInfo").GetProperty("version").GetString() != EngineVersion ||
                result.GetProperty("agentCapabilities").GetProperty("sessionCapabilities")
                    .GetProperty("close").ValueKind != JsonValueKind.Object)
                throw Version();
        }
        catch (Exception error) when (error is KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw Version();
        }
    }

    internal async Task NewSessionAsync(string work, string model, CancellationToken token)
    {
        if (sessionId is not null) throw Protocol();
        disabledNotice = unknownNotice = answerStarted = false;
        answerCharacters = 0;
        answerText.Clear();
        promptSent = false;
        outputCharacters = outputRecords = 0;
        requestedModel = model;
        requireSelectedAgent = false;
        var result = await CallAsync("session/new", new { cwd = work, mcpServers = Array.Empty<object>() }, token).ConfigureAwait(false);
        try
        {
            var id = result.GetProperty("sessionId").GetString();
            if (string.IsNullOrEmpty(id) || id.Length > 128 || !usedSessionIds.Add(id)) throw Protocol();
            ValidateConfiguration(result.GetProperty("configOptions"), model, requireAll: true, agentSelected: false);
            sessionId = id;
            sessionCount++;
            requireSelectedAgent = true;
            var selected = await CallAsync("session/set_config_option",
                new { sessionId, configId = "agent", value = AgencyAnswerProfile.Name }, token).ConfigureAwait(false);
            ValidateConfiguration(selected.GetProperty("configOptions"), model, requireAll: true);
        }
        catch (Exception error) when (error is KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw Protocol();
        }
    }

    internal async IAsyncEnumerable<string> PromptAsync(string prompt, Action submitted,
        [EnumeratorCancellation] CancellationToken token, bool allowClarification = false)
    {
        if (sessionId is null || promptSent) throw Protocol();
        promptSent = true;
        var id = await SendAsync("session/prompt",
            new { sessionId, prompt = new[] { new { type = "text", text = prompt } } }, token).ConfigureAwait(false);
        submitted();
        while (true)
        {
            var root = await ReadAsync(token).ConfigureAwait(false);
            if (TryResult(root, id, out var result))
            {
                if (!result.TryGetProperty("stopReason", out var stop) || stop.ValueKind != JsonValueKind.String ||
                    stop.GetString() != "end_turn" ||
                    answerCharacters == 0 || !disabledNotice || !unknownNotice)
                    throw new AnswerProviderException("agency_incomplete", "Agency ended without a verified complete answer.");
                if (!allowClarification && RequestsMoreInput(answerText.ToString()))
                    throw new AnswerProviderException("agency_nonanswer",
                        "The model requested more input instead of providing a standalone answer. No follow-up input was sent.");
                yield break;
            }
            var delta = AcceptUpdate(root, allowAnswer: true);
            if (delta is not null) yield return delta;
        }
    }

    internal async Task CloseSessionAsync(CancellationToken token)
    {
        if (sessionId is null) throw Protocol();
        await CallAsync("session/close", new { sessionId }, token).ConfigureAwait(false);
        sessionId = null;
        answerText.Clear();
    }

    private async Task<JsonElement> CallAsync(string method, object parameters, CancellationToken token)
    {
        var id = await SendAsync(method, parameters, token).ConfigureAwait(false);
        while (true)
        {
            var root = await ReadAsync(token).ConfigureAwait(false);
            if (TryResult(root, id, out var result)) return result;
            if (method == "initialize") throw Protocol();
            AcceptUpdate(root, allowAnswer: false);
        }
    }

    private async Task<long> SendAsync(string method, object parameters, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        process.CheckErrorLimit();
        if (process.Process.HasExited) throw Protocol();
        var id = ++serial;
        var text = JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters }) + "\n";
        try
        {
            await process.Process.StandardInput.WriteAsync(text.AsMemory(), token).ConfigureAwait(false);
            await process.Process.StandardInput.FlushAsync(token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or EncoderFallbackException)
        {
            process.CheckErrorLimit();
            token.ThrowIfCancellationRequested();
            throw new AnswerProviderException("agency_stdin", "Agency could not receive the isolated request.");
        }
        return id;
    }

    private async Task<JsonElement> ReadAsync(CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            bool hasRecord;
            try { hasRecord = await records.MoveNextAsync().ConfigureAwait(false); }
            catch (IOException)
            {
                process.CheckErrorLimit();
                token.ThrowIfCancellationRequested();
                throw Protocol();
            }
            process.CheckErrorLimit();
            token.ThrowIfCancellationRequested();
            if (!hasRecord) throw new AnswerProviderException("agency_incomplete", "Agency's connection ended before terminal success.");
            var record = records.Current;
            outputCharacters += record.Length;
            if (outputCharacters > CopilotJsonProtocol.MaximumOutputCharacters ||
                ++outputRecords > CopilotJsonProtocol.MaximumRecords) throw CopilotJsonProtocol.Limit();
            if (!record.StartsWith('{'))
            {
                if (AgencyAuthenticationGuard.IsAuthenticationNotice(record)) throw AgencyAuthenticationGuard.Required();
                if (!hasJson && record == "📦 Resolving Copilot CLI..." && !sawResolving && !sawResolved)
                {
                    sawResolving = true;
                    continue;
                }
                const string prefix = "✅ Copilot CLI resolved (in ";
                if (!hasJson && !sawResolved && record.StartsWith(prefix, StringComparison.Ordinal) && record.EndsWith(" ms)", StringComparison.Ordinal))
                {
                    var number = record.AsSpan(prefix.Length, record.Length - prefix.Length - 4);
                    if (number.Length is > 0 and <= 6 && number.ToArray().All(char.IsAsciiDigit))
                    {
                        sawResolved = true;
                        continue;
                    }
                }
                throw Protocol();
            }
            try
            {
                using var document = JsonDocument.Parse(record, new JsonDocumentOptions { MaxDepth = 32 });
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || root.GetProperty("jsonrpc").GetString() != "2.0") throw Protocol();
                hasJson = true;
                return root.Clone();
            }
            catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                throw Protocol();
            }
        }
    }

    private static bool TryResult(JsonElement root, long id, out JsonElement result)
    {
        result = default;
        if (root.TryGetProperty("method", out _))
        {
            if (root.TryGetProperty("id", out _)) throw Tools();
            return false;
        }
        if (!root.TryGetProperty("id", out var value) || value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt64(out var observed) || observed != id) throw Protocol();
        if (root.TryGetProperty("error", out var error))
        {
            if (AgencyAuthenticationGuard.IsAuthenticationNotice(error.GetRawText())) throw AgencyAuthenticationGuard.Required();
            throw new AnswerProviderException("agency_rpc", "Agency rejected the request. Check the selected model and existing Agency access; no fallback or login was attempted.");
        }
        if (!root.TryGetProperty("result", out result) || result.ValueKind != JsonValueKind.Object) throw Protocol();
        return true;
    }

    private string? AcceptUpdate(JsonElement root, bool allowAnswer)
    {
        try
        {
            if (root.GetProperty("method").GetString() != "session/update") throw Tools();
            var parameters = root.GetProperty("params");
            var id = parameters.GetProperty("sessionId").GetString();
            if (sessionId is not null && id != sessionId) throw Protocol();
            if (sessionId is null && (string.IsNullOrEmpty(id) || usedSessionIds.Contains(id))) throw Protocol();
            var update = parameters.GetProperty("update");
            switch (update.GetProperty("sessionUpdate").GetString())
            {
                case "agent_message_chunk":
                    var content = update.GetProperty("content");
                    if (content.GetProperty("type").GetString() != "text") throw Protocol();
                    var text = content.GetProperty("text").GetString() ?? throw Protocol();
                    if (AcceptConfigurationNotice(text)) return null;
                    if (!allowAnswer || !disabledNotice || !unknownNotice) throw Protocol();
                    if (text.Length > CopilotJsonProtocol.MaximumAnswerCharacters - answerCharacters) throw CopilotJsonProtocol.Limit();
                    answerCharacters += text.Length;
                    answerText.Append(text);
                    answerStarted |= text.Length != 0;
                    return text.Length == 0 ? null : text;
                case "available_commands_update":
                case "session_info_update":
                case "usage_update":
                case "agent_thought_chunk":
                    return null;
                case "config_option_update":
                    ValidateConfiguration(update.GetProperty("configOptions"), requestedModel ?? throw Protocol(),
                        requireAll: true, agentSelected: requireSelectedAgent);
                    return null;
                case "tool_call": case "tool_call_update": case "plan":
                    throw Tools();
                default:
                    throw Protocol();
            }
        }
        catch (Exception error) when (error is KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw Protocol();
        }
    }

    private bool AcceptConfigurationNotice(string text)
    {
        const string unknown = "Info: Unknown tool name in the tool allowlist: \"phantom_fudge_no_tools\"";
        const string disabled = "Info: Disabled tools: ";
        if (!answerStarted && !unknownNotice && text == unknown)
        {
            unknownNotice = true;
            return true;
        }
        if (!answerStarted && !disabledNotice && text.StartsWith(disabled, StringComparison.Ordinal))
        {
            var tools = text[disabled.Length..].Split(", ");
            if (tools.Length is < 1 or > 128 || tools.Any(tool => tool.Length is < 1 or > 80 ||
                tool.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '_')))) throw Protocol();
            disabledNotice = true;
            return true;
        }
        return false;
    }

    internal static void ValidateConfiguration(JsonElement options, string model, bool requireAll, bool agentSelected = true)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var option in options.EnumerateArray())
        {
            var name = option.GetProperty("id").GetString()!;
            var value = option.GetProperty("currentValue").GetString();
            if (!seen.Add(name)) throw Protocol();
            switch (name)
            {
                case "mode" when value == "https://agentclientprotocol.com/protocol/session-modes#agent": break;
                case "allow_all" when value == "off": break;
                case "model" when value == model: break;
                case "reasoning_effort" when value == AgencyAnswerProfile.ReasoningEffort: break;
                case "agent" when value == AgencyAnswerProfile.Name || (!agentSelected && value == ""):
                    if (!option.GetProperty("options").EnumerateArray().Any(item =>
                        item.GetProperty("value").GetString() == AgencyAnswerProfile.Name))
                        throw new AnswerProviderException("agency_configuration", "Agency did not advertise the controlled answer role. No question was sent.");
                    break;
                default: throw new AnswerProviderException("agency_configuration", "Agency did not select the explicit model, reasoning level, or restricted permissions. No question was sent.");
            }
        }
        if (requireAll && !seen.SetEquals(["mode", "allow_all", "model", "reasoning_effort", "agent"]))
            throw new AnswerProviderException("agency_configuration", "Agency did not confirm the explicit answer profile. No question was sent.");
    }

    public async ValueTask DisposeAsync()
    {
        // Dispose the process first: an interrupted ReadAsync may still be waiting on its pipe.
        await process.DisposeAsync().ConfigureAwait(false);
        try { await records.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException or NotSupportedException) { }
    }

    internal static bool RequestsMoreInput(string text)
    {
        var prose = new StringBuilder(text.Length);
        var inCode = false;
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
                inCode = !inCode;
            else if (!inCode) prose.AppendLine(line);
        }
        var value = prose.ToString().TrimStart();
        return (new[] { "I can't", "I cannot", "I can’t", "I need" }.Any(prefix =>
                value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) &&
            (value.Contains("problem statement", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("problem text", StringComparison.OrdinalIgnoreCase))) ||
            value.StartsWith("No specific problem was provided", StringComparison.OrdinalIgnoreCase) ||
            (new[] { "Please provide", "Please paste", "Please send", "Please share" }.Any(prefix =>
                value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) &&
                (value.Contains("problem", StringComparison.OrdinalIgnoreCase) || value.Contains("question", StringComparison.OrdinalIgnoreCase))) ||
            new[] { "If you meant a different problem", "tell me the exact name", "paste the statement",
               "paste the problem statement", "share the problem statement", "could you clarify", "can you clarify" }
               .Any(invitation => value.Split('\n').Any(line =>
                   line.TrimStart(' ', '\t', '-', '*', '#').StartsWith(invitation, StringComparison.OrdinalIgnoreCase)));
    }

    private static AnswerProviderException Protocol() => new("agency_protocol", "Agency returned an unsupported or malformed ACP stream.");
    private static AnswerProviderException Tools() => new("agency_tools", "Agency attempted a tool, plan, or client capability. The answer was stopped.");
    private static AnswerProviderException Version() => new("agency_engine_version", "The installed Agency engine is not the qualified Copilot 1.0.92 ACP implementation. No question was sent.");
}
