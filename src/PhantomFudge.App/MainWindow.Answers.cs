using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using PhantomFudge.Core;
using PhantomFudge.Core.Answers;

namespace PhantomFudge.App;

public partial class MainWindow
{
    private AnswerSettings configuredAnswerSettings = new(AnswerProviderMode.SnapshotOnly);
    private bool useAnswerProvider;
    private bool answerViewReady;
    private bool answerSettingsValid;
    private AnswerRun? answerRun;
    private CancellationTokenSource? answerCancellation;
    private Task? answerTask;
    private IAnswerProvider? diagnosticAnswerProvider;
    private readonly System.Windows.Threading.DispatcherTimer answerRenderTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(40)
    };
    private bool AnswerBusy => answerTask is { IsCompleted: false };
    private bool HasAnswerContent => answerRun is not null;

    private void InitializeAnswers()
    {
        answerViewReady = true;
        answerRenderTimer.Tick += (_, _) => RenderAnswer();
        LoadAnswerSettings();
    }

    private void LoadAnswerSettings(Func<AnswerSettings>? readSettings = null)
    {
        if (AnswerBusy)
        {
            QuestionStatus.Text = "Cancel the current answer and wait for cleanup before changing providers.";
            return;
        }
        try
        {
            configuredAnswerSettings = (readSettings ?? AnswerConfiguration.Load)();
            answerSettingsValid = configuredAnswerSettings.Mode != AnswerProviderMode.SnapshotOnly;
            useAnswerProvider = answerSettingsValid;
            QuestionStatus.Text = "Answer setup loaded. Requests start only when you press Ask.";
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            configuredAnswerSettings = new(AnswerProviderMode.SnapshotOnly);
            answerSettingsValid = false;
            useAnswerProvider = false;
            QuestionStatus.Text = "Answer setup is invalid. Fix answers.json using Initialize-Answers.ps1, then Reload setup.";
        }
        UpdateAnswerControls();
    }

    private string ProviderLabel => configuredAnswerSettings.Mode switch
    {
        AnswerProviderMode.Copilot => $"Copilot CLI / GitHub-hosted model ({configuredAnswerSettings.Model ?? "auto"})",
        AnswerProviderMode.LocalModel => $"Copilot CLI / local model ({configuredAnswerSettings.Model})",
        AnswerProviderMode.Agency => $"Agency / existing model service ({configuredAnswerSettings.Model ?? "default"})",
        _ => "Snapshot only"
    };

    private void AnswerSnapshotMode_Click(object sender, RoutedEventArgs args)
    {
        if (AnswerBusy)
        {
            QuestionStatus.Text = "Cancel and wait for the current answer before changing providers.";
            return;
        }
        useAnswerProvider = false;
        UpdateAnswerControls();
    }

    private void AnswerConfiguredMode_Click(object sender, RoutedEventArgs args)
    {
        if (AnswerBusy || !answerSettingsValid)
        {
            QuestionStatus.Text = "Run Initialize-Answers.ps1 first, then Reload setup. Finish any current answer.";
            return;
        }
        useAnswerProvider = true;
        UpdateAnswerControls();
    }

    private void AnswerReloadSettings_Click(object sender, RoutedEventArgs args) => LoadAnswerSettings();

    private void UpdateAnswerControls()
    {
        if (!answerViewReady)
            return;
        AnswerSnapshotMode.IsEnabled = !AnswerBusy;
        AnswerConfiguredMode.IsEnabled = !AnswerBusy && answerSettingsValid;
        AnswerReloadSettings.IsEnabled = !AnswerBusy;
        AnswerSnapshotMode.BorderThickness = new Thickness(useAnswerProvider ? 1 : 2);
        AnswerConfiguredMode.BorderThickness = new Thickness(useAnswerProvider ? 2 : 1);
        AnswerConfiguredMode.Content = answerSettingsValid ? ProviderLabel : "Use configured provider";
        AnswerProviderNotice.Text = !useAnswerProvider
            ? "Snapshot only: nothing is sent. Configure an answer provider to enable responses."
            : configuredAnswerSettings.Mode == AnswerProviderMode.Copilot
                ? "Ask sends only the displayed question to GitHub Copilot's cloud models. CLI runs locally; inference is not offline."
                : configuredAnswerSettings.Mode == AnswerProviderMode.Agency
                    ? "Ask runs installed Agency in the background with the selected question only. Model service is remote; organization-data tools are disabled."
                : $"Ask uses the local model at {configuredAnswerSettings.LocalEndpoint}. CLI offline mode; no GitHub model fallback.";
        AnswerCancelButton.IsEnabled = AnswerBusy && answerRun?.State == AnswerRunState.Streaming;
        AnswerCopyButton.IsEnabled = answerRun?.Text.Length > 0;
        QuestionAskButton.IsEnabled = protectionReady && !fileBusy && !AnswerBusy &&
            !string.IsNullOrWhiteSpace(questionDraft.Text);
        QuestionAskButton.Content = useAnswerProvider ? "Ask provider" : "Freeze locally";
    }

    private void StartAnswer(QuestionSnapshot snapshot)
    {
        if (!useAnswerProvider)
        {
            ClearAnswer();
            QuestionSnapshotLabel.Text = "Frozen snapshot - local only, not sent";
            return;
        }
        var run = new AnswerRun(snapshot, ProviderLabel);
        answerRun = run;
        AnswerPanel.Visibility = Visibility.Visible;
        QuestionSnapshotLabel.Text = "Exact submitted question - independent of later draft edits";
        answerCancellation?.Dispose();
        answerCancellation = new CancellationTokenSource();
        answerRenderTimer.Start();
        answerTask = RunAnswerAsync(run, diagnosticAnswerProvider, answerCancellation.Token);
        UpdateAnswerControls();
        RenderAnswer();
        QuestionStatus.Text = "Request started for the frozen question. Transcription is unchanged.";
        QuestionScroll.ScrollToEnd();
    }

    private async Task RunAnswerAsync(AnswerRun run, IAnswerProvider? provider, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(120));
        try
        {
            provider ??= configuredAnswerSettings.Mode == AnswerProviderMode.Agency
                ? new AgencyCliAnswerProvider(configuredAnswerSettings)
                : new CopilotCliAnswerProvider(configuredAnswerSettings, AnswerConfiguration.CopilotProfilePath);
            await foreach (var value in provider.StreamAsync(run.Request, deadline.Token))
            {
                deadline.Token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(answerRun, run) || run.State != AnswerRunState.Streaming)
                {
                    deadline.Cancel();
                    break;
                }
                run.Apply(value);
            }
            deadline.Token.ThrowIfCancellationRequested();
            run.Complete();
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            if (cancellationToken.IsCancellationRequested)
                run.Cancel();
            else
                run.Fail("The answer exceeded 120 seconds. Visible text is incomplete; no retry was sent.");
        }
        catch (AnswerProviderException error)
        {
            run.Fail(error.Message);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            InvalidOperationException or System.ComponentModel.Win32Exception or JsonException or ArgumentException)
        {
            run.Fail("The answer provider failed. Check setup/login and model availability; visible text is incomplete.");
        }
        finally
        {
            if (ReferenceEquals(answerRun, run))
            {
                answerRenderTimer.Stop();
                RenderAnswer();
                // The task is still finishing; re-evaluate the buttons on the next dispatcher turn.
            }
            _ = Dispatcher.BeginInvoke(UpdateAnswerControls);
        }
    }

    private void RenderAnswer()
    {
        if (!answerViewReady || answerRun is not { } run)
            return;
        AnswerRunLabel.Text = run.ProviderLabel;
        if (AnswerText.Text != run.Text)
        {
            AnswerText.Text = run.Text;
            AnswerText.ScrollToEnd();
        }
        AnswerStatus.Text = run.Status;
        UpdateAnswerControls();
    }

    private void CancelAnswer()
    {
        answerRun?.Cancel();
        answerCancellation?.Cancel();
        RenderAnswer();
    }

    private async Task StopAnswerAsync()
    {
        CancelAnswer();
        if (answerTask is not null)
            await answerTask;
        answerRenderTimer.Stop();
        answerCancellation?.Dispose();
        answerCancellation = null;
    }

    private void AnswerCancel_Click(object sender, RoutedEventArgs args) => CancelAnswer();

    private void AnswerCopy_Click(object sender, RoutedEventArgs args)
    {
        if (answerRun is not { Text.Length: > 0 } run)
        {
            AnswerStatus.Text = "There is no answer to copy.";
            return;
        }
        try
        {
            var prefix = run.State == AnswerRunState.Completed ? "" : "[Incomplete answer]\n";
            Clipboard.SetText(prefix + run.Text);
            AnswerStatus.Text = "The visible answer was copied explicitly to the Windows clipboard.";
        }
        catch (ExternalException)
        {
            AnswerStatus.Text = "Clipboard is busy; the answer remains in memory.";
        }
    }

    private void ClearAnswer()
    {
        CancelAnswer();
        answerRun = null;
        AnswerText.Clear();
        AnswerPanel.Visibility = Visibility.Collapsed;
        answerRenderTimer.Stop();
    }
}
