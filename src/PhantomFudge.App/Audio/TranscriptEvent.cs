using PhantomFudge.Core;

namespace PhantomFudge.App.Audio;

public sealed record RenderDevice(string Id, string Name);
public sealed record MicrophoneDevice(string Id, string Name);
public sealed record AudioInputSelection(string? ComputerDeviceId, string? MicrophoneDeviceId);

public sealed record TranscriptEvent(
    string Type,
    string Text,
    int? SegmentId = null,
    double? StartSeconds = null,
    double? EndSeconds = null,
    AudioSource? Source = null,
    Guid? CaptureSessionId = null);
