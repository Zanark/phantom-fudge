using System.Text.Json;
using PhantomFudge.Core.Answers;

namespace PhantomFudge.Core.Tests;

public sealed class CopilotJsonProtocolTests
{
    private static string Event(string type, object data) => JsonSerializer.Serialize(new { type, data });
    private static CopilotJsonProtocol Started()
    {
        var parser = new CopilotJsonProtocol();
        parser.Accept(Event("assistant.turn_start", new { turnId = "0" }));
        parser.Accept(Event("model.call_start", new { }));
        parser.Accept(Event("assistant.message_start", new { messageId = "m" }));
        return parser;
    }

    private static void End(CopilotJsonProtocol parser, string text)
    {
        Assert.Null(parser.Accept(Event("assistant.message", new { messageId = "m", content = text, toolRequests = Array.Empty<object>() })));
        parser.Accept(Event("assistant.turn_end", new { turnId = "0" }));
        parser.Accept(Event("assistant.idle", new { }));
        parser.Accept("""{"type":"result","exitCode":0}""");
    }

    [Fact]
    public void StreamsUnicodeBeforeTerminalAndNeverAppendsFinalTextTwice()
    {
        var parser = Started();
        Assert.Equal("Hello ", parser.Accept(Event("assistant.message_delta", new { messageId = "m", deltaContent = "Hello " })));
        Assert.Equal("🙂\r\n", parser.Accept(Event("assistant.message_delta", new { messageId = "m", deltaContent = "🙂\r\n" })));
        Assert.Throws<AnswerProviderException>(() => parser.Complete(0));
        End(parser, "Hello 🙂\r\n");
        parser.Complete(0);
    }

    [Fact]
    public void SupportsSingleFinalMessageWithoutDeltas()
    {
        var parser = Started();
        Assert.Equal("one", parser.Accept(Event("assistant.message", new { messageId = "m", content = "one" })));
        parser.Accept(Event("assistant.turn_end", new { turnId = "0" }));
        parser.Accept(Event("assistant.idle", new { }));
        parser.Accept("""{"type":"result","exitCode":0}""");
        parser.Complete(0);
    }

    [Theory]
    [InlineData("session.error", "copilot_model_error")]
    [InlineData("model.call_failure", "copilot_model_error")]
    [InlineData("assistant.turn_retry", "copilot_model_error")]
    [InlineData("tool.execution_start", "copilot_tools")]
    [InlineData("tool.execution_complete", "copilot_tools")]
    [InlineData("unknown.future.event", "copilot_protocol")]
    public void ErrorsToolsAndUnrecognizedEventsFailClosed(string type, string code)
    {
        var error = Assert.Throws<AnswerProviderException>(() => Started().Accept(Event(type, new { message = "private synthetic marker" })));
        Assert.Equal(code, error.Code);
        Assert.DoesNotContain("private synthetic marker", error.Message);
    }

    [Fact]
    public void ToolRequestIsRejectedBeforeAnyAssociatedContent()
    {
        var parser = Started();
        Assert.Equal("copilot_tools", Assert.Throws<AnswerProviderException>(() => parser.Accept(Event("assistant.message",
            new { messageId = "m", content = "never show", toolRequests = new[] { new { name = "shell" } } }))).Code);
    }

    [Fact]
    public void ReasoningAndUsageAreNotAnswers()
    {
        var parser = Started();
        Assert.Null(parser.Accept(Event("assistant.reasoning_delta", new { deltaContent = "reasoning" })));
        Assert.Null(parser.Accept(Event("assistant.usage", new { content = "usage" })));
        Assert.Throws<AnswerProviderException>(() => parser.Complete(0));
    }

