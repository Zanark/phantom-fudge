using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using PhantomFudge.App.Audio;
using PhantomFudge.App.Native;
using PhantomFudge.Core;
using PhantomFudge.Core.Answers;

namespace PhantomFudge.App;

public partial class MainWindow
{
    private sealed class SyntheticCoveredAnswer : IAnswerProvider
    {
        public TaskCompletionSource Accepted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async IAsyncEnumerable<AnswerEvent> StreamAsync(AnswerRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Accepted.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            yield return new(request.RequestId, 1, AnswerEventKind.TextDelta, "COVER_BACKGROUND_ANSWER");
            yield return new(request.RequestId, 2, AnswerEventKind.Completed);
        }
    }

    private static IReadOnlyList<AutomationPeer> AutomationDescendants(AutomationPeer root)
    {
        var peers = new List<AutomationPeer>();
        var pendingPeers = new Queue<AutomationPeer>();
        pendingPeers.Enqueue(root);
        while (pendingPeers.TryDequeue(out var next))
        {
            if (peers.Count >= 4096)
                throw new InvalidOperationException("Synthetic automation tree exceeded its bound.");
            peers.Add(next);
            foreach (var child in next.GetChildren() ?? [])
                pendingPeers.Enqueue(child);
        }
        return peers;
    }

