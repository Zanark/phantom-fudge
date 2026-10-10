using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using PhantomFudge.Core;

namespace PhantomFudge.App;

public partial class MainWindow
{
    private enum KeyboardMode { Inactive, Control, Select, Edit, Read, Command, Search, Devices, Help }
    private KeyboardMode keyboardMode;
    private KeyboardMode keyboardPromptReturnMode = KeyboardMode.Control;
    private KeyboardShortcutSettings keyboardShortcut = new();
    private string? keyboardShortcutNotice;
    private bool switchingKeyboardMode;
    private bool suppressKeyboardActivation;
    private bool keyboardActionBusy;
    private nint keyboardReturnWindow;
    private uint keyboardReturnProcess;
    private TextBox? keyboardEditorTarget;
    private bool KeyboardControlActive => keyboardMode != KeyboardMode.Inactive;
    private bool KeyboardSelecting => keyboardMode is KeyboardMode.Select or KeyboardMode.Search;

    private void InitializeKeyboardControls()
    {
        LoadKeyboardShortcut();
        foreach (var list in new[] { TranscriptText, PartialText })
            list.ItemContainerGenerator.StatusChanged += (_, _) => UpdateKeyboardTranscriptCursor();
        UpdateKeyboardBar();
    }

    private void LoadKeyboardShortcut(Func<KeyboardShortcutSettings>? load = null)
    {
        keyboardShortcutNotice = null;
        try { keyboardShortcut = load is null ? KeyboardShortcutConfiguration.Load() : load(); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or JsonException)
        {
            keyboardShortcut = new(false);
            keyboardShortcutNotice = "Keyboard shortcut setup is invalid. The Keys button still works.";
            ReportStatus(keyboardShortcutNotice);
        }
        AutomationProperties.SetName(KeyboardToggleButton, keyboardShortcut.Enabled
            ? $"Keyboard control: Ctrl+Alt+{keyboardShortcut.ActivationKey.ToUpperInvariant()}"
            : "Enter keyboard control; global shortcut is disabled");
    }

    private void RegisterKeyboardControlHotkey()
    {
        if (diagnosticRendering || !keyboardShortcut.Enabled)
            return;
        try { hotkeys!.Register(Native.WindowHotkeys.KeyboardControl, keyboardShortcut.VirtualKey); }
        catch (Win32Exception)
        {
            keyboardShortcutNotice = $"Ctrl+Alt+{keyboardShortcut.ActivationKey.ToUpperInvariant()} is unavailable. Use Keys or change keyboard.json.";
            ReportStatus(keyboardShortcutNotice);
        }
    }

    private void KeyboardToggle_Click(object sender, RoutedEventArgs args) => ToggleKeyboardControl();

    private void ToggleKeyboardControl()
    {
        if (KeyboardControlActive) { LeaveKeyboardControl(); return; }
        if (fileBusy || pending != PendingAction.None)
        {
            ReportStatus("Finish the file or confirmation operation before keyboard control.");
            return;
        }
        var previous = GetForegroundWindow();
        if (!IsVisible || !protectionReady) TryReveal();
        if (!protectionReady) return;
        SetCover(false);
        keyboardReturnWindow = previous == new WindowInteropHelper(this).Handle ? 0 : previous;
        keyboardReturnProcess = 0;
        if (keyboardReturnWindow != 0)
            KeyboardGetWindowThreadProcessId(keyboardReturnWindow, out keyboardReturnProcess);
        SetKeyboardMode(KeyboardMode.Control);
        if (keyboardShortcutNotice is not null) KeyboardHints.Text = keyboardShortcutNotice;
    }

