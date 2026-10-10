using System.Windows;
using System.Windows.Automation;
using PhantomFudge.Core.Answers;

namespace PhantomFudge.App;

public partial class MainWindow
{
    private bool usePreviousAnswer;
    private string? clarificationPreviousAnswer;

    private bool CanUseDisplayedAnswer => answerRun is { State: AnswerRunState.Completed } run &&
        run.Request.Clarification is not { Confirmed: false } && !string.IsNullOrWhiteSpace(run.Text);

    private void PreviousAnswerToggle_Click(object sender, RoutedEventArgs args) =>
        SetUsePreviousAnswer(PreviousAnswerToggle.IsChecked == true);

    private void SetUsePreviousAnswer(bool enabled)
    {
        if (AnswerBusy || fileBusy || !protectionReady || ClarificationLocksQuestion)
            QuestionStatus.Text = "Finish or clear the current request/clarification before changing answer context.";
        else if (enabled && (!useAnswerProvider || !CanUseDisplayedAnswer))
            QuestionStatus.Text = "A completed answer and configured provider are needed to use previous-answer context.";
        else
        {
            usePreviousAnswer = enabled;
            QuestionStatus.Text = enabled
                ? "Previous answer is on. Ask will send the displayed answer with your next question; nothing is sent by this toggle."
                : "Previous answer is off. The next question will not include it.";
        }
        UpdateAnswerControls();
    }

    private string? CapturePreviousAnswer()
    {
        // A clarification replaces the answer display; explicit retries retain its original snapshot.
        if (ClarificationLocksQuestion)
            return clarificationPreviousAnswer;
        if (!usePreviousAnswer)
            return null;
        if (!CanUseDisplayedAnswer)
            throw new AnswerProviderException("previous_answer_unavailable",
                "Previous-answer context needs a completed answer. Turn it off or complete an answer first.");
        return answerRun!.Text;
    }

    private void ResetPreviousAnswer()
    {
        usePreviousAnswer = false;
        clarificationPreviousAnswer = null;
        PreviousAnswerToggle.IsChecked = false;
        PreviousAnswerContextText.Clear();
    }

    private void UpdatePreviousAnswerControls()
    {
        if (!useAnswerProvider || !AnswerBusy && !ClarificationLocksQuestion &&
            answerRun?.State != AnswerRunState.Streaming && !CanUseDisplayedAnswer)
            usePreviousAnswer = false;
        PreviousAnswerToggle.IsChecked = usePreviousAnswer;
        PreviousAnswerToggle.IsEnabled = protectionReady && !fileBusy && !AnswerBusy &&
            !ClarificationLocksQuestion && useAnswerProvider && (CanUseDisplayedAnswer || usePreviousAnswer);
        PreviousAnswerToggle.Content = usePreviousAnswer ? "Previous answer: ON" : "Previous answer";
        AutomationProperties.SetHelpText(PreviousAnswerToggle,
            "Optional context for a follow-up. Off by default; includes only the displayed completed answer on Ask. Clear resets it.");
        var submitted = answerRun?.Request.PreviousAnswer;
        PreviousAnswerContextText.Text = submitted ?? "";
        PreviousAnswerContextText.Visibility = submitted is null ? Visibility.Collapsed : Visibility.Visible;
    }
}
