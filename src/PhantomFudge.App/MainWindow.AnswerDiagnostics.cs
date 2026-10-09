using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using PhantomFudge.Core.Answers;

namespace PhantomFudge.App;

public partial class MainWindow
{
    private sealed class SyntheticAnswerProvider(bool waitForCancellation = false) : IAnswerProvider
    {
        public AnswerRequest? Request { get; private set; }
        public TaskCompletionSource PartialDelivered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async IAsyncEnumerable<AnswerEvent> StreamAsync(AnswerRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Request = request;
            yield return new(request.RequestId, 1, AnswerEventKind.TextDelta, "Synthetic illustration: ");
            PartialDelivered.TrySetResult();
            if (waitForCancellation)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                yield break;
            }
            await Task.Delay(50, cancellationToken);
            yield return new(request.RequestId, 2, AnswerEventKind.TextDelta,
                "Service Fabric helps a group of computers run and recover applications together.");
            yield return new(request.RequestId, 3, AnswerEventKind.Completed);
        }
    }

    private async Task<object> RunAnswerDiagnosticAsync(string directory)
    {
        var shutdownOrder = new List<string>();
        var releaseProvider = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shutdown = BeginConcurrentShutdown(
            () => { shutdownOrder.Add("capture"); return Task.CompletedTask; },
            () => { shutdownOrder.Add("provider"); return releaseProvider.Task; });
        var captureStopsBeforeProviderCleanup = shutdownOrder.SequenceEqual(new[] { "capture", "provider" }) &&
            !shutdown.IsCompleted;
        releaseProvider.SetResult();
        await shutdown;
        LoadAnswerSettings(() => throw new System.IO.InvalidDataException("Synthetic invalid config"));
        var invalidConfigDisabled = !useAnswerProvider && !answerSettingsValid;
        SetQuestionExpanded(true);
        configuredAnswerSettings = new(AnswerProviderMode.Agency, Model: "synthetic-fixture");
        useAnswerProvider = true;
        answerSettingsValid = true;
        var fake = new SyntheticAnswerProvider();
        diagnosticAnswerProvider = fake;
        questionDraft.EditText("Explain Service Fabric like I'm five.");
        RenderQuestionDraft();
        QuestionAsk_Click(this, new RoutedEventArgs());
        var first = answerRun ?? throw new InvalidOperationException("Missing synthetic answer run.");
        await fake.PartialDelivered.Task;
        var streamingEarly = first.Text == "Synthetic illustration: " && first.State == AnswerRunState.Streaming;
        questionDraft.EditText("A later draft must not alter that request.");
        RenderQuestionDraft();
        var countBeforeBusyAsk = questionSnapshotCount;
        QuestionAsk_Click(this, new RoutedEventArgs());
        var busyAskBlocked = questionSnapshotCount == countBeforeBusyAsk;
        await (answerTask ?? Task.CompletedTask);
        RenderAnswer();
        var exactSnapshot = fake.Request?.ExactText == "Explain Service Fabric like I'm five." &&
            first.Snapshot.ExactText == fake.Request.ExactText &&
            !fake.Request.ExactText.Contains("later", StringComparison.Ordinal);
        var completed = first.State == AnswerRunState.Completed && first.Text.Contains("recover applications", StringComparison.Ordinal);
        AnswerProviderNotice.Text = "SYNTHETIC UI FIXTURE: no CLI, local model or cloud request was started.";
        AnswerRunLabel.Text = "Synthetic provider illustration - not an actual model response";
        EnsureQuestionReadingVisible();
        var originalBackground = BackgroundOpacity.Value;
        BackgroundOpacity.Value = 10;
        var transparentAnswer = await RenderDiagnosticAsync(System.IO.Path.Combine(directory, "answer-background-10.png"));
        var answerPoint = AnswerText.TranslatePoint(
            new Point(AnswerText.ActualWidth - 30, AnswerText.ActualHeight - 30), this);
        var answerDpi = VisualTreeHelper.GetDpi(this);
        var answerRect = new Int32Rect((int)(answerPoint.X * answerDpi.DpiScaleX),
            (int)(answerPoint.Y * answerDpi.DpiScaleY), 1, 1);
        var answerPixel = new byte[4];
        transparentAnswer.CopyPixels(answerRect, answerPixel, 4, 0);
        var answerLowAlpha = answerPixel[3];
        BackgroundOpacity.Value = 100;
        var opaqueAnswer = await RenderDiagnosticAsync(System.IO.Path.Combine(directory, "answer-background-100.png"));
        opaqueAnswer.CopyPixels(answerRect, answerPixel, 4, 0);
        var answerOpacityWorks = answerLowAlpha is > 0 and < 60 && answerPixel[3] == 255 &&
            AnswerText.Opacity == 1 && Opacity == 1 &&
            AnswerText.Foreground is SolidColorBrush { Color.A: 255 };
        BackgroundOpacity.Value = originalBackground;
        await RenderDiagnosticAsync(System.IO.Path.Combine(directory, "answer-synthetic.png"));
        var oldWidth = Width;
        var oldHeight = Height;
        Width = MinWidth;
        Height = MinHeight;
        UpdateLayout();
        await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.Render);
        var minimumAnswerLayout = IsReadingControlReachable(TranscriptText) &&
            IsReadingControlReachable(QuestionEditor) && IsReadingControlReachable(QuestionSnapshotText) &&
            IsReadingControlReachable(AnswerText) && IsReadingControlReachable(AnswerCancelButton) &&
            IsReadingControlReachable(AnswerCopyButton) && IsReadingControlReachable(QuestionAskButton) &&
            !HasOuterReadingScroll(AnswerText) && !HasOuterReadingScroll(AnswerCancelButton);
        await WriteReadingLayoutDiagnosticAsync(directory, "answer-minimum-layout");
        var answerFontHalved = AnswerText.FontSize == 9;
        var responsiveAnswerLayout = true;
        foreach (var size in new[] { (Width: 900d, Height: 600d), (Width: 1240d, Height: 760d), (Width: 620d, Height: 900d) })
        {
            Width = size.Width;
            Height = size.Height;
            UpdateLayout();
            await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.Render);
            answerFontHalved &= AnswerText.FontSize == 10;
            responsiveAnswerLayout &= IsReadingControlReachable(QuestionEditor) &&
                IsReadingControlReachable(QuestionSnapshotText) && IsReadingControlReachable(AnswerText) &&
                IsReadingControlReachable(QuestionAskButton) && IsReadingControlReachable(AnswerCopyButton) &&
                !HasOuterReadingScroll(AnswerText) && AnswerText.Text == first.Text;
        }
        Width = oldWidth;
        Height = oldHeight;
        UpdateLayout();
        var cancellationFake = new SyntheticAnswerProvider(waitForCancellation: true);
        diagnosticAnswerProvider = cancellationFake;
        QuestionAsk_Click(this, new RoutedEventArgs());
        var cancelledRun = answerRun ?? throw new InvalidOperationException("Missing cancellation run.");
        await cancellationFake.PartialDelivered.Task;
        CancelAnswer();
        await (answerTask ?? Task.CompletedTask);
        var cancelled = cancelledRun.State == AnswerRunState.Cancelled &&
            cancelledRun.Text == "Synthetic illustration: ";
        var rejectedStale = !cancelledRun.Apply(new(first.Request.RequestId, 99, AnswerEventKind.TextDelta, "wrong request"));
        QuestionClear_Click(this, new RoutedEventArgs());
        var cleared = answerRun is null && AnswerText.Text.Length == 0 && AnswerPanel.Visibility == Visibility.Collapsed;
        diagnosticAnswerProvider = null;
        useAnswerProvider = false;
        configuredAnswerSettings = new(AnswerProviderMode.SnapshotOnly);
        answerSettingsValid = false;
        UpdateAnswerControls();
        var passed = answerFontHalved && captureStopsBeforeProviderCleanup && answerOpacityWorks && invalidConfigDisabled && minimumAnswerLayout && responsiveAnswerLayout && streamingEarly &&
            busyAskBlocked && exactSnapshot && completed && cancelled && rejectedStale && cleared;
        if (!passed)
            throw new InvalidOperationException("Synthetic answer lifecycle failed: " +
                System.Text.Json.JsonSerializer.Serialize(new { answerFontHalved, captureStopsBeforeProviderCleanup, answerOpacityWorks, answerLowAlpha, invalidConfigDisabled, minimumAnswerLayout, responsiveAnswerLayout, streamingEarly, busyAskBlocked, exactSnapshot,
                    completed, cancelled, rejectedStale, cleared }));
        return new { passed, answerFontHalved, captureStopsBeforeProviderCleanup, answerOpacityWorks, answerLowAlpha, invalidConfigDisabled, minimumAnswerLayout, responsiveAnswerLayout, streamingEarly, busyAskBlocked, exactSnapshot, completed, cancelled, rejectedStale,
            cleared, modelRequests = 0, audioCaptured = false };
    }
}
