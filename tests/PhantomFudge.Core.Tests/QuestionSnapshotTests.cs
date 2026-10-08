using System.Diagnostics;

namespace PhantomFudge.Core.Tests;

public sealed class QuestionSnapshotTests
{
    private static readonly Guid Session = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid NextSession = Guid.Parse("40000000-0000-0000-0000-000000000002");

    [Fact]
    public void FreezeCapturesExactDisplayedRevisionAndMonotonicCreationTime()
    {
        var draft = new QuestionDraft();
        const string exactText = "\tQuestion with e\u0301 and \u2603.\r\nDo not trim.  ";
        draft.EditText(exactText);
        var before = Stopwatch.GetTimestamp();
        var snapshot = draft.Freeze();
        var after = Stopwatch.GetTimestamp();
        Assert.Equal(draft.QuestionId, snapshot.QuestionId);
        Assert.Equal(draft.Revision, snapshot.SnapshotRevision);
        Assert.Equal(exactText, snapshot.ExactText);
        Assert.InRange(snapshot.CreatedMonotonic, before, after);
        Assert.Empty(snapshot.SourceRefs);
        Assert.False(snapshot.WasProvisional);
        Assert.True(snapshot.UserEdited);
        Assert.Equal(1, draft.Revision);
    }

    [Fact]
    public void SnapshotSurvivesLateAsrEditsClearsEvictionAndCaptureRestart()
    {
        var store = new StructuredTranscriptStore(1);
        store.BeginSession(Session);
        store.SetPartial(Session, AudioSource.Microphone, "frozen guess");
        var draft = new QuestionDraft();
        draft.ReplaceSelection(store.Segments);
        var snapshot = draft.Freeze();
        var source = Assert.Single(snapshot.SourceRefs);
        var questionId = snapshot.QuestionId;
        var revision = snapshot.SnapshotRevision;
        var created = snapshot.CreatedMonotonic;

        store.SetPartial(Session, AudioSource.Microphone, "new guess");
        draft.Refresh(store);
        store.AddFinal(Session, AudioSource.Microphone, 1, "actual final", 0, 2);
        draft.Refresh(store);
        Assert.Equal("actual final", draft.Text);
        Assert.False(draft.IsProvisional);
        store.AddFinal(Session, AudioSource.Computer, 2, "eviction", 1, 2);
        draft.EditText("edited later");
        draft.Clear();
        store.Clear();
        store.BeginSession(NextSession);
        store.SetPartial(NextSession, AudioSource.Microphone, "new session");
        draft.ReplaceSelection(store.Segments);

        Assert.Equal("frozen guess", snapshot.ExactText);
        Assert.Equal(questionId, snapshot.QuestionId);
        Assert.Equal(revision, snapshot.SnapshotRevision);
        Assert.Equal(created, snapshot.CreatedMonotonic);
        Assert.True(snapshot.WasProvisional);
        Assert.False(snapshot.UserEdited);
        Assert.Equal(source, Assert.Single(snapshot.SourceRefs));
        Assert.Equal("frozen guess", source.Text);
        Assert.Equal(1, source.Revision);
        Assert.Equal(Session, source.Key.CaptureSessionId);
        Assert.Equal(0, source.Key.SourceEpoch);
        Assert.False(source.IsFinal);
        Assert.Null(source.ProtocolSegmentId);
    }

