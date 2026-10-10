using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PhantomFudge.Core;
using PhantomFudge.Core.Answers;

namespace PhantomFudge.App;

public partial class MainWindow
{
    private bool clarifyFirst;
    private QuestionSnapshot? clarificationOriginal;
    private IReadOnlyList<ClarificationTurn> clarificationHistory = Array.Empty<ClarificationTurn>();
    private Guid? clarificationRequestId;
    private bool clarificationAnswered;
    private string clarificationReplyText = "";
    private bool renderingClarificationReply;

    private bool AwaitingClarificationReply => clarifyFirst && clarificationOriginal is not null &&
        clarificationHistory.Count > 0 && !clarificationAnswered;
    private bool ClarificationLocksQuestion => clarifyFirst && clarificationOriginal is not null && !clarificationAnswered;

    private void ClarifyFirst_Click(object sender, RoutedEventArgs args)
    {
        if (AnswerBusy || fileBusy || !protectionReady)
        {
            ClarifyFirstToggle.IsChecked = clarifyFirst;
            QuestionStatus.Text = "Finish the current request/file operation before changing clarification mode.";
            return;
        }
        clarifyFirst = ClarifyFirstToggle.IsChecked == true;
        ResetClarification();
        QuestionStatus.Text = clarifyFirst
            ? "Clarify first is on. Ask adds an interpretation instruction; replies include only this question's clarification history."
            : "Clarify first is off. Normal Ask sends the question without clarification history. Original draft retained.";
        UpdateQuestionInteraction();
    }

    private void BeginQuestionClarification(QuestionSnapshot snapshot, string? previousAnswer)
    {
        ResetClarification();
        clarificationOriginal = snapshot;
        clarificationPreviousAnswer = previousAnswer;
        usePreviousAnswer = previousAnswer is not null;
        StartAnswer(snapshot, new AnswerClarification(false, Array.Empty<ClarificationTurn>()), previousAnswer);
    }

    private void ResetClarification()
    {
        clarificationOriginal = null;
        clarificationPreviousAnswer = null;
        clarificationHistory = Array.Empty<ClarificationTurn>();
        clarificationRequestId = null;
        clarificationAnswered = false;
        ClarificationReplyEditor.Clear();
        UpdateQuestionInteraction();
    }

    private void UpdateClarificationControls()
    {
        if (!questionViewReady || !answerViewReady)
            return;
        var waiting = AwaitingClarificationReply;
        ClarifyFirstToggle.IsEnabled = protectionReady && !fileBusy && !AnswerBusy;
        ClarificationReplyPanel.Visibility = waiting ? Visibility.Visible : Visibility.Collapsed;
        QuestionEditor.Visibility = waiting ? Visibility.Collapsed : Visibility.Visible;
        var canReply = waiting && protectionReady && !fileBusy && !AnswerBusy && ProviderCanAnswer;
        var reply = ClarificationReplyEditor.Text;
        var confirm = IsConfirmationReply(reply);
        ClarificationSendReply.IsEnabled = canReply && !string.IsNullOrWhiteSpace(reply) &&
            (confirm || clarificationHistory.Count < AnswerClarification.MaximumHistoryTurns);
        ClarificationConfirm.IsEnabled = canReply && (string.IsNullOrWhiteSpace(reply) || confirm);
        ClarificationReplyEditor.IsReadOnly = !questionEditing || !waiting || AnswerBusy;
        ClarificationReplyEditor.Focusable = questionEditing && waiting && !AnswerBusy;
        InputMethod.SetIsInputMethodEnabled(ClarificationReplyEditor, ClarificationReplyEditor.Focusable);
        ClarificationReplyHint.Text = clarificationHistory.Count >= AnswerClarification.MaximumHistoryTurns
            ? "Four interpretations reached. Type Yes to answer, or Clear question to start over."
            : "Review the interpretation below. Type Yes, or a correction, then Send reply.";
        ClarificationContextText.Visibility = clarifyFirst ? Visibility.Visible : Visibility.Collapsed;
        ClarificationContextText.Text = clarifyFirst
            ? "Extra instruction: infer and restate my intended question before answering. No logs, devices or unselected transcript.\n\n" +
                string.Join("\n\n", clarificationHistory.Select((turn, index) =>
                    $"Turn {index + 1} - user:\n{turn.UserText}\nInterpretation:\n{turn.Interpretation}"))
            : "";
        UpdateWorkspaceLayout();
    }

