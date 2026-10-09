using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Input;

namespace PhantomFudge.App;

public partial class MainWindow
{
    private bool coverActive;
    private IInputElement? coveredKeyboardFocus;
    private bool switchingCover;

    private void InitializeCover()
    {
        CoverHost.Child = null;
        CoverHost.Visibility = Visibility.Collapsed;
    }

    private void CoverToggle_Click(object sender, RoutedEventArgs args) => SetCover(!coverActive);

    private void SetCover(bool covered)
    {
        if (covered == coverActive || switchingCover)
            return;
        if (covered && (fileBusy || pending != PendingAction.None || !protectionReady))
        {
            ReportStatus("Finish the pending file or confirmation operation and restore protection before covering.");
            return;
        }
        switchingCover = true;
        try
        {
            if (covered)
            {
                coveredKeyboardFocus = Keyboard.FocusedElement is DependencyObject focused &&
                    WorkspaceRoot.IsAncestorOf(focused) ? Keyboard.FocusedElement : null;
                Keyboard.ClearFocus();
                FocusManager.SetFocusedElement(this, null);
                coverActive = true;
                WorkspaceRoot.IsEnabled = false;
                // Detach, rather than paint over, the workspace. Its state and async work remain alive.
                WorkspaceHost.Child = null;
                WorkspaceHost.Visibility = Visibility.Collapsed;
                CoverHost.Child = CoverView;
                CoverHost.Visibility = Visibility.Visible;
            }
            else
            {
                CoverHost.Child = null;
                CoverHost.Visibility = Visibility.Collapsed;
                WorkspaceHost.Child = WorkspaceRoot;
                WorkspaceHost.Visibility = Visibility.Visible;
                WorkspaceRoot.IsEnabled = true;
                coverActive = false;
            }
            CoverToggleButton.Content = covered ? "Uncover" : "Cover";
            AutomationProperties.SetName(CoverToggleButton, covered
                ? "Uncover"
                : "Show notepad cover; transcription and answers keep running");
            if (UIElementAutomationPeer.FromElement(this) is { } peer)
            {
                peer.ResetChildrenCache();
                peer.InvalidatePeer();
            }
        }
        finally { switchingCover = false; }
        if (!covered)
        {
            UpdateWorkspaceLayout();
            QueueDeviceCards();
            RenderAnswer();
            if (!diagnosticRendering && coveredKeyboardFocus is UIElement { IsVisible: true, IsEnabled: true, Focusable: true } previous)
                previous.Focus();
            coveredKeyboardFocus = null;
        }
    }
}
