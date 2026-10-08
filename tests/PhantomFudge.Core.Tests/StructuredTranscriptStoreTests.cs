namespace PhantomFudge.Core.Tests;

public sealed class StructuredTranscriptStoreTests
{
    private static readonly Guid Session = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid NextSession = Guid.Parse("10000000-0000-0000-0000-000000000002");

    [Fact]
    public void NewStoreRequiresAnExplicitSession()
    {
        var store = new StructuredTranscriptStore();
        Assert.Equal(Guid.Empty, store.SessionId);
        Assert.Empty(store.Segments);
        Assert.False(store.SetPartial(Session, AudioSource.Microphone, "not started"));
        Assert.False(store.AddFinal(Session, AudioSource.Computer, 1, "not started", 0, 1));
        Assert.Empty(store.Segments);
    }

    [Theory]
    [InlineData(0, 1, "maximumSegments")]
    [InlineData(-1, 1, "maximumSegments")]
    [InlineData(1, 0, "maximumCharacters")]
    [InlineData(1, -1, "maximumCharacters")]
    public void CapacityMustBePositive(int segments, int characters, string parameter)
    {
        Assert.Equal(parameter, Assert.Throws<ArgumentOutOfRangeException>(
            () => new StructuredTranscriptStore(segments, characters)).ParamName);
    }

    [Fact]
    public void PartialRevisionsAndFinalKeepOneLocalIdentity()
    {
        var store = Started();
        Assert.True(store.SetPartial(Session, AudioSource.Microphone, "explain"));
        var first = Assert.Single(store.Segments);
        Assert.Equal(1, first.Revision);
        Assert.Equal(0, first.Key.SourceEpoch);
        Assert.True(first.IsActive);
        Assert.False(first.IsFinal);
        Assert.Null(first.ProtocolSegmentId);

        Assert.True(store.SetPartial(Session, AudioSource.Microphone, "explain"));
        Assert.Equal(first, Assert.Single(store.Segments));
        Assert.True(store.SetPartial(Session, AudioSource.Microphone, "explain the design"));
        var second = Assert.Single(store.Segments);
        Assert.Equal(first.Key, second.Key);
        Assert.Equal(2, second.Revision);
        Assert.Equal("explain", first.Text);

        Assert.True(store.AddFinal(Session, AudioSource.Microphone, 72, "explain the design?", 1.25, 2.75));
        var final = Assert.Single(store.Segments);
        Assert.Equal(first.Key, final.Key);
        Assert.Equal(3, final.Revision);
        Assert.Equal(72, final.ProtocolSegmentId);
        Assert.NotEqual((long)final.ProtocolSegmentId.GetValueOrDefault(), final.Key.LocalSegmentId);
        Assert.True(final.IsFinal);
        Assert.False(final.IsActive);
        Assert.Equal(1.25, final.StartSeconds);
        Assert.Equal(2.75, final.EndSeconds);

        store.SetPartial(Session, AudioSource.Microphone, "next question");
        Assert.NotEqual(first.Key, store.Segments[1].Key);
        Assert.Equal(1, store.Segments[1].Revision);
    }

    [Fact]
    public void FinalWithoutPartialHasIndependentLocalAndProtocolIdentity()
    {
        var store = Started();
        store.AddFinal(Session, AudioSource.Computer, 200, "first", null, null);
        var first = Assert.Single(store.Segments);
        Assert.Equal(1, first.Key.LocalSegmentId);
        Assert.Equal(200, first.ProtocolSegmentId);
        Assert.Equal(1, first.Revision);
        Assert.Null(first.StartSeconds);
        Assert.Null(first.EndSeconds);
    }

    [Fact]
    public void SourcesHaveIndependentPartials()
    {
        var store = Started();
        store.SetPartial(Session, AudioSource.Computer, "remote");
        store.SetPartial(Session, AudioSource.Microphone, "my question");
        var mic = store.Segments[1];
        store.AddFinal(Session, AudioSource.Computer, 1, "remote final", 0, 1);
        Assert.Equal(mic, store.Segments[1]);
        Assert.True(store.Segments[1].IsActive);
        Assert.Equal("remote final", store.Segments[0].Text);
        Assert.Contains("[Computer] [Final]", store.Segments[0].DisplayText);
        Assert.Contains("[Mic] [Partial]", store.Segments[1].DisplayText);
    }