    [Fact]
    public void TerminalResultAndActualZeroExitAreBothRequired()
    {
        var parser = Started();
        parser.Accept(Event("assistant.message_delta", new { messageId = "m", deltaContent = "x" }));
        End(parser, "x");
        Assert.Throws<AnswerProviderException>(() => parser.Complete(1));
        Assert.Throws<AnswerProviderException>(() => parser.Accept(Event("assistant.message_delta", new { messageId = "m", deltaContent = "late" })));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"type":"result","exitCode":0}""")]
    [InlineData("""{"type":"result","exitCode":1}""")]
    [InlineData("""{"type":"assistant.message_delta","data":{"messageId":"other","deltaContent":"x"}}""")]
    [InlineData("""{"type":"assistant.message_delta","data":{"messageId":"m","deltaContent":3}}""")]
    [InlineData("""{"type":"assistant.turn_end","data":{"turnId":"0"}}""")]
    public void MalformedOrPrematureRecordsCannotComplete(string record) =>
        Assert.Throws<AnswerProviderException>(() => Started().Accept(record));

    [Fact]
    public void ChangedFinalMessageDoesNotSilentlyOverwriteDeltas()
    {
        var parser = Started();
        parser.Accept(Event("assistant.message_delta", new { messageId = "m", deltaContent = "first" }));
        Assert.Throws<AnswerProviderException>(() => parser.Accept(Event("assistant.message", new { messageId = "m", content = "second" })));
    }

    [Fact]
    public void LimitsAnswerCharacters()
    {
        var parser = Started();
        parser.Accept(Event("assistant.message_delta", new { messageId = "m", deltaContent = new string('a', 32_768) }));
        Assert.Equal("copilot_limit", Assert.Throws<AnswerProviderException>(() => parser.Accept(Event("assistant.message_delta", new { messageId = "m", deltaContent = "x" }))).Code);
    }

    [Fact]
    public void LoadedCustomizationsFailClosed()
    {
        Assert.Throws<AnswerProviderException>(() => Started().Accept(Event("session.mcp_servers_loaded", new { servers = new[] { "server" } })));
        Assert.Equal("copilot_skills", Assert.Throws<AnswerProviderException>(() =>
            Started().Accept(Event("session.skills_loaded", new { skills = new[] { new { source = "user" } } }))).Code);
        Assert.Null(Started().Accept(Event("session.skills_loaded", new { skills = new[] { new { source = "builtin" } } })));
    }

    [Fact]
    public void NpmReportsDisabledBuiltInMcpDescriptorsWithoutLoadingServers()
    {
        Assert.Null(Started().Accept(Event("session.mcp_servers_loaded",
            new { servers = new[] { new { name = "github-mcp-server", status = "disabled" }, new { name = "githubiq", status = "disabled" } } })));
        Assert.Equal("copilot_mcp", Assert.Throws<AnswerProviderException>(() => Started().Accept(Event("session.mcp_servers_loaded",
            new { servers = new[] { new { name = "github-mcp-server", status = "connected" } } }))).Code);
        Assert.Equal("copilot_mcp", Assert.Throws<AnswerProviderException>(() => Started().Accept(Event("session.mcp_servers_loaded",
            new { servers = new[] { new { name = "custom", status = "disabled" } } }))).Code);
        Assert.Equal("copilot_mcp", Assert.Throws<AnswerProviderException>(() => Started().Accept(Event("session.mcp_servers_loaded",
            new { servers = new[] { new { name = "github-mcp-server", status = "disabled", source = "user" } } }))).Code);
        Assert.Equal("copilot_mcp", Assert.Throws<AnswerProviderException>(() => Started().Accept(Event("session.mcp_servers_loaded",
            new { servers = new[] { new { name = "github-mcp-server", status = "disabled", pluginName = "custom" } } }))).Code);
    }

    [Fact]
    public void NativeNpmResultEventsMustReportSuccessWithoutEdits()
    {
        var parser = Started();
        Assert.Null(parser.Accept(Event("model.call_finished", new { outcome = "success", containsBuiltInFileEditRequest = false })));
        Assert.Null(parser.Accept(Event("model.call_final_result", new { result = "success" })));
        Assert.Throws<AnswerProviderException>(() => Started().Accept(Event("model.call_finished", new { outcome = "failure" })));
        Assert.Throws<AnswerProviderException>(() => Started().Accept(Event("model.call_final_result", new { result = "error" })));
        Assert.Equal("copilot_tools", Assert.Throws<AnswerProviderException>(() =>
            Started().Accept(Event("model.call_finished", new { outcome = "success", containsBuiltInFileEditRequest = true }))).Code);
    }

    [Fact]
    public async Task FragmentsPreserveUnicodeCrLfAndRecordBoundaries()
    {
        using var reader = new FragmentReader("{\"type\":\"a🙂\"}\r\n{\"type\":\"b\"}\n", 1);
        var records = new List<string>();
        await foreach (var record in CopilotJsonProtocol.ReadRecordsAsync(reader)) records.Add(record);
        Assert.Equal(["{\"type\":\"a🙂\"}", "{\"type\":\"b\"}"], records);
    }

    [Theory]
    [InlineData(262145, false)]
    [InlineData(8193, true)]
    public async Task OversizedRecordsAndRecordCountsAreBounded(int count, bool many)
    {
        using var reader = new StringReader(many ? string.Concat(Enumerable.Repeat("{}\n", count)) : new string('x', count));
        var error = await Assert.ThrowsAsync<AnswerProviderException>(async () =>
        {
            await foreach (var unused in CopilotJsonProtocol.ReadRecordsAsync(reader)) { }
        });
        Assert.Equal("copilot_limit", error.Code);
    }

    [Fact]
    public async Task TotalOutputAndTruncatedFinalRecordAreRejected()
    {
        using var oversized = new StringReader(string.Concat(Enumerable.Repeat(new string('x', 1000) + "\n", 2100)));
        Assert.Equal("copilot_limit", (await Assert.ThrowsAsync<AnswerProviderException>(async () =>
        {
            await foreach (var unused in CopilotJsonProtocol.ReadRecordsAsync(oversized)) { }
        })).Code);
        using var truncated = new StringReader("{}");
        Assert.Equal("copilot_incomplete", (await Assert.ThrowsAsync<AnswerProviderException>(async () =>
        {
            await foreach (var unused in CopilotJsonProtocol.ReadRecordsAsync(truncated)) { }
        })).Code);
    }

    private sealed class FragmentReader(string text, int fragmentSize) : StringReader(text)
    {
        public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(fragmentSize, buffer.Length)], cancellationToken);
    }
}
