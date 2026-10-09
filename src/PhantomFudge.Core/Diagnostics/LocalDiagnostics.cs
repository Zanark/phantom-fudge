using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace PhantomFudge.Core.Diagnostics;

public enum DiagnosticEvent
{
    ApplicationStarted, ApplicationStopped, ConfigurationLoaded, ConfigurationFailed,
    CaptureStarting, CaptureStarted, CaptureStopped, CaptureFailed, AudioLevel, AudioSamples, AudioClippedSamples,
    TranscriptPartial, TranscriptFinal,
    QuestionFrozen, AnswerStarted, ProviderLaunch, ProviderVersion, ProviderReady, ModelStarted,
    FirstAnswerText, FirstAnswerVisible, AnswerCompleted, AnswerCancelled, AnswerFailed, RequestCleanup, LoggingDropped, ExceptionFrame
}

public enum DiagnosticContent { TranscriptFinal, SubmittedQuestion, Answer }

public sealed record DiagnosticRecord(
    DateTimeOffset Utc, long MonotonicTicks, int ProcessId, DiagnosticEvent Event,
    Guid? CorrelationId = null, long? ElapsedMs = null, long? Count = null,
    string? Code = null, string? Provider = null, DiagnosticContent? ContentKind = null,
    string? Text = null, Guid? RelatedId = null);

