using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using PhantomFudge.Core;

namespace PhantomFudge.App;

public partial class MainWindow
{
    private bool workspacePositioned;

    public void ClampWindowToWorkArea(bool recenter = false)
    {
        var area = CurrentMonitorWorkArea();
        var restoring = WindowState == WindowState.Minimized && !RestoreBounds.IsEmpty;
        var requestedWidth = restoring ? RestoreBounds.Width : Width;
        var requestedHeight = restoring ? RestoreBounds.Height : Height;
        var requestedLeft = restoring ? RestoreBounds.Left : Left;
        var requestedTop = restoring ? RestoreBounds.Top : Top;
        var bounds = WorkspaceLayout.FitToWorkArea(
            area.Left, area.Top, area.Width, area.Height, requestedWidth, requestedHeight,
            !workspacePositioned || recenter ? null : requestedLeft,
            !workspacePositioned || recenter ? null : requestedTop);
        MinWidth = bounds.MinimumWidth;
        MinHeight = bounds.MinimumHeight;
        Width = bounds.Width;
        Height = bounds.Height;
        Left = bounds.Left;
        Top = bounds.Top;
        workspacePositioned = true;
        UpdateWorkspaceLayout();
    }

    public void ShowCaptureProtectionStatus()
    {
        ProtectionStatusText.Text = "Capture protection requested";
    }

