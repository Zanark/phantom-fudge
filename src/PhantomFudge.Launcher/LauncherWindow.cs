using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PhantomFudge.Launcher;

internal sealed class LauncherWindow : Window
{
    private readonly TextBlock heading;
    private readonly TextBlock description;
    private readonly ProgressBar progress;
    private readonly Button notes;
    private readonly Button close;
    private bool busy = true;

    internal Func<Task>? NotesOnly { get; set; }

    internal LauncherWindow()
    {
        Title = "Phantom Fudge";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(18, 25, 31));
        Foreground = Brushes.White;
        var panel = new StackPanel { Margin = new Thickness(28) };
        heading = new TextBlock { Text = "Opening Phantom Fudge", FontSize = 23, FontWeight = FontWeights.SemiBold };
        description = new TextBlock
        {
            Text = "Preparing local transcription. Audio stays off until you choose Start in the notes window.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            Margin = new Thickness(0, 16, 0, 20)
        };
        progress = new ProgressBar { IsIndeterminate = true, Height = 5, Margin = new Thickness(0, 0, 0, 20) };
        notes = new Button
        {
            Content = "Open notes without transcription",
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 0, 0, 10),
            Visibility = Visibility.Collapsed
        };
        notes.Click += async (_, _) =>
        {
            if (NotesOnly is not null)
                await NotesOnly();
        };
        close = new Button { Content = "Close", Padding = new Thickness(12, 7, 12, 7), IsEnabled = false };
        close.Click += (_, _) => Close();
        panel.Children.Add(heading);
        panel.Children.Add(description);
        panel.Children.Add(progress);
        panel.Children.Add(notes);
        panel.Children.Add(close);
        Content = panel;
        Closing += (_, eventArgs) => eventArgs.Cancel = busy;
    }

    internal void Progress(string message)
    {
        busy = true;
        heading.Text = "Opening Phantom Fudge";
        description.Text = message;
        progress.Visibility = Visibility.Visible;
        notes.Visibility = Visibility.Collapsed;
        close.IsEnabled = false;
    }

    internal void Failure(LauncherCode code, bool allowNotes)
    {
        busy = false;
        heading.Text = "Phantom Fudge needs attention";
        description.Text = LauncherStatus.Message(code);
        progress.Visibility = Visibility.Collapsed;
        notes.Visibility = allowNotes ? Visibility.Visible : Visibility.Collapsed;
        close.IsEnabled = true;
    }

    internal void Complete()
    {
        busy = false;
        Close();
    }
}
