using System.Windows;
using System.Windows.Media;
using PhantomFudge.Core;

namespace PhantomFudge.App;

public partial class MainWindow
{
    private void UpdateRecordingIndicator(bool recording) =>
        RecordingIndicator.Visibility = recording ? Visibility.Visible : Visibility.Hidden;

    private async Task<object> RunRecordingIndicatorDiagnosticAsync(string directory)
    {
        var previousBusy = audioBusy;
        var previousStatus = AudioStatus.Text;
        var previousTheme = currentTheme;
        var stoppedHidden = RecordingIndicator.Visibility == Visibility.Hidden && !transcriber.IsRunning;
        var startingHidden = false;
        var recordingVisible = true;
        var stoppedAgainHidden = false;
        try
        {
            audioBusy = true;
            UpdateAudioControls();
            startingHidden = RecordingIndicator.Visibility == Visibility.Hidden;
            foreach (var theme in Enum.GetValues<UiTheme>())
            {
                ApplyTheme(theme);
                UpdateRecordingIndicator(true);
                UpdateLayout();
                recordingVisible &= RecordingIndicator.IsVisible &&
                    RecordingIndicator.Fill is SolidColorBrush brush && brush.Color == ThemeBrushes.Color(ThemePalette.For(theme).Error) &&
                    RecordingIndicator.Opacity == 1 && RecordingIndicator.ActualWidth == 12 && RecordingIndicator.ActualHeight == 12;
            }
            ApplyTheme(previousTheme);
            UpdateRecordingIndicator(true);
            AudioStatus.Text = "SYNTHETIC recording indicator example. No audio inputs were opened.";
            await RenderDiagnosticAsync(System.IO.Path.Combine(directory, "recording-active.png"));
            UpdateRecordingIndicator(false);
            stoppedAgainHidden = RecordingIndicator.Visibility == Visibility.Hidden;
        }
        finally
        {
            audioBusy = previousBusy;
            AudioStatus.Text = previousStatus;
            ApplyTheme(previousTheme);
            UpdateAudioControls();
        }
        var passed = stoppedHidden && startingHidden && recordingVisible && stoppedAgainHidden && !transcriber.IsRunning;
        if (!passed)
            throw new InvalidOperationException("Synthetic recording-indicator lifecycle assertion failed.");
        return new { passed, stoppedHidden, startingHidden, recordingVisible, stoppedAgainHidden, recordingStarted = false };
    }
}
