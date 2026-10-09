using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PhantomFudge.App.Audio;
using PhantomFudge.Core;
using PhantomFudge.Core.Answers;

namespace PhantomFudge.App;

public partial class MainWindow
{
    private readonly StructuredTranscriptStore questionTranscript = new();
    private readonly QuestionDraft questionDraft = new();
    private QuestionSnapshot? lastQuestionSnapshot;
    private AudioSource? questionSourceFilter = AudioSource.Microphone;
    private bool questionViewReady;
    private bool renderingQuestion;
    private bool questionEditing;
    private bool questionExpanded;
    private int questionSnapshotCount;

    private bool PassiveWindow => presenting && !questionEditing && !KeyboardControlActive;
    private bool HasQuestionContent => questionDraft.Text.Length > 0 || lastQuestionSnapshot is not null || HasAnswerContent;

    private void InitializeQuestionView()
    {
        questionTranscript.BeginSession(Guid.NewGuid());
        TranscriptText.ItemsSource = transcriptRows;
        questionViewReady = true;
        RenderQuestionDraft();
        RefreshQuestionSegments();
        SetQuestionExpanded(true);
    }

    private void QuestionToggle_Click(object sender, RoutedEventArgs args) =>
        SetQuestionExpanded(!questionExpanded);

    private void SetQuestionExpanded(bool expanded)
    {
        questionExpanded = expanded;
        if (!expanded)
        {
            CancelAnswer();
            _ = StopAgencyProviderAsync();
        }
        if (!expanded && questionEditing)
            SetQuestionEditing(false);
        if (expanded)
        {
            SetNotesExpanded(false);
            AudioPanel.Visibility = Visibility.Collapsed;
        }
        QuestionPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        PartialText.Visibility = Visibility.Visible;
        QuestionToggleButton.Content = expanded ? "Hide Q/A" : "Show Q/A";
        ResizeQuestionPanel();
        if (expanded)
            PrepareAgencyProvider();
    }

    private void TranscriptArea_SizeChanged(object sender, SizeChangedEventArgs args) => ResizeQuestionPanel();

    private void ResizeQuestionPanel()
    {
        UpdateWorkspaceLayout();
    }

    private void QuestionMicFilter_Click(object sender, RoutedEventArgs args) => SetQuestionFilter(AudioSource.Microphone);
    private void QuestionComputerFilter_Click(object sender, RoutedEventArgs args) => SetQuestionFilter(AudioSource.Computer);
    private void QuestionAllFilter_Click(object sender, RoutedEventArgs args) => SetQuestionFilter(null);

    private void SetQuestionFilter(AudioSource? sourceFilter)
    {
        questionSourceFilter = sourceFilter;
        RefreshQuestionSegments();
    }

    private void RefreshQuestionSegments()
    {
        if (!questionViewReady)
            return;
        var selected = QuestionSegments.SelectedItems.Cast<TranscriptSegment>().Select(item => item.Key).ToHashSet();
        var rows = questionTranscript.Segments
            .Where(item => item.Text.Length > 0 && (questionSourceFilter is null || item.Key.Source == questionSourceFilter))
            .ToArray();
        QuestionSegments.ItemsSource = rows;
        foreach (var item in rows.Where(item => selected.Contains(item.Key)))
            QuestionSegments.SelectedItems.Add(item);
        QuestionMicFilter.BorderThickness = new Thickness(questionSourceFilter == AudioSource.Microphone ? 2 : 1);
        QuestionComputerFilter.BorderThickness = new Thickness(questionSourceFilter == AudioSource.Computer ? 2 : 1);
        QuestionAllFilter.BorderThickness = new Thickness(questionSourceFilter is null ? 2 : 1);
    }

