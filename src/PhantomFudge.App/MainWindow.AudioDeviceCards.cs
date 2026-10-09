using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using PhantomFudge.App.Audio;
using PhantomFudge.Core;

namespace PhantomFudge.App;

public partial class MainWindow
{
    private sealed class DeviceCardPage
    {
        public int Index;
        public int Size = int.MaxValue;
        public bool RevealSelection;
    }

    private readonly DeviceCardPage outputCardPage = new();
    private readonly DeviceCardPage microphoneCardPage = new();
    private bool deviceCardsQueued;
    private bool arrangingDeviceCards;

    private void InitializeAudioDeviceCards()
    {
        foreach (var list in new[] { OutputDevices, MicrophoneDevices })
        {
            ((INotifyCollectionChanged)list.Items).CollectionChanged += (_, _) => QueueDeviceCards();
            list.ItemContainerGenerator.StatusChanged += (_, _) =>
            {
                if (list.ItemContainerGenerator.Status == GeneratorStatus.ContainersGenerated)
                    QueueDeviceCards();
            };
        }
    }

    private void AudioPanel_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (!switchingCover && !switchingKeyboardMode && args.NewValue is true && AudioPanel.Visibility == Visibility.Visible)
        {
            SetNotesExpanded(false);
            SetQuestionExpanded(false);
            outputCardPage.RevealSelection = microphoneCardPage.RevealSelection = true;
        }
        SourceButton.Content = AudioPanel.Visibility == Visibility.Visible ? "Close setup" : "Audio setup";
        UpdateWorkspaceLayout();
        QueueDeviceCards();
    }

    private void AudioAdvanced_Click(object sender, RoutedEventArgs args)
    {
        AudioAdvancedPanel.Visibility = AudioAdvancedPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        AudioAdvancedToggle.Content = AudioAdvancedPanel.Visibility == Visibility.Visible ? "Close service setup" : "Service setup";
        QueueDeviceCards();
    }

    private void AudioDeviceCards_SizeChanged(object sender, SizeChangedEventArgs args) => QueueDeviceCards();

    private void AudioDeviceCard_Selected(object sender, SelectionChangedEventArgs args)
    {
        if (sender is ListBox list && list.SelectedIndex >= 0)
            (list == OutputDevices ? outputCardPage : microphoneCardPage).RevealSelection = true;
        QueueDeviceCards();
    }

    private void QueueDeviceCards()
    {
        if (deviceCardsQueued || arrangingDeviceCards || !questionViewReady || AudioPanel.Visibility != Visibility.Visible)
            return;
        deviceCardsQueued = true;
        _ = Dispatcher.BeginInvoke(() =>
        {
            deviceCardsQueued = false;
            UpdateDeviceCards();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void UpdateDeviceCards()
    {
        if (arrangingDeviceCards || !questionViewReady || AudioPanel.Visibility != Visibility.Visible)
            return;
        arrangingDeviceCards = true;
        try
        {
            AudioDeviceGroups.Columns = captureMode == AudioCaptureMode.Both ? 2 : 1;
            OutputDevicesEmpty.Visibility = OutputDevices.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            MicrophoneDevicesEmpty.Visibility = MicrophoneDevices.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            LayoutDeviceCards(OutputDevices, outputCardPage, OutputDevicePager, OutputDevicePageText, OutputPreviousPage, OutputNextPage);
            LayoutDeviceCards(MicrophoneDevices, microphoneCardPage, MicrophoneDevicePager, MicrophoneDevicePageText, MicrophonePreviousPage, MicrophoneNextPage);
        }
        finally { arrangingDeviceCards = false; }
    }

    private void LayoutDeviceCards(ListBox list, DeviceCardPage page, Grid pager, TextBlock label, Button previous, Button next)
    {
        if (!list.IsVisible || list.ActualWidth < 40 || list.ActualHeight < 40 ||
            list.ItemContainerGenerator.Status != GeneratorStatus.ContainersGenerated)
            return;
        var columns = Math.Max(1, (int)(list.ActualWidth / 190));
        var width = Math.Floor(list.ActualWidth / columns) - 8;
        var availableHeight = list.ActualHeight + (pager.Visibility == Visibility.Visible ? pager.ActualHeight : 0);
        var height = 68d;
        foreach (var item in list.Items)
        {
            var name = item switch
            {
                RenderDevice device => device.Name,
                MicrophoneDevice device => device.Name,
                _ => throw new InvalidOperationException("Unexpected audio device card.")
            };
            var text = new FormattedText(name, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                12, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip) { MaxTextWidth = Math.Max(10, width - 36) };
            height = Math.Max(height, Math.Ceiling(text.Height) + 24);
        }
        height = Math.Min(height, Math.Max(32, availableHeight - 40));
        var rowsWithoutPager = Math.Max(1, (int)(availableHeight / (height + 8)));
        var needsPages = list.Items.Count > columns * rowsWithoutPager;
        var rows = Math.Max(1, (int)((availableHeight - (needsPages ? 32 : 0)) / (height + 8)));
        page.Size = columns * rows;
        var pages = Math.Max(1, (int)Math.Ceiling((double)list.Items.Count / page.Size));
        if (page.RevealSelection && list.SelectedIndex >= 0)
            page.Index = list.SelectedIndex / page.Size;
        page.RevealSelection = false;
        page.Index = Math.Clamp(page.Index, 0, pages - 1);
        pager.Visibility = pages > 1 ? Visibility.Visible : Visibility.Collapsed;
        label.Text = $"Page {page.Index + 1}/{pages}" +
            (list.SelectedIndex >= 0 ? $" | selected page {list.SelectedIndex / page.Size + 1}" : "");
        previous.IsEnabled = list.IsEnabled && page.Index > 0;
        next.IsEnabled = list.IsEnabled && page.Index + 1 < pages;
        for (var index = 0; index < list.Items.Count; index++)
        {
            if (list.ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem card)
                continue;
            if (Math.Abs(card.Width - width) > 0.1) card.Width = width;
            if (Math.Abs(card.Height - height) > 0.1) card.Height = height;
            card.Visibility = index / page.Size == page.Index ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void OutputDevicePage_Click(object sender, RoutedEventArgs args) =>
        ChangeDevicePage(OutputDevices, outputCardPage, sender);

    private void MicrophoneDevicePage_Click(object sender, RoutedEventArgs args) =>
        ChangeDevicePage(MicrophoneDevices, microphoneCardPage, sender);

    private void ChangeDevicePage(ListBox list, DeviceCardPage page, object sender)
    {
        if (!list.IsEnabled || audioBusy || audioStopping || transcriber.IsRunning)
        {
            AudioStatus.Text = "Stop transcription before browsing or changing audio devices.";
            return;
        }
        page.Index += sender is Button { Tag: "-1" } ? -1 : 1;
        page.RevealSelection = false;
        UpdateDeviceCards();
    }
}
