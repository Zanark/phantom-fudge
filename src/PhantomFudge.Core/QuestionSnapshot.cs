namespace PhantomFudge.Core;

public sealed record QuestionSnapshot
{
    internal QuestionSnapshot(Guid questionId, long snapshotRevision, string exactText,
        IReadOnlyList<TranscriptSegment> sourceRefs, bool wasProvisional, bool userEdited, long createdMonotonic)
    {
        QuestionId = questionId;
        SnapshotRevision = snapshotRevision;
        ExactText = exactText;
        SourceRefs = Array.AsReadOnly(sourceRefs.Select(segment => segment.Copy()).ToArray());
        WasProvisional = wasProvisional;
        UserEdited = userEdited;
        CreatedMonotonic = createdMonotonic;
    }

    public Guid QuestionId { get; }
    public long SnapshotRevision { get; }
    public string ExactText { get; }
    public IReadOnlyList<TranscriptSegment> SourceRefs { get; }
    public bool WasProvisional { get; }
    public bool UserEdited { get; }
    public long CreatedMonotonic { get; }
}
