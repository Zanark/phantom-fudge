using System.Security.Cryptography;
using System.Text;

namespace PhantomFudge.Core;

public sealed record NoteSnapshot(string Text, bool HasUtf8Bom, string Fingerprint);

public sealed class NoteConflictException() : IOException("The file changed outside this application.");

public static class NoteFileStore
{
    public const int MaximumBytes = 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static bool Fits(string text, bool withBom = false)
    {
        try
        {
            return StrictUtf8.GetByteCount(text) <= MaximumBytes - (withBom ? 3 : 0);
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    public static async Task<NoteSnapshot> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var source = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite, 16_384, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var bytes = new MemoryStream();
        var buffer = new byte[16_384];
        int count;
        while ((count = await source.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (bytes.Length + count > MaximumBytes)
                throw new InvalidDataException("The text file exceeds the 1 MiB limit.");
            bytes.Write(buffer, 0, count);
        }

        var content = bytes.ToArray();
        var bom = content.AsSpan().StartsWith(Encoding.UTF8.Preamble);
        var text = StrictUtf8.GetString(content, bom ? 3 : 0, content.Length - (bom ? 3 : 0));
        return new NoteSnapshot(text, bom, Convert.ToHexString(SHA256.HashData(content)));
    }

    public static async Task<NoteSnapshot> WriteAsync(string path, string text, bool withBom,
        string? expectedFingerprint, CancellationToken cancellationToken = default)
    {
        if (!Fits(text, withBom))
            throw new InvalidDataException("The text file exceeds the 1 MiB limit.");

        var fullPath = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(fullPath)!;
        var temporary = Path.Combine(parent, $".phantom-{Guid.NewGuid():N}.tmp");
        var data = StrictUtf8.GetBytes(text);
        if (withBom)
            data = [.. Encoding.UTF8.Preamble, .. data];

        await VerifyUnchangedAsync(fullPath, expectedFingerprint, cancellationToken);
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 16_384, FileOptions.Asynchronous))
            {
                await stream.WriteAsync(data, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            await VerifyUnchangedAsync(fullPath, expectedFingerprint, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, fullPath, overwrite: expectedFingerprint is not null);
            return new NoteSnapshot(text, withBom, Convert.ToHexString(SHA256.HashData(data)));
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static async Task VerifyUnchangedAsync(string path, string? expected,
        CancellationToken cancellationToken)
    {
        if (expected is null)
        {
            if (File.Exists(path))
                throw new NoteConflictException();
            return;
        }

        if (!File.Exists(path) ||
            !StringComparer.Ordinal.Equals((await ReadAsync(path, cancellationToken)).Fingerprint, expected))
            throw new NoteConflictException();
    }
}