    [Fact]
    public void DuplicateAndOlderGlobalFinalIdsCannotClearAnotherSource()
    {
        var store = Started();
        store.AddFinal(Session, AudioSource.Computer, 10, "remote final", 0, 1);
        store.SetPartial(Session, AudioSource.Microphone, "pending");
        var before = store.Segments;
        Assert.False(store.AddFinal(Session, AudioSource.Microphone, 10, "duplicate", 0, 1, 3));
        Assert.False(store.AddFinal(Session, AudioSource.Microphone, 9, "", 0, 1));
        Assert.Equal(before, store.Segments);
        Assert.True(store.SetPartial(Session, AudioSource.Microphone, "still epoch zero"));
        Assert.True(store.AddFinal(Session, AudioSource.Microphone, 11, "real final", 0, 1));
    }

    [Fact]
    public void NewSessionPreservesOldFinalsAndClosesPartialsWithoutInventingFinals()
    {
        var store = Started();
        store.AddFinal(Session, AudioSource.Computer, 1, "old final", 0, 1);
        store.SetPartial(Session, AudioSource.Microphone, "unfinished");
        var partial = store.Segments[1];
        store.BeginSession(NextSession);
        Assert.Equal(NextSession, store.SessionId);
        Assert.Equal("old final", store.Segments[0].Text);
        var closed = store.Segments[1];
        Assert.Equal(partial.Key, closed.Key);
        Assert.Equal(partial.Revision + 1, closed.Revision);
        Assert.Equal("unfinished", closed.Text);
        Assert.False(closed.IsActive);
        Assert.False(closed.IsFinal);
        Assert.Null(closed.ProtocolSegmentId);
        Assert.Contains("[Mic] [Unfinished partial]", closed.DisplayText);
        Assert.Contains("[Session 00000001]", closed.DisplayText);
        Assert.False(store.SetPartial(Session, AudioSource.Microphone, "late partial"));
        Assert.False(store.AddFinal(Session, AudioSource.Microphone, 2, "late final", 1, 2));
        Assert.True(store.AddFinal(NextSession, AudioSource.Microphone, 1, "new final", 0, 1));
        Assert.Contains("[Session 00000002]", store.Segments[2].DisplayText);
        Assert.NotEqual(partial.Key.LocalSegmentId, store.Segments[2].Key.LocalSegmentId);
        Assert.Equal(3, store.Segments.Count);
    }

    [Fact]
    public void InvalidOrReusedSessionStartIsAtomic()
    {
        var store = Started();
        store.SetPartial(Session, AudioSource.Microphone, "keep");
        var before = store.Segments;
        Assert.Equal("sessionId", Assert.Throws<ArgumentException>(() => store.BeginSession(Guid.Empty)).ParamName);
        Assert.Throws<ArgumentException>(() => store.BeginSession(Session));
        Assert.Equal(before, store.Segments);
        store.BeginSession(NextSession);
        Assert.Throws<ArgumentException>(() => store.BeginSession(Session));
        Assert.Equal(NextSession, store.SessionId);
    }

    [Fact]
    public void ClearInvalidatesCallbacksAndNeverReusesLocalIdentity()
    {
        var store = Started();
        store.SetPartial(Session, AudioSource.Microphone, "old");
        var old = Assert.Single(store.Segments);
        var before = store.Segments;
        store.Clear();
        Assert.Equal(Guid.Empty, store.SessionId);
        Assert.Empty(store.Segments);
        Assert.False(store.SetPartial(Session, AudioSource.Microphone, "late"));
        Assert.False(store.AddFinal(Session, AudioSource.Microphone, 1, "late", 0, 1));
        Assert.Equal(old, Assert.Single(before));
        store.BeginSession(NextSession);
        Assert.True(store.AddFinal(NextSession, AudioSource.Microphone, 1, "new", 0, 1));
        Assert.NotEqual(old.Key.LocalSegmentId, Assert.Single(store.Segments).Key.LocalSegmentId);
    }

