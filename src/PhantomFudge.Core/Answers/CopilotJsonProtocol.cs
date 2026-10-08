using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

[assembly: InternalsVisibleTo("PhantomFudge.Core.Tests")]

namespace PhantomFudge.Core.Answers;

internal sealed class CopilotJsonProtocol
{
    internal const int MaximumRecordCharacters = 262_144;
    internal const int MaximumOutputCharacters = 2_097_152;
    internal const int MaximumRecords = 8192;
    internal const int MaximumAnswerCharacters = 32_768;
    private readonly StringBuilder text = new();
    private string? messageId;
    private string? turnId;
    private bool messageEnded;
    private bool turnEnded;
    private bool idle;
    private bool result;
    private bool modelStarted;

    internal string? Accept(string record)
    {
        try
        {
            using var json = JsonDocument.Parse(record, new JsonDocumentOptions { MaxDepth = 32 });
            var root = json.RootElement;
            var type = RequiredString(root, "type");
            if (result) throw Protocol();
            if (type.StartsWith("tool.", StringComparison.Ordinal) || type.Contains("tool_call", StringComparison.Ordinal))
                throw Tool();
            if (type is "session.error" or "model.call_failure" or "assistant.turn_retry" or "session.abort" ||
                type.EndsWith(".error", StringComparison.Ordinal) || type.EndsWith(".failed", StringComparison.Ordinal))
                throw new AnswerProviderException("copilot_model_error", "Copilot did not complete the answer. No retry was requested by Phantom Fudge.");
            if (type == "result")
            {
                if (!root.TryGetProperty("exitCode", out var exit) || !exit.TryGetInt32(out var code)) throw Protocol();
                if (code != 0) throw Failed();
                if (!messageEnded || !turnEnded || !idle || text.Length == 0) throw Protocol();
                result = true;
                return null;
            }
            var data = root.GetProperty("data");
            if (data.ValueKind != JsonValueKind.Object) throw Protocol();
            switch (type)
            {
                case "assistant.turn_start":
                    if (turnId is not null) throw Protocol();
                    turnId = RequiredString(data, "turnId");
                    break;
                case "model.call_start":
                    if (turnId is null || modelStarted) throw Protocol();
                    modelStarted = true;
                    break;
                case "assistant.message_start":
                    if (messageId is not null || turnId is null) throw Protocol();
                    messageId = RequiredString(data, "messageId");
                    break;
                case "assistant.message_delta":
                    MatchMessage(data);
                    if (messageEnded) throw Protocol();
                    var delta = RequiredString(data, "deltaContent", allowEmpty: true);
                    Append(delta);
                    return delta.Length == 0 ? null : delta;
                case "assistant.message":
                    if (data.TryGetProperty("toolRequests", out var tools) &&
                        (tools.ValueKind != JsonValueKind.Array || tools.GetArrayLength() != 0))
                        throw Tool();
                    if (messageEnded || turnId is null) throw Protocol();
                    messageId ??= RequiredString(data, "messageId");
                    MatchMessage(data);
                    var content = RequiredString(data, "content", allowEmpty: true);
                    messageEnded = true;
                    if (text.Length == 0)
                    {
                        Append(content);
                        return content.Length == 0 ? null : content;
                    }
                    if (!string.Equals(content, text.ToString(), StringComparison.Ordinal)) throw Protocol();
                    break;
                case "assistant.turn_end":
                    if (!messageEnded || turnEnded || RequiredString(data, "turnId") != turnId) throw Protocol();
                    turnEnded = true;
                    break;
                case "assistant.idle":
                    if (!turnEnded || idle) throw Protocol();
                    idle = true;
                    break;
                case "session.mcp_servers_loaded":
                    foreach (var server in data.GetProperty("servers").EnumerateArray())
                        if (RequiredString(server, "name") is not ("github-mcp-server" or "githubiq") ||
                            RequiredString(server, "status") != "disabled" ||
                            (server.TryGetProperty("source", out var source) &&
                                (source.ValueKind != JsonValueKind.String || source.GetString() != "builtin")) ||
                            HasPluginOrigin(server))
                            throw new AnswerProviderException("copilot_mcp",
                                "Copilot reported an unrecognized or non-disabled MCP server. The answer was stopped.");
                    break;
                case "session.skills_loaded":
                    foreach (var skill in data.GetProperty("skills").EnumerateArray())
                        if (RequiredString(skill, "source") != "builtin")
                            throw new AnswerProviderException("copilot_skills",
                                "Copilot reported a non-bundled skill. The answer was stopped.");
                    break;
                case "session.info":
                    if (data.TryGetProperty("infoType", out var info) && info.GetString() == "model_retry")
                        throw Failed();
                    break;
                case "model.call_finished":
                    if (RequiredString(data, "outcome") != "success") throw Failed();
                    if (data.TryGetProperty("containsBuiltInFileEditRequest", out var edit) && edit.ValueKind != JsonValueKind.False)
                        throw Tool();
                    break;
                case "model.call_final_result":
                    if (RequiredString(data, "result") != "success") throw Failed();
                    break;
                case "session.usage_checkpoint":
                    ValidateUsageCheckpoint(data);
                    break;
                case "session.start": case "session.tools_updated": case "user.message":
                case "assistant.reasoning": case "assistant.reasoning_delta":
                case "assistant.usage": case "model.call_end": case "session.usage_info":
                    break;
                default:
                    throw Protocol();
            }
            return null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            throw Protocol();
        }
    }

