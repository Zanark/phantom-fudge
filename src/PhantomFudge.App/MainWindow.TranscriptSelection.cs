using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PhantomFudge.App.Audio;
using PhantomFudge.Core;

namespace PhantomFudge.App;

public partial class MainWindow
{
    private sealed record TranscriptRow(string DisplayText, TranscriptSegment? Segment)
    {
        public bool IsSelectable => Segment is not null;
    }

    private readonly ObservableCollection<TranscriptRow> transcriptRows = [];
    private TranscriptRow[] partialTranscriptRows = [];
    private bool renderingTranscriptSelection;

    private void AppendFinalTranscriptRow(TranscriptEvent value, int previousLength)
    {
        var rendered = transcript.Text;
        if (rendered.Length == previousLength)
            return;
        var segment = questionTranscript.Segments.Single(item => item.IsFinal &&
            item.Key.CaptureSessionId == questionTranscript.SessionId && item.ProtocolSegmentId == value.SegmentId);
        var scroll = FindTranscriptScrollViewer(TranscriptText);
        var followEnd = !KeyboardSelecting && (scroll is null || scroll.VerticalOffset >= scroll.ScrollableHeight - 1);
        var row = new TranscriptRow(rendered[previousLength..^Environment.NewLine.Length], segment);
        transcriptRows.Add(row);
        if (followEnd)
            TranscriptText.ScrollIntoView(row);
    }

    private void ArchiveTranscriptPartials(bool sessionSeparator)
    {
        foreach (var row in partialTranscriptRows)
        {
            var closed = questionTranscript.Segments.Single(item => item.Key == row.Segment!.Key);
            transcriptRows.Add(new("[Unfinished recognition] " + row.DisplayText, closed));
        }
        if (sessionSeparator)
            transcriptRows.Add(new("--- New session ---", null));
        RefreshTranscriptPartials();
    }

    private void RefreshTranscriptPartials()
    {
        renderingTranscriptSelection = true;
        try
        {
            partialTranscriptRows = questionTranscript.Segments
                .Where(item => item.IsActive && item.Text.Length > 0)
                .OrderBy(item => item.Key.Source)
                .Select(item => new TranscriptRow($"[{item.Key.Source.DisplayName()}] {item.Text}", item))
                .ToArray();
            PartialText.ItemsSource = partialTranscriptRows;
        }
        finally { renderingTranscriptSelection = false; }
        RefreshKeyboardTranscriptView();
        SyncTranscriptSelection();
    }

    private void SyncTranscriptSelection()
    {
        if (!questionViewReady)
            return;
        var keys = questionDraft.SelectedSegments.Select(item => item.Key).ToHashSet();
        renderingTranscriptSelection = true;
        try
        {
            foreach (var list in new[] { TranscriptText, PartialText })
            {
                list.SelectedItems.Clear();
                foreach (TranscriptRow row in list.Items)
                    if (row.Segment is not null && keys.Contains(row.Segment.Key))
                        list.SelectedItems.Add(row);
            }
        }
        finally { renderingTranscriptSelection = false; }
    }

    private void TranscriptRows_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs args)
    {
        if (sender is not ListBox list || args.OriginalSource is not DependencyObject target ||
            ItemsControl.ContainerFromElement(list, target) is not ListBoxItem { IsEnabled: true } item)
            return;
        args.Handled = true;
        item.IsSelected = !item.IsSelected;
    }

    private void TranscriptRows_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!questionViewReady || renderingTranscriptSelection)
            return;
        if (!protectionReady || fileBusy || questionDraft.UserEdited || ClarificationLocksQuestion)
        {
            if (protectionReady && !fileBusy)
            {
                SetQuestionExpanded(true);
                EnsureQuestionReadingVisible();
            }
            QuestionStatus.Text = ClarificationLocksQuestion
                ? "Finish or clear the clarification before selecting another question. Original draft retained."
                : questionDraft.UserEdited
                ? "Edited question retained. Clear question before choosing a different set of lines."
                : "Restore capture protection and finish the file operation before selecting question lines.";
            SyncTranscriptSelection();
            return;
        }
        ApplyTranscriptSelection(args.RemovedItems.Cast<TranscriptRow>(), args.AddedItems.Cast<TranscriptRow>());
    }

    private void ApplyTranscriptSelection(IEnumerable<TranscriptRow> removed, IEnumerable<TranscriptRow> added)
    {
        var selected = questionDraft.SelectedSegments.ToDictionary(item => item.Key);
        foreach (var row in removed)
            if (row.Segment is not null)
                selected.Remove(row.Segment.Key);
        foreach (var row in added)
            if (row.Segment is not null)
                selected[row.Segment.Key] = row.Segment;
        // Use displayed history order, not click order, and retain copied entries outside the view.
        var ordered = transcriptRows.Concat(partialTranscriptRows).Where(row => row.Segment is not null)
            .Select(row => row.Segment!).Where(item => selected.ContainsKey(item.Key)).ToArray();
        var visibleKeys = ordered.Select(item => item.Key).ToHashSet();
        try
        {
            questionDraft.ReplaceSelection(selected.Values.Where(item => !visibleKeys.Contains(item.Key)).Concat(ordered));
            RenderQuestionDraft();
            SetQuestionExpanded(true);
            EnsureQuestionReadingVisible();
            if (!KeyboardSelecting) SetQuestionEditing(true);
            QuestionSegments.SelectedItems.Clear();
            var selectedKeys = questionDraft.SelectedSegments.Select(item => item.Key).ToHashSet();
            foreach (TranscriptSegment segment in QuestionSegments.Items)
                if (selectedKeys.Contains(segment.Key))
                    QuestionSegments.SelectedItems.Add(segment);
            QuestionStatus.Text = $"{selected.Count} line(s) copied locally. Review before Ask. Nothing was sent.";
        }
        catch (Exception error) when (IsQuestionInputFailure(error))
        {
            QuestionStatus.Text = "Selection rejected: choose one session and at most 8192 characters. Draft retained.";
            SyncTranscriptSelection();
        }
    }

    private static ScrollViewer? FindTranscriptScrollViewer(DependencyObject element)
    {
        if (element is ScrollViewer scroll)
            return scroll;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
            if (FindTranscriptScrollViewer(VisualTreeHelper.GetChild(element, index)) is { } child)
                return child;
        return null;
    }
}