    [Fact]
    public void NewEpochClosesOnlyItsSourceAndRejectsObsoleteEpochs()
    {
        var store = Started();
        store.SetPartial(Session, AudioSource.Microphone, "unfinished epoch zero");
        store.SetPartial(Session, AudioSource.Computer, "computer");
        var old = store.Segments[0];
        var computer = store.Segments[1];
        store.SetPartial(Session, AudioSource.Microphone, "epoch one", 1);
        Assert.Equal(computer, store.Segments[1]);
        Assert.False(store.Segments[0].IsActive);
        Assert.False(store.Segments[0].IsFinal);
        Assert.Equal(old.Key, store.Segments[0].Key);
        Assert.Equal(1, store.Segments[2].Key.SourceEpoch);
        Assert.NotEqual(old.Key, store.Segments[2].Key);
        var before = store.Segments;
        Assert.False(store.SetPartial(Session, AudioSource.Microphone, "old", 0));
        Assert.False(store.AddFinal(Session, AudioSource.Microphone, 50, "old", 0, 1, 0));
        Assert.Equal(before, store.Segments);
        Assert.True(store.AddFinal(Session, AudioSource.Microphone, 1, "new final", 0, 1, 1));
        Assert.True(store.SetPartial(Session, AudioSource.Computer, "still epoch zero"));
    }

    [Fact]
    public void EpochCanAdvanceOnFinalWithoutFinalizingTheOldEpochGuess()
    {
        var store = Started();
        store.SetPartial(Session, AudioSource.Microphone, "old guess");
        var oldKey = store.Segments[0].Key;
        store.AddFinal(Session, AudioSource.Microphone, 1, "new epoch final", 0, 1, 2);
        Assert.False(store.Segments[0].IsFinal);
        Assert.False(store.Segments[0].IsActive);
        Assert.Equal(oldKey, store.Segments[0].Key);
        Assert.True(store.Segments[1].IsFinal);
        Assert.Equal(2, store.Segments[1].Key.SourceEpoch);
    }

    [Fact]
    public void EmptyPartialDoesNotCreateJunkButRetainsExistingUtteranceIdentity()
    {
        var store = Started();
        Assert.True(store.SetPartial(Session, AudioSource.Microphone, ""));
        Assert.Empty(store.Segments);
        store.SetPartial(Session, AudioSource.Microphone, "guess");
        var key = Assert.Single(store.Segments).Key;
        store.SetPartial(Session, AudioSource.Microphone, "");
        Assert.Equal(key, Assert.Single(store.Segments).Key);
        Assert.Equal("", store.Segments[0].Text);
        Assert.Equal(2, store.Segments[0].Revision);
        store.SetPartial(Session, AudioSource.Microphone, "revised");
        Assert.Equal(key, Assert.Single(store.Segments).Key);
        Assert.Equal(3, store.Segments[0].Revision);
    }

    [Fact]
    public void EmptyFinalSettlesOnlyTheMatchingPartialAndDoesNotCreateNewEntries()
    {
        var store = Started();
        store.SetPartial(Session, AudioSource.Computer, "computer");
        store.SetPartial(Session, AudioSource.Microphone, "discarded guess");
        var computer = store.Segments[0];
        var micKey = store.Segments[1].Key;
        Assert.True(store.AddFinal(Session, AudioSource.Microphone, 1, "", 0, 1));
        Assert.Equal(computer, store.Segments[0]);
        var settled = store.Segments[1];
        Assert.Equal(micKey, settled.Key);
        Assert.Equal("", settled.Text);
        Assert.True(settled.IsFinal);
        Assert.False(settled.IsActive);
        Assert.True(store.AddFinal(Session, AudioSource.Microphone, 2, "", 1, 2));
        Assert.Equal(2, store.Segments.Count);
    }

    [Fact]
    public void EmptyFinalFloodIsBoundedAndKeepsGlobalDuplicateRejection()
    {
        var store = Started(2, 10);
        for (var id = 1; id <= 10_000; id++)
            Assert.True(store.AddFinal(Session, id % 2 == 0 ? AudioSource.Computer : AudioSource.Microphone, id, "", null, null));
        Assert.Empty(store.Segments);
        Assert.False(store.AddFinal(Session, AudioSource.Computer, 1, "duplicate", null, null));
        Assert.False(store.AddFinal(Session, AudioSource.Microphone, 10_000, "", null, null));
    }

