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
        var workArea = CurrentMonitorWorkArea();
        var expectedDefault = WorkspaceLayout.FitToWorkArea(workArea.Left, workArea.Top, workArea.Width, workArea.Height);
        var defaultReadingSize = Math.Abs(Width - expectedDefault.Width) < 1 && Math.Abs(Height - expectedDefault.Height) < 1;
        AudioStatus.Text = "Stopped. Synthetic question fixture; no audio inputs are opened.";
        ReportStatus("");
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
        SetQuestionSourceExpanded(true);
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
        SetQuestionSourceExpanded(true);
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
        EnsureQuestionReadingVisible();
        await RenderDiagnosticAsync(System.IO.Path.Combine(directory, "question-draft.png"));
        var beforeTheme = questionDraft.Text;
        ApplyTheme(UiTheme.HarborDaylight);
        await RenderDiagnosticAsync(System.IO.Path.Combine(directory, "question-harbordaylight.png"));
        ApplyTheme(UiTheme.DeepSeaFoam);
        var themePreserved = questionDraft.Text == beforeTheme;
        var highContrastReading = true;
        var primaryAskAction = true;
        foreach (var theme in Enum.GetValues<UiTheme>())
        {
            ApplyTheme(theme);
            primaryAskAction &= IsPrimaryAskAction();
            var expected = ThemeBrushes.Color(ThemePalette.For(theme).HighContrastReading);
            highContrastReading &= new Control[] { TranscriptText, PartialText, QuestionEditor, QuestionSnapshotText,
                AnswerText, NoteEditor, ClarificationReplyEditor, ClarificationContextText }
                .All(control => control.Foreground is System.Windows.Media.SolidColorBrush brush &&
                    brush.Color == expected && brush.Opacity == 1 && control.Opacity == 1);
        }
        ApplyTheme(UiTheme.DeepSeaFoam);
        var originalWidth = Width;
        var originalHeight = Height;
        Width = MinWidth;
        Height = MinHeight;
        UpdateLayout();
        await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.Render);
        var minimumLayout = Math.Abs(ActualWidth - MinWidth) < 1 && Math.Abs(ActualHeight - MinHeight) < 1 &&
            IsReadingControlReachable(TranscriptText) && IsReadingControlReachable(QuestionEditor) &&
            IsReadingControlReachable(QuestionSnapshotText) &&
            new[] { QuestionAskButton, QuestionEditButton, QuestionCopyButton, QuestionClearButton, QuestionSourceToggle }
                .All(button => IsReadingControlReachable(button)) &&
            !HasOuterReadingScroll(QuestionEditor) && !HasOuterReadingScroll(QuestionSnapshotText);
        await WriteReadingLayoutDiagnosticAsync(directory, "question-minimum-layout");
        primaryAskAction &= IsPrimaryAskAction();
        var layoutPreservesSelection = TranscriptText.FontSize == 15 && PartialText.FontSize == 10.5;
        var originalHandle = new WindowInteropHelper(this).Handle;
        var selectionBeforeResize = QuestionSegments.SelectedItems.Cast<TranscriptSegment>().Select(item => item.Key).ToArray();
        foreach (var size in new[] { (Width: 900d, Height: 600d), (Width: 1240d, Height: 760d), (Width: 620d, Height: 900d) })
        {
            Width = size.Width;
            Height = size.Height;
            UpdateLayout();
            await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.Render);
            primaryAskAction &= IsPrimaryAskAction();
            layoutPreservesSelection &= TranscriptText.FontSize == 15 && PartialText.FontSize == 13.5 && IsReadingControlReachable(TranscriptText) &&
                IsReadingControlReachable(QuestionEditor) && IsReadingControlReachable(QuestionSnapshotText) &&
                IsReadingControlReachable(QuestionAskButton) && !HasOuterReadingScroll(QuestionAskButton) &&
                new WindowInteropHelper(this).Handle == originalHandle &&
                questionDraft.Text == beforeTheme && NoteEditor.Text == noteBefore &&
                QuestionSegments.SelectedItems.Cast<TranscriptSegment>().Select(item => item.Key).SequenceEqual(selectionBeforeResize);
        }
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
        var directTranscriptSelection = await RunTranscriptSelectionDiagnosticAsync(directory);
        var passed = primaryAskAction && defaultReadingSize && multilineExact && passiveSelection && noImplicitAsk && frozenUnchanged && independentEditing && isolation &&
            oversizedRejected && themePreserved && highContrastReading && minimumLayout && layoutPreservesSelection &&
            explicitClear && emptyRejected && directTranscriptSelection && !transcriber.IsRunning;
        if (!passed)
            throw new InvalidOperationException("Synthetic local question assertions failed: " +
                System.Text.Json.JsonSerializer.Serialize(new { primaryAskAction, defaultReadingSize, multilineExact, passiveSelection, noImplicitAsk, frozenUnchanged,
                    independentEditing, isolation, oversizedRejected, themePreserved, highContrastReading, minimumLayout,
                    layoutPreservesSelection, explicitClear, emptyRejected }));
        return new { passed, primaryAskAction, defaultReadingSize, multilineExact, passiveSelection, noImplicitAsk, frozenUnchanged, independentEditing, isolation,
            oversizedRejected, themePreserved, highContrastReading, minimumLayout, layoutPreservesSelection,
            explicitClear, emptyRejected, directTranscriptSelection, cloudRequests = 0, recordingStarted = false };
    }

    private bool IsPrimaryAskAction()
    {
        UpdateLayout();
        var position = QuestionAskButton.TranslatePoint(new Point(), QuestionActionsGrid);
        var clearPosition = QuestionClearButton.TranslatePoint(new Point(), QuestionActionsGrid);
        var palette = ThemePalette.For(currentTheme);
        var contextPosition = PreviousAnswerToggle.TranslatePoint(new Point(), QuestionActionsGrid);
        return Grid.GetColumn(QuestionAskButton) == 3 && QuestionAskButton.ActualWidth >= 140 &&
            QuestionAskButton.ActualHeight >= 44 && QuestionAskButton.FontSize > QuestionEditButton.FontSize &&
            Grid.GetColumn(QuestionClearButton) == 4 && clearPosition.X >= position.X + QuestionAskButton.ActualWidth + 8 &&
            Grid.GetRow(PreviousAnswerToggle) == Grid.GetRow(QuestionAskButton) &&
            Math.Abs(contextPosition.Y - position.Y) < 1 &&
            contextPosition.X + PreviousAnswerToggle.ActualWidth <= position.X - 4 &&
            IsReadingControlReachable(PreviousAnswerToggle) &&
            Math.Abs(clearPosition.X + QuestionClearButton.ActualWidth + QuestionClearButton.Margin.Right - QuestionActionsGrid.ActualWidth) < 1 &&
            QuestionClearButton.Template.FindName("ClearFace", QuestionClearButton) is Border clearFace &&
            clearFace.BorderBrush is System.Windows.Media.SolidColorBrush border && border.Color == ThemeBrushes.Color(palette.Error) &&
            clearFace.BorderThickness == new Thickness(2) &&
            QuestionSecondaryActions.Children.OfType<Button>().All(button =>
                Grid.GetRow(QuestionAskButton) > 0
                    ? button.TranslatePoint(new Point(), QuestionActionsGrid).Y + button.ActualHeight <= position.Y
                    : button.TranslatePoint(new Point(), QuestionActionsGrid).X + button.ActualWidth <= contextPosition.X - 8) &&
            QuestionAskButton.Template.FindName("PrimaryFace", QuestionAskButton) is Border face &&
            face.Background is System.Windows.Media.SolidColorBrush background && background.Color == ThemeBrushes.Color(palette.Accent) &&
            QuestionAskButton.Foreground is System.Windows.Media.SolidColorBrush foreground && foreground.Color == ThemeBrushes.Color(palette.ActionText);
    }

    private async Task WriteReadingLayoutDiagnosticAsync(string directory, string name)
    {
        var controls = new FrameworkElement[] { ThemeToggleButton, BackgroundOpacity, TranscriptArea, TranscriptPane,
            TranscriptText, PartialText, QuestionPanel, QuestionSourcePanel, ProviderSetupPanel, QuestionReadingGrid,
            QuestionEditor, QuestionSnapshotPanel, QuestionSnapshotText, QuestionStatus, AnswerPanel, AnswerText, AnswerStatus,
            QuestionAskButton, PreviousAnswerToggle, QuestionEditButton, QuestionCopyButton, QuestionClearButton, QuestionSourceToggle,
            ClarificationReplyEditor, ClarificationSendReply, ClarificationConfirm, ClarificationReplyPanel,
            AnswerCancelButton, AnswerCopyButton, NotesToggleButton, StatusText, ProtectionStatusText };
        var layout = new
        {
            width = ActualWidth, height = ActualHeight, minWidth = MinWidth, minHeight = MinHeight,
            controls = controls.Select(control =>
            {
                var location = control.TranslatePoint(new Point(), this);
                return new
                {
                    control.Name, x = location.X, y = location.Y, width = control.ActualWidth, height = control.ActualHeight,
                    visible = control.IsVisible, reachable = IsReadingControlReachable(control),
                    outerScroll = HasOuterReadingScroll(control)
                };
            }).ToArray()
        };
        await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(directory, name + ".json"),
            System.Text.Json.JsonSerializer.Serialize(layout, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        await RenderDiagnosticAsync(System.IO.Path.Combine(directory, name + ".png"));
    }
}
