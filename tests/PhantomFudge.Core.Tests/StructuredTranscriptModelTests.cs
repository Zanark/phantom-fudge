namespace PhantomFudge.Core.Tests;

public sealed class StructuredTranscriptModelTests
{
    private static readonly Guid Session = Guid.Parse("20000000-0000-0000-0000-000000000001");

    [Fact]
    public void KeyEqualityUsesEveryIdentityField()
    {
        var key = new TranscriptSegmentKey(Session, AudioSource.Microphone, 0, 1);
        Assert.Equal(key, new TranscriptSegmentKey(Session, AudioSource.Microphone, 0, 1));
        Assert.NotEqual(key, new TranscriptSegmentKey(Guid.Parse("20000000-0000-0000-0000-000000000002"), AudioSource.Microphone, 0, 1));
        Assert.NotEqual(key, new TranscriptSegmentKey(Session, AudioSource.Computer, 0, 1));
        Assert.NotEqual(key, new TranscriptSegmentKey(Session, AudioSource.Microphone, 1, 1));
        Assert.NotEqual(key, new TranscriptSegmentKey(Session, AudioSource.Microphone, 0, 2));
    }

    [Fact]
    public void KeyAndSegmentExposeOnlyReadOnlyProperties()
    {
        Assert.All(typeof(TranscriptSegmentKey).GetProperties(), property => Assert.Null(property.SetMethod));
        Assert.All(typeof(TranscriptSegment).GetProperties(), property => Assert.Null(property.SetMethod));
        Assert.All(typeof(QuestionSnapshot).GetProperties(), property => Assert.Null(property.SetMethod));
    }

    [Fact]
    public void KeyRejectsInvalidIdentity()
    {
        Assert.Equal("captureSessionId", Assert.Throws<ArgumentException>(
            () => new TranscriptSegmentKey(Guid.Empty, AudioSource.Microphone, 0, 1)).ParamName);
        Assert.Equal("source", Assert.Throws<ArgumentOutOfRangeException>(
            () => new TranscriptSegmentKey(Session, (AudioSource)0, 0, 1)).ParamName);
        Assert.Equal("sourceEpoch", Assert.Throws<ArgumentOutOfRangeException>(
            () => new TranscriptSegmentKey(Session, AudioSource.Microphone, -1, 1)).ParamName);
        Assert.Equal("localSegmentId", Assert.Throws<ArgumentOutOfRangeException>(
            () => new TranscriptSegmentKey(Session, AudioSource.Microphone, 0, 0)).ParamName);
    }

    [Fact]
    public void SegmentRejectsNullKeyInvalidRevisionAndText()
    {
        var key = new TranscriptSegmentKey(Session, AudioSource.Microphone, 0, 1);
        Assert.Equal("key", Assert.Throws<ArgumentNullException>(
            () => new TranscriptSegment(null!, 1, "x", false, null, null, null)).ParamName);
        Assert.Equal("revision", Assert.Throws<ArgumentOutOfRangeException>(
            () => new TranscriptSegment(key, 0, "x", false, null, null, null)).ParamName);
        Assert.Equal("text", Assert.Throws<ArgumentNullException>(
            () => new TranscriptSegment(key, 1, null!, false, null, null, null)).ParamName);
        Assert.Equal("text", Assert.Throws<ArgumentException>(
            () => new TranscriptSegment(key, 1, new string('x', 16_385), false, null, null, null)).ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public void FinalRequiresRealPositiveProtocolMetadata(int? protocolId)
    {
        var key = new TranscriptSegmentKey(Session, AudioSource.Microphone, 0, 1);
        Assert.Equal("protocolSegmentId", Assert.Throws<ArgumentOutOfRangeException>(
            () => new TranscriptSegment(key, 1, "x", true, protocolId, null, null)).ParamName);
    }

    [Fact]
    public void ProvisionalCannotClaimFinalMetadataAndFinalCannotClaimActivePartial()
    {
        var key = new TranscriptSegmentKey(Session, AudioSource.Microphone, 0, 1);
        Assert.Equal("protocolSegmentId", Assert.Throws<ArgumentException>(
            () => new TranscriptSegment(key, 1, "x", false, 1, null, null)).ParamName);
        Assert.Equal("isActive", Assert.Throws<ArgumentException>(
            () => new TranscriptSegment(key, 1, "x", true, 1, null, null, true)).ParamName);
    }

    [Fact]
    public void DisplayLabelsComeFromStructuredFieldsNotText()
    {
        var key = new TranscriptSegmentKey(Session, AudioSource.Microphone, 0, 1);
        var segment = new TranscriptSegment(key, 1, "[Computer] [Final] literal text", false, null, null, null, true);
        Assert.Equal("[Session 00000001] [Mic] [Partial] [Computer] [Final] literal text", segment.DisplayText);
        Assert.Equal(AudioSource.Microphone, segment.Key.Source);
        Assert.False(segment.IsFinal);
    }

    [Fact]
    public void DisplayIncludesShortSessionDiscriminatorAcrossIdenticalSourceAndText()
    {
        var firstKey = new TranscriptSegmentKey(Session, AudioSource.Microphone, 0, 1);
        var nextKey = new TranscriptSegmentKey(Guid.Parse("20000000-0000-0000-0000-000000000002"), AudioSource.Microphone, 0, 1);
        var old = new TranscriptSegment(firstKey, 1, "same text", false, null, null, null);
        var current = new TranscriptSegment(nextKey, 1, "same text", false, null, null, null, true);
        var final = new TranscriptSegment(nextKey, 2, "same text", true, 1, null, null);

        Assert.Equal("[Session 00000001] [Mic] [Unfinished partial] same text", old.DisplayText);
        Assert.Equal("[Session 00000002] [Mic] [Partial] same text", current.DisplayText);
        Assert.Equal("[Session 00000002] [Mic] [Final] same text", final.DisplayText);
        Assert.NotEqual(old.DisplayText, current.DisplayText);
    }

    [Fact]
    public void DirectSegmentConstructionValidatesTiming()
    {
        var key = new TranscriptSegmentKey(Session, AudioSource.Microphone, 0, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => new TranscriptSegment(key, 1, "x", true, 1, double.NaN, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TranscriptSegment(key, 1, "x", true, 1, 0, double.PositiveInfinity));
        Assert.Throws<ArgumentException>(() => new TranscriptSegment(key, 1, "x", true, 1, 2, 1));
    }
}
