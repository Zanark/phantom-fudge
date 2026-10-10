using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using PhantomFudge.Core;
using PhantomFudge.Core.Answers;

namespace PhantomFudge.App;

public partial class MainWindow
{
    private sealed class SyntheticContextProvider(string text, bool hold = false, bool fail = false) : IAnswerProvider
    {
        public List<AnswerRequest> Requests { get; } = [];
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async IAsyncEnumerable<AnswerEvent> StreamAsync(AnswerRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            yield return new(request.RequestId, 1, AnswerEventKind.TextDelta, text);
            Started.TrySetResult();
            await Task.Delay(hold ? Timeout.Infinite : 10, cancellationToken);
            if (fail)
                throw new AnswerProviderException("synthetic_failure", "Synthetic failed answer.");
            yield return new(request.RequestId, 2, AnswerEventKind.Completed);
        }
    }

    private async Task<object> RunAnswerContextDiagnosticAsync(string directory)
    {
        var checks = new Dictionary<string, bool>();
        QuestionClear_Click(this, new RoutedEventArgs());
        SetQuestionExpanded(true);
        configuredAnswerSettings = new(AnswerProviderMode.Agency, Model: "synthetic-fixture");
        useAnswerProvider = answerSettingsValid = true;
        UpdateQuestionInteraction();
        checks["defaultOffWithoutAnswer"] = !usePreviousAnswer && PreviousAnswerToggle.IsChecked == false &&
            !PreviousAnswerToggle.IsEnabled;
        const string firstText = "Use exponential backoff with jitter.\r\nRetry only transient failures; cap attempts at 3.";
        var first = new SyntheticContextProvider(firstText);
        diagnosticAnswerProvider = first;
        SetUsePreviousAnswer(true);
        checks["missingAnswerRejected"] = !usePreviousAnswer && first.Requests.Count == 0;

        async Task FinishAsync()
        {
            await (answerTask ?? Task.CompletedTask);
            await Dispatcher.InvokeAsync(UpdateQuestionInteraction, System.Windows.Threading.DispatcherPriority.Background);
        }
        void Ask(string text)
        {
            questionDraft.EditText(text);
            RenderQuestionDraft();
            QuestionAsk_Click(this, new RoutedEventArgs());
        }
        Ask("How should a service retry transient failures?");
        await FinishAsync();
        checks["normalAskUnchanged"] = first.Requests.Single().PreviousAnswer is null &&
            CanUseDisplayedAnswer && PreviousAnswerToggle.IsEnabled && !usePreviousAnswer;
        var foreground = GetForegroundWindow();
        PreviousAnswerToggle.IsChecked = true;
        PreviousAnswerToggle_Click(this, new RoutedEventArgs());
        checks["toggleDoesNotSendOrActivate"] = usePreviousAnswer && first.Requests.Count == 1 &&
            GetForegroundWindow() == foreground;
        questionDraft.EditText("How would you make that safe for a payment request?");
        RenderQuestionDraft();
        checks["typingRetainsDisplayedAnswer"] = AnswerText.Text == firstText && first.Requests.Count == 1;
        AnswerProviderNotice.Text = "SYNTHETIC UI FIXTURE: no model or audio request.";
        AnswerRunLabel.Text = "Synthetic answer for follow-up context illustration";
        await RenderDiagnosticAsync(System.IO.Path.Combine(directory, "answer-context.png"));

        var oldWidth = Width;
        var oldHeight = Height;
        var layout = true;
        var themed = true;
        foreach (var theme in Enum.GetValues<UiTheme>())
        {
            ApplyTheme(theme);
            UpdateLayout();
            themed &= PreviousAnswerToggle.Template.FindName("ContextFace", PreviousAnswerToggle) is System.Windows.Controls.Border face &&
                face.Background is SolidColorBrush brush && brush.Color == ThemeBrushes.Color(ThemePalette.For(theme).Selection);
        }
        ApplyTheme(UiTheme.DeepSeaFoam);
        foreach (var size in new[] { (Width: MinWidth, Height: MinHeight), (Width: 900d, Height: 600d), (Width: 1240d, Height: 760d) })
        {
            Width = size.Width;
            Height = size.Height;
            await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.Render);
            layout &= IsPrimaryAskAction() && IsReadingControlReachable(QuestionEditor) &&
                IsReadingControlReachable(AnswerText) && !HasOuterReadingScroll(PreviousAnswerToggle);
        }
        Width = oldWidth;
        Height = oldHeight;
        UpdateLayout();
        checks["noScrollActionLayout"] = layout;
        checks["selectedThemeColors"] = themed;

        const string secondText = "Attach one stable idempotency key to the payment and reuse it for every retry.";
        var second = new SyntheticContextProvider(secondText, hold: true);
        diagnosticAnswerProvider = second;
        QuestionAsk_Click(this, new RoutedEventArgs());
        await second.Started.Task;
        var frozen = second.Requests.Single();
        checks["exactAnswerSnapshot"] = frozen.PreviousAnswer == firstText &&
            frozen.ExactText == "How would you make that safe for a payment request?" &&
            PreviousAnswerContextText.Text == firstText && !PreviousAnswerToggle.IsEnabled;
        SetUsePreviousAnswer(false);
        questionDraft.EditText("A later draft is not part of that submitted request.");
        RenderQuestionDraft();
        checks["busyToggleAndEditsCannotChangeRequest"] = usePreviousAnswer &&
            frozen.PreviousAnswer == firstText && !frozen.ExactText.StartsWith("A later", StringComparison.Ordinal);
        CancelAnswer();
        await FinishAsync();
        checks["cancelledTextNotEligible"] = !CanUseDisplayedAnswer && !usePreviousAnswer &&
            !PreviousAnswerToggle.IsEnabled;

