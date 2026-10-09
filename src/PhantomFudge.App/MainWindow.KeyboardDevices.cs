using System.Windows.Controls;
using System.Windows.Input;
using PhantomFudge.Core;

namespace PhantomFudge.App;

public partial class MainWindow
{
    private ListBox? keyboardDeviceList;
    private object? keyboardDeviceTarget;

    private void BeginKeyboardDevices()
    {
        keyboardDeviceList = captureMode == AudioCaptureMode.Microphone ? MicrophoneDevices : OutputDevices;
        keyboardDeviceTarget = keyboardDeviceList.SelectedItem ?? keyboardDeviceList.Items.Cast<object>().FirstOrDefault();
        RevealKeyboardDeviceCursor();
    }

    private void ClearKeyboardDeviceCursor()
    {
        foreach (var list in new[] { OutputDevices, MicrophoneDevices })
            foreach (var device in list.Items)
                if (list.ItemContainerGenerator.ContainerFromItem(device) is ListBoxItem card)
                    card.Tag = null;
    }

    private void RevealKeyboardDeviceCursor()
    {
        ClearKeyboardDeviceCursor();
        if (keyboardMode != KeyboardMode.Devices || keyboardDeviceList is not { } list) return;
        var index = list.Items.IndexOf(keyboardDeviceTarget);
        if (index < 0) { KeyboardHints.Text = "No device available for this source. Tab changes source; Esc returns."; return; }
        var page = list == OutputDevices ? outputCardPage : microphoneCardPage;
        page.RevealSelection = false;
        page.Index = index / page.Size;
        UpdateDeviceCards();
        if (list.ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem card) card.Tag = "KeyboardCursor";
        KeyboardHints.Text = $"{(list == OutputDevices ? "Computer" : "Mic")} card {index + 1}/{list.Items.Count} | " +
            "arrows move; Space selects; Tab source; [ / ] page; Esc returns";
    }

    private bool HandleKeyboardDeviceKey(Key key, bool repeated)
    {
        if (audioBusy || audioStopping || transcriber.IsRunning || keyboardDeviceList is not { IsEnabled: true } list)
        { KeyboardHints.Text = "Stop transcription before browsing or changing devices."; return true; }
        if (key == Key.Tab && captureMode == AudioCaptureMode.Both)
        {
            keyboardDeviceList = list == OutputDevices ? MicrophoneDevices : OutputDevices;
            keyboardDeviceTarget = keyboardDeviceList.SelectedItem ?? keyboardDeviceList.Items.Cast<object>().FirstOrDefault();
            RevealKeyboardDeviceCursor();
            return true;
        }
        if (list.Items.Count == 0) { RevealKeyboardDeviceCursor(); return true; }
        var index = Math.Max(0, list.Items.IndexOf(keyboardDeviceTarget));
        var page = list == OutputDevices ? outputCardPage : microphoneCardPage;
        var columns = Math.Max(1, (int)(list.ActualWidth / 190));
        var delta = key switch
        {
            Key.J or Key.Down => columns,
            Key.K or Key.Up => -columns,
            Key.Right => 1,
            Key.Left => -1,
            Key.OemCloseBrackets or Key.PageDown => page.Size,
            Key.OemOpenBrackets or Key.PageUp => -page.Size,
            _ => 0
        };
        index = (int)Math.Clamp((long)index + delta, 0, list.Items.Count - 1);
        keyboardDeviceTarget = list.Items[index];
        if (key == Key.Space && !repeated) list.SelectedItem = keyboardDeviceTarget;
        RevealKeyboardDeviceCursor();
        return true;
    }
}