    private void QuestionSegments_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs args)
    {
        if (!PassiveWindow || args.OriginalSource is not DependencyObject target ||
            ItemsControl.ContainerFromElement(QuestionSegments, target) is not ListBoxItem { IsEnabled: true } item)
            return;
        item.IsSelected = !item.IsSelected;
        args.Handled = true;
    }

    private void UseQuestionSelection_Click(object sender, RoutedEventArgs args)
    {
        if (ClarificationLocksQuestion)
        {
            QuestionStatus.Text = "Finish or clear the clarification before selecting another question. Original draft retained.";
            return;
        }
        var selected = QuestionSegments.SelectedItems.Cast<TranscriptSegment>().Select(item => item.Key).ToHashSet();
        if (selected.Count == 0)
        {
            QuestionStatus.Text = "Select the intended segments first; the current draft was retained.";
            return;
        }
        try
        {
            questionDraft.ReplaceSelection(questionTranscript.Segments.Where(item => selected.Contains(item.Key)));
            RenderQuestionDraft();
            QuestionStatus.Text = "Selected text copied locally. Review it before Ask; nothing was sent.";
            EnsureQuestionReadingVisible();
        }
        catch (Exception error) when (IsQuestionInputFailure(error))
        {
            QuestionStatus.Text = "Selection rejected: choose one session and at most 8192 characters. Draft retained.";
        }
    }

    private static bool IsQuestionInputFailure(Exception error) =>
        error is ArgumentException or InvalidOperationException or InvalidDataException;

    private void UpdateQuestionTranscript(TranscriptEvent value, Guid captureSessionId)
    {
        var audioSource = value.Source ?? AudioSource.Computer;
        if (value.Type == "partial")
            questionTranscript.SetPartial(captureSessionId, audioSource, value.Text);
        else if (value.Type == "final")
            questionTranscript.AddFinal(captureSessionId, audioSource,
                value.SegmentId ?? throw new InvalidDataException("Missing segment identity."),
                value.Text, value.StartSeconds, value.EndSeconds);
        try
        {
            if (!ClarificationLocksQuestion)
                questionDraft.Refresh(questionTranscript);
        }
        catch (Exception error) when (IsQuestionInputFailure(error))
        {
            QuestionStatus.Text = "Recognition update exceeds the draft limit. Existing draft retained; edit or select less text.";
        }
        RefreshQuestionSegments();
        RenderQuestionDraft();
    }

    private void QuestionEdit_Click(object sender, RoutedEventArgs args) => SetQuestionEditing(!questionEditing);

    private void SetQuestionEditing(bool editing)
    {
        if (KeyboardControlActive && !switchingKeyboardMode)
        {
            SetKeyboardMode(editing ? KeyboardMode.Edit : KeyboardMode.Control);
            return;
        }
        if (editing && (fileBusy || !protectionReady))
        {
            QuestionStatus.Text = "Finish the file operation and restore capture protection before changing editing mode.";
            return;
        }
        if (questionEditing == editing)
            return;
        if (!protectionReady)
        {
            questionEditing = false;
            UpdateInteractionControls();
            return;
        }
        var wasVisible = IsVisible;
        Hide();
        questionEditing = editing;
        UpdateInteractionControls();
        if (wasVisible)
            TryReveal();
        if (editing && protectionReady && !diagnosticRendering)
        {
            if (AwaitingClarificationReply)
                ClarificationReplyEditor.Focus();
            else
                QuestionEditor.Focus();
        }
        if (editing)
            EnsureQuestionReadingVisible();
        QuestionStatus.Text = editing
            ? $"Editing the local {(AwaitingClarificationReply ? "reply" : "question")} takes keyboard focus; transcription continues. Use Done typing to return."
            : "Question remains local. Ask freezes exactly the displayed text.";
    }

    private void UpdateQuestionInteraction()
    {
        if (!questionViewReady)
            return;
        QuestionEditor.IsReadOnly = !questionEditing || ClarificationLocksQuestion;
        QuestionEditor.Focusable = questionEditing && !ClarificationLocksQuestion;
        InputMethod.SetIsInputMethodEnabled(QuestionEditor, QuestionEditor.Focusable);
        QuestionEditButton.Content = questionEditing ? "Done typing" : AwaitingClarificationReply ? "Type reply" : "Type / edit";
        QuestionEditButton.IsEnabled = protectionReady && !fileBusy &&
            (!ClarificationLocksQuestion || AwaitingClarificationReply && !AnswerBusy);
        QuestionAskButton.IsEnabled = protectionReady && !fileBusy && !string.IsNullOrWhiteSpace(questionDraft.Text);
        QuestionCopyButton.IsEnabled = questionDraft.Text.Length > 0;
        QuestionCopyButton.Content = AwaitingClarificationReply ? "Copy original" : "Copy draft";
        UpdateAnswerControls();
    }

    private void QuestionEditor_TextChanged(object sender, TextChangedEventArgs args)
    {
        if (!questionViewReady || renderingQuestion)
            return;
        if (ClarificationLocksQuestion)
        {
            RenderQuestionDraft();
            QuestionStatus.Text = "Original question retained. Use the clarification reply field, or Clear question to start over.";
            return;
        }
        try
        {
            questionDraft.EditText(QuestionEditor.Text);
            RenderQuestionDraft();
        }
        catch (Exception error) when (IsQuestionInputFailure(error))
        {
            RenderQuestionDraft();
            QuestionStatus.Text = "Question limit is 8192 characters. The prior draft was retained; input was not truncated.";
        }
    }

    private void QuestionEditor_Pasting(object sender, DataObjectPastingEventArgs args)
    {
        if (!questionEditing || args.SourceDataObject.GetData(DataFormats.UnicodeText) is not string text ||
            (long)QuestionEditor.Text.Length - QuestionEditor.SelectionLength + text.Length > QuestionDraft.MaximumCharacters)
        {
            args.CancelCommand();
            QuestionStatus.Text = "Paste rejected. Use Type / edit and plain text within the 8192-character limit.";
        }
    }

    private void RenderQuestionDraft()
    {
        if (!questionViewReady)
            return;
        renderingQuestion = true;
        try
        {
            if (QuestionEditor.Text != questionDraft.Text)
                QuestionEditor.Text = questionDraft.Text;
            QuestionProvenance.Text = $"Draft revision {questionDraft.Revision} | {questionDraft.Text.Length}/{QuestionDraft.MaximumCharacters} characters | " +
                (questionDraft.IsProvisional ? "Provisional recognition" : "Final or typed text") +
                (questionDraft.UserEdited ? " | User edited; ASR cannot overwrite" : "") +
                $" | {questionDraft.SelectedSegments.Count} explicitly selected segments";
            UpdateQuestionInteraction();
            SyncTranscriptSelection();
        }
        finally { renderingQuestion = false; }
    }

    private void QuestionAsk_Click(object sender, RoutedEventArgs args)
    {
        if (AwaitingClarificationReply)
        {
            QuestionStatus.Text = "Use Send reply or Yes, answer for this interpretation; no duplicate question was sent.";
            return;
        }
        if (AnswerBusy || !ProviderCanAnswer)
        {
            QuestionStatus.Text = AnswerBusy
                ? "Cancel and wait for the current answer before another Ask."
                : "Agency is preparing or stopped. Select the configured provider or Reload setup; the question remains local.";
            return;
        }
        if (!protectionReady || fileBusy)
        {
            QuestionStatus.Text = "Restore capture protection and finish the file operation before Ask.";
            return;
        }
        try
        {
            lastQuestionSnapshot = questionDraft.Freeze();
            questionSnapshotCount++;
            QuestionSnapshotText.Text = lastQuestionSnapshot.ExactText;
            QuestionSnapshotPanel.Visibility = Visibility.Visible;
            QuestionStatus.Text = "Snapshot frozen locally. Azure is not connected; no request was sent.";
            EnsureQuestionReadingVisible();
            if (clarifyFirst && useAnswerProvider)
                BeginQuestionClarification(lastQuestionSnapshot);
            else
                StartAnswer(lastQuestionSnapshot);
        }
        catch (AnswerProviderException error)
        {
            QuestionStatus.Text = error.Message;
        }
        catch (Exception error) when (IsQuestionInputFailure(error))
        {
            QuestionStatus.Text = "Ask needs a nonempty question of at most 8192 characters. Nothing was sent.";
        }
    }

    private void QuestionCopy_Click(object sender, RoutedEventArgs args)
    {
        if (questionDraft.Text.Length == 0)
        {
            QuestionStatus.Text = "There is no question text to copy.";
            return;
        }
        try
        {
            Clipboard.SetText(questionDraft.Text);
            QuestionStatus.Text = AwaitingClarificationReply
                ? "Only the original question was copied to the Windows clipboard."
                : "Only the displayed question was copied to the Windows clipboard.";
        }
        catch (ExternalException)
        {
            QuestionStatus.Text = "The clipboard is busy. The local question was retained.";
        }
    }

    private void QuestionClear_Click(object sender, RoutedEventArgs args)
    {
        ClearAnswer();
        questionDraft.Clear();
        lastQuestionSnapshot = null;
        QuestionSnapshotText.Clear();
        QuestionSnapshotPanel.Visibility = Visibility.Collapsed;
        QuestionSegments.SelectedItems.Clear();
        RenderQuestionDraft();
        QuestionStatus.Text = "Question and frozen snapshot cleared locally. Transcript and notes were not changed.";
    }
}
