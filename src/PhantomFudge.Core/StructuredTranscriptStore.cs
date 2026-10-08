namespace PhantomFudge.Core;

/// <summary>A bounded, caller-serialized transcript model, independent of display/export text.</summary>
public sealed class StructuredTranscriptStore
{
    private readonly int maximumSegments;
    private readonly int maximumCharacters;
    private readonly Dictionary<AudioSource, long> sourceEpochs = [];
    private List<TranscriptSegment> segments = [];
    private long lastLocalSegmentId;
    private int lastProtocolSegmentId;

    public StructuredTranscriptStore(int maximumSegments = 256, int maximumCharacters = 64_000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumSegments, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCharacters, 1);
        this.maximumSegments = maximumSegments;
        this.maximumCharacters = maximumCharacters;
    }

    public Guid SessionId { get; private set; }
    public IReadOnlyList<TranscriptSegment> Segments => Array.AsReadOnly(segments.ToArray());

    public void BeginSession(Guid sessionId)
    {
        TranscriptValidation.Session(sessionId, nameof(sessionId));
        if (sessionId == SessionId || segments.Any(segment => segment.Key.CaptureSessionId == sessionId))
            throw new ArgumentException("Begin capture with a new session ID.", nameof(sessionId));

        segments = segments.Select(segment => segment.IsActive ? ClosePartial(segment) : segment).ToList();
        sourceEpochs.Clear();
        lastProtocolSegmentId = 0;
        SessionId = sessionId;
    }

    /// <returns>False for an obsolete session/epoch; true for an accepted event, including unchanged text.</returns>
    public bool SetPartial(Guid sessionId, AudioSource source, string text, long sourceEpoch = 0)
    {
        ValidateEvent(sessionId, source, text, sourceEpoch);
        if (!IsCurrent(sessionId, source, sourceEpoch))
            return false;

        var updated = PrepareUpdate(source, sourceEpoch);
        var index = FindPartial(updated, source, sourceEpoch);
        TranscriptSegmentKey? changedKey = null;
        var localId = lastLocalSegmentId;
        if (index >= 0)
        {
            var previous = updated[index];
            changedKey = previous.Key;
            if (previous.Text != text)
                updated[index] = new(previous.Key, checked(previous.Revision + 1), text, false, null, null, null, true);
        }
        else if (text.Length > 0)
        {
            changedKey = new(SessionId, source, sourceEpoch, checked(++localId));
            updated.Add(new(changedKey, 1, text, false, null, null, null, true));
        }

        ApplyUpdate(updated, changedKey);
        sourceEpochs[source] = sourceEpoch;
        lastLocalSegmentId = localId;
        return true;
    }

    /// <returns>False for an obsolete session/epoch or duplicate/out-of-order final ID; otherwise true.</returns>
    public bool AddFinal(Guid sessionId, AudioSource source, int segmentId, string text,
        double? startSeconds, double? endSeconds, long sourceEpoch = 0)
    {
        ValidateEvent(sessionId, source, text, sourceEpoch);
        ArgumentOutOfRangeException.ThrowIfLessThan(segmentId, 1);
        TranscriptValidation.Timing(startSeconds, endSeconds);
        if (!IsCurrent(sessionId, source, sourceEpoch) || segmentId <= lastProtocolSegmentId)
            return false;

        var updated = PrepareUpdate(source, sourceEpoch);
        var index = FindPartial(updated, source, sourceEpoch);
        TranscriptSegmentKey? changedKey = null;
        var localId = lastLocalSegmentId;
        if (index >= 0)
        {
            var previous = updated[index];
            changedKey = previous.Key;
            // An empty final settles a selected partial to empty, not to its last guessed text.
            updated[index] = new(previous.Key, checked(previous.Revision + 1), text, true,
                segmentId, startSeconds, endSeconds);
        }
        else if (text.Length > 0)
        {
            changedKey = new(SessionId, source, sourceEpoch, checked(++localId));
            updated.Add(new(changedKey, 1, text, true, segmentId, startSeconds, endSeconds));
        }

        ApplyUpdate(updated, changedKey);
        sourceEpochs[source] = sourceEpoch;
        lastLocalSegmentId = localId;
        // v2 delivers session-global finals in order. A high-water mark bounds deduplication
        // even when a session sends arbitrarily many empty finals.
        lastProtocolSegmentId = segmentId;
        return true;
    }

    public void Clear()
    {
        segments.Clear();
        sourceEpochs.Clear();
        lastProtocolSegmentId = 0;
        SessionId = Guid.Empty;
        // Local identities are never reused, including after clearing and restarting capture.
    }

    private static void ValidateEvent(Guid sessionId, AudioSource source, string text, long sourceEpoch)
    {
        TranscriptValidation.Session(sessionId, nameof(sessionId));
        TranscriptValidation.Source(source);
        TranscriptValidation.Text(text);
        TranscriptValidation.Epoch(sourceEpoch);
    }

    private bool IsCurrent(Guid sessionId, AudioSource source, long sourceEpoch) =>
        sessionId == SessionId && (!sourceEpochs.TryGetValue(source, out var currentEpoch) || sourceEpoch >= currentEpoch);

    private List<TranscriptSegment> PrepareUpdate(AudioSource source, long sourceEpoch) =>
        segments.Select(segment => segment.IsActive && segment.Key.Source == source &&
            segment.Key.SourceEpoch != sourceEpoch ? ClosePartial(segment) : segment).ToList();

    private static TranscriptSegment ClosePartial(TranscriptSegment previous) =>
        new(previous.Key, checked(previous.Revision + 1), previous.Text, false, null,
            previous.StartSeconds, previous.EndSeconds);

    private static int FindPartial(List<TranscriptSegment> values, AudioSource source, long sourceEpoch) =>
        values.FindIndex(segment => segment.IsActive && segment.Key.Source == source && segment.Key.SourceEpoch == sourceEpoch);

    private void ApplyUpdate(List<TranscriptSegment> updated, TranscriptSegmentKey? changedKey)
    {
        var characters = updated.Sum(segment => (long)segment.Text.Length);
        while (updated.Count > maximumSegments || characters > maximumCharacters)
        {
            var index = updated.FindIndex(segment => !segment.IsActive && segment.Key != changedKey);
            if (index < 0)
                throw new InvalidOperationException("The transcript capacity cannot retain this update and the live partials. Finish or clear capture before retrying.");
            characters -= updated[index].Text.Length;
            updated.RemoveAt(index);
        }
        segments = updated;
    }
}
