using System.Runtime.CompilerServices;
using System.Windows;
using PhantomFudge.Core.Answers;

namespace PhantomFudge.App;

public partial class MainWindow
{
    private sealed class SyntheticClarificationProvider(bool hold = false, bool oversized = false) : IAnswerProvider
    {
        public List<AnswerRequest> Requests { get; } = [];
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async IAsyncEnumerable<AnswerEvent> StreamAsync(AnswerRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var text = request.Clarification switch
            {
                null => "A direct answer with no clarification history.",
                { Confirmed: true } => "Synthetic confirmed answer: use a dictionary for linear-time Two Sum in Python.",
                { History.Count: 0 } => "I think you want a solution to the Two Sum coding problem. Is that correct?",
                _ => "You want a Two Sum solution in Python, including an explanation. Is that correct?"
            };
            if (oversized)
                text = new string('x', 8193);
            yield return new(request.RequestId, 1, AnswerEventKind.TextDelta, text);
            Started.TrySetResult();
            await Task.Delay(hold ? Timeout.Infinite : 25, cancellationToken);
            yield return new(request.RequestId, 2, AnswerEventKind.Completed);
        }
    }

    private async Task<object> RunClarificationDiagnosticAsync(string directory)
    {
        QuestionClear_Click(this, new RoutedEventArgs());
        var defaultOff = !clarifyFirst && ClarifyFirstToggle.IsChecked == false;
        configuredAnswerSettings = new(AnswerProviderMode.Agency, Model: "synthetic-fixture");
        useAnswerProvider = true;
        answerSettingsValid = true;
        var provider = new SyntheticClarificationProvider();
        diagnosticAnswerProvider = provider;
        ClarifyFirstToggle.IsChecked = true;
        ClarifyFirst_Click(this, new RoutedEventArgs());
        var toggleNoRequest = provider.Requests.Count == 0;
        const string original = "write me the lead good problem to some solution";
        questionDraft.EditText(original);
        RenderQuestionDraft();
        QuestionAsk_Click(this, new RoutedEventArgs());
        var firstRun = answerRun ?? throw new InvalidOperationException("Missing interpretation run.");
        var originalLocked = QuestionEditor.IsReadOnly && ClarificationLocksQuestion;
        await (answerTask ?? Task.CompletedTask);
        UpdateQuestionInteraction();
        var interpretation = firstRun.State == AnswerRunState.Completed && AwaitingClarificationReply &&
            clarificationHistory.Count == 1 && AnswerHeading.Text == "INTERPRETATION" &&
            provider.Requests.Single().Clarification is { Confirmed: false, History.Count: 0 } &&
            provider.Requests[0].ExactText == original && questionDraft.Text == original;
        var widthBefore = Width;
        var heightBefore = Height;
        var replyLayouts = true;
        foreach (var size in new[] { (Width: MinWidth, Height: MinHeight), (Width: 900d, Height: 600d), (Width: 620d, Height: 900d) })
        {
            Width = size.Width;
            Height = size.Height;
            UpdateLayout();
            await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.Render);
            SetQuestionEditing(true);
            UpdateLayout();
            replyLayouts &= IsReadingControlReachable(ClarificationReplyEditor) && IsReadingControlReachable(ClarificationSendReply);
            SetQuestionEditing(false);
            await WriteReadingLayoutDiagnosticAsync(directory, $"clarification-layout-{size.Width:0}-{size.Height:0}");
            replyLayouts &= IsReadingControlReachable(ClarificationReplyEditor) &&
                IsReadingControlReachable(ClarificationSendReply) && IsReadingControlReachable(ClarificationConfirm) &&
                IsReadingControlReachable(AnswerText) && !HasOuterReadingScroll(ClarificationReplyEditor);
        }
        Width = widthBefore;
        Height = heightBefore;
        UpdateLayout();
        SetQuestionEditing(true);
        const string correction = "No, use Python and explain the approach.";
        ClarificationReplyEditor.Text = correction;
        var typingNoRequest = provider.Requests.Count == 1;
        ClarificationConfirm_Click(this, new RoutedEventArgs());
        var correctionNotDiscarded = provider.Requests.Count == 1 && ClarificationReplyEditor.Text == correction;
        ClarificationReplyEditor.Text = new string('x', 8193);
        var oversizedReplyRetained = ClarificationReplyEditor.Text == correction;
        ClarificationSendReply_Click(this, new RoutedEventArgs());
        await (answerTask ?? Task.CompletedTask);
        UpdateQuestionInteraction();
        var correctionIncluded = provider.Requests.Count == 2 &&
            provider.Requests[1].ExactText == correction &&
            provider.Requests[1].Clarification is { Confirmed: false, History.Count: 1 } metadata &&
            metadata.History[0].UserText == original && metadata.History[0].Interpretation == firstRun.Text &&
            clarificationHistory.Count == 2 && questionDraft.Text == original;
        AudioStatus.Text = "Stopped. Synthetic clarification conversation; no model or audio input.";
        await RenderDiagnosticAsync(System.IO.Path.Combine(directory, "question-clarification.png"));
        ClarificationReplyEditor.Text = " yEs ";
        var noAutoConfirmation = provider.Requests.Count == 2;
        ClarificationSendReply_Click(this, new RoutedEventArgs());
        await (answerTask ?? Task.CompletedTask);
        UpdateQuestionInteraction();
        var confirmed = provider.Requests.Count == 3 &&
            provider.Requests[2].Clarification is { Confirmed: true, History.Count: 2 } &&
            provider.Requests[2].ExactText == " yEs " && !AwaitingClarificationReply &&
            answerRun?.State == AnswerRunState.Completed && AnswerHeading.Text == "ANSWER" &&
            questionDraft.Text == original && QuestionEditor.IsVisible;
        ClarifyFirstToggle.IsChecked = false;
        ClarifyFirst_Click(this, new RoutedEventArgs());
        QuestionAsk_Click(this, new RoutedEventArgs());
        await (answerTask ?? Task.CompletedTask);
        var defaultUnchanged = provider.Requests.Count == 4 && provider.Requests[^1].Clarification is null &&
            provider.Requests[^1].ExactText == original && clarificationHistory.Count == 0;

