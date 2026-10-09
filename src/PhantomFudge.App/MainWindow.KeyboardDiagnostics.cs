using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using PhantomFudge.App.Audio;
using PhantomFudge.App.Native;
using PhantomFudge.Core;
using PhantomFudge.Core.Answers;

namespace PhantomFudge.App;

public partial class MainWindow
{
    private async Task<object> RunKeyboardDiagnosticAsync(string directory)
    {
        var checks = new Dictionary<string, bool>();
        var width = Width;
        var height = Height;
        checks["invalidConfigDisablesOnlyShortcut"] = true;
        foreach (var error in new Exception[] { new InvalidDataException(), new JsonException(),
            new UnauthorizedAccessException(), new ArgumentException() })
        {
            LoadKeyboardShortcut(() => throw error);
            checks["invalidConfigDisablesOnlyShortcut"] &= !keyboardShortcut.Enabled && keyboardShortcutNotice is not null;
        }
        LoadKeyboardShortcut(() => new KeyboardShortcutSettings());
        ReportStatus("");
        AudioStatus.Text = "Synthetic keyboard fixture. No audio inputs or model services are opened.";
        QuestionClear_Click(this, new RoutedEventArgs());
        SetQuestionEditing(false);
        var snapshots = questionSnapshotCount;
        var session = questionTranscript.SessionId;
        ReceiveTranscript(new("final", "How do replicas recover?", 50000, 0, 1, AudioSource.Computer, session));
        ReceiveTranscript(new("partial", "Explain the recovery", Source: AudioSource.Microphone, CaptureSessionId: session));
        using var audioGuard = new CancellationTokenSource();
        audioStartCancellation = audioGuard;
        audioBusy = true;
        ToggleKeyboardControl();
        checks["activation"] = keyboardMode == KeyboardMode.Control && !PassiveWindow && NoteEditor.IsReadOnly &&
            (WindowProtection.ReadStyles(new WindowInteropHelper(this).Handle) & 0x08000000) == 0;
        var routedSelect = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this), Environment.TickCount, Key.V)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        Window_PreviewKeyDown(this, routedSelect);
        checks["localKeyRouting"] = routedSelect.Handled && keyboardMode == KeyboardMode.Select;
        var partialKey = keyboardTranscriptKey;
        HandleKeyboardKey(Key.Space, ModifierKeys.None, false);
        HandleKeyboardKey(Key.Space, ModifierKeys.None, true);
        checks["spaceOnce"] = questionDraft.SelectedSegments.Count == 1 && keyboardMode == KeyboardMode.Select &&
            !questionEditing && !QuestionAskButton.IsEnabled;
        ReceiveTranscript(new("final", "Explain the recovery process.", 50001, 1, 2, AudioSource.Microphone, session));
        checks["stableFinalization"] = keyboardTranscriptKey == partialKey &&
            questionDraft.Text == "Explain the recovery process." && questionDraft.SelectedSegments.Single().IsFinal;
        HandleKeyboardKey(Key.K, ModifierKeys.None, false);
        HandleKeyboardKey(Key.Space, ModifierKeys.None, false);
        checks["twoSourceSelection"] = questionDraft.SelectedSegments.Count == 2 &&
            questionDraft.Text == "How do replicas recover?" + Environment.NewLine + "Explain the recovery process.";
        var cursor = keyboardTranscriptKey;
        ReceiveTranscript(new("final", "Unselected new speech.", 50002, 2, 3, AudioSource.Computer, session));
        checks["incomingKeepsCursor"] = keyboardTranscriptKey == cursor && questionDraft.SelectedSegments.Count == 2;
        HandleKeyboardKey(Key.D1, ModifierKeys.None, false);
        checks["micFilter"] = TranscriptText.Items.Cast<TranscriptRow>().All(row => row.Segment?.Key.Source == AudioSource.Microphone) &&
            questionDraft.SelectedSegments.Count == 2;
        HandleKeyboardKey(Key.D3, ModifierKeys.None, false);
        OpenKeyboardPrompt(true);
        KeyboardPrompt.Text = "recovery process";
        HandleKeyboardKey(Key.Enter, ModifierKeys.None, false);
        checks["localSearch"] = keyboardMode == KeyboardMode.Select && TranscriptText.Items.Count == 1 &&
            keyboardTranscriptKey == partialKey && questionSnapshotCount == snapshots;
        FindKeyboardTranscript("");
        HandleKeyboardKey(Key.G, ModifierKeys.None, false);
        HandleKeyboardKey(Key.G, ModifierKeys.None, false);
        checks["firstAndLatest"] = keyboardTranscriptKey == KeyboardTranscriptRows()[0].Segment!.Key;
        HandleKeyboardKey(Key.G, ModifierKeys.Shift, false);
        checks["firstAndLatest"] &= keyboardTranscriptKey == KeyboardTranscriptRows()[^1].Segment!.Key;
        Width = MinWidth;
        Height = MinHeight;
        UpdateLayout();
        checks["compactSelectionBorrowsReader"] = !QuestionPanel.IsVisible && IsReadingControlReachable(TranscriptText) &&
            questionExpanded && audioBusy && !audioGuard.IsCancellationRequested;
        Width = width;
        Height = height;
        await RenderDiagnosticAsync(Path.Combine(directory, "keyboard-selection.png"));
        HandleKeyboardKey(Key.Enter, ModifierKeys.Control, false);
        checks["selectCannotSubmit"] = questionSnapshotCount == snapshots && keyboardMode == KeyboardMode.Select;
        HandleKeyboardKey(Key.Enter, ModifierKeys.None, false);
        checks["reviewOnly"] = keyboardMode == KeyboardMode.Edit && questionEditing &&
            questionSnapshotCount == snapshots && TranscriptText.Items.Count == transcriptRows.Count;
        checks["literalEditKeys"] = !HandleKeyboardKey(Key.J, ModifierKeys.None, false) &&
            !HandleKeyboardKey(Key.Enter, ModifierKeys.None, false) &&
            !HandleKeyboardKey(Key.V, ModifierKeys.Control, false);
        const string exact = "Explain recovery.\r\nInclude one example.";
        QuestionEditor.Text = exact;
        var provider = new SyntheticCoveredAnswer();
        configuredAnswerSettings = new(AnswerProviderMode.Agency, Model: "synthetic-fixture");
        answerSettingsValid = useAnswerProvider = true;
        diagnosticAnswerProvider = provider;
        HandleKeyboardKey(Key.Enter, ModifierKeys.Control, false);
        HandleKeyboardKey(Key.Enter, ModifierKeys.Control, true);
        await provider.Accepted.Task;
        var run = answerRun ?? throw new InvalidOperationException("Missing keyboard answer fixture.");
        checks["explicitExactSubmitOnce"] = questionSnapshotCount == snapshots + 1 && run.Request.ExactText == exact;
        HandleKeyboardKey(Key.Escape, ModifierKeys.None, false);
        HandleKeyboardKey(Key.A, ModifierKeys.None, false);
        HandleKeyboardKey(Key.Add, ModifierKeys.None, false);
        var chosen = chosenAnswerFontSize;
        OpenKeyboardPrompt(false);
        KeyboardPrompt.Text = "font 14";
        await RenderDiagnosticAsync(Path.Combine(directory, "keyboard-command.png"));
        HandleKeyboardKey(Key.Enter, ModifierKeys.None, false);
        checks["typedFontCommand"] = chosen is not null && AnswerText.FontSize == 14 && AnswerFontSlider.Value == 14;
        OpenKeyboardPrompt(false);
        KeyboardPrompt.Text = "start & unwanted";
        HandleKeyboardKey(Key.Enter, ModifierKeys.None, false);
        checks["invalidCommandStaysLocal"] = keyboardMode == KeyboardMode.Command && !transcriber.IsRunning &&
            run.State == AnswerRunState.Streaming;
        HandleKeyboardKey(Key.Escape, ModifierKeys.None, false);
        HandleKeyboardKey(Key.M, ModifierKeys.None, false);
        checks["maskDisarms"] = coverActive && !KeyboardControlActive && PassiveWindow;
        ToggleKeyboardControl();
        checks["hotkeyUnmasks"] = !coverActive && keyboardMode == KeyboardMode.Control;
        provider.Release.TrySetResult();
        await (answerTask ?? Task.CompletedTask);
        checks["workContinues"] = run.State == AnswerRunState.Completed && run.Text == "COVER_BACKGROUND_ANSWER" &&
            audioBusy && !audioGuard.IsCancellationRequested && questionTranscript.SessionId == session;
        SetKeyboardMode(KeyboardMode.Read);
        AnswerFontSlider.Value = 24;
        var fontFits = true;
        foreach (var size in new[] { (Width: MinWidth, Height: MinHeight), (Width: 900d, Height: 600d), (Width: 1240d, Height: 760d) })
        {
            Width = size.Width;
            Height = size.Height;
            UpdateLayout();
            await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.Render);
            var fits = AnswerText.FontSize == 24 && AnswerFontSlider.Value == 24 &&
                IsReadingControlReachable(AnswerText) && IsReadingControlReachable(AnswerFontSlider) &&
                IsReadingControlReachable(QuestionAskButton);
            checks[$"fontFits{size.Width:0}x{size.Height:0}"] = fits;
            fontFits &= fits;
            await WriteReadingLayoutDiagnosticAsync(directory, $"keyboard-font-{size.Width:0}-{size.Height:0}");
            if (size.Width == MinWidth)
            {
                await RenderDiagnosticAsync(Path.Combine(directory, "keyboard-minimum.png"));
                LeaveKeyboardControl();
                UpdateLayout();
                checks["passiveLargeFontFits"] = AnswerText.FontSize == 24 && IsReadingControlReachable(AnswerText) &&
                    IsReadingControlReachable(AnswerFontSlider) && PassiveWindow;
                ToggleKeyboardControl();
                SetKeyboardMode(KeyboardMode.Read);
            }
        }
        checks["chosenFontSurvivesResizeAndFits"] = fontFits;
        HandleKeyboardKey(Key.Add, ModifierKeys.None, false);
        checks["fontBounds"] = AnswerText.FontSize == 24;
        AnswerFontSlider.Value = 8;
        HandleKeyboardKey(Key.Subtract, ModifierKeys.None, false);
        checks["fontBounds"] &= AnswerText.FontSize == 8;
        AnswerFontSlider.Value = 14;
        await RenderDiagnosticAsync(Path.Combine(directory, "keyboard-answer-size.png"));
        HandleKeyboardKey(Key.Escape, ModifierKeys.None, false);
        BeginKeyboardSelection();
        checks["editedDraftProtected"] = keyboardMode == KeyboardMode.Control && questionDraft.Text == exact;
        audioBusy = false;
        audioStartCancellation = null;
        UpdateAudioControls();
        checks["devices"] = await RunKeyboardDevicesDiagnosticAsync(checks);
        SetQuestionExpanded(true);
        QuestionClear_Click(this, new RoutedEventArgs());
        var clarification = new SyntheticClarificationProvider();
        diagnosticAnswerProvider = clarification;
        await ExecuteKeyboardCommandAsync(KeyboardCommands.Parse("clarify on"));
        SetKeyboardMode(KeyboardMode.Edit);
        QuestionEditor.Text = "Please explain recovery.";
        HandleKeyboardKey(Key.Enter, ModifierKeys.Control, false);
        await (answerTask ?? Task.CompletedTask);
        await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        checks["asyncReplyTarget"] = keyboardMode == KeyboardMode.Edit &&
            keyboardEditorTarget == ClarificationReplyEditor && KeyboardModeLabel.Text == "EDIT REPLY";
        ClarificationReplyEditor.Text = "Yes, with a concrete example.";
        HandleKeyboardKey(Key.Enter, ModifierKeys.Control, false);
        HandleKeyboardKey(Key.Enter, ModifierKeys.Control, true);
        await (answerTask ?? Task.CompletedTask);
        checks["clarificationReplyOnce"] = clarification.Requests.Count == 2 &&
            clarification.Requests[^1].ExactText == "Yes, with a concrete example." &&
            clarification.Requests[^1].Clarification is { Confirmed: false };
        ClarificationReplyEditor.Text = "yes";
        HandleKeyboardKey(Key.Enter, ModifierKeys.Control, false);
        await (answerTask ?? Task.CompletedTask);
        await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        checks["clarificationConfirmation"] = clarification.Requests.Count == 3 &&
            clarification.Requests[^1].Clarification is { Confirmed: true };
        checks["asyncOriginalTarget"] = keyboardEditorTarget == QuestionEditor && KeyboardModeLabel.Text == "EDIT QUESTION";
        await ExecuteKeyboardCommandAsync(KeyboardCommands.Parse("clarify off"));
        SetKeyboardMode(KeyboardMode.Control);
        HandleKeyboardKey(Key.Escape, ModifierKeys.None, false);
        checks["escapeRestoresPassive"] = !KeyboardControlActive && !questionEditing && PassiveWindow &&
            (WindowProtection.ReadStyles(new WindowInteropHelper(this).Handle) & 0x08000000) != 0;
        checks["inactiveLettersUntouched"] = !HandleKeyboardKey(Key.V, ModifierKeys.None, false);
        Width = width;
        Height = height;
        chosenAnswerFontSize = null;
        UpdateWorkspaceLayout();
        QuestionClear_Click(this, new RoutedEventArgs());
        diagnosticAnswerProvider = null;
        configuredAnswerSettings = new(AnswerProviderMode.SnapshotOnly);
        answerSettingsValid = useAnswerProvider = false;
        UpdateAnswerControls();
        var result = new { passed = checks.Values.All(value => value), checks, audioCaptured = false, modelRequests = 0,
            limitation = "Synthetic local routing/styles only; real shortcut collisions and cross-application foreground return are not qualified." };
        await File.WriteAllTextAsync(Path.Combine(directory, "keyboard-controls.json"),
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        if (!result.passed) throw new InvalidOperationException("Keyboard fixture failed: " + JsonSerializer.Serialize(result));
        return result;
    }

    private async Task<bool> RunKeyboardDevicesDiagnosticAsync(Dictionary<string, bool> checks)
    {
        var output = OutputDevices.ItemsSource;
        var microphone = MicrophoneDevices.ItemsSource;
        var selectedOutput = OutputDevices.SelectedItem;
        var selectedMicrophone = MicrophoneDevices.SelectedItem;
        try
        {
            OutputDevices.ItemsSource = Enumerable.Range(0, 30).Select(i => new RenderDevice($"keys-{i}", $"Synthetic output {i}")).ToArray();
            MicrophoneDevices.ItemsSource = new[] { new MicrophoneDevice("keys-mic", "Synthetic microphone") };
            OutputDevices.SelectedIndex = MicrophoneDevices.SelectedIndex = 0;
            SetKeyboardMode(KeyboardMode.Control);
            await ExecuteKeyboardCommandAsync(KeyboardCommands.Parse("devices"));
            await SettleDeviceCardsAsync();
            HandleKeyboardKey(Key.Right, ModifierKeys.None, false);
            var passed = OutputDevices.SelectedIndex == 0;
            checks["deviceNavigationDoesNotSelect"] = passed && keyboardMode == KeyboardMode.Devices;
            HandleKeyboardKey(Key.Space, ModifierKeys.None, false);
            passed &= OutputDevices.SelectedIndex == 1;
            checks["deviceSpaceSelects"] = OutputDevices.SelectedIndex == 1;
            HandleKeyboardKey(Key.OemCloseBrackets, ModifierKeys.None, false);
            await SettleDeviceCardsAsync();
            passed &= OutputDevices.SelectedIndex == 1 && CardsFitWithoutScrolling(OutputDevices);
            checks["devicePagePreservesSelection"] = OutputDevices.SelectedIndex == 1 && CardsFitWithoutScrolling(OutputDevices);
            HandleKeyboardKey(Key.Tab, ModifierKeys.None, false);
            passed &= keyboardDeviceList == MicrophoneDevices;
            checks["deviceTabChangesSource"] = keyboardDeviceList == MicrophoneDevices;
            audioBusy = true;
            HandleKeyboardKey(Key.Space, ModifierKeys.None, false);
            passed &= MicrophoneDevices.SelectedIndex == 0 && !transcriber.IsRunning;
            audioBusy = false;
            HandleKeyboardKey(Key.Escape, ModifierKeys.None, false);
            return passed && AudioPanel.Visibility == Visibility.Collapsed && keyboardMode == KeyboardMode.Control;
        }
        finally
        {
            audioBusy = false;
            OutputDevices.ItemsSource = output;
            MicrophoneDevices.ItemsSource = microphone;
            OutputDevices.SelectedItem = selectedOutput;
            MicrophoneDevices.SelectedItem = selectedMicrophone;
            AudioPanel.Visibility = Visibility.Collapsed;
        }
    }
}