        diagnosticAnswerProvider = new SyntheticContextProvider(secondText);
        Ask("Give a safe payment retry policy.");
        await FinishAsync();
        await ExecuteKeyboardCommandAsync(KeyboardCommands.Parse("context on"));
        checks["keyboardContextOn"] = usePreviousAnswer;
        var third = new SyntheticContextProvider("Store the key and outcome atomically.");
        diagnosticAnswerProvider = third;
        Ask("Where should that key be stored?");
        await FinishAsync();
        checks["latestAnswerOnly"] = third.Requests.Single().PreviousAnswer == secondText &&
            third.Requests[0].PreviousAnswer != firstText && usePreviousAnswer;
        await ExecuteKeyboardCommandAsync(KeyboardCommands.Parse("context off"));
        Ask("Explain independent retries.");
        await FinishAsync();
        checks["keyboardOffOmitsContext"] = !usePreviousAnswer && third.Requests[^1].PreviousAnswer is null;

        var priorToClarification = AnswerText.Text;
        SetUsePreviousAnswer(true);
        ClarifyFirstToggle.IsChecked = true;
        ClarifyFirst_Click(this, new RoutedEventArgs());
        var failedInterpretation = new SyntheticContextProvider("Incomplete interpretation", fail: true);
        diagnosticAnswerProvider = failedInterpretation;
        Ask("Can you explain that differently?");
        await FinishAsync();
        checks["failedInterpretationRetainsSnapshot"] = answerRun?.State == AnswerRunState.Failed &&
            clarificationPreviousAnswer == priorToClarification && usePreviousAnswer;
        var clarifying = new SyntheticClarificationProvider();
        diagnosticAnswerProvider = clarifying;
        QuestionAsk_Click(this, new RoutedEventArgs());
        await FinishAsync();
        SetUsePreviousAnswer(false);
        checks["interpretationLocksContext"] = usePreviousAnswer && !PreviousAnswerToggle.IsEnabled &&
            clarifying.Requests.Single().PreviousAnswer == priorToClarification;
        SubmitClarificationReply("No, explain the storage transaction.");
        await FinishAsync();
        SubmitClarificationReply("Yes");
        await FinishAsync();
        checks["sameContextAcrossClarification"] = clarifying.Requests.Count == 3 &&
            clarifying.Requests.All(request => request.PreviousAnswer == priorToClarification) &&
            clarifying.Requests[^1].Clarification is { Confirmed: true } && CanUseDisplayedAnswer;
        ClarifyFirstToggle.IsChecked = false;
        ClarifyFirst_Click(this, new RoutedEventArgs());

        var failure = new SyntheticContextProvider("Partial failed response", fail: true);
        diagnosticAnswerProvider = failure;
        Ask("Another follow-up.");
        await FinishAsync();
        checks["failedAnswerNotEligible"] = !CanUseDisplayedAnswer && !usePreviousAnswer &&
            answerRun?.State == AnswerRunState.Failed;

        diagnosticAnswerProvider = first;
        Ask("Seed a completed answer.");
        await FinishAsync();
        SetUsePreviousAnswer(true);
        LoadAnswerSettings(() => new(AnswerProviderMode.Agency, Model: "another-synthetic-model"));
        checks["providerChangeResetsToggle"] = !usePreviousAnswer && clarificationPreviousAnswer is null;
        SetUsePreviousAnswer(true);
        AnswerSnapshotMode_Click(this, new RoutedEventArgs());
        var beforeSnapshot = first.Requests.Count;
        Ask("Freeze locally only.");
        checks["snapshotModeNeverSends"] = first.Requests.Count == beforeSnapshot && !usePreviousAnswer &&
            answerRun is null && PreviousAnswerContextText.Text.Length == 0;

        useAnswerProvider = true;
        diagnosticAnswerProvider = first;
        Ask("Seed before clear.");
        await FinishAsync();
        SetUsePreviousAnswer(true);
        var held = new SyntheticContextProvider("Unfinished", hold: true);
        diagnosticAnswerProvider = held;
        Ask("Clear while a contextual answer is streaming.");
        await held.Started.Task;
        QuestionClear_Click(this, new RoutedEventArgs());
        await FinishAsync();
        checks["clearDuringRequestDropsContext"] = answerRun is null && AnswerText.Text.Length == 0 &&
            PreviousAnswerContextText.Text.Length == 0 && clarificationPreviousAnswer is null &&
            !usePreviousAnswer && !PreviousAnswerToggle.IsEnabled;
        diagnosticAnswerProvider = first;
        Ask("A new unrelated question after clearing.");
        await FinishAsync();
        checks["clearDoesNotLeakIntoNextQuestion"] = first.Requests[^1].PreviousAnswer is null;
        QuestionClear_Click(this, new RoutedEventArgs());
        diagnosticAnswerProvider = null;
        useAnswerProvider = answerSettingsValid = false;
        configuredAnswerSettings = new(AnswerProviderMode.SnapshotOnly);
        UpdateQuestionInteraction();
        var passed = checks.Values.All(value => value) && !transcriber.IsRunning;
        var result = new { passed, checks, modelRequests = 0, recordingStarted = false };
        await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(directory, "answer-context.json"),
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        if (!passed)
            throw new InvalidOperationException("Previous-answer context assertions failed: " + JsonSerializer.Serialize(result));
        return result;
    }
}
