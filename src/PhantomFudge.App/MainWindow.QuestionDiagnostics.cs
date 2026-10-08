using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using PhantomFudge.App.Audio;
using PhantomFudge.App.Native;
using PhantomFudge.Core;

namespace PhantomFudge.App;

public partial class MainWindow
{
    private async Task<object> RunQuestionDiagnosticAsync(string directory)
    {
        if (!presenting)
            await TogglePresentationAsync();
        AudioStatus.Text = "Stopped. Synthetic question fixture; no audio inputs are opened.";
        var multilineExact = true;
        foreach (var text in new[] { "First\nSecond", "First\rSecond", "First\r\nSecond" })
        {
            questionDraft.EditText(text);
            RenderQuestionDraft();
            QuestionAsk_Click(this, new RoutedEventArgs());
            multilineExact &= QuestionEditor.Text == text && lastQuestionSnapshot?.ExactText == QuestionEditor.Text;
        }
        QuestionClear_Click(this, new RoutedEventArgs());
        var oldSession = questionTranscript.SessionId;
        var currentSession = Guid.Parse("11111111-2222-4333-8444-555555555555");
        transcript.BeginSession();
        questionTranscript.BeginSession(currentSession);
        var snapshotsBefore = questionSnapshotCount;
        var noteBefore = NoteEditor.Text;
        SetQuestionExpanded(true);
        ReceiveTranscript(new TranscriptEvent("final", "Unselected computer context.", 1, 0, 1,
            AudioSource.Computer, currentSession));
        ReceiveTranscript(new TranscriptEvent("final", "Earlier unrelated microphone conversation.", 2, 0, 1,
            AudioSource.Microphone, currentSession));
        ReceiveTranscript(new TranscriptEvent("partial", "Explain Service Fabric like", Source: AudioSource.Microphone,
            CaptureSessionId: currentSession));
        SetQuestionFilter(AudioSource.Microphone);
        var micPartial = questionTranscript.Segments.Single(item => !item.IsFinal &&
            item.Key.Source == AudioSource.Microphone && item.Key.CaptureSessionId == currentSession);
        QuestionSegments.ScrollIntoView(micPartial);
        UpdateLayout();
        var row = (ListBoxItem)QuestionSegments.ItemContainerGenerator.ContainerFromItem(micPartial);
        var foreground = GetForegroundWindow();
        row.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        {
            RoutedEvent = Mouse.PreviewMouseDownEvent
        });
        UseQuestionSelection_Click(this, new RoutedEventArgs());
        var passiveSelection = QuestionSegments.SelectedItems.Count == 1 && GetForegroundWindow() == foreground
            && PassiveWindow && questionDraft.Text == micPartial.Text;
        var noImplicitAsk = questionSnapshotCount == snapshotsBefore && lastQuestionSnapshot is null;
        QuestionScroll.ScrollToTop();
        await RenderDiagnosticAsync(System.IO.Path.Combine(directory, "question-selection.png"));
        QuestionAsk_Click(this, new RoutedEventArgs());
        var frozen = lastQuestionSnapshot ?? throw new InvalidOperationException("Question snapshot was not created.");
        ReceiveTranscript(new TranscriptEvent("final", "Explain Service Fabric like I am five.", 3, 1, 2,
            AudioSource.Microphone, currentSession));
        var frozenUnchanged = frozen.ExactText == "Explain Service Fabric like" &&
            QuestionSnapshotText.Text == frozen.ExactText && questionDraft.Text == "Explain Service Fabric like I am five.";
        using var startGuard = new CancellationTokenSource();
        audioStartCancellation = startGuard;
        audioBusy = true;
        SetQuestionEditing(true);
        var independentEditing = questionEditing && !QuestionEditor.IsReadOnly && NoteEditor.IsReadOnly &&
            audioBusy && !startGuard.IsCancellationRequested && !PassiveWindow;
        QuestionEditor.Text = "Explain Service Fabric like I'm five.";
        SetQuestionEditing(false);
        independentEditing &= audioBusy && !startGuard.IsCancellationRequested && PassiveWindow &&
            (WindowProtection.ReadStyles(new WindowInteropHelper(this).Handle) & 0x08000000) != 0;
        audioStartCancellation = null;
        audioBusy = false;
        UpdateAudioControls();
        ReceiveTranscript(new TranscriptEvent("partial", "Unrelated later mic speech?", Source: AudioSource.Microphone,
            CaptureSessionId: currentSession));
        ReceiveTranscript(new TranscriptEvent("final", "STALE SESSION MUST NOT APPEAR", 99, 0, 1,
            AudioSource.Microphone, oldSession));
        var isolation = questionDraft.Text == "Explain Service Fabric like I'm five." &&
            !questionDraft.Text.Contains("Unselected", StringComparison.Ordinal) &&
            !transcript.Text.Contains("STALE SESSION", StringComparison.Ordinal) &&
            !questionTranscript.Segments.Any(item => item.Text.Contains("STALE SESSION", StringComparison.Ordinal)) &&
            questionSnapshotCount == snapshotsBefore + 1 && NoteEditor.Text == noteBefore;
        var beforeOversize = questionDraft.Text;
        QuestionEditor.Text = new string('x', QuestionDraft.MaximumCharacters + 1);
        var oversizedRejected = questionDraft.Text == beforeOversize && QuestionEditor.Text == beforeOversize;
        QuestionAsk_Click(this, new RoutedEventArgs());
        QuestionScroll.ScrollToEnd();
        await RenderDiagnosticAsync(System.IO.Path.Combine(directory, "question-draft.png"));
        var beforeTheme = questionDraft.Text;
        ApplyTheme(UiTheme.HarborDaylight);
        await RenderDiagnosticAsync(System.IO.Path.Combine(directory, "question-harbordaylight.png"));
        ApplyTheme(UiTheme.DeepSeaFoam);
        var themePreserved = questionDraft.Text == beforeTheme;
        var originalWidth = Width;
        var originalHeight = Height;
        Width = MinWidth;
        Height = MinHeight;
        UpdateLayout();
        await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.Render);
        var minimumLayout = Math.Abs(ActualWidth - MinWidth) < 1 && Math.Abs(ActualHeight - MinHeight) < 1 &&
            TranscriptText.ActualHeight > 0 && QuestionScroll.ViewportHeight > 0 &&
            QuestionPanel.TranslatePoint(new Point(0, QuestionPanel.ActualHeight), this).Y <=
                NotesToggleButton.TranslatePoint(new Point(0, 0), this).Y &&
            QuestionPanel.TranslatePoint(new Point(QuestionPanel.ActualWidth, 0), this).X <= ActualWidth;
        Width = originalWidth;
        Height = originalHeight;
        UpdateLayout();
        var oldQuestionId = questionDraft.QuestionId;
        QuestionClear_Click(this, new RoutedEventArgs());
        var explicitClear = questionDraft.QuestionId != oldQuestionId && questionDraft.Text.Length == 0 &&
            lastQuestionSnapshot is null && transcript.HasContent && NoteEditor.Text == noteBefore;
        var beforeEmpty = questionSnapshotCount;
        QuestionAsk_Click(this, new RoutedEventArgs());
        var emptyRejected = questionSnapshotCount == beforeEmpty;
        var passed = multilineExact && passiveSelection && noImplicitAsk && frozenUnchanged && independentEditing && isolation &&
            oversizedRejected && themePreserved && minimumLayout && explicitClear && emptyRejected && !transcriber.IsRunning;
        if (!passed)
            throw new InvalidOperationException("Synthetic local question assertions failed: " +
                System.Text.Json.JsonSerializer.Serialize(new { multilineExact, passiveSelection, noImplicitAsk, frozenUnchanged,
                    independentEditing, isolation, oversizedRejected, themePreserved, minimumLayout, explicitClear, emptyRejected }));
        return new { passed, multilineExact, passiveSelection, noImplicitAsk, frozenUnchanged, independentEditing, isolation,
            oversizedRejected, themePreserved, minimumLayout, explicitClear, emptyRejected, cloudRequests = 0, recordingStarted = false };
    }
}