    private static bool IsConfirmationReply(string text) =>
        string.Equals(text.Trim(), "yes", StringComparison.OrdinalIgnoreCase);

    private void ClarificationReply_TextChanged(object sender, TextChangedEventArgs args)
    {
        if (renderingClarificationReply)
            return;
        if (ClarificationReplyEditor.Text.Length > QuestionDraft.MaximumCharacters)
        {
            renderingClarificationReply = true;
            try { ClarificationReplyEditor.Text = clarificationReplyText; }
            finally { renderingClarificationReply = false; }
            QuestionStatus.Text = "Reply exceeds 8192 characters. Previous reply retained; input was not truncated.";
        }
        else
            clarificationReplyText = ClarificationReplyEditor.Text;
        UpdateAnswerControls();
    }

    private void ClarificationReply_Pasting(object sender, DataObjectPastingEventArgs args)
    {
        if (!questionEditing || AnswerBusy || args.SourceDataObject.GetData(DataFormats.UnicodeText) is not string text ||
            (long)ClarificationReplyEditor.Text.Length - ClarificationReplyEditor.SelectionLength + text.Length > QuestionDraft.MaximumCharacters)
        {
            args.CancelCommand();
            QuestionStatus.Text = "Reply paste rejected. Use Type reply and at most 8192 characters.";
        }
    }

    private void ClarificationSendReply_Click(object sender, RoutedEventArgs args) =>
        SubmitClarificationReply(ClarificationReplyEditor.Text);

    private void ClarificationConfirm_Click(object sender, RoutedEventArgs args)
    {
        if (!string.IsNullOrWhiteSpace(ClarificationReplyEditor.Text) && !IsConfirmationReply(ClarificationReplyEditor.Text))
        {
            QuestionStatus.Text = "Send your written correction first; it was not discarded or treated as confirmation.";
            return;
        }
        SubmitClarificationReply("Yes");
    }

    private void SubmitClarificationReply(string reply)
    {
        if (!AwaitingClarificationReply || AnswerBusy || !ProviderCanAnswer || !protectionReady || fileBusy)
        {
            QuestionStatus.Text = "Wait for a complete interpretation and provider readiness before sending a reply.";
            return;
        }
        try
        {
            var metadata = new AnswerClarification(IsConfirmationReply(reply), clarificationHistory);
            metadata.ValidateFor(reply);
            var draft = new QuestionDraft();
            draft.EditText(reply);
            var snapshot = draft.Freeze();
            lastQuestionSnapshot = snapshot;
            questionSnapshotCount++;
            QuestionSnapshotText.Text = snapshot.ExactText;
            QuestionSnapshotPanel.Visibility = Visibility.Visible;
            StartAnswer(snapshot, metadata, clarificationPreviousAnswer);
        }
        catch (AnswerProviderException error)
        {
            QuestionStatus.Text = error.Message;
        }
        catch (Exception error) when (IsQuestionInputFailure(error))
        {
            QuestionStatus.Text = "Reply rejected. Use nonempty text within the question/history limits; nothing was sent.";
        }
    }

    private static void ValidateClarificationResult(AnswerRun run)
    {
        if (run.Request.Clarification is not { Confirmed: false } metadata)
            return;
        var history = metadata.History.Append(new ClarificationTurn(run.Request.ExactText, run.Text));
        // Keep enough room for an explicit confirmation; never silently trim an interpretation.
        new AnswerClarification(true, history).ValidateFor("Yes");
    }

    private bool AcceptClarificationResult(AnswerRun run)
    {
        if (run.Request.Clarification is not { } metadata || !ReferenceEquals(answerRun, run) ||
            clarificationRequestId != run.Request.RequestId || !clarifyFirst || clarificationOriginal is null)
            return false;
        if (metadata.Confirmed)
        {
            clarificationAnswered = true;
            QuestionStatus.Text = "Answered your confirmed question. The original draft is retained; no further request was sent.";
        }
        else
        {
            clarificationHistory = metadata.History.Append(new ClarificationTurn(run.Request.ExactText, run.Text)).ToArray();
            QuestionStatus.Text = "Interpretation ready, not a solution yet. Type Yes or a correction below; sending is always explicit.";
        }
        ClarificationReplyEditor.Clear();
        UpdateQuestionInteraction();
        return true;
    }
}
