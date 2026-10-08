using PhantomFudge.Core.Answers;

namespace PhantomFudge.Core.Tests;

public sealed class AnswerRunTests
{
    private static AnswerRun Create()
    {
        var draft = new QuestionDraft();
        draft.EditText("Explain Service Fabric.");
        return new(draft.Freeze(), "Synthetic provider");
    }

    [Fact]
    public void RequestContainsExactTextWithoutProvenance()
    {
        var run = Create();
        Assert.Equal(run.Snapshot.ExactText, run.Request.ExactText);
        Assert.NotEqual(Guid.Empty, run.Request.RequestId);
        Assert.Equal(2, typeof(AnswerRequest).GetProperties().Length);
    }

    [Fact]
    public void TextArrivesBeforeCompletionAndNeedsTerminalEvidence()
    {
        var run = Create();
        run.Apply(new(run.Request.RequestId, 1, AnswerEventKind.TextDelta, "Hello"));
        Assert.Equal("Hello", run.Text);
        Assert.Equal(AnswerRunState.Streaming, run.State);
        Assert.Throws<AnswerProviderException>(run.Complete);
        run.Apply(new(run.Request.RequestId, 2, AnswerEventKind.Completed));
        run.Complete();
        Assert.Equal(AnswerRunState.Completed, run.State);
    }

    [Fact]
    public void CancelledOrForeignEventsNeverAppend()
    {
        var run = Create();
        Assert.False(run.Apply(new(Guid.NewGuid(), 1, AnswerEventKind.TextDelta, "wrong")));
        run.Apply(new(run.Request.RequestId, 1, AnswerEventKind.TextDelta, "partial"));
        run.Cancel();
        Assert.False(run.Apply(new(run.Request.RequestId, 2, AnswerEventKind.TextDelta, "late")));
        run.Complete();
        Assert.Equal("partial", run.Text);
        Assert.Equal(AnswerRunState.Cancelled, run.State);
    }

    [Fact]
    public void DuplicateEventsAndEventsAfterTerminalFail()
    {
        var run = Create();
        run.Apply(new(run.Request.RequestId, 1, AnswerEventKind.TextDelta, "partial"));
        Assert.Throws<AnswerProviderException>(() => run.Apply(new(run.Request.RequestId, 1, AnswerEventKind.TextDelta, "replay")));
        run.Apply(new(run.Request.RequestId, 2, AnswerEventKind.Completed));
        Assert.Throws<AnswerProviderException>(() => run.Apply(new(run.Request.RequestId, 3, AnswerEventKind.TextDelta, "late")));
    }

    [Fact]
    public void OutputOverflowRetainsOnlyAcceptedText()
    {
        var run = Create();
        run.Apply(new(run.Request.RequestId, 1, AnswerEventKind.TextDelta, new string('x', AnswerRun.MaximumCharacters)));
        Assert.Throws<AnswerProviderException>(() => run.Apply(new(run.Request.RequestId, 2, AnswerEventKind.TextDelta, "extra")));
        run.Fail("Incomplete");
        Assert.Equal(AnswerRun.MaximumCharacters, run.Text.Length);
        Assert.Equal(AnswerRunState.Failed, run.State);
    }

    [Fact]
    public void EmptyTerminalIsNotAnAnswer()
    {
        var run = Create();
        run.Apply(new(run.Request.RequestId, 1, AnswerEventKind.Completed));
        Assert.Throws<AnswerProviderException>(run.Complete);
    }

    [Theory]
    [InlineData("http://localhost:11434/v1")]
    [InlineData("http://127.1:11434/v1")]
    [InlineData("http://127.0.0.1.evil.test/v1")]
    [InlineData("https://127.0.0.1/v1")]
    [InlineData("http://user@127.0.0.1/v1")]
    [InlineData("http://127.0.0.1/v1?q=1")]
    [InlineData("http://127.0.0.1/v1#fragment")]
    [InlineData("http://2130706433/v1")]
    [InlineData("http://127.0.0.1/v%31")]
    public void LocalEndpointsCannotSelectAnotherDestination(string endpoint) =>
        Assert.Throws<ArgumentException>(() => new AnswerSettings(AnswerProviderMode.LocalModel, Model: "example", LocalEndpoint: endpoint).Validate());

    [Theory]
    [InlineData("http://127.0.0.1:11434/v1")]
    [InlineData("http://[::1]:11434/v1/")]
    public void LiteralLoopbackModelEndpointsAreAccepted(string endpoint) =>
        new AnswerSettings(AnswerProviderMode.LocalModel, Model: "qwen3:8b", LocalEndpoint: endpoint).Validate();

    [Fact]
    public void ModeAndConfigurationNeverImplicitlyChangeProviders()
    {
        Assert.Throws<ArgumentException>(() => new AnswerSettings((AnswerProviderMode)99).Validate());
        Assert.Throws<ArgumentException>(() => new AnswerSettings(AnswerProviderMode.LocalModel).Validate());
        Assert.Throws<ArgumentException>(() => new AnswerSettings(AnswerProviderMode.Copilot, LocalEndpoint: "http://127.0.0.1/v1").Validate());
        Assert.Throws<ArgumentException>(() => new AnswerSettings(AnswerProviderMode.Copilot, CliPath: "copilot --yolo").Validate());
        Assert.Throws<ArgumentException>(() => new AnswerSettings(AnswerProviderMode.Copilot, Model: "x --allow-all").Validate());
    }
}