    private void SetKeyboardMode(KeyboardMode mode)
    {
        if (mode == KeyboardMode.Edit && (fileBusy || !protectionReady || ClarificationLocksQuestion && !AwaitingClarificationReply))
        {
            KeyboardHints.Text = "Wait for the interpretation or finish the file operation before editing.";
            return;
        }
        var wasPassive = PassiveWindow;
        var selectingBefore = KeyboardSelecting;
        switchingKeyboardMode = true;
        try
        {
            if (wasPassive != (presenting && mode == KeyboardMode.Inactive))
                Hide();
            keyboardMode = mode;
            questionEditing = mode == KeyboardMode.Edit;
            if (selectingBefore && !KeyboardSelecting)
                ResetKeyboardSelectionView();
            else if (KeyboardSelecting)
                RefreshKeyboardTranscriptView();
            if (mode != KeyboardMode.Devices) ClearKeyboardDeviceCursor();
            UpdateInteractionControls();
            UpdateKeyboardBar();
            if (!IsVisible && protectionReady) TryReveal();
        }
        finally { switchingKeyboardMode = false; }
        UpdateWorkspaceLayout();
        RefreshKeyboardEditorTarget();
        if (mode == KeyboardMode.Devices) BeginKeyboardDevices();
        if (!diagnosticRendering && IsVisible && protectionReady)
        {
            if (mode is KeyboardMode.Command or KeyboardMode.Search) KeyboardPrompt.Focus();
            else if (mode == KeyboardMode.Edit)
            {
                keyboardEditorTarget?.Focus();
            }
            else if (mode != KeyboardMode.Inactive) KeyboardFocusSink.Focus();
        }
    }

    private void RefreshKeyboardEditorTarget()
    {
        if (keyboardMode != KeyboardMode.Edit) { keyboardEditorTarget = null; return; }
        KeyboardModeLabel.Text = AwaitingClarificationReply ? "EDIT REPLY" : "EDIT QUESTION";
        var candidate = AwaitingClarificationReply ? ClarificationReplyEditor : QuestionEditor;
        var target = candidate.IsVisible && candidate.IsEnabled && candidate.Focusable && !candidate.IsReadOnly ? candidate : null;
        var changed = !ReferenceEquals(target, keyboardEditorTarget);
        keyboardEditorTarget = target;
        // An async interpretation replaces the question editor. Recover only our own active Edit focus.
        if (changed && target is not null && !diagnosticRendering && !switchingKeyboardMode &&
            !coverActive && protectionReady && IsVisible && IsActive)
            target.Focus();
    }

    private void LeaveKeyboardControl(bool restoreForeground = true)
    {
        if (!KeyboardControlActive) return;
        var previous = keyboardReturnWindow;
        var process = keyboardReturnProcess;
        suppressKeyboardActivation = true;
        try { SetKeyboardMode(KeyboardMode.Inactive); }
        finally { suppressKeyboardActivation = false; }
        keyboardReturnWindow = 0;
        keyboardReturnProcess = 0;
        Keyboard.ClearFocus();
        if (!restoreForeground || diagnosticRendering || previous == 0)
            return;
        KeyboardGetWindowThreadProcessId(previous, out var currentProcess);
        if (process != currentProcess || !KeyboardIsWindow(previous) || !KeyboardSetForegroundWindow(previous))
            ReportStatus("Keyboard control ended. Switch back to your presentation if Windows kept focus here.");
    }

    private void AbandonKeyboardControl()
    {
        if (!KeyboardControlActive) return;
        keyboardMode = KeyboardMode.Inactive;
        questionEditing = false;
        keyboardReturnWindow = 0;
        keyboardReturnProcess = 0;
        ResetKeyboardSelectionView();
        ClearKeyboardDeviceCursor();
        UpdateKeyboardBar();
    }

    private void KeyboardWindow_Deactivated(object? sender, EventArgs args)
    {
        if (KeyboardControlActive && !switchingKeyboardMode && !switchingCover && !diagnosticRendering)
            LeaveKeyboardControl(restoreForeground: false);
    }

