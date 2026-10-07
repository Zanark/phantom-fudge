namespace PhantomFudge.Core.Tests;

public sealed class TranscriptBufferTests
{
    [Fact]
    public void PartialReplacesInsteadOfAppending()
    {
        var buffer = new TranscriptBuffer();
        buffer.SetPartial("a");
        buffer.SetPartial("a sentence");
        Assert.Equal("a sentence", buffer.Partial);
        Assert.Empty(buffer.Text);
        Assert.Contains("[Unfinished recognition]", buffer.ExportText());
    }

    [Fact]
    public void FinalClearsPartialAndDeduplicatesSegmentIds()
    {
        var buffer = new TranscriptBuffer();
        buffer.SetPartial("hel");
        Assert.True(buffer.AddFinal(1, "hello", 2));
        Assert.False(buffer.AddFinal(1, "hello", 2));
        Assert.False(buffer.AddFinal(1, "replayed data", 2));
        Assert.Empty(buffer.Partial);
        Assert.Equal("[00:00:02] hello" + Environment.NewLine, buffer.Text);
    }

    [Fact]
    public void SessionStartKeepsPriorTranscriptButResetsIds()
    {
        var buffer = new TranscriptBuffer();
        buffer.AddFinal(1, "first", null);
        buffer.BeginSession();
        Assert.True(buffer.AddFinal(1, "second", null));
        Assert.Contains("first", buffer.Text);
        Assert.Contains("New session", buffer.Text);
        Assert.Contains("second", buffer.Text);
    }

    [Fact]
    public void RestartPreservesUnfinishedRecognitionForExport()
    {
        var buffer = new TranscriptBuffer();
        buffer.SetPartial("unfinished words after connection loss");
        buffer.BeginSession();
        Assert.True(buffer.HasContent);
        Assert.Contains("[Unfinished recognition] unfinished words after connection loss", buffer.ExportText());
        Assert.Contains("--- New session ---", buffer.Text);
        Assert.Empty(buffer.Partial);
    }

    [Fact]
    public void FailedRestartAtCapacityPreservesUnfinishedRecognition()
    {
        var buffer = new TranscriptBuffer();
        for (var id = 1; id <= 15; id++)
            buffer.AddFinal(id, new string('x', 16_000), null);
        buffer.SetPartial(new string('y', 16_000));
        var before = buffer.ExportText();
        Assert.Throws<InvalidOperationException>(() => buffer.BeginSession());
        Assert.Equal(before, buffer.ExportText());
        Assert.True(buffer.HasContent);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(2_592_001)]
    public void RejectsInvalidOffsets(double seconds)
    {
        var buffer = new TranscriptBuffer();
        Assert.Throws<InvalidDataException>(() => buffer.AddFinal(1, "text", seconds));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsInvalidIds(int id)
    {
        var buffer = new TranscriptBuffer();
        Assert.Throws<InvalidDataException>(() => buffer.AddFinal(id, "text", null));
    }

    [Fact]
    public void LongSessionTimestampDoesNotWrapToZeroHours()
    {
        var buffer = new TranscriptBuffer();
        buffer.AddFinal(1, "text", 90_001);
        Assert.StartsWith("[25:00:01]", buffer.Text);
    }

    [Fact]
    public void LimitStopsInsteadOfDiscardingEarlierWords()
    {
        var buffer = new TranscriptBuffer();
        var sentence = new string('a', 16_000);
        for (var id = 1; id <= 15; id++)
            buffer.AddFinal(id, sentence, null);
        var before = buffer.Text;
        Assert.Throws<InvalidOperationException>(() => buffer.AddFinal(16, sentence, null));
        Assert.True(buffer.IsFull);
        Assert.Equal(before, buffer.Text);
        Assert.Throws<InvalidOperationException>(() => buffer.BeginSession());
        buffer.Clear();
        Assert.False(buffer.IsFull);
        Assert.False(buffer.HasContent);
        buffer.BeginSession();
    }

    [Fact]
    public void RejectsHugePartial()
    {
        var buffer = new TranscriptBuffer();
        Assert.Throws<InvalidDataException>(() => buffer.SetPartial(new string('x', 16_385)));
    }
}
