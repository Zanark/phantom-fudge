using System.IO;
using System.Windows;
using PhantomFudge.Core.Answers;
using PhantomFudge.Core.Diagnostics;

namespace PhantomFudge.App;

public partial class MainWindow
{
    private Task StopVisibleActivityAsync() => BeginConcurrentShutdown(StopTranscriptionAsync, StopAnswerAsync);

    private static Task BeginConcurrentShutdown(Func<Task> stopCapture, Func<Task> stopProvider)
    {
        var capture = stopCapture();
        var provider = stopProvider();
        return Task.WhenAll(capture, provider);
    }

    private AgencyCliAnswerProvider? warmAgencyProvider;
    private CancellationTokenSource? agencyPreparationCancellation;
    private Task? agencyPreparation;
    private Task? agencyShutdown;
    private bool agencyReady;
    private bool answerProviderClosing;
    private bool prepareAfterShutdown;

    private bool ProviderLifecycleBusy =>
        agencyPreparation is { IsCompleted: false } || agencyShutdown is { IsCompleted: false };
    private bool ProviderCanAnswer => !useAnswerProvider ||
        configuredAnswerSettings.Mode != AnswerProviderMode.Agency || diagnosticAnswerProvider is not null || agencyReady;

    private void PrepareAgencyProvider()
    {
        if (diagnosticRendering || answerProviderClosing || !useAnswerProvider ||
            configuredAnswerSettings.Mode != AnswerProviderMode.Agency || AnswerBusy || agencyReady)
            return;
        if (agencyShutdown is { IsCompleted: false })
        {
            prepareAfterShutdown = true;
            return;
        }
        if (ProviderLifecycleBusy)
            return;
        agencyPreparationCancellation?.Dispose();
        agencyPreparationCancellation = new CancellationTokenSource();
        agencyPreparation = PrepareAgencyCoreAsync(configuredAnswerSettings, agencyPreparationCancellation.Token);
        UpdateAnswerControls();
    }

    private async Task PrepareAgencyCoreAsync(AnswerSettings settings, CancellationToken cancellationToken)
    {
        agencyReady = false;
        QuestionStatus.Text = "Preparing Agency in the background. No question has been sent; dictation and editing remain available.";
        try
        {
            if (warmAgencyProvider is not null)
                await warmAgencyProvider.DisposeAsync();
            warmAgencyProvider = null;
            cancellationToken.ThrowIfCancellationRequested();
            var provider = new AgencyCliAnswerProvider(settings);
            warmAgencyProvider = provider;
            await provider.WarmUpAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            agencyReady = true;
            QuestionStatus.Text = clarifyFirst
                ? "Agency is ready. Interpret the question first; each correction or confirmation needs an explicit click."
                : "Agency is ready. Ask sends one frozen question and needs no follow-up input.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            QuestionStatus.Text = "Agency preparation stopped. Select the configured provider or Reload setup to prepare it again.";
        }
        catch (AnswerProviderException error)
        {
            LocalDiagnostics.Record(DiagnosticEvent.AnswerFailed, code: error.Code, provider: "Agency");
            QuestionStatus.Text = error.Message;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException)
        {
            LocalDiagnostics.Record(DiagnosticEvent.AnswerFailed, code: error.GetType().Name, provider: "Agency");
            QuestionStatus.Text = "Agency preparation failed. Check the configured installation; no question was submitted.";
        }
        finally
        {
            _ = Dispatcher.BeginInvoke(UpdateAnswerControls);
        }
    }

    private Task StopAgencyProviderAsync()
    {
        agencyPreparationCancellation?.Cancel();
        agencyReady = false;
        if (agencyShutdown is { IsCompleted: false })
            return agencyShutdown;
        prepareAfterShutdown = false;
        return agencyShutdown = StopAgencyCoreAsync();
    }

    private async Task StopAgencyCoreAsync()
    {
        try
        {
            if (agencyPreparation is not null)
                await agencyPreparation;
            var provider = warmAgencyProvider;
            warmAgencyProvider = null;
            if (provider is not null)
                await provider.DisposeAsync();
        }
        catch (Exception error) when (error is AnswerProviderException or IOException or InvalidOperationException)
        {
            LocalDiagnostics.Record(DiagnosticEvent.AnswerFailed, code: "provider_cleanup", provider: "Agency");
            QuestionStatus.Text = "Agency cleanup failed. Close the application before retrying.";
            answerProviderClosing = true;
        }
        finally
        {
            agencyPreparationCancellation?.Dispose();
            agencyPreparationCancellation = null;
            _ = Dispatcher.BeginInvoke(() =>
            {
                UpdateAnswerControls();
                if (prepareAfterShutdown && IsVisible)
                {
                    prepareAfterShutdown = false;
                    PrepareAgencyProvider();
                }
            });
        }
    }

    private async Task ReconfigureAgencyAsync()
    {
        await StopAgencyProviderAsync();
        if (IsVisible || !IsLoaded)
            PrepareAgencyProvider();
    }
}