    private void UpdateKeyboardBar()
    {
        if (KeyboardBar is null) return;
        KeyboardBar.Visibility = KeyboardControlActive ? Visibility.Visible : Visibility.Collapsed;
        KeyboardPrompt.Visibility = keyboardMode is KeyboardMode.Command or KeyboardMode.Search ? Visibility.Visible : Visibility.Collapsed;
        KeyboardModeLabel.Text = keyboardMode == KeyboardMode.Edit
            ? AwaitingClarificationReply ? "EDIT REPLY" : "EDIT QUESTION" : keyboardMode.ToString().ToUpperInvariant();
        KeyboardHints.Text = keyboardMode switch
        {
            KeyboardMode.Control => "v Select | i Edit | a Read | : / F2 Commands | ? / F1 Help | m Mask | Esc leave",
            KeyboardMode.Select => KeyboardSelectionHint(),
            KeyboardMode.Edit => "Type normally. Enter = newline | Ctrl+Enter = submit | Esc = Control",
            KeyboardMode.Read => "j/k or Page keys read | +/- answer size | : Commands | Esc = Control",
            KeyboardMode.Command => "Built-in commands only. Enter executes | Tab completes | Esc cancels",
            KeyboardMode.Search => "Local transcript search. Enter finds | Esc returns to selection",
            KeyboardMode.Devices => "Tab changes source | arrows move | Space selects | [ / ] pages | Esc = Control",
            KeyboardMode.Help => "v Select: j/k, Space toggle, Enter review, 1 Mic / 2 Computer / 3 all, / or F3 search. i Edit: type normally, Ctrl+Enter sends. a Read: j/k, +/- size. m Mask. F2 commands: start; stop; source both; devices; clarify on; context on/off; confirm; font 14; copy answer; clear question; cancel; theme dark; opacity 55. Esc returns.",
            _ => ""
        };
    }

    private void OpenKeyboardPrompt(bool search)
    {
        keyboardPromptReturnMode = keyboardMode;
        SetKeyboardMode(search ? KeyboardMode.Search : KeyboardMode.Command);
        KeyboardPrompt.Clear();
        if (!diagnosticRendering) KeyboardPrompt.Focus();
    }

    private void KeyboardPrompt_TextChanged(object sender, TextChangedEventArgs args)
    {
        if (keyboardMode != KeyboardMode.Command) return;
        try
        {
            var examples = KeyboardCommands.Suggest(KeyboardPrompt.Text);
            KeyboardHints.Text = examples.Count == 0 ? "No matching built-in command. Enter validates; Esc cancels."
                : string.Join(" | ", examples);
        }
        catch (ArgumentException error) { KeyboardHints.Text = error.Message; }
    }

