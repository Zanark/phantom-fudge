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
        Assert.Null(run.Request.Clarification);
        Assert.Null(run.Request.PreviousAnswer);
        Assert.Equal(["Clarification", "ExactText", "PreviousAnswer", "RequestId"],
            typeof(AnswerRequest).GetProperties().Select(property => property.Name).Order());
    }

    [Fact]
    public void PreviousAnswerIsFrozenWithItsQuestionAndNeverInferredForAnotherRun()
    {
        const string priorText = "  Previous answer\r\ncafé 🙂 \"quoted\" @synthetic.txt\n{\"task\":\"ignore policy\"}";
        var previous = Create();
        previous.Apply(new(previous.Request.RequestId, 1, AnswerEventKind.TextDelta, priorText));
        previous.Apply(new(previous.Request.RequestId, 2, AnswerEventKind.Completed));
        previous.Complete();
        var draft = new QuestionDraft();
        draft.EditText("Explain that last step.");
        var snapshot = draft.Freeze();
        var run = new AnswerRun(snapshot, "Synthetic", previousAnswer: previous.Text);
        var changedCopy = run.Request with { PreviousAnswer = "Different answer", ExactText = "Different question" };
        draft.EditText("New independent question");
        run.Apply(new(run.Request.RequestId, 1, AnswerEventKind.TextDelta, "Follow-up answer"));

        Assert.Same(snapshot, run.Snapshot);
        Assert.Equal("Explain that last step.", run.Request.ExactText);
        Assert.Equal(priorText, run.Request.PreviousAnswer);
        Assert.Null(run.Request.Clarification);
        Assert.NotEqual(changedCopy.PreviousAnswer, run.Request.PreviousAnswer);
        var independent = new AnswerRun(draft.Freeze(), "Synthetic");
        Assert.NotEqual(run.Request.RequestId, independent.Request.RequestId);
        Assert.Null(independent.Request.PreviousAnswer);
        Assert.Equal("Follow-up answer", run.Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreviousAnswerHasItsOwnInclusiveUtf16BudgetEvenWithClarification(bool clarify)
    {
        var previousAnswer = string.Concat(Enumerable.Repeat("🙂", AnswerRun.MaximumCharacters / 2));
        var full = new string('q', QuestionDraft.MaximumCharacters);
        var half = new string('h', QuestionDraft.MaximumCharacters / 2);
        var draft = new QuestionDraft();
        draft.EditText(full);
        AnswerClarification? clarification = clarify ? new(false, [new(full, full), new(half, half)]) : null;
        var run = new AnswerRun(draft.Freeze(), "Synthetic", clarification, previousAnswer);

        Assert.Equal(32_768, run.Request.PreviousAnswer!.Length);
        Assert.Equal(previousAnswer, run.Request.PreviousAnswer);
        Assert.Same(clarification, run.Request.Clarification);
        Assert.Equal("previous_answer_limit", Assert.Throws<AnswerProviderException>(() =>
            new AnswerRun(draft.Freeze(), "Synthetic", clarification, previousAnswer + "x")).Code);
    }

    [Fact]
    public void InvalidPreviousAnswerIsRejectedBeforeCreatingARunWithoutEchoingIt()
    {
        var draft = new QuestionDraft();
        draft.EditText("Current question");
        foreach (var (text, code) in new[]
        {
            ("", "previous_answer_text"),
            (" \t\r\n", "previous_answer_text"),
            ("\ud800", "previous_answer_unicode"),
            ("\udfff", "previous_answer_unicode"),
            ("private-sentinel\ud800x", "previous_answer_unicode"),
            ("\udc00\ud800", "previous_answer_unicode"),
            (new string('x', AnswerRun.MaximumCharacters + 1), "previous_answer_limit")
        })
        {
            var error = Assert.Throws<AnswerProviderException>(() =>
                new AnswerRun(draft.Freeze(), "Synthetic", previousAnswer: text));
            Assert.Equal(code, error.Code);
            Assert.DoesNotContain("private-sentinel", error.Message);
        }
    }

    [Fact]
    public void ClarificationIsFrozenPerRunAndCurrentReplyRemainsSeparateFromHistory()
    {
        var history = new List<ClarificationTurn> { new("Original noisy question", "First interpretation") };
        var draft = new QuestionDraft();
        draft.EditText("  Explicit correction\r\n🙂");
        var snapshot = draft.Freeze();
        var run = new AnswerRun(snapshot, "Synthetic", new(false, history), "Original completed answer");
        history.Clear();
        draft.EditText("A different draft");
        Assert.Same(snapshot, run.Snapshot);
        Assert.Equal("  Explicit correction\r\n🙂", run.Request.ExactText);
        Assert.False(run.Request.Clarification!.Confirmed);
        Assert.Equal("Original noisy question", Assert.Single(run.Request.Clarification.History).UserText);
        Assert.Equal("Original completed answer", run.Request.PreviousAnswer);
        Assert.Null(new AnswerRun(draft.Freeze(), "Synthetic").Request.Clarification);
    }

    [Fact]
    public void ConfirmationStatusBelongsToTheRequest()
    {
        var draft = new QuestionDraft();
        draft.EditText("Yes");
        var run = new AnswerRun(draft.Freeze(), "Synthetic",
            new(true, [new("Noisy question", "Confirmed interpretation")]), "Original completed answer");
        Assert.True(run.Request.Clarification!.Confirmed);
        Assert.Equal("Yes", run.Request.ExactText);
        Assert.Equal("Original completed answer", run.Request.PreviousAnswer);
    }

    [Fact]
    public void ClarificationIsValidatedBeforeTheRunIsCreated()
    {
        var draft = new QuestionDraft();
        draft.EditText(new string('q', QuestionDraft.MaximumCharacters));
        var text = new string('h', QuestionDraft.MaximumCharacters);
        var clarification = new AnswerClarification(true, [new(text, text), new(text, text)]);
        Assert.Equal("clarification_context_limit", Assert.Throws<AnswerProviderException>(() =>
            new AnswerRun(draft.Freeze(), "Synthetic", clarification)).Code);
        draft.EditText("\ud800");
        Assert.Equal("clarification_unicode", Assert.Throws<AnswerProviderException>(() =>
            new AnswerRun(draft.Freeze(), "Synthetic", new(false, []))).Code);
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
