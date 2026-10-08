namespace PhantomFudge.Core.Tests;

public sealed class SourceTranscriptTests
{
    [Fact]
    public void PartialsRemainIndependentAcrossSources()
    {
        var buffer = new TranscriptBuffer();
        buffer.SetPartial("remote words", AudioSource.Computer);
        buffer.SetPartial("dictated question", AudioSource.Microphone);
        Assert.Contains("[Computer] remote words", buffer.Partial);
        Assert.Contains("[Mic] dictated question", buffer.Partial);
        buffer.AddFinal(1, "remote sentence", 1, AudioSource.Computer);
        Assert.Contains("[00:00:01] [Computer] remote sentence", buffer.Text);
        Assert.DoesNotContain("remote words", buffer.Partial);
        Assert.Contains("[Mic] dictated question", buffer.Partial);
        buffer.AddFinal(2, "dictated question", 0.5, AudioSource.Microphone);
        Assert.Contains("[00:00:00] [Mic] dictated question", buffer.Text);
        Assert.Empty(buffer.Partial);
    }

    [Fact]
    public void NewSessionPreservesBothUnfinishedTracks()
    {
        var buffer = new TranscriptBuffer();
        buffer.SetPartial("computer unfinished", AudioSource.Computer);
        buffer.SetPartial("microphone unfinished", AudioSource.Microphone);
        buffer.BeginSession();
        Assert.Contains("[Computer] computer unfinished", buffer.Text);
        Assert.Contains("[Mic] microphone unfinished", buffer.Text);
        Assert.Empty(buffer.Partial);
        buffer.AddFinal(1, "new", 0, AudioSource.Microphone);
        Assert.Contains("[Mic] new", buffer.Text);
    }

    [Fact]
    public void EmptyFinalClearsOnlyItsOwnPartial()
    {
        var buffer = new TranscriptBuffer();
        buffer.SetPartial("computer", AudioSource.Computer);
        buffer.SetPartial("mic", AudioSource.Microphone);
        Assert.False(buffer.AddFinal(1, "", 0, AudioSource.Microphone));
        Assert.Equal("[Computer] computer", buffer.Partial);
        Assert.Empty(buffer.Text);
    }

    [Fact]
    public void DuplicateGlobalIdsCannotEraseOtherSourceText()
    {
        var buffer = new TranscriptBuffer();
        buffer.SetPartial("mic still pending", AudioSource.Microphone);
        buffer.AddFinal(1, "computer final", 0, AudioSource.Computer);
        Assert.False(buffer.AddFinal(1, "duplicate mic", 0, AudioSource.Microphone));
        Assert.Contains("mic still pending", buffer.Partial);
        Assert.DoesNotContain("duplicate mic", buffer.Text);
    }

    [Fact]
    public void ClearResetsBothSources()
    {
        var buffer = new TranscriptBuffer();
        buffer.SetPartial("computer", AudioSource.Computer);
        buffer.SetPartial("mic", AudioSource.Microphone);
        buffer.AddFinal(1, "sentence", 0, AudioSource.Computer);
        buffer.Clear();
        Assert.Empty(buffer.Text);
        Assert.Empty(buffer.Partial);
        Assert.False(buffer.HasContent);
        Assert.True(buffer.AddFinal(1, "next", 0, AudioSource.Microphone));
    }

    [Fact]
    public void InvalidSourceIsExplicitlyRejected()
    {
        var buffer = new TranscriptBuffer();
        Assert.Throws<InvalidDataException>(() => buffer.SetPartial("text", (AudioSource)99));
        Assert.Throws<InvalidDataException>(() => buffer.AddFinal(1, "text", 0, (AudioSource)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => ((AudioSource)99).WireName());
    }

    [Theory]
    [InlineData(AudioSource.Computer, "computer", "Computer")]
    [InlineData(AudioSource.Microphone, "microphone", "Mic")]
    public void LabelsDescribeInputNotSpeaker(AudioSource source, string wire, string label)
    {
        Assert.Equal(wire, source.WireName());
        Assert.Equal(label, source.DisplayName());
    }
}
