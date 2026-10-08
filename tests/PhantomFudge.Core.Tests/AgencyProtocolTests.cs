using System.Text.Json;
using PhantomFudge.Core.Answers;

namespace PhantomFudge.Core.Tests;

public sealed class AgencyProtocolTests
{
    // Structure from the authorized synthetic receipt. IDs/model keys are replacements;
    // cache fingerprints, request IDs, system segments, and account metadata are excluded.
    internal const string Replay = """
        {"type":"session.info","data":{"infoType":"configuration","message":"Synthetic configuration notice"}}
        {"type":"session.info","data":{"infoType":"configuration","message":"Synthetic zero-tool notice"}}
        {"type":"session.mcp_servers_loaded","data":{"servers":[{"name":"github-mcp-server","status":"disabled","source":"builtin"},{"name":"githubiq","status":"disabled"}]}}
        {"type":"session.tools_updated","data":{"model":"synthetic"}}
        {"type":"user.message","data":{"content":"Synthetic question"}}
        {"type":"assistant.turn_start","data":{"turnId":"0"}}
        {"type":"model.call_start","data":{"turnId":"0","model":"synthetic"}}
        {"type":"assistant.message_start","data":{"messageId":"synthetic-message"}}
        {"type":"assistant.message_delta","data":{"messageId":"synthetic-message","deltaContent":"PHANTOM_O"}}
        {"type":"assistant.message_delta","data":{"messageId":"synthetic-message","deltaContent":"K"}}
        {"type":"model.call_finished","data":{"turnId":"0","outcome":"success","containsBuiltInFileEditRequest":false}}
        {"type":"assistant.message","data":{"messageId":"synthetic-message","content":"PHANTOM_OK","toolRequests":[]}}
        {"type":"model.call_final_result","data":{"result":"success"}}
        {"type":"assistant.turn_end","data":{"turnId":"0"}}
        {"type":"session.usage_checkpoint","data":{"promptCacheBreakState":[{"models":{"synthetic":{"tool_count":0,"tool_tokens":0,"tools":[],"tools_truncated":0}}}]}}
        {"type":"assistant.idle","data":{}}
        {"type":"result","exitCode":0}
        """;

    [Fact]
    public void ExplicitlySelectedSyntheticReceiptCanBeReplayedWithoutLaunchingAnyProcess()
    {
        var path = Environment.GetEnvironmentVariable("PHANTOMFUDGE_AGENCY_REPLAY_FILE");
        if (string.IsNullOrEmpty(path)) return;
        Assert.InRange(new FileInfo(path).Length, 1, CopilotJsonProtocol.MaximumOutputCharacters);
        using var receipt = JsonDocument.Parse(File.ReadAllText(path));
        var parser = new AgencyJsonProtocol();
        var text = new System.Text.StringBuilder();
        var records = 0;
        foreach (var record in receipt.RootElement.EnumerateArray())
        {
            records++;
            var delta = parser.Accept(record.GetRawText());
            if (delta is not null) text.Append(delta);
        }
        Assert.Equal(17, records);
        Assert.Equal("PHANTOM_OK", text.ToString());
        parser.Complete(0);
    }

    [Fact]
    public void AuthorizedShapeReplaysWithoutDuplicatingFinalTextOrMetadata()
    {
        var parser = new AgencyJsonProtocol();
        Assert.Null(parser.Accept("📦 Resolving Copilot CLI..."));
        Assert.Null(parser.Accept("✅ Copilot CLI resolved (in 12084 ms)"));
        var deltas = Replay.Split('\n').Select(line => parser.Accept(line.TrimEnd('\r'))).OfType<string>().ToArray();
        Assert.Equal(["PHANTOM_O", "K"], deltas);
        parser.Complete(0);
        Assert.Throws<AnswerProviderException>(() => parser.Complete(1));
    }

    [Theory]
    [InlineData("Anything else")]
    [InlineData("✅ Copilot CLI resolved (in -1 ms)")]
    [InlineData("✅ Copilot CLI resolved (in 9999999 ms)")]
    [InlineData("✅ Copilot CLI resolved (in 12 ms) extra text")]
    public void OnlyExactBoundedStartupNoticesAreAccepted(string notice) =>
        Assert.Equal("copilot_protocol", Assert.Throws<AnswerProviderException>(() => new AgencyJsonProtocol().Accept(notice)).Code);

    [Fact]
    public void StartupNoticesCannotHideErrorsAfterJsonStarts()
    {
        var parser = new AgencyJsonProtocol();
        parser.Accept("""{"type":"session.info","data":{"infoType":"configuration"}}""");
        Assert.Throws<AnswerProviderException>(() => parser.Accept("📦 Resolving Copilot CLI..."));
        var duplicate = new AgencyJsonProtocol();
        duplicate.Accept("📦 Resolving Copilot CLI...");
        Assert.Throws<AnswerProviderException>(() => duplicate.Accept("📦 Resolving Copilot CLI..."));
    }

    [Theory]
    [InlineData("tool_count", "1")]
    [InlineData("tool_tokens", "1")]
    [InlineData("tools", "[\"synthetic-tool\"]")]
    [InlineData("tools_truncated", "true")]
    [InlineData("tools_truncated", "1")]
    public void UsageMetadataCannotConcealAdvertisedTools(string property, string jsonValue)
    {
        var parser = new AgencyJsonProtocol();
        var record = """{"type":"session.usage_checkpoint","data":{"promptCacheBreakState":[{"models":{"synthetic":{""" +
            JsonSerializer.Serialize(property) + ":" + jsonValue + "}}}]}}";
        Assert.Equal("copilot_tools", Assert.Throws<AnswerProviderException>(() => parser.Accept(record)).Code);
    }

    [Theory]
    [InlineData("""{"type":"session.usage_checkpoint","data":{"promptCacheBreakState":{}}}""")]
    [InlineData("""{"type":"session.usage_checkpoint","data":{"promptCacheBreakState":[{"models":null}]}}""")]
    [InlineData("""{"type":"session.usage_checkpoint","data":{"promptCacheBreakState":[{}]}}""")]
    public void MalformedCheckpointShapesFail(string record) =>
        Assert.Throws<AnswerProviderException>(() => new AgencyJsonProtocol().Accept(record));

    [Theory]
    [InlineData("opening your browser")]
    [InlineData("waiting for authorization")]
    [InlineData("device code")]
    [InlineData("authentication is required")]
    [InlineData("please sign in")]
    [InlineData("InteractionRequired")]
    public void AuthenticationNoticesAreBoundedDetectedAcrossFragmentsAndNeverEchoed(string notice)
    {
        var guard = new AgencyAuthenticationGuard();
        Assert.Null(guard.Inspect(notice[..3]));
        var error = guard.Inspect(notice[3..] + " synthetic-private-detail");
        Assert.NotNull(error);
        Assert.Equal("agency_auth_required", error.Code);
        Assert.DoesNotContain("synthetic-private-detail", error.Message);
    }

    [Fact]
    public void AQuestionAboutSignInIsNotTreatedAsAnAuthenticationNotice()
    {
        var record = JsonSerializer.Serialize(new { type = "user.message", data = new { content = "Explain please sign in." } });
        Assert.Null(new AgencyJsonProtocol().Accept(record));
    }
}
