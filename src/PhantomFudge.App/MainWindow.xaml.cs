using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PhantomFudge.App.Audio;
using PhantomFudge.App.Native;
using PhantomFudge.Core;

namespace PhantomFudge.App;

public partial class MainWindow : Window
{
    private enum PendingAction { None, New, Open, Exit }
    private readonly LoopbackTranscriber transcriber = new();
    private readonly TranscriptBuffer transcript = new();
    private WindowHotkeys? hotkeys;
    private HwndSource? source;
    private string savedText = "";
    private string exportedTranscript = "";
    private string? documentPath;
    private string? fingerprint;
    private bool withBom;
    private bool presenting = true;
    private UiTheme currentTheme = UiTheme.DeepSeaFoam;
    private bool protectionReady;
    private bool fileBusy;
    private bool audioBusy;
    private bool allowClose;
    private bool closeRequestQueued;
    private bool subscribed;
    private bool showing;
    private bool diagnosticRendering;
    private CancellationTokenSource? audioStartCancellation;
    private ProtectionResult? lastProtection;
    private PendingAction pending;

    public MainWindow()
    {
        Resources["ControlsFocusable"] = false;
        ThemeBrushes.Apply(Resources, currentTheme);
        InitializeComponent();
        ApplyTheme(currentTheme);
        ServiceToken.Password = Environment.GetEnvironmentVariable("PHANTOM_TRANSCRIPTION_TOKEN") ?? "";
        transcriber.TranscriptReceived += value => Dispatch(() => ReceiveTranscript(value));
        transcriber.StatusChanged += value => Dispatch(() =>
        {
            AudioStatus.Text = value;
            UpdateAudioControls();
        });
        transcriber.Faulted += value => Dispatch(() =>
        {
            AudioStatus.Text = value;
            AudioLevel.Value = 0;
            UpdateAudioControls();
        });
        transcriber.LevelChanged += value => Dispatch(() => AudioLevel.Value = Math.Clamp(value, 0, 1));
        RefreshDevices();
        UpdateInteractionControls();
    }

    public void ShowProtected()
    {
        showing = true;
        try
        {
            protectionReady = false;
            lastProtection = WindowProtection.Configure(this, presenting);
            protectionReady = true;
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            if (source is null)
            {
                var handle = new WindowInteropHelper(this).Handle;
                source = HwndSource.FromHwnd(handle);
                source?.AddHook(WindowMessage);
                hotkeys = new WindowHotkeys(handle);
                RegisterHotkey(WindowHotkeys.ToggleVisibility, 0x20);
                RegisterHotkey(WindowHotkeys.ToggleEditing, 0x45);
                SystemEvents.SessionSwitch += SessionSwitch;
                SystemEvents.PowerModeChanged += PowerModeChanged;
                subscribed = true;
            }
            ShowActivated = !presenting && !diagnosticRendering;
            UpdateAudioControls();
            Show();
            ReportStatus("Capture exclusion requested. UNQUALIFIED PREVIEW: compatibility varies by capture method.");
        }
        finally { showing = false; }
    }

    private void RegisterHotkey(int id, uint key)
    {
        try
        {
            hotkeys!.Register(id, key);
        }
        catch (Win32Exception)
        {
            AudioStatus.Text = "A shortcut is already in use. Relaunch this application to recover its window.";
        }
    }

    public void RevealAndReset()
    {
        var area = SystemParameters.WorkArea;
        Width = Math.Min(Width, Math.Max(MinWidth, area.Width - 24));
        Height = Math.Min(Height, Math.Max(MinHeight, area.Height - 24));
        Left = area.Left + Math.Max(0, (area.Width - Width) / 2);
        Top = area.Top + Math.Max(0, (area.Height - Height) / 2);
        TryReveal();
    }

    private void TryReveal()
    {
        try
        {
            ShowProtected();
            if (!presenting && !diagnosticRendering)
                Activate();
        }
        catch (Exception error) when (IsProtectionFailure(error))
        {
            _ = InvalidateAsync("Capture setup failed. Notes remain hidden; close and restart to retry.");
            LocalFailureNotice.Record(WindowFailure.CaptureSetup);
        }
    }

    private static bool IsProtectionFailure(Exception error) =>
        error is Win32Exception or COMException or InvalidOperationException or PlatformNotSupportedException;

    private void Dispatch(Action action)
    {
        if (!Dispatcher.HasShutdownStarted)
            Dispatcher.BeginInvoke(action);
    }

    public void ReportStatus(string message) => StatusText.Text = message;

