using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PhantomFudge.App.Audio;
using PhantomFudge.Core;

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
    private int questionSnapshotCount;

    private bool PassiveWindow => presenting && !questionEditing;
    private bool HasQuestionContent => questionDraft.Text.Length > 0 || lastQuestionSnapshot is not null || HasAnswerContent;

    private void InitializeQuestionView()
    {
        questionTranscript.BeginSession(Guid.NewGuid());
        questionViewReady = true;
        RenderQuestionDraft();
        RefreshQuestionSegments();
    }

    private void QuestionToggle_Click(object sender, RoutedEventArgs args) =>
        SetQuestionExpanded(QuestionPanel.Visibility != Visibility.Visible);

    private void SetQuestionExpanded(bool expanded)
    {
        if (!expanded)
            CancelAnswer();
        if (!expanded && questionEditing)
            SetQuestionEditing(false);
        if (expanded)
        {
            SetNotesExpanded(false);
            AudioPanel.Visibility = Visibility.Collapsed;
        }
        QuestionPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        PartialText.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
        QuestionToggleButton.Content = expanded ? "Close question" : "Question";
        ResizeQuestionPanel();
    }

    private void TranscriptArea_SizeChanged(object sender, SizeChangedEventArgs args) => ResizeQuestionPanel();

    private void ResizeQuestionPanel()
    {
        if (questionViewReady)
            QuestionScroll.MaxHeight = Math.Max(60, TranscriptArea.ActualHeight * 0.65 - 22);
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
            QuestionEditor.BringIntoView();
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
            QuestionEditor.Focus();
        if (editing)
            QuestionEditor.BringIntoView();
        QuestionStatus.Text = editing
            ? "Editing the local question takes keyboard focus; transcription continues. Use Done typing to return."
            : "Question remains local. Ask freezes exactly the displayed text.";
    }

    private void UpdateQuestionInteraction()
    {
        if (!questionViewReady)
            return;
        QuestionEditor.IsReadOnly = !questionEditing;
        QuestionEditor.Focusable = questionEditing;
        InputMethod.SetIsInputMethodEnabled(QuestionEditor, questionEditing);
        QuestionEditButton.Content = questionEditing ? "Done typing" : "Type / edit";
        QuestionEditButton.IsEnabled = protectionReady && !fileBusy;
        QuestionAskButton.IsEnabled = protectionReady && !fileBusy && !string.IsNullOrWhiteSpace(questionDraft.Text);
        QuestionCopyButton.IsEnabled = questionDraft.Text.Length > 0;
        UpdateAnswerControls();
    }

    private void QuestionEditor_TextChanged(object sender, TextChangedEventArgs args)
    {
        if (!questionViewReady || renderingQuestion)
            return;
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
        }
        finally { renderingQuestion = false; }
    }

    private void QuestionAsk_Click(object sender, RoutedEventArgs args)
    {
        if (AnswerBusy)
        {
            QuestionStatus.Text = "Cancel and wait for the current answer before another Ask.";
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
            QuestionScroll.ScrollToEnd();
            StartAnswer(lastQuestionSnapshot);
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
            QuestionStatus.Text = "Only the displayed question was copied to the Windows clipboard.";
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