public sealed class JsonlDiagnosticLog : IDisposable
{
    private readonly string directory;
    private readonly long maximumFileBytes;
    private readonly int maximumFiles;
    private readonly Channel<DiagnosticRecord> queue;
    private readonly Task writer;
    private readonly string identity = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Environment.ProcessId}-{Guid.NewGuid():N}";
    private string? currentFile;
    private int sequence;
    private int disposed;
    private long dropped;
    private string? failureCode;

    public JsonlDiagnosticLog(string directory, long maximumFileBytes = 2_097_152,
        int maximumFiles = 10, int queueCapacity = 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumFileBytes, 1024);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumFiles, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(queueCapacity, 1);
        this.directory = Path.GetFullPath(directory);
        this.maximumFileBytes = maximumFileBytes;
        this.maximumFiles = maximumFiles;
        queue = Channel.CreateBounded<DiagnosticRecord>(new BoundedChannelOptions(queueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait, SingleReader = true
        });
        writer = WriteAsync();
    }

    public string? FailureCode => Volatile.Read(ref failureCode);
    public long DroppedRecords => Interlocked.Read(ref dropped);

    public bool TryWrite(DiagnosticRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (Volatile.Read(ref disposed) != 0 || !queue.Writer.TryWrite(record))
        {
            Interlocked.Increment(ref dropped);
            return false;
        }
        return true;
    }

    private async Task WriteAsync()
    {
        await foreach (var record in queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                Directory.CreateDirectory(directory);
                if (new DirectoryInfo(directory).Attributes.HasFlag(FileAttributes.ReparsePoint))
                    throw new IOException("Diagnostic directory cannot redirect.");
                var line = JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine;
                var bytes = Encoding.UTF8.GetByteCount(line);
                if (bytes > maximumFileBytes)
                {
                    Interlocked.Increment(ref dropped);
                    Volatile.Write(ref failureCode, "record_limit");
                    continue;
                }
                if (currentFile is null || (File.Exists(currentFile) && new FileInfo(currentFile).Length + bytes > maximumFileBytes))
                    currentFile = Path.Combine(directory, $"events-{identity}-{++sequence:D4}.jsonl");
                await File.AppendAllTextAsync(currentFile, line, new UTF8Encoding(false)).ConfigureAwait(false);
                Prune();
                Volatile.Write(ref failureCode, null);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Interlocked.Increment(ref dropped);
                Volatile.Write(ref failureCode, "diagnostic_io");
                Trace.TraceError("Phantom Fudge local diagnostics could not be written.");
            }
        }
    }

    private void Prune()
    {
        var files = new DirectoryInfo(directory).EnumerateFiles("events-*.jsonl")
            .Where(file => !file.Attributes.HasFlag(FileAttributes.ReparsePoint))
            .Where(file => !string.Equals(file.FullName, currentFile, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(file => file.LastWriteTimeUtc).ToArray();
        foreach (var file in files.Skip(maximumFiles - 1))
            file.Delete();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;
        queue.Writer.TryComplete();
        if (!writer.Wait(TimeSpan.FromSeconds(3)))
        {
            Volatile.Write(ref failureCode, "flush_timeout");
            Trace.TraceError("Phantom Fudge diagnostics flush exceeded its deadline.");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };
}

public static class LocalDiagnostics
{
    public static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhantomFudge", "diagnostics");
    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhantomFudge", "diagnostics.json");
    private static readonly Lazy<JsonlDiagnosticLog> Sink = new(() => new(DirectoryPath));
    private static volatile bool includeText;

    public static string? FailureCode => Sink.IsValueCreated ? Sink.Value.FailureCode : null;
    public static long DroppedRecords => Sink.IsValueCreated ? Sink.Value.DroppedRecords : 0;
    public static bool IncludeText => includeText;

    public static void ReloadSettings()
    {
        includeText = false;
        if (!File.Exists(SettingsPath))
            return;
        using var stream = File.OpenRead(SettingsPath);
        if (stream.Length > 1024)
            throw new InvalidDataException("Diagnostic settings are too large.");
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1 ||
            !root.TryGetProperty("includeText", out var enabled) ||
            enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("Diagnostic settings must contain only includeText.");
        includeText = enabled.GetBoolean();
    }

    public static void Record(DiagnosticEvent kind, Guid? id = null, long? elapsedMs = null,
        long? count = null, string? code = null, string? provider = null, Guid? relatedId = null)
    {
        if (!Enum.IsDefined(kind) || elapsedMs is < 0 || count is < 0 || !SafeCode(code) ||
            provider is not (null or "Agency" or "Copilot" or "LocalModel" or "SnapshotOnly" or "Audio" or "Application"))
            throw new ArgumentException("Diagnostics accept only bounded metadata, not arbitrary text.");
        Sink.Value.TryWrite(new(DateTimeOffset.UtcNow, Stopwatch.GetTimestamp(), Environment.ProcessId,
            kind, id, elapsedMs, count, code, provider, RelatedId: relatedId));
    }

    public static void RecordContent(DiagnosticContent kind, string text, Guid? id = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (!includeText)
            return;
        var truncated = text.Length > 32_768;
        Sink.Value.TryWrite(new(DateTimeOffset.UtcNow, Stopwatch.GetTimestamp(), Environment.ProcessId,
            kind == DiagnosticContent.TranscriptFinal ? DiagnosticEvent.TranscriptFinal :
                kind == DiagnosticContent.SubmittedQuestion ? DiagnosticEvent.QuestionFrozen : DiagnosticEvent.AnswerCompleted,
            id, Count: text.Length, Code: truncated ? "content_truncated" : "content_opt_in",
            ContentKind: kind, Text: truncated ? text[..32_768] : text));
    }

    public static void RecordException(DiagnosticEvent kind, Exception error, Guid? id = null, string? provider = null)
    {
        ArgumentNullException.ThrowIfNull(error);
        Record(kind, id, code: error.GetType().Name, provider: provider);
        var current = error;
        for (var depth = 0; current is not null && depth < 4; depth++, current = current.InnerException)
        {
            foreach (var frame in new StackTrace(current, false).GetFrames().Take(8))
            {
                var method = frame.GetMethod();
                var name = (method?.DeclaringType?.Name ?? "Unknown") + "." + (method?.Name ?? "Unknown");
                var safe = new string(name.Select(character =>
                    char.IsAsciiLetterOrDigit(character) || "_.-".Contains(character) ? character : '_').Take(96).ToArray());
                Record(DiagnosticEvent.ExceptionFrame, id, count: depth, code: safe, provider: provider);
            }
        }
    }

    public static void FlushAndStop()
    {
        if (Sink.IsValueCreated)
            Sink.Value.Dispose();
    }

    private static bool SafeCode(string? value) => value is null || (value.Length is > 0 and <= 96 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || "_-.:".Contains(character)));
}