    [Fact]
    public void SourceRefsAreDeepCopiedReadOnlyAndCarryFullProvenance()
    {
        var store = new StructuredTranscriptStore();
        store.BeginSession(Session);
        store.SetPartial(Session, AudioSource.Microphone, "guess", 3);
        store.AddFinal(Session, AudioSource.Microphone, 27, "final text", 1.25, 4.75, 3);
        var original = Assert.Single(store.Segments);
        var draft = new QuestionDraft();
        draft.ReplaceSelection(store.Segments);
        var selected = Assert.Single(draft.SelectedSegments);
        var snapshot = draft.Freeze();
        var copied = Assert.Single(snapshot.SourceRefs);
        Assert.Equal(original, copied);
        Assert.NotSame(original, copied);
        Assert.NotSame(selected, copied);
        Assert.NotSame(selected.Key, copied.Key);
        Assert.Equal(2, copied.Revision);
        Assert.Equal(27, copied.ProtocolSegmentId);
        Assert.Equal(3, copied.Key.SourceEpoch);
        Assert.Equal(1.25, copied.StartSeconds);
        Assert.Equal(4.75, copied.EndSeconds);
        var list = Assert.IsAssignableFrom<IList<TranscriptSegment>>(snapshot.SourceRefs);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list[0] = selected);
        Assert.Throws<NotSupportedException>(() => list.Clear());
        Assert.Throws<NotSupportedException>(() => list.Add(selected));
    }

    [Fact]
    public void EditedSnapshotKeepsOriginalAsrTextAndRevisionAsProvenance()
    {
        var store = new StructuredTranscriptStore();
        store.BeginSession(Session);
        store.SetPartial(Session, AudioSource.Microphone, "uncorrected");
        var draft = new QuestionDraft();
        draft.ReplaceSelection(store.Segments);
        draft.EditText("corrected exact question");
        store.AddFinal(Session, AudioSource.Microphone, 1, "late ASR final", 0, 1);
        draft.Refresh(store);
        var snapshot = draft.Freeze();
        Assert.Equal("corrected exact question", snapshot.ExactText);
        Assert.Equal(2, snapshot.SnapshotRevision);
        Assert.True(snapshot.UserEdited);
        Assert.True(snapshot.WasProvisional);
        Assert.Equal("uncorrected", Assert.Single(snapshot.SourceRefs).Text);
        Assert.Equal(1, snapshot.SourceRefs[0].Revision);
        Assert.Null(snapshot.SourceRefs[0].ProtocolSegmentId);
    }

    [Fact]
    public void RepeatedExplicitFreezeDoesNotMutateDraftOrPreviousSnapshot()
    {
        var draft = new QuestionDraft();
        draft.EditText("first");
        var first = draft.Freeze();
        var sameRevision = draft.Freeze();
        Assert.NotSame(first, sameRevision);
        Assert.Equal(first.QuestionId, sameRevision.QuestionId);
        Assert.Equal(first.SnapshotRevision, sameRevision.SnapshotRevision);
        Assert.Equal(1, draft.Revision);
        draft.EditText("second");
        var second = draft.Freeze();
        Assert.Equal(first.QuestionId, second.QuestionId);
        Assert.Equal(first.SnapshotRevision + 1, second.SnapshotRevision);
        Assert.Equal("first", first.ExactText);
        Assert.Equal("second", second.ExactText);
        draft.Clear();
        draft.EditText("after cancel");
        var afterCancel = draft.Freeze();
        Assert.NotEqual(first.QuestionId, afterCancel.QuestionId);
        Assert.Equal(1, afterCancel.SnapshotRevision);
    }

    [Fact]
    public void MultiSourceSnapshotContainsOnlyExplicitSelectionInItsChosenOrder()
    {
        var store = new StructuredTranscriptStore();
        store.BeginSession(Session);
        store.AddFinal(Session, AudioSource.Microphone, 1, "unrelated", 0, 1);
        store.AddFinal(Session, AudioSource.Computer, 2, "explicit context", 1, 2);
        store.SetPartial(Session, AudioSource.Microphone, "question");
        var draft = new QuestionDraft();
        draft.ReplaceSelection([store.Segments[2], store.Segments[1]]);
        var snapshot = draft.Freeze();
        Assert.Equal("question" + Environment.NewLine + "explicit context", snapshot.ExactText);
        Assert.Equal(2, snapshot.SourceRefs.Count);
        Assert.Equal(AudioSource.Microphone, snapshot.SourceRefs[0].Key.Source);
        Assert.Equal(AudioSource.Computer, snapshot.SourceRefs[1].Key.Source);
        Assert.DoesNotContain(snapshot.SourceRefs, segment => segment.Text == "unrelated");
        Assert.True(snapshot.WasProvisional);
        Assert.False(snapshot.UserEdited);
    }
}