    private async Task<object> RunCoverDiagnosticAsync(string directory)
    {
        QuestionClear_Click(this, new RoutedEventArgs());
        SetQuestionExpanded(true);
        var handle = new WindowInteropHelper(this).Handle;
        var styleBefore = WindowProtection.ReadStyles(handle);
        var previousNote = NoteEditor.Text;
        var previousTheme = currentTheme;
        var previousMode = captureMode;
        var session = questionTranscript.SessionId;
        NoteEditor.Text = "COVER_PRIVATE_NOTE";
        questionDraft.EditText("COVER_PRIVATE_QUESTION");
        RenderQuestionDraft();
        var provider = new SyntheticCoveredAnswer();
        diagnosticAnswerProvider = provider;
        configuredAnswerSettings = new(AnswerProviderMode.Agency, Model: "synthetic-fixture");
        answerSettingsValid = useAnswerProvider = true;
        var snapshotsBefore = questionSnapshotCount;
        QuestionAsk_Click(this, new RoutedEventArgs());
        await provider.Accepted.Task;
        var run = answerRun ?? throw new InvalidOperationException("Missing covered answer fixture.");
        var rootPeer = UIElementAutomationPeer.CreatePeerForElement(this)
            ?? throw new InvalidOperationException("Missing window automation peer.");
        var beforePeers = AutomationDescendants(rootPeer);
        var editorPeer = UIElementAutomationPeer.CreatePeerForElement(QuestionEditor);
        var notePeer = UIElementAutomationPeer.CreatePeerForElement(NoteEditor);
        var answerPeer = UIElementAutomationPeer.CreatePeerForElement(AnswerText);
        var accessibilityBefore = editorPeer is not null && beforePeers.Contains(editorPeer);
        using var audioGuard = new CancellationTokenSource();
        audioStartCancellation = audioGuard;
        audioBusy = true;
        SetCover(true);
        await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var detached = coverActive && WorkspaceHost.Child is null && WorkspaceRoot.Parent is null &&
            VisualTreeHelper.GetParent(WorkspaceRoot) is null && !WorkspaceRoot.IsEnabled &&
            !QuestionEditor.IsVisible && !TranscriptText.IsVisible && !DiagnosticsButton.IsVisible;
        var coverPeers = AutomationDescendants(rootPeer);
        var accessibilityCovered = !coverPeers.Contains(editorPeer!) && !coverPeers.Contains(notePeer!) &&
            !coverPeers.Contains(answerPeer!) && coverPeers.Any(peer => peer.GetName() == "NOTEPAD IS READY") &&
            coverPeers.Any(peer => peer == UIElementAutomationPeer.CreatePeerForElement(CoverToggleButton));
        var cachedPeerValueReadable = editorPeer?.GetPattern(PatternInterface.Value) is IValueProvider cachedValue &&
            cachedValue.Value == "COVER_PRIVATE_QUESTION";
        var accidentalShortcut = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this), Environment.TickCount, Key.S)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        Window_PreviewKeyDown(this, accidentalShortcut);
        var keysBlocked = accidentalShortcut.Handled;
        ReceiveTranscript(new TranscriptEvent("final", "COVER_BACKGROUND_TRANSCRIPT", 40000, 1, 2, AudioSource.Microphone, session));
        provider.Release.TrySetResult();
        await (answerTask ?? Task.CompletedTask);
        var backgroundContinues = run.State == AnswerRunState.Completed && run.Text == "COVER_BACKGROUND_ANSWER" &&
            transcript.Text.Contains("COVER_BACKGROUND_TRANSCRIPT", StringComparison.Ordinal) &&
            questionDraft.Text == "COVER_PRIVATE_QUESTION" && NoteEditor.Text == "COVER_PRIVATE_NOTE" &&
            audioBusy && !audioGuard.IsCancellationRequested && captureMode == previousMode &&
            questionTranscript.SessionId == session && questionSnapshotCount == snapshotsBefore + 1;
        var noHiddenVisibleTiming = !firstAnswerVisibleLogged;
        var coverAppearance = true;
        foreach (var theme in Enum.GetValues<UiTheme>())
        {
            ApplyTheme(theme);
            UpdateLayout();
            coverAppearance &= CoverMessage.Text == "NOTEPAD IS READY" && CoverMessage.IsVisible &&
                CoverToggleButton.IsVisible && CoverMessage.Foreground is SolidColorBrush brush &&
                brush.Color == ThemeBrushes.Color(ThemePalette.For(theme).HighContrastReading) &&
                coverPeers.All(peer => peer.GetPattern(PatternInterface.Value) is not IValueProvider value ||
                    !value.Value.Contains("COVER_PRIVATE", StringComparison.Ordinal));
        }
        ApplyTheme(previousTheme);
        var coverImage = await RenderDiagnosticAsync(System.IO.Path.Combine(directory, "notepad-cover.png"));
        var textPosition = CoverMessage.TranslatePoint(new Point(), this);
        var dpi = VisualTreeHelper.GetDpi(this);
        var textBounds = new Int32Rect((int)(textPosition.X * dpi.DpiScaleX), (int)(textPosition.Y * dpi.DpiScaleY),
            (int)Math.Ceiling(CoverMessage.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(CoverMessage.ActualHeight * dpi.DpiScaleY));
        var pixels = new byte[textBounds.Width * textBounds.Height * 4];
        coverImage.CopyPixels(textBounds, pixels, textBounds.Width * 4, 0);
        var renderedMessage = Enumerable.Range(0, pixels.Length / 4).Count(index =>
            pixels[index * 4] > 180 && pixels[index * 4 + 1] > 180 && pixels[index * 4 + 2] > 180) > 100;
        var oldWidth = Width;
        var oldHeight = Height;
        Width = MinWidth;
        Height = MinHeight;
        UpdateLayout();
        var compactCover = CoverToggleButton.IsVisible && CoverMessage.IsVisible &&
            CoverMessage.ActualWidth <= ActualWidth && CoverMessage.ActualHeight < ActualHeight - 38;
        Width = oldWidth;
        Height = oldHeight;
        var coverHandleStable = handle == new WindowInteropHelper(this).Handle &&
            WindowProtection.ReadStyles(handle) == styleBefore && protectionReady;
        var peekPreventionRequested = lastProtection?.PeekPreventionRequested == true;
        SetCover(false);
        await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var restored = WorkspaceHost.Child == WorkspaceRoot && WorkspaceRoot.IsEnabled && QuestionEditor.IsVisible &&
            AnswerText.Text == "COVER_BACKGROUND_ANSWER" && firstAnswerVisibleLogged && !coverActive &&
            AutomationDescendants(rootPeer).Contains(editorPeer!);
        audioBusy = false;
        audioStartCancellation = null;
        UpdateAudioControls();
        SetQuestionEditing(true);
        SetCover(true);
        SetCover(false);
        var editingPreserved = questionEditing && !QuestionEditor.IsReadOnly && presenting;
        SetQuestionEditing(false);
        AudioPanel.Visibility = Visibility.Visible;
        await SettleDeviceCardsAsync();
        var output = OutputDevices.SelectedItem;
        var mic = MicrophoneDevices.SelectedItem;
        SetCover(true);
        SetCover(false);
        await SettleDeviceCardsAsync();
        var setupRestored = AudioPanel.IsVisible && ReferenceEquals(output, OutputDevices.SelectedItem) &&
            ReferenceEquals(mic, MicrophoneDevices.SelectedItem);
        fileBusy = true;
        SetCover(true);
        var busyCoverRefused = !coverActive;
        fileBusy = false;
        AudioPanel.Visibility = Visibility.Collapsed;
        SetQuestionExpanded(true);
        NoteEditor.Text = previousNote;
        QuestionClear_Click(this, new RoutedEventArgs());
        diagnosticAnswerProvider = null;
        configuredAnswerSettings = new(AnswerProviderMode.SnapshotOnly);
        answerSettingsValid = useAnswerProvider = false;
        UpdateAnswerControls();
        var passed = accessibilityBefore && detached && accessibilityCovered && keysBlocked && backgroundContinues &&
            noHiddenVisibleTiming && coverAppearance && renderedMessage && compactCover && coverHandleStable &&
            restored && editingPreserved && setupRestored && busyCoverRefused && peekPreventionRequested;
        var result = new { passed, accessibilityBefore, detached, accessibilityCovered, keysBlocked, backgroundContinues,
            noHiddenVisibleTiming, coverAppearance, renderedMessage, compactCover, coverHandleStable, restored,
            editingPreserved, setupRestored, busyCoverRefused, cachedPeerValueReadable, peekPreventionRequested,
            modelRequests = 0, audioCaptured = false,
            limitation = "Fresh in-process WPF peer traversal only; cached clients, process inspection and recipient capture are not qualified." };
        await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(directory, "notepad-cover.json"),
            System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        if (!passed)
            throw new InvalidOperationException("Synthetic notepad cover checks failed: " + System.Text.Json.JsonSerializer.Serialize(result));
        return result;
    }
}
