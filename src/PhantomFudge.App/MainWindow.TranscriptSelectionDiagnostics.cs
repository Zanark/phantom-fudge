using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PhantomFudge.App.Audio;
using PhantomFudge.Core;

namespace PhantomFudge.App;

public partial class MainWindow
{
    private async Task<bool> RunTranscriptSelectionDiagnosticAsync(string directory)
    {
        QuestionClear_Click(this, new RoutedEventArgs());
        SetQuestionEditing(false);
        transcript.BeginSession();
        var session = Guid.NewGuid();
        questionTranscript.BeginSession(session);
        ArchiveTranscriptPartials(true);
        var snapshotsBefore = questionSnapshotCount;
        var notesBefore = NoteEditor.Text;
        var firstText = "Explain [Computer] labels without parsing them.\nKeep this wrapped continuation in the same question.";
        ReceiveTranscript(new("final", firstText, 1, 0, 2, AudioSource.Microphone, session));
        ReceiveTranscript(new("final", "Compare the two approaches.", 2, 2, 3, AudioSource.Computer, session));
        ReceiveTranscript(new("final", "Compare the two approaches.", 3, 3, 4, AudioSource.Microphone, session));
        var first = transcriptRows.Single(row => row.Segment?.Key.CaptureSessionId == session && row.Segment.ProtocolSegmentId == 1);
        var second = transcriptRows.Single(row => row.Segment?.Key.CaptureSessionId == session && row.Segment.ProtocolSegmentId == 2);
        var duplicateText = transcriptRows.Single(row => row.Segment?.Key.CaptureSessionId == session && row.Segment.ProtocolSegmentId == 3);
        SetQuestionExpanded(false);
        using var captureGuard = new CancellationTokenSource();
        audioStartCancellation = captureGuard;
        audioBusy = true;
        var exportBeforeClicks = transcript.ExportText();
        ClickTranscriptRow(TranscriptText, second);
        ClickTranscriptRow(TranscriptText, first);
        var mixedOrder = questionDraft.Text == firstText + Environment.NewLine + second.Segment!.Text &&
            questionDraft.SelectedSegments.Select(item => item.Key.Source).SequenceEqual(new[] { AudioSource.Microphone, AudioSource.Computer }) &&
            TranscriptText.SelectedItems.Count == 2 && QuestionPanel.IsVisible && !QuestionEditor.IsReadOnly;
        var noCaptureOrSend = audioBusy && !captureGuard.IsCancellationRequested && NoteEditor.IsReadOnly &&
            NoteEditor.Text == notesBefore && questionSnapshotCount == snapshotsBefore && !transcriber.IsRunning &&
            transcript.ExportText() == exportBeforeClicks;
        ClickTranscriptRow(TranscriptText, duplicateText);
        var distinctIdentity = questionDraft.SelectedSegments.Count == 3;
        ClickTranscriptRow(TranscriptText, second);
        var deselection = questionDraft.SelectedSegments.Count == 2 &&
            questionDraft.SelectedSegments[1].Key == duplicateText.Segment!.Key && !TranscriptText.SelectedItems.Contains(second);
        ClickTranscriptRow(TranscriptText, duplicateText);
        var firstOnly = questionDraft.Text == firstText && TranscriptText.SelectedItems.Count == 1;
        ClickTranscriptRow(TranscriptText, second);
        audioStartCancellation = null;
        audioBusy = false;
        UpdateAudioControls();
        AudioStatus.Text = "Stopped. Synthetic multi-line selection; no audio inputs opened.";
        await RenderDiagnosticAsync(System.IO.Path.Combine(directory, "transcript-multiselect.png"));

        QuestionAsk_Click(this, new RoutedEventArgs());
        var frozen = lastQuestionSnapshot ?? throw new InvalidOperationException("Missing multi-line snapshot.");
        ClickTranscriptRow(TranscriptText, second);
        var frozenStable = frozen.ExactText == firstText + Environment.NewLine + second.Segment!.Text &&
            QuestionSnapshotText.Text == frozen.ExactText && questionSnapshotCount == snapshotsBefore + 1;
        QuestionEditor.Text = "My edited question must be retained.";
        ClickTranscriptRow(TranscriptText, second);
        var editedProtected = questionDraft.Text == "My edited question must be retained." &&
            TranscriptText.SelectedItems.Count == 1 && QuestionStatus.Text.Contains("Edited question retained", StringComparison.Ordinal);
        QuestionClear_Click(this, new RoutedEventArgs());
        var clearSelection = TranscriptText.SelectedItems.Count == 0 && PartialText.SelectedItems.Count == 0;

        ReceiveTranscript(new("partial", "My provisional question", Source: AudioSource.Microphone, CaptureSessionId: session));
        ClickTranscriptRow(PartialText, partialTranscriptRows.Single());
        ReceiveTranscript(new("partial", "My revised question", Source: AudioSource.Microphone, CaptureSessionId: session));
        var partialRefresh = PartialText.SelectedItems.Count == 1 && questionDraft.Text == "My revised question";
        ReceiveTranscript(new("final", "My final question.", 4, 4, 5, AudioSource.Microphone, session));
        var partialToFinal = PartialText.SelectedItems.Count == 0 && TranscriptText.SelectedItems.Count == 1 &&
            questionDraft.Text == "My final question." && !questionDraft.IsProvisional;
        var finalRow = (TranscriptRow)TranscriptText.SelectedItem;
        // Evict the selected final from the smaller chooser, while the main transcript retains it.
        for (var id = 5; id <= 264; id++)
            ReceiveTranscript(new("final", $"Synthetic context {id}.", id, id, id + 1, AudioSource.Computer, session));
        var evictionSafe = !questionTranscript.Segments.Any(item => item.Key == finalRow.Segment!.Key) &&
            questionDraft.Text == "My final question." && TranscriptText.SelectedItems.Contains(finalRow);
        QuestionClear_Click(this, new RoutedEventArgs());
        ClickTranscriptRow(TranscriptText, first);
        var oldVisibleSelectable = questionDraft.Text == firstText;
        var scroll = FindTranscriptScrollViewer(TranscriptText) ?? throw new InvalidOperationException("Missing transcript scrollbar.");
        scroll.ScrollToTop();
        UpdateLayout();
        ReceiveTranscript(new("partial", "New speech while reading history", Source: AudioSource.Microphone, CaptureSessionId: session));
        ReceiveTranscript(new("final", "Do not jump past the selected history.", 265, 265, 266, AudioSource.Computer, session));
        UpdateLayout();
        var readingPosition = scroll.VerticalOffset < 1;
        QuestionClear_Click(this, new RoutedEventArgs());
        ReceiveTranscript(new("final", new string('x', QuestionDraft.MaximumCharacters + 1), 266, 266, 267, AudioSource.Computer, session));
        var oversized = transcriptRows.Last();
        ClickTranscriptRow(TranscriptText, first);
        ClickTranscriptRow(TranscriptText, oversized);
        var oversizedRejected = questionDraft.Text == firstText && TranscriptText.SelectedItems.Count == 1;
        fileBusy = true;
        ClickTranscriptRow(TranscriptText, duplicateText);
        var busyRejected = questionDraft.Text == firstText && TranscriptText.SelectedItems.Count == 1;
        fileBusy = false;
        var previousProtection = protectionReady;
        protectionReady = false;
        ClickTranscriptRow(TranscriptText, duplicateText);
        var protectionRejected = questionDraft.Text == firstText && TranscriptText.SelectedItems.Count == 1;
        protectionReady = previousProtection;

        transcript.BeginSession();
        var nextSession = Guid.NewGuid();
        questionTranscript.BeginSession(nextSession);
        ArchiveTranscriptPartials(true);
        var separator = transcriptRows.Last();
        ClickTranscriptRow(TranscriptText, separator);
        var separatorIgnored = questionDraft.Text == firstText;
        ReceiveTranscript(new("final", "A different capture session.", 1, 0, 1, AudioSource.Computer, nextSession));
        ClickTranscriptRow(TranscriptText, transcriptRows.Last());
        var sessionRejected = questionDraft.Text == firstText && TranscriptText.SelectedItems.Count == 1;
        QuestionClear_Click(this, new RoutedEventArgs());
        var archivedPartial = transcriptRows.Last(row => row.Segment is { IsFinal: false });
        ClickTranscriptRow(TranscriptText, archivedPartial);
        var unfinishedSelectable = questionDraft.Text == "New speech while reading history" &&
            questionDraft.IsProvisional && !questionDraft.SelectedSegments[0].IsActive;
        exportedTranscript = transcript.ExportText();
        ClearTranscript_Click(this, new RoutedEventArgs());
        var clearTranscriptKeepsDraft = transcriptRows.Count == 0 && PartialText.Items.Count == 0 &&
            questionDraft.Text == "New speech while reading history";
        QuestionClear_Click(this, new RoutedEventArgs());
        SetQuestionEditing(false);
        transcript.BeginSession();
        questionTranscript.BeginSession(Guid.NewGuid());
        ReceiveTranscript(new("final", "Click a meeting line to add it to your question.", 1, 0, 2, AudioSource.Computer));
        ReceiveTranscript(new("final", "Click another line to combine both. Edit, then Ask.", 2, 2, 4, AudioSource.Microphone));

        var checks = new { mixedOrder, noCaptureOrSend, distinctIdentity, deselection, firstOnly,
            frozenStable, editedProtected, clearSelection, partialRefresh, partialToFinal, evictionSafe,
            oldVisibleSelectable, readingPosition, oversizedRejected, busyRejected, protectionRejected,
            separatorIgnored, sessionRejected, unfinishedSelectable, clearTranscriptKeepsDraft };
        var passed = mixedOrder && noCaptureOrSend && distinctIdentity && deselection && firstOnly &&
            frozenStable && editedProtected && clearSelection && partialRefresh && partialToFinal && evictionSafe &&
            oldVisibleSelectable && readingPosition && oversizedRejected && busyRejected && protectionRejected &&
            separatorIgnored && sessionRejected && unfinishedSelectable && clearTranscriptKeepsDraft;
        await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(directory, "transcript-selection.json"),
            System.Text.Json.JsonSerializer.Serialize(new { passed, checks, modelRequests = 0, recordingStarted = false },
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        if (!passed)
            throw new InvalidOperationException("Transcript selection assertions failed: " + System.Text.Json.JsonSerializer.Serialize(checks));
        return true;
    }

    private void ClickTranscriptRow(ListBox list, TranscriptRow row)
    {
        list.ScrollIntoView(row);
        UpdateLayout();
        var container = list.ItemContainerGenerator.ContainerFromItem(row) as ListBoxItem
            ?? throw new InvalidOperationException("Transcript row was not realized.");
        container.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        {
            RoutedEvent = Mouse.PreviewMouseDownEvent
        });
    }
}
