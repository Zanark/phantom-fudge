namespace PhantomFudge.Core;

public enum AudioSource : byte
{
    Computer = 1,
    Microphone = 2
}

public enum AudioCaptureMode
{
    Computer,
    Microphone,
    Both
}

public static class AudioSourceNames
{
    public static string WireName(this AudioSource source) => source switch
    {
        AudioSource.Computer => "computer",
        AudioSource.Microphone => "microphone",
        _ => throw new ArgumentOutOfRangeException(nameof(source))
    };

    public static string DisplayName(this AudioSource source) => source switch
    {
        AudioSource.Computer => "Computer",
        AudioSource.Microphone => "Mic",
        _ => throw new ArgumentOutOfRangeException(nameof(source))
    };
}
