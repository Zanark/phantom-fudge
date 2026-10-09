using System.Text;

namespace PhantomFudge.Core.Answers;

public sealed record ClarificationTurn(string UserText, string Interpretation);

public sealed class AnswerClarification
{
    public const int MaximumHistoryTurns = 4;
    public const int MaximumContextCharacters = 32_768;
    private readonly int historyCharacters;

    public AnswerClarification(bool confirmed, IEnumerable<ClarificationTurn> history)
    {
        if (history is null)
            throw new AnswerProviderException("clarification_history", "Clarification history is missing. Start a new question.");
        var copy = new List<ClarificationTurn>();
        foreach (var turn in history)
        {
            if (copy.Count == MaximumHistoryTurns)
                throw new AnswerProviderException("clarification_history_limit", "Clarification history cannot exceed four completed interpretations.");
            if (turn is null)
                throw new AnswerProviderException("clarification_history", "Clarification history contains an invalid turn. Start a new question.");
            ValidateText(turn.UserText);
            ValidateText(turn.Interpretation);
            historyCharacters += turn.UserText.Length + turn.Interpretation.Length;
            ValidateContextLength(historyCharacters);
            copy.Add(new(turn.UserText, turn.Interpretation));
        }
        Confirmed = confirmed;
        History = copy.AsReadOnly();
        ValidateStage();
    }

    public bool Confirmed { get; }
    public IReadOnlyList<ClarificationTurn> History { get; }

    public void ValidateFor(string exactText)
    {
        ValidateText(exactText);
        ValidateStage();
        ValidateContextLength(historyCharacters + exactText.Length);
    }

    private void ValidateStage()
    {
        if (Confirmed && History.Count == 0)
            throw new AnswerProviderException("clarification_stage", "Complete an interpretation before confirming it.");
        if (!Confirmed && History.Count == MaximumHistoryTurns)
            throw new AnswerProviderException("clarification_round_limit", "Four interpretations are complete. Confirm the latest one or start a new question.");
    }

    private static void ValidateContextLength(int characters)
    {
        if (characters > MaximumContextCharacters)
            throw new AnswerProviderException("clarification_context_limit", "The question and clarification history exceed 32768 characters. Start a shorter question.");
    }

    private static void ValidateText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new AnswerProviderException("clarification_text", "Clarification text cannot be empty or blank.");
        if (text.Length > QuestionDraft.MaximumCharacters)
            throw new AnswerProviderException("clarification_text_limit", "Each question, correction and interpretation must be at most 8192 characters.");
        try { _ = new UTF8Encoding(false, true).GetByteCount(text); }
        catch (EncoderFallbackException)
        {
            throw new AnswerProviderException("clarification_unicode", "Clarification text contains invalid Unicode. Edit it before submitting.");
        }
    }
}