    [Fact]
    public void CountEvictionRemovesOldestHistoricalEntry()
    {
        var store = Started(2);
        store.AddFinal(Session, AudioSource.Computer, 1, "first", null, null);
        store.AddFinal(Session, AudioSource.Computer, 2, "second", null, null);
        store.AddFinal(Session, AudioSource.Computer, 3, "third", null, null);
        Assert.Equal(["second", "third"], store.Segments.Select(segment => segment.Text));
        Assert.False(store.AddFinal(Session, AudioSource.Microphone, 1, "evicted duplicate", null, null));
    }

    [Fact]
    public void CharacterEvictionDoesNotTruncateTheIncomingSegment()
    {
        var store = Started(10, 6);
        store.AddFinal(Session, AudioSource.Computer, 1, "aaa", null, null);
        store.AddFinal(Session, AudioSource.Computer, 2, "bbb", null, null);
        store.AddFinal(Session, AudioSource.Computer, 3, "cccc", null, null);
        Assert.Equal("cccc", Assert.Single(store.Segments).Text);
    }

    [Fact]
    public void LivePartialCannotBeEvictedOrRetargetedByOtherSourceFinals()
    {
        var store = Started(2, 100);
        store.SetPartial(Session, AudioSource.Microphone, "selected partial");
        var key = store.Segments[0].Key;
        for (var id = 1; id <= 100; id++)
            store.AddFinal(Session, AudioSource.Computer, id, $"remote {id}", null, null);
        Assert.Equal(2, store.Segments.Count);
        Assert.Equal(key, store.Segments[0].Key);
        store.SetPartial(Session, AudioSource.Microphone, "revised partial");
        store.AddFinal(Session, AudioSource.Microphone, 101, "selected final", 0, 1);
        Assert.Equal(key, store.Segments[0].Key);
        Assert.True(store.Segments[0].IsFinal);
        Assert.Equal("selected final", store.Segments[0].Text);
    }

    [Fact]
    public void GrowingPartialEvictsHistoryButPreservesItsIdentity()
    {
        var store = Started(3, 8);
        store.AddFinal(Session, AudioSource.Computer, 1, "old", null, null);
        store.SetPartial(Session, AudioSource.Microphone, "a");
        var key = store.Segments[1].Key;
        store.SetPartial(Session, AudioSource.Microphone, "12345678");
        Assert.Equal(key, Assert.Single(store.Segments).Key);
        Assert.Equal("12345678", store.Segments[0].Text);
    }

    [Fact]
    public void CapacityFailureDoesNotChangeTextRevisionEpochOrConsumeFinalId()
    {
        var store = Started(2, 6);
        store.SetPartial(Session, AudioSource.Microphone, "mic");
        store.SetPartial(Session, AudioSource.Computer, "pc");
        var before = store.Segments;
        Assert.Throws<InvalidOperationException>(() => store.SetPartial(Session, AudioSource.Computer, "1234"));
        Assert.Equal(before, store.Segments);
        Assert.Throws<InvalidOperationException>(() => store.AddFinal(Session, AudioSource.Computer, 8, "1234567", 0, 1, 5));
        Assert.Equal(before, store.Segments);
        Assert.True(store.AddFinal(Session, AudioSource.Computer, 8, "pc!", 0, 1, 0));
        Assert.Equal(before[1].Key, store.Segments[1].Key);
    }

    [Fact]
    public void CountCapacityFailureNeverDiscardsTheOtherLivePartial()
    {
        var store = Started(1);
        store.SetPartial(Session, AudioSource.Microphone, "mic");
        var before = Assert.Single(store.Segments);
        Assert.Throws<InvalidOperationException>(() => store.SetPartial(Session, AudioSource.Computer, "pc", 5));
        Assert.Throws<InvalidOperationException>(() => store.AddFinal(Session, AudioSource.Computer, 1, "pc", 0, 1));
        Assert.Equal(before, Assert.Single(store.Segments));
        Assert.True(store.SetPartial(Session, AudioSource.Computer, "", 0));
        Assert.True(store.AddFinal(Session, AudioSource.Microphone, 1, "mic final", 0, 1));
    }

