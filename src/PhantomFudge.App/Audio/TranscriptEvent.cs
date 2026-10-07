namespace PhantomFudge.App.Audio;

public sealed record RenderDevice(string Id, string Name);

public sealed record TranscriptEvent(
    string Type,
    string Text,
    int? SegmentId = null,
    double? StartSeconds = null,
    double? EndSeconds = null);
