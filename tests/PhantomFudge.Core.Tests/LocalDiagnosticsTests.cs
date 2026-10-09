using System.Text.Json;
using PhantomFudge.Core.Diagnostics;

namespace PhantomFudge.Core.Tests;

public sealed class LocalDiagnosticsTests
{
    [Fact]
    public void MetadataLogFlushesAndDoesNotInventContent()
    {
        var directory = Path.Combine(Path.GetTempPath(), "phantom-log-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var log = new JsonlDiagnosticLog(directory))
                Assert.True(log.TryWrite(new(DateTimeOffset.UtcNow, 123, 4, DiagnosticEvent.AnswerStarted,
                    Guid.NewGuid(), Count: 42, Provider: "Agency")));
            var line = File.ReadAllLines(Directory.GetFiles(directory).Single()).Single();
            using var json = JsonDocument.Parse(line);
            Assert.Equal("AnswerStarted", json.RootElement.GetProperty("Event").GetString());
            Assert.Equal(42, json.RootElement.GetProperty("Count").GetInt64());
            Assert.False(json.RootElement.TryGetProperty("Text", out _));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void RotationBoundsFilesAndRecordSize()
    {
        var directory = Path.Combine(Path.GetTempPath(), "phantom-log-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var log = new JsonlDiagnosticLog(directory, 1024, 3, 200))
                for (var i = 0; i < 100; i++)
                    Assert.True(log.TryWrite(new(DateTimeOffset.UtcNow, i, 4, DiagnosticEvent.TranscriptPartial, Count: i)));
            var files = Directory.GetFiles(directory);
            Assert.InRange(files.Length, 1, 3);
            Assert.All(files, file => Assert.InRange(new FileInfo(file).Length, 1, 1024));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void IoFailureIsObservableWithoutThrowingAwayAppState()
    {
        var file = Path.Combine(Path.GetTempPath(), "phantom-log-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(file, "synthetic obstacle");
            using var log = new JsonlDiagnosticLog(file);
            log.TryWrite(new(DateTimeOffset.UtcNow, 1, 1, DiagnosticEvent.ApplicationStarted));
            log.Dispose();
            Assert.Equal("diagnostic_io", log.FailureCode);
            Assert.Equal(1, log.DroppedRecords);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void ArbitraryStringsCannotBeWrittenAsMetadata()
    {
        Assert.Throws<ArgumentException>(() => LocalDiagnostics.Record(DiagnosticEvent.AnswerFailed, code: "private question text"));
        Assert.Throws<ArgumentException>(() => LocalDiagnostics.Record(DiagnosticEvent.AnswerFailed, provider: "user input"));
        Assert.Throws<ArgumentException>(() => LocalDiagnostics.Record(DiagnosticEvent.AudioLevel, count: -1));
        Assert.False(LocalDiagnostics.IncludeText);
    }
}