    private bool HandleKeyboardKey(Key key, ModifierKeys modifiers, bool repeated)
    {
        if (!KeyboardControlActive) return false;
        if ((modifiers & ModifierKeys.Alt) != 0) return false;
        if (key == Key.Escape)
        {
            if (!repeated)
            {
                if (keyboardMode == KeyboardMode.Control) LeaveKeyboardControl();
                else if (keyboardMode == KeyboardMode.Search) SetKeyboardMode(KeyboardMode.Select);
                else if (keyboardMode == KeyboardMode.Command) SetKeyboardMode(keyboardPromptReturnMode);
                else if (keyboardMode == KeyboardMode.Devices) { AudioPanel.Visibility = Visibility.Collapsed; SetKeyboardMode(KeyboardMode.Control); }
                else SetKeyboardMode(KeyboardMode.Control);
            }
            return true;
        }
        if (keyboardMode is KeyboardMode.Command or KeyboardMode.Search)
        {
            if (key == Key.Enter)
            {
                if (!repeated)
                {
                    if (keyboardMode == KeyboardMode.Search) FindKeyboardTranscript(KeyboardPrompt.Text);
                    else SubmitKeyboardCommand();
                }
                return true;
            }
            if (key == Key.Tab && keyboardMode == KeyboardMode.Command)
            {
                try
                {
                    var first = KeyboardCommands.Suggest(KeyboardPrompt.Text, 1).FirstOrDefault();
                    if (first is not null) { KeyboardPrompt.Text = first; KeyboardPrompt.CaretIndex = first.Length; }
                }
                catch (ArgumentException error) { KeyboardHints.Text = error.Message; }
                return true;
            }
            return false;
        }
        if (key == Key.Enter && modifiers == ModifierKeys.Control)
        {
            if (!repeated)
            {
                if (KeyboardSelecting) KeyboardHints.Text = "Press Enter to review selected lines before submitting.";
                else if (keyboardMode is KeyboardMode.Control or KeyboardMode.Edit) SubmitKeyboardQuestion();
            }
            return true;
        }
        if (keyboardMode == KeyboardMode.Edit) return false;
        if ((modifiers & (ModifierKeys.Control | ModifierKeys.Windows)) != 0) return true;
        if (key == Key.OemSemicolon && modifiers == ModifierKeys.Shift) { if (!repeated) OpenKeyboardPrompt(false); return true; }
        if (key == Key.F1 || key == Key.OemQuestion && modifiers == ModifierKeys.Shift)
        { if (!repeated) SetKeyboardMode(KeyboardMode.Help); return true; }
        if (key == Key.F2) { if (!repeated) OpenKeyboardPrompt(false); return true; }
        if (keyboardMode == KeyboardMode.Select) return HandleKeyboardSelectionKey(key, modifiers, repeated);
        if (keyboardMode == KeyboardMode.Devices) return HandleKeyboardDeviceKey(key, repeated);
        if (keyboardMode == KeyboardMode.Read)
        {
            switch (key)
            {
                case Key.J: case Key.Down: AnswerText.LineDown(); break;
                case Key.K: case Key.Up: AnswerText.LineUp(); break;
                case Key.PageDown: AnswerText.PageDown(); break;
                case Key.PageUp: AnswerText.PageUp(); break;
                case Key.Home: AnswerText.ScrollToHome(); break;
                case Key.End: AnswerText.ScrollToEnd(); break;
                case Key.Add: case Key.OemPlus: AdjustAnswerFont(1); break;
                case Key.Subtract: case Key.OemMinus: AdjustAnswerFont(-1); break;
            }
            return true;
        }
        if (repeated) return true;
        switch (key)
        {
            case Key.V: BeginKeyboardSelection(); break;
            case Key.I: SetQuestionExpanded(true); EnsureQuestionReadingVisible(); SetKeyboardMode(KeyboardMode.Edit); break;
            case Key.A: SetQuestionExpanded(true); EnsureQuestionReadingVisible(); SetKeyboardMode(KeyboardMode.Read); break;
            case Key.M: MaskFromKeyboard(); break;
            case Key.OemQuestion when modifiers == ModifierKeys.Shift: SetKeyboardMode(KeyboardMode.Help); break;
        }
        return true;
    }

    private void SubmitKeyboardQuestion()
    {
        if (AwaitingClarificationReply) SubmitClarificationReply(ClarificationReplyEditor.Text);
        else QuestionAsk_Click(this, new RoutedEventArgs());
        KeyboardHints.Text = QuestionStatus.Text;
    }

    private async void SubmitKeyboardCommand()
    {
        KeyboardCommand command;
        try
        {
            command = KeyboardCommands.Parse(KeyboardPrompt.Text);
        }
        catch (ArgumentException error) { KeyboardHints.Text = error.Message; return; }
        SetKeyboardMode(KeyboardMode.Control);
        await ExecuteKeyboardCommandAsync(command);
    }