    [Fact]
    public void SingleSegmentLargerThanStoreCapacityIsRejectedAtomically()
    {
        var store = Started(2, 3);
        store.AddFinal(Session, AudioSource.Computer, 1, "old", null, null);
        var before = store.Segments;
        Assert.Throws<InvalidOperationException>(() => store.AddFinal(Session, AudioSource.Computer, 2, "large", null, null));
        Assert.Throws<InvalidOperationException>(() => store.SetPartial(Session, AudioSource.Microphone, "large"));
        Assert.Equal(before, store.Segments);
        Assert.True(store.AddFinal(Session, AudioSource.Computer, 2, "new", null, null));
        Assert.Equal("new", Assert.Single(store.Segments).Text);
    }

    [Fact]
    public void BoundedHistoryCanEvictClosedUnfinishedPartialsAcrossSessions()
    {
        var store = Started(1);
        store.SetPartial(Session, AudioSource.Microphone, "old unfinished");
        store.BeginSession(NextSession);
        store.SetPartial(NextSession, AudioSource.Microphone, "new");
        Assert.Equal(NextSession, Assert.Single(store.Segments).Key.CaptureSessionId);
        Assert.False(store.SetPartial(Session, AudioSource.Microphone, "late"));
    }

    [Fact]
    public void StoreSnapshotsAreReadOnlyAndDoNotObserveFutureMutations()
    {
        var store = Started();
        store.SetPartial(Session, AudioSource.Microphone, "original");
        var snapshot = store.Segments;
        var list = Assert.IsAssignableFrom<IList<TranscriptSegment>>(snapshot);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list.Clear());
        Assert.Throws<NotSupportedException>(() => list[0] = snapshot[0]);
        store.SetPartial(Session, AudioSource.Microphone, "new");
        store.Clear();
        Assert.Equal("original", Assert.Single(snapshot).Text);
        Assert.Equal(1, snapshot[0].Revision);
    }

    [Fact]
    public void MixedFloodRespectsBothLimitsOnEveryUpdate()
    {
        var store = Started(7, 80);
        store.SetPartial(Session, AudioSource.Microphone, "fixed live");
        var key = store.Segments[0].Key;
        for (var id = 1; id <= 500; id++)
        {
            store.AddFinal(Session, AudioSource.Computer, id, new string('x', id % 20), null, null);
            Assert.InRange(store.Segments.Count, 1, 7);
            Assert.InRange(store.Segments.Sum(segment => segment.Text.Length), 0, 80);
            Assert.Equal(key, store.Segments[0].Key);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(255)]
    public void InvalidSourceIsRejectedPrecisely(int source)
    {
        var store = Started();
        Assert.Equal("source", Assert.Throws<ArgumentOutOfRangeException>(
            () => store.SetPartial(Session, (AudioSource)source, "x")).ParamName);
        Assert.Equal("source", Assert.Throws<ArgumentOutOfRangeException>(
            () => store.AddFinal(Session, (AudioSource)source, 1, "x", null, null)).ParamName);
        Assert.Empty(store.Segments);
    }

    [Fact]
    public void NullAndOversizedTextNeverChangeTheStore()
    {
        var store = Started();
        store.SetPartial(Session, AudioSource.Microphone, "keep");
        var before = store.Segments;
        Assert.Equal("text", Assert.Throws<ArgumentNullException>(
            () => store.SetPartial(Session, AudioSource.Microphone, null!)).ParamName);
        Assert.Equal("text", Assert.Throws<ArgumentNullException>(
            () => store.AddFinal(Session, AudioSource.Microphone, 1, null!, null, null)).ParamName);
        var tooLong = new string('x', TranscriptSegment.MaximumCharacters + 1);
        Assert.Equal("text", Assert.Throws<ArgumentException>(
            () => store.SetPartial(Session, AudioSource.Microphone, tooLong)).ParamName);
        Assert.Equal("text", Assert.Throws<ArgumentException>(
            () => store.AddFinal(Session, AudioSource.Microphone, 1, tooLong, null, null)).ParamName);
        Assert.Equal(before, store.Segments);
        Assert.True(store.AddFinal(Session, AudioSource.Microphone, 1, "accepted", null, null));
    }

    [Fact]
    public void MaximumTextIsAcceptedWithoutTrimmingOrTruncation()
    {
        var store = Started();
        var text = " \t" + new string('x', TranscriptSegment.MaximumCharacters - 4) + "\r\n";
        store.SetPartial(Session, AudioSource.Microphone, text);
        Assert.Equal(text, Assert.Single(store.Segments).Text);
        store.AddFinal(Session, AudioSource.Microphone, int.MaxValue, text, null, null);
        Assert.Equal(text, Assert.Single(store.Segments).Text);
    }

    [Fact]
    public void EmptySessionAndNegativeEpochAreInvalidInputs()
    {
        var store = Started();
        Assert.Equal("sessionId", Assert.Throws<ArgumentException>(
            () => store.SetPartial(Guid.Empty, AudioSource.Microphone, "x")).ParamName);
        Assert.Equal("sessionId", Assert.Throws<ArgumentException>(
            () => store.AddFinal(Guid.Empty, AudioSource.Microphone, 1, "x", 0, 1)).ParamName);
        Assert.Equal("sourceEpoch", Assert.Throws<ArgumentOutOfRangeException>(
            () => store.SetPartial(Session, AudioSource.Microphone, "x", -1)).ParamName);
        Assert.Equal("sourceEpoch", Assert.Throws<ArgumentOutOfRangeException>(
            () => store.AddFinal(Session, AudioSource.Microphone, 1, "x", 0, 1, -1)).ParamName);
        Assert.Empty(store.Segments);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void FinalIdsMustBePositive(int id)
    {
        var store = Started();
        Assert.Equal("segmentId", Assert.Throws<ArgumentOutOfRangeException>(
            () => store.AddFinal(Session, AudioSource.Microphone, id, "x", 0, 1)).ParamName);
        Assert.Empty(store.Segments);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-0.1)]
    [InlineData(2_592_001)]
    public void InvalidTimingIsRejectedForEachBound(double value)
    {
        var store = Started();
        store.SetPartial(Session, AudioSource.Microphone, "keep");
        var before = store.Segments;
        Assert.Equal("startSeconds", Assert.Throws<ArgumentOutOfRangeException>(
            () => store.AddFinal(Session, AudioSource.Microphone, 1, "x", value, null)).ParamName);
        Assert.Equal("endSeconds", Assert.Throws<ArgumentOutOfRangeException>(
            () => store.AddFinal(Session, AudioSource.Microphone, 1, "x", null, value)).ParamName);
        Assert.Equal(before, store.Segments);
    }

    [Fact]
    public void ReversedTimingIsRejectedWithoutConsumingFinalId()
    {
        var store = Started();
        Assert.Equal("endSeconds", Assert.Throws<ArgumentException>(
            () => store.AddFinal(Session, AudioSource.Microphone, 1, "x", 2, 1)).ParamName);
        Assert.True(store.AddFinal(Session, AudioSource.Microphone, 1, "x", 0, TranscriptSegment.MaximumSeconds));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0.0, null)]
    [InlineData(null, 0.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(2_592_000.0, 2_592_000.0)]
    public void ValidOptionalTimingBoundsArePreserved(double? start, double? end)
    {
        var store = Started();
        store.AddFinal(Session, AudioSource.Microphone, 1, "text", start, end, long.MaxValue);
        var segment = Assert.Single(store.Segments);
        Assert.Equal(start, segment.StartSeconds);
        Assert.Equal(end, segment.EndSeconds);
        Assert.Equal(long.MaxValue, segment.Key.SourceEpoch);
    }

    private static StructuredTranscriptStore Started(int maximumSegments = 256, int maximumCharacters = 64_000)
    {
        var store = new StructuredTranscriptStore(maximumSegments, maximumCharacters);
        store.BeginSession(Session);
        return store;
    }
}
