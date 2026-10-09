using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PhantomFudge.App.Audio;
using PhantomFudge.Core;

namespace PhantomFudge.App;

public partial class MainWindow
{
    private async Task SettleDeviceCardsAsync()
    {
        for (var pass = 0; pass < 3; pass++)
        {
            UpdateLayout();
            UpdateDeviceCards();
            await Dispatcher.InvokeAsync(UpdateLayout, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private static bool CardsFitWithoutScrolling(ListBox list)
    {
        if (FindTranscriptScrollViewer(list) is not null)
            return false;
        foreach (var item in list.Items)
        {
            if (list.ItemContainerGenerator.ContainerFromItem(item) is not ListBoxItem card || !card.IsVisible)
                continue;
            var point = card.TranslatePoint(new Point(), list);
            if (point.X < 0 || point.Y < 0 || point.X + card.ActualWidth > list.ActualWidth + 1 ||
                point.Y + card.ActualHeight > list.ActualHeight + 1)
                return false;
        }
        return list.Items.Count == 0 || list.Items.Cast<object>().Any(item =>
            list.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem { IsVisible: true });
    }

    private async Task<object> RunAudioDeviceCardsDiagnosticAsync(string directory)
    {
        var oldOutput = OutputDevices.ItemsSource;
        var oldMicrophone = MicrophoneDevices.ItemsSource;
        var oldSelectedOutput = OutputDevices.SelectedItem;
        var oldSelectedMicrophone = MicrophoneDevices.SelectedItem;
        var oldWidth = Width;
        var oldHeight = Height;
        var oldMode = captureMode;
        var oldStatus = AudioStatus.Text;
        var oldTheme = currentTheme;
        var outputs = Enumerable.Range(1, 23).Select(index =>
            new RenderDevice($"card-output-{index}", $"Example playback device {index} - USB audio")).ToArray();
        var microphones = Enumerable.Range(1, 17).Select(index =>
            new MicrophoneDevice($"card-mic-{index}", $"Example microphone {index} - headset")).ToArray();
        var allCardsFit = true;
        var pagingPreservesSelection = false;
        var directCardSelection = false;
        var sourceModes = false;
        var lockedWhileBusy = false;
        var serviceSetupFits = false;
        var emptyState = false;
        var themeCards = true;
        try
        {
            SetCaptureMode(AudioCaptureMode.Both);
            OutputDevices.ItemsSource = outputs;
            MicrophoneDevices.ItemsSource = microphones;
            OutputDevices.SelectedIndex = MicrophoneDevices.SelectedIndex = 0;
            AudioPanel.Visibility = Visibility.Visible;
            AudioAdvancedPanel.Visibility = Visibility.Collapsed;
            AudioStatus.Text = "Synthetic device cards. No audio inputs are opened.";
            foreach (var size in new[] { (Width: MinWidth, Height: MinHeight), (Width: 900d, Height: 600d), (Width: 1240d, Height: 900d) })
            {
                Width = size.Width;
                Height = size.Height;
                await SettleDeviceCardsAsync();
                allCardsFit &= CardsFitWithoutScrolling(OutputDevices) && CardsFitWithoutScrolling(MicrophoneDevices) &&
                    IsReadingControlReachable(OutputDevices) && IsReadingControlReachable(MicrophoneDevices);
            }
            Width = MinWidth;
            Height = MinHeight;
            await SettleDeviceCardsAsync();
            var selected = OutputDevices.SelectedItem;
            ChangeDevicePage(OutputDevices, outputCardPage, new Button { Tag = "1" });
            await SettleDeviceCardsAsync();
            pagingPreservesSelection = outputCardPage.Index == 1 && ReferenceEquals(selected, OutputDevices.SelectedItem) &&
                OutputDevicePager.IsVisible && CardsFitWithoutScrolling(OutputDevices);
            var targetIndex = outputCardPage.Index * outputCardPage.Size;
            var card = (ListBoxItem)OutputDevices.ItemContainerGenerator.ContainerFromIndex(targetIndex);
            var foreground = GetForegroundWindow();
            card.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = Mouse.PreviewMouseDownEvent });
            await SettleDeviceCardsAsync();
            directCardSelection = ReferenceEquals(OutputDevices.SelectedItem, outputs[targetIndex]) &&
                ReferenceEquals(MicrophoneDevices.SelectedItem, microphones[0]) && GetForegroundWindow() == foreground && PassiveWindow;
            foreach (var theme in Enum.GetValues<UiTheme>())
            {
                ApplyTheme(theme);
                await SettleDeviceCardsAsync();
                themeCards &= card.Template.FindName("DeviceCardFace", card) is Border face &&
                    face.BorderBrush is SolidColorBrush brush && brush.Color == ThemeBrushes.Color(ThemePalette.For(theme).Accent);
            }
            ApplyTheme(oldTheme);
            await RenderDiagnosticAsync(System.IO.Path.Combine(directory, "audio-setup-cards-compact.png"));
            audioBusy = true;
            UpdateAudioControls();
            await SettleDeviceCardsAsync();
            var pageBefore = outputCardPage.Index;
            ChangeDevicePage(OutputDevices, outputCardPage, new Button { Tag = "1" });
            lockedWhileBusy = !OutputDevices.IsEnabled && !MicrophoneDevices.IsEnabled &&
                !OutputNextPage.IsEnabled && outputCardPage.Index == pageBefore;
            audioBusy = false;
            UpdateAudioControls();
            SetCaptureMode(AudioCaptureMode.Computer);
            await SettleDeviceCardsAsync();
            sourceModes = AudioDeviceGroups.Columns == 1 && MicrophoneDeviceSection.Visibility == Visibility.Collapsed &&
                ReferenceEquals(OutputDevices.SelectedItem, outputs[targetIndex]) && CardsFitWithoutScrolling(OutputDevices);
            SetCaptureMode(AudioCaptureMode.Microphone);
            await SettleDeviceCardsAsync();
            sourceModes &= AudioDeviceGroups.Columns == 1 && ComputerDeviceSection.Visibility == Visibility.Collapsed &&
                ReferenceEquals(MicrophoneDevices.SelectedItem, microphones[0]) && CardsFitWithoutScrolling(MicrophoneDevices);
            SetCaptureMode(AudioCaptureMode.Both);
            AudioAdvancedPanel.Visibility = Visibility.Visible;
            await SettleDeviceCardsAsync();
            serviceSetupFits = CardsFitWithoutScrolling(OutputDevices) && CardsFitWithoutScrolling(MicrophoneDevices) &&
                IsReadingControlReachable(ServiceToken);
            AudioAdvancedPanel.Visibility = Visibility.Collapsed;
            OutputDevices.ItemsSource = Array.Empty<RenderDevice>();
            MicrophoneDevices.ItemsSource = Array.Empty<MicrophoneDevice>();
            await SettleDeviceCardsAsync();
            emptyState = OutputDevicesEmpty.IsVisible && MicrophoneDevicesEmpty.IsVisible &&
                !OutputDevicePager.IsVisible && !MicrophoneDevicePager.IsVisible;
        }
        finally
        {
            audioBusy = false;
            Width = oldWidth;
            Height = oldHeight;
            OutputDevices.ItemsSource = oldOutput;
            MicrophoneDevices.ItemsSource = oldMicrophone;
            OutputDevices.SelectedItem = oldSelectedOutput;
            MicrophoneDevices.SelectedItem = oldSelectedMicrophone;
            SetCaptureMode(oldMode);
            AudioAdvancedPanel.Visibility = Visibility.Collapsed;
            AudioPanel.Visibility = Visibility.Collapsed;
            AudioStatus.Text = oldStatus;
            ApplyTheme(oldTheme);
            SetQuestionExpanded(true);
        }
        var passed = allCardsFit && pagingPreservesSelection && directCardSelection && themeCards &&
            lockedWhileBusy && sourceModes && serviceSetupFits && emptyState && !transcriber.IsRunning;
        var result = new { passed, allCardsFit, pagingPreservesSelection, directCardSelection, themeCards,
            lockedWhileBusy, sourceModes, serviceSetupFits, emptyState, audioCaptured = false };
        await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(directory, "audio-device-cards.json"),
            System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        if (!passed)
            throw new InvalidOperationException("Synthetic audio-device card assertions failed: " + System.Text.Json.JsonSerializer.Serialize(result));
        return result;
    }
}
