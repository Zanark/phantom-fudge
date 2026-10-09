using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using PhantomFudge.Core;

namespace PhantomFudge.App;

public partial class MainWindow
{
    private TranscriptSegmentKey? keyboardTranscriptKey;
    private AudioSource? keyboardTranscriptSource;
    private string keyboardTranscriptSearch = "";
    private bool keyboardFirstG;

    private void BeginKeyboardSelection()
    {
        if (!CanKeyboardSelect()) return;
        keyboardTranscriptSource = null;
        keyboardTranscriptSearch = "";
        keyboardFirstG = false;
        SetQuestionExpanded(true);
        EnsureQuestionReadingVisible();
        SetKeyboardMode(KeyboardMode.Select);
        var rows = KeyboardTranscriptRows();
        keyboardTranscriptKey = rows.LastOrDefault()?.Segment?.Key;
        RefreshKeyboardTranscriptView();
        RevealKeyboardTranscriptCursor();
    }

    private bool CanKeyboardSelect()
    {
        if (protectionReady && !fileBusy && !questionDraft.UserEdited && !ClarificationLocksQuestion)
            return true;
        KeyboardHints.Text = ClarificationLocksQuestion ? "Finish or clear clarification before selecting."
            : questionDraft.UserEdited ? "Edited draft retained. Use :clear question before selecting new lines."
            : "Restore protection and finish the file operation before selecting.";
        return false;
    }

    private bool MatchesKeyboardTranscript(TranscriptRow row) => row.Segment is { } segment &&
        (keyboardTranscriptSource is null || segment.Key.Source == keyboardTranscriptSource) &&
        segment.Text.Contains(keyboardTranscriptSearch, StringComparison.OrdinalIgnoreCase);

    private TranscriptRow[] KeyboardTranscriptRows() =>
        transcriptRows.Concat(partialTranscriptRows).Where(MatchesKeyboardTranscript).ToArray();

    private string KeyboardSelectionHint() =>
        $"{(keyboardTranscriptSource?.DisplayName() ?? "All")} | {questionDraft.SelectedSegments.Count} selected | " +
        "j/k move; Space toggle; Enter review; 1/2/3 source; / find; G latest; gg first";

    private void RefreshKeyboardTranscriptView()
    {
        if (!KeyboardSelecting || !questionViewReady) return;
        renderingTranscriptSelection = true;
        try
        {
            foreach (var list in new[] { TranscriptText, PartialText })
                if (list.ItemsSource is not null)
                    CollectionViewSource.GetDefaultView(list.ItemsSource).Filter =
                        value => value is TranscriptRow row && MatchesKeyboardTranscript(row);
        }
        finally { renderingTranscriptSelection = false; }
        SyncTranscriptSelection();
        UpdateKeyboardTranscriptCursor();
    }

    private void ResetKeyboardSelectionView()
    {
        renderingTranscriptSelection = true;
        try
        {
            foreach (var list in new[] { TranscriptText, PartialText })
                if (list.ItemsSource is not null)
                    CollectionViewSource.GetDefaultView(list.ItemsSource).Filter = null;
        }
        finally { renderingTranscriptSelection = false; }
        SyncTranscriptSelection();
        UpdateKeyboardTranscriptCursor();
    }

    private void UpdateKeyboardTranscriptCursor()
    {
        foreach (var list in new[] { TranscriptText, PartialText })
            foreach (TranscriptRow row in list.Items)
                if (list.ItemContainerGenerator.ContainerFromItem(row) is ListBoxItem item)
                    item.Tag = KeyboardSelecting && row.Segment?.Key == keyboardTranscriptKey ? "KeyboardCursor" : null;
    }

    private void RevealKeyboardTranscriptCursor()
    {
        foreach (var list in new[] { TranscriptText, PartialText })
            if (list.Items.Cast<TranscriptRow>().FirstOrDefault(row => row.Segment?.Key == keyboardTranscriptKey) is { } row)
                list.ScrollIntoView(row);
        UpdateKeyboardTranscriptCursor();
        KeyboardHints.Text = KeyboardSelectionHint();
    }

    private void FindKeyboardTranscript(string text)
    {
        keyboardTranscriptSearch = text.Trim();
        SetKeyboardMode(KeyboardMode.Select);
        RefreshKeyboardTranscriptView();
        var rows = KeyboardTranscriptRows();
        if (!rows.Any(row => row.Segment!.Key == keyboardTranscriptKey))
            keyboardTranscriptKey = rows.FirstOrDefault()?.Segment?.Key;
        RevealKeyboardTranscriptCursor();
        if (rows.Length == 0) KeyboardHints.Text = "No matching lines. / then empty Enter clears search; Esc leaves.";
    }

    private bool HandleKeyboardSelectionKey(Key key, ModifierKeys modifiers, bool repeated)
    {
        var rows = KeyboardTranscriptRows();
        var index = Array.FindIndex(rows, row => row.Segment!.Key == keyboardTranscriptKey);
        var first = keyboardFirstG && key == Key.G && modifiers == ModifierKeys.None;
        keyboardFirstG = !repeated && key == Key.G && modifiers == ModifierKeys.None && !first;
        if (key == Key.OemQuestion && modifiers == ModifierKeys.None || key == Key.F3)
        {
            if (!repeated) OpenKeyboardPrompt(true);
            return true;
        }
        if (key == Key.Enter)
        {
            if (!repeated) SetKeyboardMode(KeyboardMode.Edit);
            return true;
        }
        if (key is Key.D1 or Key.D2 or Key.D3)
        {
            keyboardTranscriptSource = key == Key.D1 ? AudioSource.Microphone : key == Key.D2 ? AudioSource.Computer : null;
            FindKeyboardTranscript(keyboardTranscriptSearch);
            return true;
        }
        if (rows.Length == 0) { KeyboardHints.Text = "No matching transcript lines yet. / clears search; 3 shows both sources."; return true; }
        if (key == Key.Space)
        {
            if (repeated || index < 0 || !CanKeyboardSelect()) return true;
            var row = rows[index];
            var selected = questionDraft.SelectedSegments.Any(segment => segment.Key == row.Segment!.Key);
            ApplyTranscriptSelection(selected ? [row] : [], selected ? [] : [row]);
            RevealKeyboardTranscriptCursor();
            return true;
        }
        index = key switch
        {
            Key.J or Key.Down => Math.Min(rows.Length - 1, index + 1),
            Key.K or Key.Up => Math.Max(0, index - 1),
            Key.PageDown => Math.Clamp(index + 5, 0, rows.Length - 1),
            Key.PageUp => Math.Clamp(index - 5, 0, rows.Length - 1),
            Key.Home => 0,
            Key.End => rows.Length - 1,
            Key.G when modifiers == ModifierKeys.Shift => rows.Length - 1,
            Key.G when first => 0,
            _ => index
        };
        if (index >= 0)
        {
            keyboardTranscriptKey = rows[index].Segment!.Key;
            RevealKeyboardTranscriptCursor();
        }
        return true;
    }
}
