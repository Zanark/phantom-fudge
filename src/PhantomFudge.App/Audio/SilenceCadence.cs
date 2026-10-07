using System.Diagnostics;

namespace PhantomFudge.App.Audio;

// The caller holds the same audio lock for real packets and silence scheduling.
internal sealed class SilenceCadence
{
    private long _lastPacket;
    private long _lastSilence;

    public void ObservePacket(long now) => _lastPacket = _lastSilence = now;

    public bool TrySchedule(long now)
    {
        if (Stopwatch.GetElapsedTime(_lastPacket, now).TotalMilliseconds < 250 ||
            Stopwatch.GetElapsedTime(_lastSilence, now).TotalMilliseconds < 100)
            return false;
        _lastSilence = now;
        return true;
    }
}
