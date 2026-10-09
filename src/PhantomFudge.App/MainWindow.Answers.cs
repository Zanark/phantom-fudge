using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using PhantomFudge.Core;
using PhantomFudge.Core.Answers;
using PhantomFudge.Core.Diagnostics;

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
        if (AnswerBusy || ProviderLifecycleBusy)
        {
            QuestionStatus.Text = "Cancel the current answer and wait for cleanup before changing providers.";
            return;
        }
        var previousSettings = configuredAnswerSettings;
        var previousUseProvider = useAnswerProvider;
        try
        {
            configuredAnswerSettings = (readSettings ?? AnswerConfiguration.Load)();
            answerSettingsValid = configuredAnswerSettings.Mode != AnswerProviderMode.SnapshotOnly;
            useAnswerProvider = answerSettingsValid;
            LocalDiagnostics.Record(DiagnosticEvent.ConfigurationLoaded, provider: configuredAnswerSettings.Mode.ToString());
            QuestionStatus.Text = "Answer setup loaded. Requests start only when you press Ask.";
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            LocalDiagnostics.Record(DiagnosticEvent.ConfigurationFailed, code: error.GetType().Name, provider: "Application");
            configuredAnswerSettings = new(AnswerProviderMode.SnapshotOnly);
            answerSettingsValid = false;
            useAnswerProvider = false;
            QuestionStatus.Text = "Answer setup is invalid. Fix answers.json using Initialize-Answers.ps1, then Reload setup.";
        }
        if (previousSettings != configuredAnswerSettings || previousUseProvider != useAnswerProvider)
            ResetClarification();
        _ = ReconfigureAgencyAsync();
        UpdateAnswerControls();
    }

    private string ProviderLabel => configuredAnswerSettings.Mode switch
    {
        AnswerProviderMode.Copilot => $"Copilot CLI / GitHub-hosted model ({configuredAnswerSettings.Model ?? "auto"})",
        AnswerProviderMode.LocalModel => $"Copilot CLI / local model ({configuredAnswerSettings.Model})",
        AnswerProviderMode.Agency => $"Agency / {configuredAnswerSettings.Model ?? AgencyCliAnswerProvider.DefaultModel} / {AgencyCliAnswerProvider.ReasoningEffort}",
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
        ResetClarification();
        _ = StopAgencyProviderAsync();
        UpdateAnswerControls();
    }

    private void AnswerConfiguredMode_Click(object sender, RoutedEventArgs args)
    {
        if (AnswerBusy || ProviderLifecycleBusy || !answerSettingsValid)
        {
            QuestionStatus.Text = "Run Initialize-Answers.ps1 first, then Reload setup. Finish any current answer.";
            return;
        }
        if (!useAnswerProvider)
            ResetClarification();
        useAnswerProvider = true;
        PrepareAgencyProvider();
        UpdateAnswerControls();
    }

    private void AnswerReloadSettings_Click(object sender, RoutedEventArgs args) => LoadAnswerSettings();

    private void UpdateAnswerControls()
    {
        if (!answerViewReady)
            return;
        AnswerSnapshotMode.IsEnabled = !AnswerBusy;
        AnswerConfiguredMode.IsEnabled = !AnswerBusy && !ProviderLifecycleBusy && answerSettingsValid;
        AnswerReloadSettings.IsEnabled = !AnswerBusy && !ProviderLifecycleBusy;
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
        AnswerCancelButton.IsEnabled = (AnswerBusy && answerRun?.State == AnswerRunState.Streaming) ||
            agencyPreparation is { IsCompleted: false };
        AnswerCopyButton.IsEnabled = answerRun?.Text.Length > 0;
        UpdateAnswerActionLabels();
        QuestionAskButton.IsEnabled = protectionReady && !fileBusy && !AnswerBusy && ProviderCanAnswer &&
            !AwaitingClarificationReply && !KeyboardSelecting && !string.IsNullOrWhiteSpace(questionDraft.Text);
        QuestionAskButton.Content = useAnswerProvider
            ? AwaitingClarificationReply ? "Reply below" : clarifyFirst ? "Interpret question" : "Ask provider"
            : "Freeze locally";
        if (clarifyFirst && useAnswerProvider)
            AnswerProviderNotice.Text += " Clarify first adds a fixed instruction; explicit replies include this clarification history only.";
        UpdateClarificationControls();
        RefreshKeyboardEditorTarget();
    }

    private void StartAnswer(QuestionSnapshot snapshot, AnswerClarification? clarification = null)
    {
        if (!useAnswerProvider)
        {
            LocalDiagnostics.Record(DiagnosticEvent.QuestionFrozen, snapshot.QuestionId,
                count: snapshot.ExactText.Length, code: "snapshot_only", provider: "SnapshotOnly");
            ClearAnswer();
            QuestionSnapshotLabel.Text = "Frozen snapshot - local only, not sent";
            return;
        }
        var run = new AnswerRun(snapshot, ProviderLabel, clarification);
        if (clarification is not null)
            clarificationRequestId = run.Request.RequestId;
        answerStartedAt = Stopwatch.GetTimestamp();
        firstAnswerLogged = false;
        firstAnswerVisibleLogged = false;
        LocalDiagnostics.Record(DiagnosticEvent.QuestionFrozen, run.Request.RequestId, count: snapshot.ExactText.Length,
            code: clarification is null ? null : clarification.Confirmed ? "confirmed_question" : "clarify_question",
            provider: configuredAnswerSettings.Mode.ToString(),
            relatedId: snapshot.SourceRefs.FirstOrDefault()?.Key.CaptureSessionId);
        LocalDiagnostics.RecordContent(DiagnosticContent.SubmittedQuestion, snapshot.ExactText, run.Request.RequestId);
        LocalDiagnostics.Record(DiagnosticEvent.AnswerStarted, run.Request.RequestId, elapsedMs: 0,
            code: diagnosticRendering ? "ui_fixture" : "user_ask", provider: configuredAnswerSettings.Mode.ToString());
        answerRun = run;
        AnswerPanel.Visibility = Visibility.Visible;
        QuestionSnapshotLabel.Text = clarification is null
            ? "Exact submitted question - independent of later draft edits"
            : "Submitted text + clarification instruction" + (clarification.History.Count > 0 ? " + prior clarification turns (Setup / details)" : "");
        answerCancellation?.Dispose();
        answerCancellation = new CancellationTokenSource();
        answerRenderTimer.Start();
        answerTask = RunAnswerAsync(run, diagnosticAnswerProvider, answerCancellation.Token);
        UpdateAnswerControls();
        RenderAnswer();
        QuestionStatus.Text = clarification is { Confirmed: false }
            ? "Requesting an interpretation, not a solution. No reply will be sent automatically."
            : "Request started for the frozen question. Transcription is unchanged.";
        UpdateQuestionInteraction();
        EnsureQuestionReadingVisible();
    }

    private async Task RunAnswerAsync(AnswerRun run, IAnswerProvider? provider, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(120));
        try
        {
            provider ??= configuredAnswerSettings.Mode == AnswerProviderMode.Agency
                ? warmAgencyProvider ?? throw new AnswerProviderException("agency_not_ready", "Agency is not ready. Prepare the configured provider before Ask.")
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
                if (!firstAnswerLogged && value.Kind == AnswerEventKind.TextDelta)
                {
                    firstAnswerLogged = true;
                    LocalDiagnostics.Record(DiagnosticEvent.FirstAnswerText, run.Request.RequestId,
                        elapsedMs: (long)Stopwatch.GetElapsedTime(answerStartedAt).TotalMilliseconds, code: "app_received");
                }
            }
            deadline.Token.ThrowIfCancellationRequested();
            ValidateClarificationResult(run);
            run.Complete();
            if (ReferenceEquals(answerRun, run) && !AcceptClarificationResult(run))
                QuestionStatus.Text = "Answer ready. The draft may be edited independently; no additional input was requested.";
            LocalDiagnostics.Record(DiagnosticEvent.AnswerCompleted, run.Request.RequestId,
                elapsedMs: (long)Stopwatch.GetElapsedTime(answerStartedAt).TotalMilliseconds, count: run.Text.Length);
            LocalDiagnostics.RecordContent(DiagnosticContent.Answer, run.Text, run.Request.RequestId);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            LocalDiagnostics.Record(DiagnosticEvent.AnswerFailed, run.Request.RequestId,
                elapsedMs: (long)Stopwatch.GetElapsedTime(answerStartedAt).TotalMilliseconds,
                code: cancellationToken.IsCancellationRequested ? "cancelled" : "deadline");
            if (cancellationToken.IsCancellationRequested)
                run.Cancel();
            else
                run.Fail("The answer exceeded 120 seconds. Visible text is incomplete; no retry was sent.");
        }
        catch (AnswerProviderException error)
        {
            LocalDiagnostics.Record(DiagnosticEvent.AnswerFailed, run.Request.RequestId,
                elapsedMs: (long)Stopwatch.GetElapsedTime(answerStartedAt).TotalMilliseconds, code: error.Code);
            run.Fail(error.Message);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            InvalidOperationException or System.ComponentModel.Win32Exception or JsonException or ArgumentException)
        {
            LocalDiagnostics.Record(DiagnosticEvent.AnswerFailed, run.Request.RequestId,
                elapsedMs: (long)Stopwatch.GetElapsedTime(answerStartedAt).TotalMilliseconds, code: error.GetType().Name);
            run.Fail("The answer provider failed. Check setup/login and model availability; visible text is incomplete.");
        }
        finally
        {
            if (configuredAnswerSettings.Mode == AnswerProviderMode.Agency && run.State != AnswerRunState.Completed)
                agencyReady = false;
            if (ReferenceEquals(answerRun, run))
            {
                answerRenderTimer.Stop();
                RenderAnswer();
                // The task is still finishing; re-evaluate the buttons on the next dispatcher turn.
            }
            _ = Dispatcher.BeginInvoke(UpdateQuestionInteraction);
        }
    }

    private void RenderAnswer()
    {
        if (!answerViewReady || answerRun is not { } run)
            return;
        AnswerRunLabel.Text = run.ProviderLabel;
        AnswerHeading.Text = run.Request.Clarification is { Confirmed: false } ? "INTERPRETATION" : "ANSWER";
        if (AnswerText.Text != run.Text)
        {
            AnswerText.Text = run.Text;
            AnswerText.ScrollToEnd();
        }
        AnswerStatus.Text = run.Request.Clarification is { Confirmed: false } && run.State == AnswerRunState.Completed
            ? "Interpretation ready. Confirm or correct it; a solution has not been requested yet."
            : run.Status;
        if (!firstAnswerVisibleLogged && run.Text.Length > 0 && AnswerText.IsVisible)
        {
            firstAnswerVisibleLogged = true;
            LocalDiagnostics.Record(DiagnosticEvent.FirstAnswerVisible, run.Request.RequestId,
                elapsedMs: (long)Stopwatch.GetElapsedTime(answerStartedAt).TotalMilliseconds, code: "ui_text_assigned");
        }
        UpdateAnswerControls();
    }

    private void CancelAnswer()
    {
        if (answerRun is { State: AnswerRunState.Streaming } current)
            LocalDiagnostics.Record(DiagnosticEvent.AnswerCancelled, current.Request.RequestId,
                elapsedMs: (long)Stopwatch.GetElapsedTime(answerStartedAt).TotalMilliseconds, code: "ui_cancel");
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
        await StopAgencyProviderAsync();
    }

    private void AnswerCancel_Click(object sender, RoutedEventArgs args)
    {
        var activeAnswer = AnswerBusy;
        CancelAnswer();
        _ = activeAnswer ? ReconfigureAgencyAsync() : StopAgencyProviderAsync();
    }

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
        _ = ReconfigureAgencyAsync();
        answerRun = null;
        AnswerText.Clear();
        AnswerPanel.Visibility = Visibility.Collapsed;
        answerRenderTimer.Stop();
        ResetClarification();
    }
}
