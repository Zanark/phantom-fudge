using System.Text;

namespace PhantomFudge.Core.Answers;

public enum AnswerRunState { Streaming, Completed, Cancelled, Failed }

/// <summary>Caller-serialized state for one exact question and one provider stream.</summary>
public sealed class AnswerRun
{
    public const int MaximumCharacters = 32_768;
    private readonly StringBuilder text = new();
    private long sequence;
    private bool terminalReceived;

    public AnswerRun(QuestionSnapshot snapshot, string providerLabel, AnswerClarification? clarification = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerLabel);
        clarification?.ValidateFor(snapshot.ExactText);
        Snapshot = snapshot;
        ProviderLabel = providerLabel;
        Request = new(Guid.NewGuid(), snapshot.ExactText, clarification);
    }

    public QuestionSnapshot Snapshot { get; }
    public string ProviderLabel { get; }
    public AnswerRequest Request { get; }
    public string Text => text.ToString();
    public AnswerRunState State { get; private set; } = AnswerRunState.Streaming;
    public string Status { get; private set; } = "Waiting for answer text...";

    public bool Apply(AnswerEvent value)
    {
        if (value.RequestId != Request.RequestId || State != AnswerRunState.Streaming)
            return false;
        if (terminalReceived || value.Sequence <= sequence)
            throw new AnswerProviderException("protocol", "The answer stream had an invalid event sequence.");
        sequence = value.Sequence;
        if (value.Kind == AnswerEventKind.Completed)
        {
            if (value.Text.Length != 0)
                throw new AnswerProviderException("protocol", "Completion contained unexpected answer text.");
            terminalReceived = true;
            return true;
        }
        if (value.Kind != AnswerEventKind.TextDelta || value.Text.Length == 0)
            throw new AnswerProviderException("protocol", "The answer provider returned an invalid text event.");
        if ((long)text.Length + value.Text.Length > MaximumCharacters)
            throw new AnswerProviderException("output_limit", "Answer limit reached; the visible text is incomplete.");
        text.Append(value.Text);
        Status = "Receiving answer...";
        return true;
    }

    public void Complete()
    {
        if (State != AnswerRunState.Streaming)
            return;
        if (!terminalReceived || string.IsNullOrWhiteSpace(Text))
            throw new AnswerProviderException("incomplete", "The provider ended without a complete answer.");
        State = AnswerRunState.Completed;
        Status = "Answer complete.";
    }

    public void Cancel()
    {
        if (State != AnswerRunState.Streaming)
            return;
        State = AnswerRunState.Cancelled;
        Status = "Cancelled. Any visible answer is incomplete; nothing will resume automatically.";
    }

    public void Fail(string safeMessage)
    {
        if (State != AnswerRunState.Streaming)
            return;
        State = AnswerRunState.Failed;
        Status = safeMessage;
    }
}