    internal void Complete(int processExitCode)
    {
        if (processExitCode != 0) throw Failed();
        if (!result) throw new AnswerProviderException("copilot_incomplete", "Copilot ended without a verified complete answer.");
    }

    private void MatchMessage(JsonElement data)
    {
        if (messageId is null || RequiredString(data, "messageId") != messageId) throw Protocol();
    }

    private static void ValidateUsageCheckpoint(JsonElement data)
    {
        if (!data.TryGetProperty("promptCacheBreakState", out var states)) return;
        foreach (var state in states.EnumerateArray())
            foreach (var entry in state.GetProperty("models").EnumerateObject())
            {
                var model = entry.Value;
                foreach (var name in new[] { "tool_count", "tool_tokens" })
                    if (model.TryGetProperty(name, out var count) && (!count.TryGetInt64(out var value) || value != 0))
                        throw Tool();
                if (model.TryGetProperty("tools", out var tools) &&
                    (tools.ValueKind != JsonValueKind.Array || tools.GetArrayLength() != 0))
                    throw Tool();
                if (model.TryGetProperty("tools_truncated", out var truncated) &&
                    (truncated.ValueKind != JsonValueKind.Number || !truncated.TryGetInt64(out var omitted) || omitted != 0))
                    throw Tool();
            }
    }

    private static bool HasPluginOrigin(JsonElement server)
    {
        foreach (var name in new[] { "sourcePlugin", "sourcePluginVersion", "pluginName", "pluginVersion" })
            if (server.TryGetProperty(name, out var property) && property.ValueKind != JsonValueKind.Null)
                return true;
        return false;
    }

    private void Append(string value)
    {
        if (value.Length > MaximumAnswerCharacters - text.Length) throw Limit();
        text.Append(value);
    }

    private static string RequiredString(JsonElement data, string name, bool allowEmpty = false)
    {
        if (!data.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) throw Protocol();
        var text = value.GetString()!;
        if (!allowEmpty && text.Length == 0) throw Protocol();
        return text;
    }

    internal static async IAsyncEnumerable<string> ReadRecordsAsync(TextReader reader,
        [EnumeratorCancellation] CancellationToken cancellationToken = default, bool allowEmptyLines = false)
    {
        var buffer = new char[4096];
        var pending = new StringBuilder();
        var total = 0;
        var records = 0;
        while (true)
        {
            int count;
            try { count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false); }
            catch (DecoderFallbackException) { throw Protocol(); }
            if (count == 0) break;
            total += count;
            if (total > MaximumOutputCharacters) throw Limit();
            for (var index = 0; index < count; index++)
            {
                if (buffer[index] == '\n')
                {
                    if (++records > MaximumRecords) throw Limit();
                    if (pending.Length != 0 && pending[^1] == '\r') pending.Length--;
                    if (pending.Length == 0)
                    {
                        if (allowEmptyLines) continue;
                        throw Protocol();
                    }
                    yield return pending.ToString();
                    pending.Clear();
                }
                else
                {
                    if (pending.Length == MaximumRecordCharacters) throw Limit();
                    pending.Append(buffer[index]);
                }
            }
        }
        if (pending.Length != 0) throw new AnswerProviderException("copilot_incomplete", "Copilot ended with an incomplete output record.");
    }

    internal static AnswerProviderException Protocol() => new("copilot_protocol", "Copilot returned an unsupported or malformed answer stream.");
    internal static AnswerProviderException Limit() => new("copilot_limit", "Copilot exceeded the bounded answer or process-output limit.");
    private static AnswerProviderException Tool() => new("copilot_tools", "Copilot attempted a tool or loaded an unapproved customization. The answer was stopped.");
    private static AnswerProviderException Failed() => new("copilot_failed", "Copilot could not complete the answer. Check the dedicated profile sign-in and model availability.");
}