    private async Task ExecuteKeyboardCommandAsync(KeyboardCommand command)
    {
        if (keyboardActionBusy || fileBusy || pending != PendingAction.None || !protectionReady)
        {
            KeyboardHints.Text = "Finish the current operation or restore protection before this command.";
            return;
        }
        keyboardActionBusy = true;
        try
        {
            switch (command.Kind)
            {
                case KeyboardCommandKind.Start: Start_Click(this, new RoutedEventArgs()); KeyboardHints.Text = "Start requested; see audio status."; break;
                case KeyboardCommandKind.Stop: await StopTranscriptionAsync(); KeyboardHints.Text = "Stop requested; see audio status."; break;
                case KeyboardCommandKind.Source:
                    SetCaptureMode(command.Argument switch { "mic" => AudioCaptureMode.Microphone, "computer" => AudioCaptureMode.Computer, _ => AudioCaptureMode.Both });
                    KeyboardHints.Text = AudioStatus.Text; break;
                case KeyboardCommandKind.Devices:
                    if (audioBusy || audioStopping || transcriber.IsRunning || AnswerBusy)
                    { KeyboardHints.Text = "Stop transcription and finish/cancel the answer before device setup."; break; }
                    AudioPanel.Visibility = Visibility.Visible; SetKeyboardMode(KeyboardMode.Devices); UpdateDeviceCards(); break;
                case KeyboardCommandKind.Clarify:
                    if (AnswerBusy) { KeyboardHints.Text = "Wait or cancel the answer before changing clarification mode."; break; }
                    ClarifyFirstToggle.IsChecked = command.Argument == "on"; ClarifyFirst_Click(this, new RoutedEventArgs());
                    KeyboardHints.Text = QuestionStatus.Text; break;
                case KeyboardCommandKind.Confirm: ClarificationConfirm_Click(this, new RoutedEventArgs()); KeyboardHints.Text = QuestionStatus.Text; break;
                case KeyboardCommandKind.Context:
                    SetUsePreviousAnswer(command.Argument == "on"); KeyboardHints.Text = QuestionStatus.Text; break;
                case KeyboardCommandKind.Font: SetAnswerFontSize(command.Value!.Value); KeyboardHints.Text = $"Answer size {command.Value}."; break;
                case KeyboardCommandKind.Opacity: BackgroundOpacity.Value = command.Value!.Value; KeyboardHints.Text = $"Background {command.Value}%."; break;
                case KeyboardCommandKind.Theme:
                    ApplyTheme(command.Argument switch { "dark" => UiTheme.DeepSeaFoam, "light" => UiTheme.HarborDaylight, _ => UiTheme.Plain });
                    KeyboardHints.Text = $"Theme: {ThemePalette.For(currentTheme).Name}."; break;
                case KeyboardCommandKind.CopyQuestion: QuestionCopy_Click(this, new RoutedEventArgs()); KeyboardHints.Text = QuestionStatus.Text; break;
                case KeyboardCommandKind.CopyAnswer: AnswerCopy_Click(this, new RoutedEventArgs()); KeyboardHints.Text = AnswerStatus.Text; break;
                case KeyboardCommandKind.ClearQuestion: QuestionClear_Click(this, new RoutedEventArgs()); KeyboardHints.Text = QuestionStatus.Text; break;
                case KeyboardCommandKind.Cancel: AnswerCancel_Click(this, new RoutedEventArgs()); KeyboardHints.Text = "Answer/preparation cancellation requested."; break;
                case KeyboardCommandKind.Ask: SubmitKeyboardQuestion(); break;
                case KeyboardCommandKind.Mask: MaskFromKeyboard(); break;
                case KeyboardCommandKind.Help: SetKeyboardMode(KeyboardMode.Help); break;
                default: throw new ArgumentOutOfRangeException(nameof(command));
            }
        }
        finally { keyboardActionBusy = false; }
    }

    private void MaskFromKeyboard()
    {
        LeaveKeyboardControl();
        SetCover(true);
    }

    [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool KeyboardSetForegroundWindow(nint window);
    [DllImport("user32.dll", EntryPoint = "IsWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool KeyboardIsWindow(nint window);
    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
    private static extern uint KeyboardGetWindowThreadProcessId(nint window, out uint processId);
}
