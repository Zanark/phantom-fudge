using System.Text;

namespace PhantomFudge.Core.Tests;

public sealed class NoteFileStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "PhantomFudgeTests-" + Guid.NewGuid().ToString("N"));

    public NoteFileStoreTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, recursive: true);
    private string NotePath => Path.Combine(directory, "note.txt");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RoundTripsTextAndBom(bool bom)
    {
        const string text = "First\r\nSecond\nUnicode \u00E9 \uD83D\uDE00 e\u0301";
        var written = await NoteFileStore.WriteAsync(NotePath, text, bom, null);
        var read = await NoteFileStore.ReadAsync(NotePath);
        Assert.Equal(text, read.Text);
        Assert.Equal(bom, read.HasUtf8Bom);
        Assert.Equal(written.Fingerprint, read.Fingerprint);
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public async Task ExistingFileNeedsExpectedFingerprint()
    {
        await File.WriteAllTextAsync(NotePath, "existing");
        await Assert.ThrowsAsync<NoteConflictException>(() =>
            NoteFileStore.WriteAsync(NotePath, "new", false, null));
        Assert.Equal("existing", await File.ReadAllTextAsync(NotePath));
    }

    [Fact]
    public async Task ChangedFileIsNotOverwritten()
    {
        var first = await NoteFileStore.WriteAsync(NotePath, "first", false, null);
        await File.WriteAllTextAsync(NotePath, "external change");
        await Assert.ThrowsAsync<NoteConflictException>(() =>
            NoteFileStore.WriteAsync(NotePath, "my edits", false, first.Fingerprint));
        Assert.Equal("external change", await File.ReadAllTextAsync(NotePath));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public async Task UnchangedFileCanBeReplaced()
    {
        var first = await NoteFileStore.WriteAsync(NotePath, "first", false, null);
        var saved = await NoteFileStore.WriteAsync(NotePath, "second", false, first.Fingerprint);
        Assert.Equal("second", (await NoteFileStore.ReadAsync(NotePath)).Text);
        Assert.NotEqual(first.Fingerprint, saved.Fingerprint);
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public async Task DeletedFileIsAConflictNotSilentRecreation()
    {
        var first = await NoteFileStore.WriteAsync(NotePath, "first", false, null);
        File.Delete(NotePath);
        await Assert.ThrowsAsync<NoteConflictException>(() =>
            NoteFileStore.WriteAsync(NotePath, "second", false, first.Fingerprint));
        Assert.False(File.Exists(NotePath));
    }

    [Fact]
    public async Task RejectsInvalidUtf8()
    {
        await File.WriteAllBytesAsync(NotePath, [0xFF, 0xFE, 0xFF]);
        await Assert.ThrowsAsync<DecoderFallbackException>(() => NoteFileStore.ReadAsync(NotePath));
    }

    [Fact]
    public async Task RejectsOversizedOpen()
    {
        await File.WriteAllBytesAsync(NotePath, new byte[NoteFileStore.MaximumBytes + 1]);
        await Assert.ThrowsAsync<InvalidDataException>(() => NoteFileStore.ReadAsync(NotePath));
    }

    [Fact]
    public async Task RejectedSavePreservesOriginal()
    {
        var first = await NoteFileStore.WriteAsync(NotePath, "original", false, null);
        await Assert.ThrowsAsync<InvalidDataException>(() => NoteFileStore.WriteAsync(NotePath,
            new string('x', NoteFileStore.MaximumBytes + 1), false, first.Fingerprint));
        Assert.Equal("original", await File.ReadAllTextAsync(NotePath));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public void ByteLimitAccountsForBomAndMultibyteText()
    {
        Assert.True(NoteFileStore.Fits(new string('x', NoteFileStore.MaximumBytes)));
        Assert.False(NoteFileStore.Fits(new string('x', NoteFileStore.MaximumBytes), withBom: true));
        Assert.False(NoteFileStore.Fits(new string('\u00E9', NoteFileStore.MaximumBytes)));
        Assert.False(NoteFileStore.Fits("\uD800"));
    }

    [Fact]
    public async Task CancellationDoesNotReplacePriorFile()
    {
        var first = await NoteFileStore.WriteAsync(NotePath, "original", false, null);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            NoteFileStore.WriteAsync(NotePath, "replacement", false, first.Fingerprint, cancelled.Token));
        Assert.Equal("original", await File.ReadAllTextAsync(NotePath));
        Assert.Single(Directory.GetFiles(directory));
    }
}
