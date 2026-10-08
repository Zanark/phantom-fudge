namespace PhantomFudge.Core;

public sealed record TranscriptSegmentKey
{
    public TranscriptSegmentKey(Guid captureSessionId, AudioSource source, long sourceEpoch, long localSegmentId)
    {
        TranscriptValidation.Session(captureSessionId, nameof(captureSessionId));
        TranscriptValidation.Source(source);
        TranscriptValidation.Epoch(sourceEpoch);
        ArgumentOutOfRangeException.ThrowIfLessThan(localSegmentId, 1);
        CaptureSessionId = captureSessionId;
        Source = source;
        SourceEpoch = sourceEpoch;
        LocalSegmentId = localSegmentId;
    }

    public Guid CaptureSessionId { get; }
    public AudioSource Source { get; }
    public long SourceEpoch { get; }
    public long LocalSegmentId { get; }
}

public sealed record TranscriptSegment
{
    public const int MaximumCharacters = 16_384;
    public const double MaximumSeconds = 2_592_000;

    public TranscriptSegment(TranscriptSegmentKey key, long revision, string text, bool isFinal,
        int? protocolSegmentId, double? startSeconds, double? endSeconds, bool isActive = false)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentOutOfRangeException.ThrowIfLessThan(revision, 1);
        TranscriptValidation.Text(text);
        TranscriptValidation.Timing(startSeconds, endSeconds);
        if (isFinal && (!protocolSegmentId.HasValue || protocolSegmentId < 1))
            throw new ArgumentOutOfRangeException(nameof(protocolSegmentId), "A final requires a positive protocol segment ID.");
        if (!isFinal && protocolSegmentId.HasValue)
            throw new ArgumentException("A partial cannot claim a protocol final ID.", nameof(protocolSegmentId));
        if (isFinal && isActive)
            throw new ArgumentException("A final cannot be an active partial.", nameof(isActive));

        Key = key;
        Revision = revision;
        Text = text;
        IsFinal = isFinal;
        ProtocolSegmentId = protocolSegmentId;
        StartSeconds = startSeconds;
        EndSeconds = endSeconds;
        IsActive = isActive;
    }

    public TranscriptSegmentKey Key { get; }
    public long Revision { get; }
    public string Text { get; }
    public bool IsFinal { get; }
    public int? ProtocolSegmentId { get; }
    public double? StartSeconds { get; }
    public double? EndSeconds { get; }

    // Closing a session/epoch does not turn unfinished recognition into a final.
    public bool IsActive { get; }
    public string DisplayText => $"[Session {Key.CaptureSessionId.ToString("N")[^8..]}] [{Key.Source.DisplayName()}] [{(IsFinal ? "Final" : IsActive ? "Partial" : "Unfinished partial")}] {Text}";

    internal TranscriptSegment Copy() => new(Key with { }, Revision, Text, IsFinal,
        ProtocolSegmentId, StartSeconds, EndSeconds, IsActive);
}

internal static class TranscriptValidation
{
    internal static void Session(Guid sessionId, string parameterName)
    {
        if (sessionId == Guid.Empty)
            throw new ArgumentException("A non-empty capture session ID is required.", parameterName);
    }

    internal static void Source(AudioSource source)
    {
        if (source is not (AudioSource.Computer or AudioSource.Microphone))
            throw new ArgumentOutOfRangeException(nameof(source), "Unknown transcript audio source.");
    }

    internal static void Epoch(long sourceEpoch) => ArgumentOutOfRangeException.ThrowIfNegative(sourceEpoch);

    internal static void Text(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > TranscriptSegment.MaximumCharacters)
            throw new ArgumentException($"Transcript text cannot exceed {TranscriptSegment.MaximumCharacters} characters.", nameof(text));
    }

    internal static void Timing(double? startSeconds, double? endSeconds)
    {
        Seconds(startSeconds, nameof(startSeconds));
        Seconds(endSeconds, nameof(endSeconds));
        if (startSeconds.HasValue && endSeconds.HasValue && endSeconds < startSeconds)
            throw new ArgumentException("The end must not precede the start.", nameof(endSeconds));
    }

    private static void Seconds(double? value, string parameterName)
    {
        if (value.HasValue && (!double.IsFinite(value.Value) || value < 0 || value > TranscriptSegment.MaximumSeconds))
            throw new ArgumentOutOfRangeException(parameterName, $"Timing must be finite and between 0 and {TranscriptSegment.MaximumSeconds} seconds.");
    }
}