    private Rect CurrentMonitorWorkArea()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == 0)
            return SystemParameters.WorkArea;
        var info = new LayoutMonitorInfo { Size = Marshal.SizeOf<LayoutMonitorInfo>() };
        var monitor = LayoutMonitorFromWindow(handle, 2);
        if (monitor == 0 || !LayoutGetMonitorInfo(monitor, ref info))
            return SystemParameters.WorkArea;
        var fromDevice = HwndSource.FromHwnd(handle)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = fromDevice.Transform(new Point(info.Work.Left, info.Work.Top));
        var bottomRight = fromDevice.Transform(new Point(info.Work.Right, info.Work.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    private void Workspace_SizeChanged(object sender, SizeChangedEventArgs args) => UpdateWorkspaceLayout();

    private void UpdateWorkspaceLayout()
    {
        if (!questionViewReady || TranscriptArea is null || coverActive || switchingCover || switchingKeyboardMode)
            return;
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var columns = WorkspaceLayout.UseReadingColumns(width);
        var shortWorkspace = ActualHeight < 780 || TranscriptArea.ActualHeight < 500;
        // A compact modal view borrows reading space without invoking the Q/A close/cancel path.
        var questionOpen = questionExpanded && !(KeyboardSelecting && !columns && shortWorkspace);
        QuestionPanel.Visibility = questionOpen ? Visibility.Visible : Visibility.Collapsed;
        var sourceToolsOpen = QuestionSourcePanel.Visibility == Visibility.Visible ||
            ProviderSetupPanel.Visibility == Visibility.Visible;
        var toolsReplaceTranscript = questionOpen && !columns && shortWorkspace &&
            (sourceToolsOpen || AwaitingClarificationReply || KeyboardControlActive && !KeyboardSelecting ||
                chosenAnswerFontSize >= 14 && AnswerPanel.Visibility == Visibility.Visible);
        TranscriptPane.Visibility = toolsReplaceTranscript || AudioPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        TranscriptColumn.Width = new GridLength(1, GridUnitType.Star);
        QuestionColumn.Width = columns && questionOpen ? new GridLength(1.15, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumn(QuestionPanel, columns ? 1 : 0);
        Grid.SetRow(QuestionPanel, columns ? 0 : 1);
        QuestionPanel.Margin = columns ? new Thickness(10, 0, 0, 0) : new Thickness(0, 5, 0, 0);
        TranscriptReadingRow.Height = columns || !questionOpen
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(toolsReplaceTranscript ? 0 : Math.Min(240,
                Math.Max(0, TranscriptArea.ActualHeight * (shortWorkspace ? 0.20 : 0.25))));
        QuestionReadingRow.Height = !columns && questionOpen ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        PartialText.MaxHeight = columns || !questionOpen ? 82 : Math.Max(20, TranscriptArea.ActualHeight * 0.06);
        PartialText.FontSize = !columns && questionOpen && shortWorkspace ? 10.5 : 13.5;
        PartialText.Padding = !columns && questionOpen && shortWorkspace ? new Thickness(6, 0, 6, 0) : new Thickness(6, 3, 6, 3);
        var rowPadding = !columns && questionOpen && shortWorkspace ? new Thickness(6, 0, 6, 0) : new Thickness(6, 3, 6, 3);
        if (Resources["TranscriptRowPadding"] is not Thickness currentRowPadding || currentRowPadding != rowPadding)
            Resources["TranscriptRowPadding"] = rowPadding;
        InputModeLabel.Visibility = width >= 620 ? Visibility.Visible : Visibility.Collapsed;
        ThemeName.Visibility = width >= 620 ? Visibility.Visible : Visibility.Collapsed;
        BackgroundOpacity.Width = width >= 620 ? 140 : 90;
        ApplyAnswerFontSize(!columns && shortWorkspace ? 9 : 10);
        AnswerText.Padding = !columns && shortWorkspace ? new Thickness(4) : new Thickness(6);
        UpdateAnswerActionLabels();

        var hideClarificationSnapshot = AwaitingClarificationReply && shortWorkspace;
        QuestionSnapshotPanel.Visibility = lastQuestionSnapshot is not null && !hideClarificationSnapshot
            ? Visibility.Visible : Visibility.Collapsed;
        var snapshotVisible = QuestionSnapshotPanel.Visibility == Visibility.Visible;
        var pairedQuestion = shortWorkspace && snapshotVisible;
        QuestionSnapshotColumn.Width = pairedQuestion ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumn(QuestionSnapshotPanel, pairedQuestion ? 1 : 0);
        Grid.SetRow(QuestionSnapshotPanel, pairedQuestion ? 0 : 1);
        QuestionSnapshotPanel.Margin = pairedQuestion ? new Thickness(6, 0, 0, 4) : new Thickness(0, 0, 0, 4);
        QuestionSnapshotLabel.TextWrapping = pairedQuestion ? TextWrapping.NoWrap : TextWrapping.Wrap;
        QuestionSnapshotLabel.TextTrimming = pairedQuestion ? TextTrimming.CharacterEllipsis : TextTrimming.None;
        QuestionSnapshotRow.Height = snapshotVisible && !pairedQuestion
            ? new GridLength(0.9, GridUnitType.Star) : new GridLength(0);
        QuestionDraftRow.Height = new GridLength(AwaitingClarificationReply ? 2.5 : pairedQuestion ? 2 : 1.4, GridUnitType.Star);
    }

    private void ReadingContent_VisibilityChanged(object sender, DependencyPropertyChangedEventArgs args) =>
        UpdateWorkspaceLayout();

    private void EnsureQuestionReadingVisible()
    {
        SetQuestionSourceExpanded(false);
        ProviderSetupPanel.Visibility = Visibility.Collapsed;
        ProviderSetupToggle.Content = "Setup / details";
        UpdateWorkspaceLayout();
    }

    private void QuestionSourceToggle_Click(object sender, RoutedEventArgs args) =>
        SetQuestionSourceExpanded(QuestionSourcePanel.Visibility != Visibility.Visible);

    private void SetQuestionSourceExpanded(bool expanded)
    {
        QuestionSourcePanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        QuestionSourceToggle.Content = expanded ? "Close sources" : "Choose source";
        if (expanded)
        {
            ProviderSetupPanel.Visibility = Visibility.Collapsed;
            ProviderSetupToggle.Content = "Setup / details";
        }
        UpdateWorkspaceLayout();
    }

    private void ProviderSetupToggle_Click(object sender, RoutedEventArgs args)
    {
        var expanded = ProviderSetupPanel.Visibility != Visibility.Visible;
        SetQuestionSourceExpanded(false);
        ProviderSetupPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        ProviderSetupToggle.Content = expanded ? "Close setup" : "Setup / details";
        UpdateWorkspaceLayout();
    }

    private void ProtectionDetails_Click(object sender, RoutedEventArgs args)
    {
        var expanded = ProtectionDetailsPanel.Visibility != Visibility.Visible;
        ProtectionDetailsPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        ProtectionDetailsButton.Content = expanded ? "Close details" : "Details";
    }

    private bool IsReadingControlReachable(FrameworkElement element, double minimumHeight = 20)
    {
        if (element is TextBox text)
            minimumHeight = Math.Max(minimumHeight, text.FontSize * 1.2 + text.Padding.Top + text.Padding.Bottom +
                text.BorderThickness.Top + text.BorderThickness.Bottom);
        if (element is ListBox list && (list == TranscriptText || list == PartialText) &&
            Resources["TranscriptRowPadding"] is Thickness rowPadding)
            minimumHeight = Math.Max(minimumHeight, list.FontSize * 1.2 + list.Padding.Top + list.Padding.Bottom +
                rowPadding.Top + rowPadding.Bottom + 2);
        if (!element.IsVisible || element.ActualWidth < 24 || element.ActualHeight < minimumHeight)
            return false;
        var origin = element.TranslatePoint(new Point(), this);
        var bottom = origin.Y + element.ActualHeight;
        var readingBottom = NotesToggleButton.TranslatePoint(new Point(), this).Y;
        return origin.X >= 0 && origin.Y >= 0 && origin.X + element.ActualWidth <= ActualWidth + 1 &&
            bottom <= readingBottom + 1;
    }

    private bool HasOuterReadingScroll(DependencyObject element)
    {
        for (var parent = VisualTreeHelper.GetParent(element); parent is not null && parent != this;
            parent = VisualTreeHelper.GetParent(parent))
            if (parent is ScrollViewer)
                return true;
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LayoutRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct LayoutMonitorInfo
    {
        public int Size;
        public LayoutRect Monitor, Work;
        public uint Flags;
    }

    [DllImport("user32.dll", EntryPoint = "MonitorFromWindow")]
    private static extern nint LayoutMonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LayoutGetMonitorInfo(nint monitor, ref LayoutMonitorInfo info);
}