        QuestionClear_Click(this, new RoutedEventArgs());
        ClarifyFirstToggle.IsChecked = true;
        ClarifyFirst_Click(this, new RoutedEventArgs());
        var held = new SyntheticClarificationProvider(hold: true);
        diagnosticAnswerProvider = held;
        questionDraft.EditText(original);
        RenderQuestionDraft();
        QuestionAsk_Click(this, new RoutedEventArgs());
        await held.Started.Task;
        ClarifyFirstToggle.IsChecked = false;
        ClarifyFirst_Click(this, new RoutedEventArgs());
        var modeLockedWhileBusy = clarifyFirst && ClarifyFirstToggle.IsChecked == true;
        CancelAnswer();
        await (answerTask ?? Task.CompletedTask);
        var cancellationSafe = clarificationHistory.Count == 0 && !AwaitingClarificationReply &&
            answerRun?.State == AnswerRunState.Cancelled && questionDraft.Text == original;
        QuestionClear_Click(this, new RoutedEventArgs());
        var tooLong = new SyntheticClarificationProvider(oversized: true);
        diagnosticAnswerProvider = tooLong;
        questionDraft.EditText(original);
        RenderQuestionDraft();
        QuestionAsk_Click(this, new RoutedEventArgs());
        await (answerTask ?? Task.CompletedTask);
        var oversizedInterpretationRejected = answerRun?.State == AnswerRunState.Failed &&
            clarificationHistory.Count == 0 && questionDraft.Text == original;
        QuestionClear_Click(this, new RoutedEventArgs());
        diagnosticAnswerProvider = provider;
        useAnswerProvider = false;
        questionDraft.EditText(original);
        RenderQuestionDraft();
        var requestCount = provider.Requests.Count;
        QuestionAsk_Click(this, new RoutedEventArgs());
        var snapshotOnly = provider.Requests.Count == requestCount && lastQuestionSnapshot?.ExactText == original &&
            clarificationHistory.Count == 0 && !AwaitingClarificationReply;
        QuestionClear_Click(this, new RoutedEventArgs());
        ClarifyFirstToggle.IsChecked = false;
        ClarifyFirst_Click(this, new RoutedEventArgs());
        SetQuestionEditing(false);
        diagnosticAnswerProvider = null;

        var passed = defaultOff && toggleNoRequest && originalLocked && interpretation && replyLayouts &&
            typingNoRequest && correctionNotDiscarded && oversizedReplyRetained && correctionIncluded &&
            noAutoConfirmation && confirmed && defaultUnchanged && modeLockedWhileBusy &&
            cancellationSafe && oversizedInterpretationRejected && snapshotOnly && !transcriber.IsRunning;
        var result = new { passed, defaultOff, toggleNoRequest, originalLocked, interpretation, replyLayouts,
            typingNoRequest, correctionNotDiscarded, oversizedReplyRetained, correctionIncluded,
            noAutoConfirmation, confirmed, defaultUnchanged, modeLockedWhileBusy, cancellationSafe,
            oversizedInterpretationRejected, snapshotOnly, modelRequests = 0, recordingStarted = false };
        await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(directory, "clarification.json"),
            System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        if (!passed)
            throw new InvalidOperationException("Clarification UI assertions failed: " + System.Text.Json.JsonSerializer.Serialize(result));
        return result;
    }
}