    private nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0020 && wParam == hwnd)
        {
            CursorPolicy.SetArrow();
            handled = true;
            return 1;
        }
        if (message == 0x0312)
        {
            handled = true;
            if (wParam.ToInt32() == WindowHotkeys.ToggleVisibility)
                Dispatch(() => ToggleVisibility());
            else if (wParam.ToInt32() == WindowHotkeys.ToggleEditing)
                Dispatch(async () =>
                {
                    SetNotesExpanded(true);
                    await TogglePresentationAsync(forceEdit: true);
                });
        }
        else if (message == 0x0021 && presenting)
        {
            handled = true;
            return 3; // MA_NOACTIVATE still permits deliberate pointer interaction.
        }
        else if (!showing && message is (0x007E or 0x02E0))
        {
            Dispatch(() => InvalidateFromEvent("Display configuration changed. Relaunch or use the reveal shortcut."));
        }
        return 0;
    }

    private void SessionSwitch(object sender, SessionSwitchEventArgs args)
    {
        if (args.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff
            or SessionSwitchReason.RemoteConnect or SessionSwitchReason.RemoteDisconnect)
            Dispatch(() => InvalidateFromEvent("Session changed. Transcription stopped; reveal deliberately."));
    }

    private void PowerModeChanged(object sender, PowerModeChangedEventArgs args)
    {
        if (args.Mode is PowerModes.Suspend or PowerModes.Resume)
            Dispatch(() => InvalidateFromEvent("Power state changed. Transcription stopped; reveal deliberately."));
    }

    private async void InvalidateFromEvent(string message) => await InvalidateAsync(message);

    private async Task InvalidateAsync(string message)
    {
        protectionReady = false;
        Hide();
        await StopTranscriptionAsync();
        ReportStatus(message);
    }

    private async void ToggleVisibility()
    {
        if (IsVisible)
        {
            Hide();
            await StopTranscriptionAsync();
        }
        else
            TryReveal();
    }

    private async Task TogglePresentationAsync(bool forceEdit = false)
    {
        if (fileBusy)
        {
            ReportStatus("Wait for the file operation to finish.");
            return;
        }
        var next = forceEdit ? false : !presenting;
        Hide();
        await StopTranscriptionAsync();
        presenting = next;
        UpdateInteractionControls();
        AudioPanel.Visibility = Visibility.Collapsed;
        FindPanel.Visibility = Visibility.Collapsed;
        if (!presenting)
            SetNotesExpanded(true);
        TryReveal();
        if (!presenting && protectionReady && !diagnosticRendering)
            NoteEditor.Focus();
    }

    private void UpdateInteractionControls()
    {
        Resources["ControlsFocusable"] = !presenting;
        NoteEditor.IsReadOnly = presenting;
        NoteEditor.Focusable = !presenting;
        TranscriptText.Focusable = !presenting;
        InputMethod.SetIsInputMethodEnabled(NoteEditor, !presenting);
        FileToolbar.IsEnabled = !presenting && !fileBusy;
        SourceButton.IsEnabled = !fileBusy;
        ExportButton.IsEnabled = !presenting && !fileBusy;
        AudioEditHint.Visibility = presenting ? Visibility.Visible : Visibility.Collapsed;
        ConfirmEditHint.Visibility = presenting ? Visibility.Visible : Visibility.Collapsed;
        ModeButton.Content = presenting ? "Edit" : "Present";
        UpdateAudioControls();
    }

    private async void Mode_Click(object sender, RoutedEventArgs args) => await TogglePresentationAsync();
    private void Hide_Click(object sender, RoutedEventArgs args) => ToggleVisibility();
    private async void Exit_Click(object sender, RoutedEventArgs args) => await RequestActionAsync(PendingAction.Exit);
    private async void New_Click(object sender, RoutedEventArgs args) => await RequestActionAsync(PendingAction.New);
    private async void Open_Click(object sender, RoutedEventArgs args) => await RequestActionAsync(PendingAction.Open);
    private async void Save_Click(object sender, RoutedEventArgs args) => await SaveNoteAsync(false);
    private async void SaveAs_Click(object sender, RoutedEventArgs args) => await SaveNoteAsync(true);
    private bool NotesDirty => NoteEditor.Text != savedText;
    private bool TranscriptDirty => transcript.HasContent && transcript.ExportText() != exportedTranscript;

    private async Task RequestActionAsync(PendingAction action)
    {
        if (fileBusy || (presenting && action != PendingAction.Exit))
        {
            ReportStatus("Switch to Edit and finish any file operation first.");
            return;
        }
        if (action == PendingAction.Exit)
            await StopTranscriptionAsync();
        pending = action;
        if (NotesDirty || (action == PendingAction.Exit && TranscriptDirty))
            ConfirmPanel.Visibility = Visibility.Visible;
        else
            await ExecutePendingAsync();
    }

    private async Task ExecutePendingAsync()
    {
        if (fileBusy)
        {
            ReportStatus("Wait for the current file operation before discarding or replacing this note.");
            return;
        }
        var action = pending;
        pending = PendingAction.None;
        ConfirmPanel.Visibility = Visibility.Collapsed;
        if (action == PendingAction.Exit)
        {
            allowClose = true;
            Close();
        }
        else if (action == PendingAction.New)
        {
            documentPath = null;
            fingerprint = null;
            withBom = false;
            savedText = "";
            NoteEditor.Text = "";
            NoteEditor.IsUndoEnabled = false;
            NoteEditor.IsUndoEnabled = true;
            UpdateDocumentStatus();
        }
        else if (action == PendingAction.Open)
            await OpenNoteAsync();
    }

    private async void ConfirmSave_Click(object sender, RoutedEventArgs args)
    {
        if (await SaveNoteAsync(false) && (pending != PendingAction.Exit || !TranscriptDirty))
            await ExecutePendingAsync();
    }
    private async void ConfirmExport_Click(object sender, RoutedEventArgs args)
    {
        if (await ExportTranscriptAsync() && !NotesDirty)
            await ExecutePendingAsync();
    }
    private async void ConfirmDiscard_Click(object sender, RoutedEventArgs args) => await ExecutePendingAsync();
    private void ConfirmCancel_Click(object sender, RoutedEventArgs args)
    {
        pending = PendingAction.None;
        ConfirmPanel.Visibility = Visibility.Collapsed;
    }

    private async Task OpenNoteAsync()
    {
        var dialog = new OpenFileDialog { Filter = "UTF-8 text (*.txt)|*.txt", Multiselect = false };
        if (dialog.ShowDialog(this) != true)
            return;
        SetFileBusy(true);
        try
        {
            var note = await NoteFileStore.ReadAsync(dialog.FileName);
            documentPath = dialog.FileName;
            fingerprint = note.Fingerprint;
            withBom = note.HasUtf8Bom;
            savedText = note.Text;
            NoteEditor.Text = note.Text;
            NoteEditor.IsUndoEnabled = false;
            NoteEditor.IsUndoEnabled = true;
            UpdateDocumentStatus();
            ReportStatus("UTF-8 note opened. Native file dialogs are outside capture-exclusion assurance.");
        }
        catch (Exception error) when (IsFileFailure(error))
        {
            ReportStatus("Open failed. Use an accessible UTF-8 file no larger than 1 MiB; current notes were retained.");
        }
        finally { SetFileBusy(false); }
    }

    private async Task<bool> SaveNoteAsync(bool saveAs)
    {
        if (presenting || fileBusy)
        {
            ReportStatus("Switch to Edit and finish any file operation before saving.");
            return false;
        }
        var path = documentPath;
        var expected = fingerprint;
        if (saveAs || path is null)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "UTF-8 text (*.txt)|*.txt", FileName = "speaker-notes.txt",
                AddExtension = true, DefaultExt = ".txt", OverwritePrompt = true
            };
            if (dialog.ShowDialog(this) != true)
                return false;
            path = dialog.FileName;
            expected = null;
        }
        SetFileBusy(true);
        var content = NoteEditor.Text;
        try
        {
            if ((saveAs || documentPath is null) && File.Exists(path))
                expected = (await NoteFileStore.ReadAsync(path)).Fingerprint;
            var saved = await NoteFileStore.WriteAsync(path, content, withBom, expected);
            documentPath = path;
            fingerprint = saved.Fingerprint;
            savedText = content;
            UpdateDocumentStatus();
            ReportStatus("Note saved locally. Files on disk are not protected by screen-capture exclusion.");
            return true;
        }
        catch (NoteConflictException)
        {
            ReportStatus("The file changed outside this app. Your edits remain here; use Save as to resolve.");
            return false;
        }
        catch (Exception error) when (IsFileFailure(error))
        {
            ReportStatus("Save failed. Check file access, free space, UTF-8 text and the 1 MiB limit. Edits were retained.");
            return false;
        }
        finally { SetFileBusy(false); }
    }

    private static bool IsFileFailure(Exception error) => error is IOException or UnauthorizedAccessException
        or DecoderFallbackException or EncoderFallbackException or ArgumentException;

    private void SetFileBusy(bool value)
    {
        fileBusy = value;
        NoteEditor.IsEnabled = !value;
        ConfirmPanel.IsEnabled = !value;
        UpdateInteractionControls();
    }

    private void Note_TextChanged(object sender, TextChangedEventArgs args)
    {
        if (DocumentStatus is not null)
            UpdateDocumentStatus();
    }

    private void UpdateDocumentStatus() =>
        DocumentStatus.Text = (NotesDirty ? "Unsaved changes" : documentPath is null ? "New note" : "Saved locally")
            + "  /  no automatic saving";

    private void Note_Pasting(object sender, DataObjectPastingEventArgs args)
    {
        var pasted = args.SourceDataObject.GetData(DataFormats.UnicodeText) as string;
        if (presenting || pasted is null ||
            !NoteFileStore.Fits(NoteEditor.Text.Remove(NoteEditor.SelectionStart, NoteEditor.SelectionLength)
                .Insert(NoteEditor.SelectionStart, pasted), withBom))
        {
            args.CancelCommand();
            ReportStatus("Paste rejected: switch to Edit and keep plain UTF-8 text within 1 MiB.");
        }
    }

    private void Smaller_Click(object sender, RoutedEventArgs args) => NoteEditor.FontSize = Math.Max(12, NoteEditor.FontSize - 2);
    private void Larger_Click(object sender, RoutedEventArgs args) => NoteEditor.FontSize = Math.Min(42, NoteEditor.FontSize + 2);
    private void BackgroundOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> args)
    {
        if (WindowSurface is not null)
        {
            var alpha = (byte)Math.Round(Math.Clamp(args.NewValue, 10, 100) * 255 / 100);
            var background = ThemeBrushes.Color(ThemePalette.For(currentTheme).Background);
            WindowSurface.Background = new SolidColorBrush(Color.FromArgb(alpha, background.R, background.G, background.B));
        }
    }

    private void ApplyTheme(UiTheme theme)
    {
        currentTheme = theme;
        ThemeBrushes.Apply(Resources, theme);
        var next = ThemePalette.NextThemed(theme);
        ThemeName.Text = ThemePalette.For(theme).Name;
        ThemeToggleLabel.Text = ThemePalette.For(next).Name;
        ThemeToggleIcon.Text = next == UiTheme.HarborDaylight ? "\u2600" : "\u263E";
        System.Windows.Automation.AutomationProperties.SetName(
            ThemeToggleButton, "Switch to " + ThemePalette.For(next).Name);
        PlainThemeButton.BorderThickness = new Thickness(theme == UiTheme.Plain ? 2 : 1);
        BackgroundOpacity_ValueChanged(BackgroundOpacity,
            new RoutedPropertyChangedEventArgs<double>(BackgroundOpacity.Value, BackgroundOpacity.Value));
    }

    private void ThemeToggle_Click(object sender, RoutedEventArgs args) =>
        ApplyTheme(ThemePalette.NextThemed(currentTheme));
    private void PlainTheme_Click(object sender, RoutedEventArgs args) => ApplyTheme(UiTheme.Plain);

    private void SetNotesExpanded(bool expanded)
    {
        NotesSection.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        NotesToggleButton.Content = expanded ? "Notes \u25BE" : "Notes \u25B8";
        if (expanded)
            AudioPanel.Visibility = Visibility.Collapsed;
        else
            FindPanel.Visibility = Visibility.Collapsed;
    }

    private void NotesToggle_Click(object sender, RoutedEventArgs args) =>
        SetNotesExpanded(NotesSection.Visibility != Visibility.Visible);
    private void Wrap_Click(object sender, RoutedEventArgs args) =>
        NoteEditor.TextWrapping = WrapCheck.IsChecked == true ? TextWrapping.Wrap : TextWrapping.NoWrap;
    private void Find_Click(object sender, RoutedEventArgs args)
    {
        SetNotesExpanded(true);
        FindPanel.Visibility = Visibility.Visible;
        FindText.Focus();
    }
    private void CloseFind_Click(object sender, RoutedEventArgs args) => FindPanel.Visibility = Visibility.Collapsed;
    private void FindNext_Click(object sender, RoutedEventArgs args)
    {
        var query = FindText.Text;
        if (query.Length == 0)
        {
            ReportStatus("Enter text to find.");
            return;
        }
        var start = NoteEditor.SelectionStart + NoteEditor.SelectionLength;
        var index = NoteEditor.Text.IndexOf(query, start, StringComparison.CurrentCultureIgnoreCase);
        if (index < 0)
            index = NoteEditor.Text.IndexOf(query, StringComparison.CurrentCultureIgnoreCase);
        if (index < 0)
            ReportStatus("No matching note text.");
        else
        {
            NoteEditor.Focus();
            NoteEditor.Select(index, query.Length);
            NoteEditor.ScrollToLine(NoteEditor.GetLineIndexFromCharacterIndex(index));
        }
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs args)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control || presenting)
            return;
        if (args.Key is Key.S or Key.O or Key.N or Key.F)
            args.Handled = true;
        switch (args.Key)
        {
            case Key.S: await SaveNoteAsync(false); break;
            case Key.O: await RequestActionAsync(PendingAction.Open); break;
            case Key.N: await RequestActionAsync(PendingAction.New); break;
            case Key.F: Find_Click(sender, args); break;
        }
    }

    private void RefreshDevices()
    {
        try
        {
            var oldId = (OutputDevices.SelectedItem as RenderDevice)?.Id;
            var devices = LoopbackTranscriber.GetRenderDevices();
            OutputDevices.ItemsSource = devices;
            OutputDevices.SelectedItem = devices.FirstOrDefault(device => device.Id == oldId) ?? devices.FirstOrDefault();
            if (devices.Count == 0)
                AudioStatus.Text = "No active playback device. A microphone will never be substituted.";
        }
        catch (Exception error) when (error is COMException or InvalidOperationException)
        {
            AudioStatus.Text = "Playback-device discovery failed. Check Windows audio output and retry.";
        }
    }

    private void RefreshDevices_Click(object sender, RoutedEventArgs args)
    {
        if (transcriber.IsRunning || audioBusy)
            AudioStatus.Text = "Stop transcription before changing playback devices.";
        else
            RefreshDevices();
    }

    private void OutputDevices_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs args)
    {
        if (!presenting || !OutputDevices.IsEnabled)
            return;
        if (args.OriginalSource is DependencyObject target &&
            ItemsControl.ContainerFromElement(OutputDevices, target) is ListBoxItem { IsEnabled: true } item)
        {
            // The default ListBoxItem click path requires Focus(); passive selection must not activate us.
            OutputDevices.SelectedItem = item.Content;
            args.Handled = true;
        }
    }

    private void AudioSetup_Click(object sender, RoutedEventArgs args)
    {
        var opening = AudioPanel.Visibility != Visibility.Visible;
        if (opening)
            SetNotesExpanded(false);
        AudioPanel.Visibility = opening ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Start_Click(object sender, RoutedEventArgs args)
    {
        if (audioBusy || transcriber.IsRunning)
            return;
        if (!protectionReady ||
            OutputDevices.SelectedItem is not RenderDevice device || ServiceToken.Password.Length < 32)
        {
            SetNotesExpanded(false);
            AudioPanel.Visibility = Visibility.Visible;
            AudioStatus.Text = presenting
                ? "Select playback output. Use Edit or the launcher to configure a missing local token."
                : "Select playback output and configure the local service token first.";
            return;
        }
        var endpointText = Environment.GetEnvironmentVariable("PHANTOM_TRANSCRIPTION_URI")
            ?? "ws://127.0.0.1:8765/v1/transcribe";
        if (!Uri.TryCreate(endpointText, UriKind.Absolute, out var endpoint))
        {
            AudioStatus.Text = "The local service address is invalid. Use the repository launcher to reset it.";
            return;
        }
        audioBusy = true;
        using var startCancellation = new CancellationTokenSource();
        audioStartCancellation = startCancellation;
        UpdateAudioControls();
        try
        {
            TranscriptionSocket.ValidateEndpoint(endpoint);
            transcript.BeginSession();
            await transcriber.StartAsync(device.Id, endpoint, ServiceToken.Password, startCancellation.Token);
            TranscriptText.Text = transcript.Text;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException
            or System.Net.WebSockets.WebSocketException or OperationCanceledException or COMException)
        {
            AudioStatus.Text = "Transcription could not start. Check the local service, model, token and playback device.";
        }
        finally
        {
            audioStartCancellation = null;
            audioBusy = false;
            UpdateAudioControls();
        }
    }

    private void UpdateAudioControls()
    {
        StartButton.IsEnabled = !audioBusy && !transcriber.IsRunning && protectionReady;
        StopButton.IsEnabled = !audioBusy && transcriber.IsRunning;
        OutputDevices.IsEnabled = !audioBusy && !transcriber.IsRunning;
        ServiceToken.IsEnabled = !presenting && !audioBusy && !transcriber.IsRunning;
    }

    private async void Stop_Click(object sender, RoutedEventArgs args) => await StopTranscriptionAsync();
    private async Task StopTranscriptionAsync()
    {
        audioStartCancellation?.Cancel();
        try
        {
            await transcriber.StopAsync();
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or OperationCanceledException
            or System.Net.WebSockets.WebSocketException or COMException)
        {
            AudioStatus.Text = "Transcription stopped with a connection/device error. An unfinished tail may be missing.";
        }
        AudioLevel.Value = 0;
        UpdateAudioControls();
    }

    private async void ReceiveTranscript(TranscriptEvent value)
    {
        try
        {
            if (value.Type == "partial")
                transcript.SetPartial(value.Text);
            else if (value.Type == "final")
                transcript.AddFinal(value.SegmentId ?? throw new InvalidDataException("Missing segment ID."),
                    value.Text, value.StartSeconds);
            TranscriptText.Text = transcript.Text;
            TranscriptText.ScrollToEnd();
            PartialText.Text = transcript.Partial;
            TranscriptPlaceholder.Visibility = transcript.HasContent ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException)
        {
            await StopTranscriptionAsync();
            AudioStatus.Text = "Transcription stopped: invalid data or transcript memory limit. Export before clearing.";
        }
    }

    private async void Export_Click(object sender, RoutedEventArgs args) => await ExportTranscriptAsync();
    private async Task<bool> ExportTranscriptAsync()
    {
        if (presenting || fileBusy)
        {
            ReportStatus("Switch to Edit and finish any file operation before exporting.");
            return false;
        }
        if (!transcript.HasContent)
        {
            ReportStatus("There is no transcript to export.");
            return false;
        }
        var dialog = new SaveFileDialog
        {
            Filter = "UTF-8 text (*.txt)|*.txt", FileName = "meeting-transcript.txt",
            DefaultExt = ".txt", AddExtension = true, OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true)
            return false;
        SetFileBusy(true);
        var content = transcript.ExportText();
        try
        {
            var expected = File.Exists(dialog.FileName)
                ? (await NoteFileStore.ReadAsync(dialog.FileName)).Fingerprint : null;
            await NoteFileStore.WriteAsync(dialog.FileName, content, false, expected);
            exportedTranscript = content;
            ReportStatus("Transcript explicitly exported. No audio was saved.");
            return true;
        }
        catch (Exception error) when (IsFileFailure(error))
        {
            ReportStatus("Transcript export failed; the in-memory transcript was retained.");
            return false;
        }
        finally { SetFileBusy(false); }
    }

    private void ClearTranscript_Click(object sender, RoutedEventArgs args)
    {
        if (transcriber.IsRunning || audioBusy)
        {
            AudioStatus.Text = "Stop transcription before clearing.";
            return;
        }
        if (TranscriptDirty)
        {
            ReportStatus("Export the unsaved transcript before clearing it, or explicitly discard it when exiting.");
            return;
        }
        transcript.Clear();
        exportedTranscript = "";
        TranscriptText.Clear();
        PartialText.Text = "";
        TranscriptPlaceholder.Visibility = Visibility.Visible;
    }

    private void Window_Closing(object? sender, CancelEventArgs args)
    {
        if (allowClose)
            return;
        args.Cancel = true;
        if (closeRequestQueued)
            return;
        closeRequestQueued = true;
        Dispatch(async () =>
        {
            try { await RequestActionAsync(PendingAction.Exit); }
            finally { closeRequestQueued = false; }
        });
    }

    private async void Window_Closed(object? sender, EventArgs args)
    {
        if (subscribed)
        {
            SystemEvents.SessionSwitch -= SessionSwitch;
            SystemEvents.PowerModeChanged -= PowerModeChanged;
        }
        source?.RemoveHook(WindowMessage);
        hotkeys?.Dispose();
        await transcriber.DisposeAsync();
        Application.Current.Shutdown();
    }

    internal async Task<int> RunUiSmokeAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        diagnosticRendering = true;
        try
        {
            var foregroundBefore = GetForegroundWindow();
            ShowProtected();
            var foregroundPreservedOnShow = GetForegroundWindow() == foregroundBefore;
            var handle = new WindowInteropHelper(this).Handle;
            var startsPassive = presenting && !ShowActivated &&
                (WindowProtection.ReadStyles(handle) & 0x08000000) != 0;
            var startAvailableAfterProtection = StartButton.IsEnabled;
            var notesCollapsedByDefault = NotesSection.Visibility == Visibility.Collapsed;
            ServiceToken.Password = "";
            NoteEditor.Text = "Speaker notes\n\n1. Introduce the idea.\n2. Show the example.\n3. Invite questions.";
            ReceiveTranscript(new TranscriptEvent("final", "This is a synthetic UI transcript.", 1, 0, 2));
            ReceiveTranscript(new TranscriptEvent("partial", "This unfinished line may change"));
            await RenderDiagnosticAsync(Path.Combine(directory, "transcript-deepseafoam.png"));
            var transcriptPrimary = notesCollapsedByDefault && TranscriptText.ActualHeight >= ActualHeight * 0.40;
            var mouseActivation = SendMessage(handle, 0x0021, GetForegroundWindow(),
                new nint((0x0201 << 16) | 1)).ToInt32();
            var cursorHandled = SendMessage(handle, 0x0020, handle, new nint((0x0200 << 16) | 1)) == 1;
            var arrowPolicy = Cursor == Cursors.Arrow && ForceCursor &&
                NoteEditor.Cursor == Cursors.Arrow && TranscriptText.Cursor == Cursors.Arrow &&
                ServiceToken.Cursor == Cursors.Arrow && CursorPolicy.IsArrow && cursorHandled;
            var before = NoteEditor.Text;
            var transcriptBefore = transcript.ExportText();
            var originalHandle = handle;
            var themes = new List<object>();
            var allThemesMatch = true;
            foreach (var theme in Enum.GetValues<UiTheme>())
            {
                ApplyTheme(theme);
                var palette = ThemePalette.For(theme);
                var match = ((SolidColorBrush)Resources["ThemeText"]).Color == ThemeBrushes.Color(palette.Text)
                    && ((SolidColorBrush)Resources["ThemeSurface"]).Color == ThemeBrushes.Color(palette.Surface)
                    && ((SolidColorBrush)Resources["ThemeAccent"]).Color == ThemeBrushes.Color(palette.Accent)
                    && NoteEditor.Foreground is SolidColorBrush noteBrush && noteBrush.Color == ThemeBrushes.Color(palette.Text)
                    && TranscriptText.Foreground is SolidColorBrush transcriptBrush && transcriptBrush.Color == ThemeBrushes.Color(palette.Text);
                allThemesMatch &= match;
                themes.Add(new { palette.Name, match, palette.Background, palette.Text, palette.Accent });
                await RenderDiagnosticAsync(Path.Combine(directory, $"theme-{theme}.png"));
            }
            PlainTheme_Click(this, new RoutedEventArgs());
            var plainReset = currentTheme == UiTheme.Plain;
            ThemeToggle_Click(this, new RoutedEventArgs());
            var darkToggle = currentTheme == UiTheme.DeepSeaFoam && ThemeToggleIcon.Text == "\u2600";
            ThemeToggle_Click(this, new RoutedEventArgs());
            var lightToggle = currentTheme == UiTheme.HarborDaylight && ThemeToggleIcon.Text == "\u263E";
            ApplyTheme(UiTheme.DeepSeaFoam);
            AudioPanel.Visibility = Visibility.Visible;
            await RenderDiagnosticAsync(Path.Combine(directory, "audio-setup.png"));
            var deviceSource = OutputDevices.ItemsSource;
            var selectedDevice = OutputDevices.SelectedItem;
            var syntheticDevices = new[]
            {
                new RenderDevice("synthetic-a", "Synthetic output A"),
                new RenderDevice("synthetic-b", "Synthetic output B")
            };
            OutputDevices.ItemsSource = syntheticDevices;
            OutputDevices.SelectedIndex = 0;
            UpdateLayout();
            var secondDevice = (ListBoxItem)OutputDevices.ItemContainerGenerator.ContainerFromIndex(1);
            var focusBeforeDeviceClick = GetForegroundWindow();
            var keyboardBeforeDeviceClick = Keyboard.FocusedElement;
            var click = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            {
                RoutedEvent = Mouse.PreviewMouseDownEvent
            };
            secondDevice.RaiseEvent(click);
            var passiveDeviceSelection = ReferenceEquals(OutputDevices.SelectedItem, syntheticDevices[1])
                && !secondDevice.Focusable && GetForegroundWindow() == focusBeforeDeviceClick
                && ReferenceEquals(Keyboard.FocusedElement, keyboardBeforeDeviceClick);
            OutputDevices.ItemsSource = deviceSource;
            OutputDevices.SelectedItem = selectedDevice;
            AudioPanel.Visibility = Visibility.Collapsed;
            BackgroundOpacity.Value = 10;
            var translucentImage = await RenderDiagnosticAsync(Path.Combine(directory, "background-10.png"));
            var samplePoint = TranscriptText.TranslatePoint(
                new Point(TranscriptText.ActualWidth - 30, TranscriptText.ActualHeight - 25), this);
            var dpi = VisualTreeHelper.GetDpi(this);
            var sampleRect = new Int32Rect((int)(samplePoint.X * dpi.DpiScaleX),
                (int)(samplePoint.Y * dpi.DpiScaleY), 1, 1);
            var pixel = new byte[4];
            translucentImage.CopyPixels(sampleRect, pixel, 4, 0);
            var transparentPixelAlpha = pixel[3];
            BackgroundOpacity.Value = 100;
            var opaqueImage = await RenderDiagnosticAsync(Path.Combine(directory, "background-100.png"));
            opaqueImage.CopyPixels(sampleRect, pixel, 4, 0);
            var opaquePixelAlpha = pixel[3];
            BackgroundOpacity.Value = 55;
            var textSolid = Opacity == 1 && NoteEditor.Opacity == 1 && TranscriptText.Opacity == 1 &&
                NoteEditor.Foreground is SolidColorBrush { Color.A: 255 } &&
                TranscriptText.Foreground is SolidColorBrush { Color.A: 255 };
            var translucencyWorks = AllowsTransparency && transparentPixelAlpha is > 0 and < 60 &&
                opaquePixelAlpha == 255 && textSolid && new WindowInteropHelper(this).Handle == originalHandle;
            NotesToggle_Click(this, new RoutedEventArgs());
            UpdateLayout();
            var expandedWithoutActivation = NotesSection.Visibility == Visibility.Visible && presenting;
            await RenderDiagnosticAsync(Path.Combine(directory, "notes-expanded.png"));
            var savedTranscriptView = TranscriptText.Text;
            TranscriptText.Text = string.Join(Environment.NewLine, Enumerable.Repeat("Synthetic scrolling line", 100));
            UpdateLayout();
            TranscriptText.ScrollToEnd();
            UpdateLayout();
            var transcriptScrollWorks = TranscriptText.VerticalOffset > 0;
            TranscriptText.Text = savedTranscriptView;
            NoteEditor.TextWrapping = TextWrapping.NoWrap;
            NoteEditor.Text = new string('W', 250);
            UpdateLayout();
            NoteEditor.ScrollToHorizontalOffset(120);
            UpdateLayout();
            var noteScrollWorks = NoteEditor.HorizontalOffset > 0;
            NoteEditor.Text = before;
            NoteEditor.TextWrapping = TextWrapping.Wrap;
            var captionHit = HitTestDiagnostic(new Point(22, 18));
            var resizeHit = HitTestDiagnostic(new Point(2, ActualHeight / 2));
            var editorHit = HitTestDiagnostic(NoteEditor.TranslatePoint(new Point(30, 30), this));
            var chromeWorks = captionHit == 2 && resizeHit == 10 && editorHit == 1;
            await TogglePresentationAsync(forceEdit: true);
            var editableOnRequest = !NoteEditor.IsReadOnly && NoteEditor.Focusable;
            await TogglePresentationAsync();
            var readOnly = NoteEditor.IsReadOnly && !NoteEditor.Focusable && BackgroundOpacity.IsEnabled;
            await RenderDiagnosticAsync(Path.Combine(directory, "present.png"));
            await TogglePresentationAsync(forceEdit: true);
            SetNotesExpanded(false);
            SetNotesExpanded(true);
            var preserved = NoteEditor.Text == before && transcript.ExportText() == transcriptBefore;
            SetFileBusy(true);
            pending = PendingAction.New;
            await ExecutePendingAsync();
            var replacementBlocked = NoteEditor.Text == before && pending == PendingAction.New;
            pending = PendingAction.None;
            SetFileBusy(false);
            WindowState = WindowState.Minimized;
            RevealAndReset();
            var recoveredMinimized = WindowState == WindowState.Normal;
            var uriBefore = Environment.GetEnvironmentVariable("PHANTOM_TRANSCRIPTION_URI");
            try
            {
                ServiceToken.Password = new string('x', 64);
                Environment.SetEnvironmentVariable("PHANTOM_TRANSCRIPTION_URI", "not a valid uri");
                Start_Click(this, new RoutedEventArgs());
                if (!AudioStatus.Text.Contains("address is invalid", StringComparison.Ordinal))
                    throw new InvalidOperationException("Invalid URI was not rejected before startup.");
            }
            finally
            {
                Environment.SetEnvironmentVariable("PHANTOM_TRANSCRIPTION_URI", uriBefore);
                ServiceToken.Password = "";
            }
            var styles = WindowProtection.ReadStyles(new WindowInteropHelper(this).Handle);
            var acknowledgmentsRemoved = FindName("ConsentCheck") is null && FindName("RehearsalCheck") is null;
            var passed = protectionReady && preserved && readOnly && !ShowInTaskbar && acknowledgmentsRemoved &&
                translucencyWorks && chromeWorks && startsPassive && startAvailableAfterProtection &&
                foregroundPreservedOnShow && transcriptScrollWorks && noteScrollWorks &&
                notesCollapsedByDefault && transcriptPrimary &&
                mouseActivation == 3 && arrowPolicy && allThemesMatch && plainReset && darkToggle && lightToggle &&
                expandedWithoutActivation && editableOnRequest && passiveDeviceSelection &&
                replacementBlocked && recoveredMinimized &&
                (styles & WindowProtection.ToolWindow) != 0 && !transcriber.IsRunning;
            await File.WriteAllTextAsync(Path.Combine(directory, "result.json"),
                JsonSerializer.Serialize(new
                {
                    passed, lastProtection, preserved, replacementBlocked, recoveredMinimized, acknowledgmentsRemoved,
                    translucencyWorks, transparentPixelAlpha, opaquePixelAlpha, textSolid,
                    chromeWorks, captionHit, resizeHit, editorHit,
                    startsPassive, startAvailableAfterProtection, foregroundPreservedOnShow, notesCollapsedByDefault, transcriptPrimary,
                    mouseActivation, arrowPolicy, allThemesMatch, plainReset, darkToggle, lightToggle,
                    expandedWithoutActivation, editableOnRequest, passiveDeviceSelection, themes,
                    transcriptScrollWorks, noteScrollWorks,
                    invalidUriRejected = true, presentReadOnly = readOnly, ShowInTaskbar,
                    toolWindow = (styles & WindowProtection.ToolWindow) != 0,
                    nativeStyles = styles, recordingStarted = transcriber.IsRunning,
                    discoveredRenderEndpoints = OutputDevices.Items.Count,
                    limitation = "Synthetic local UI, cursor-shape policy and nonactivation assertions only; " +
                        "no real audio or remote captured-cursor visibility guarantee."
                }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 2;
        }
        finally
        {
            allowClose = true;
            Close();
        }
    }

    private async Task<RenderTargetBitmap> RenderDiagnosticAsync(string path)
    {
        UpdateLayout();
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render);
        var dpi = VisualTreeHelper.GetDpi(this);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);
        bitmap.Render(this);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
        return bitmap;
    }

    private int HitTestDiagnostic(Point point)
    {
        var screen = PointToScreen(point);
        var coordinates = unchecked((int)((uint)(ushort)(int)screen.X | ((uint)(ushort)(int)screen.Y << 16)));
        return SendMessage(new WindowInteropHelper(this).Handle, 0x0084, nint.Zero, new nint(coordinates)).ToInt32();
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, int message, nint wParam, nint lParam);
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
}
