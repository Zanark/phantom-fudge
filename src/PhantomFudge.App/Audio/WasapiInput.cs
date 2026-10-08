using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using PhantomFudge.Core;

namespace PhantomFudge.App.Audio;

// Construction failed before returning ownership, and its cleanup also failed.
internal sealed class CaptureReleaseException : Exception { }

// One owner per selected endpoint. Construction and disposal must stay off the UI thread.
internal sealed class WasapiInput : IWaveIn
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly RenderEndpointNotification _notifications;
    private MMDevice? _device;
    private WasapiCapture? _capture;
    private bool _registered;

    public WasapiInput(AudioSource source, string deviceId, Action disconnected)
    {
        _notifications = new RenderEndpointNotification(deviceId, disconnected);
        try
        {
            Marshal.ThrowExceptionForHR(_enumerator.RegisterEndpointNotificationCallback(_notifications));
            _registered = true;
            DataFlow direction = Direction(source);
            foreach (MMDevice candidate in _enumerator.EnumerateAudioEndPoints(direction, DeviceState.Active))
            {
                bool selected = false;
                try
                {
                    if (MatchesSelection(deviceId, source, candidate.ID, candidate.DataFlow, candidate.State))
                    {
                        _device = candidate;
                        selected = true;
                        break;
                    }
                }
                finally
                {
                    if (!selected)
                        candidate.Dispose();
                }
            }
            if (_device is null)
                throw new InvalidOperationException("The selected audio device is not active in the required direction.");
            _capture = source == AudioSource.Computer
                ? new WasapiLoopbackCapture(_device)
                : new WasapiCapture(_device);
            // NAudio 2.2.1 supplies AutoConvertPcm | SrcDefaultQuality for shared capture,
            // including loopback. Keep separate resampled tracks, never a mixed waveform.
            _capture.ShareMode = AudioClientShareMode.Shared;
            _capture.WaveFormat = new WaveFormat(TranscriptionSocket.SampleRate, 16, 1);
        }
        catch
        {
            try { Dispose(); }
            catch { throw new CaptureReleaseException(); }
            throw;
        }
    }

    internal static DataFlow Direction(AudioSource source) => source switch
    {
        AudioSource.Computer => DataFlow.Render,
        AudioSource.Microphone => DataFlow.Capture,
        _ => throw new ArgumentOutOfRangeException(nameof(source))
    };

    internal static bool MatchesSelection(string requestedId, AudioSource source, string actualId,
        DataFlow actualDirection, DeviceState state) =>
        string.Equals(requestedId, actualId, StringComparison.Ordinal) &&
        actualDirection == Direction(source) && state == DeviceState.Active;

    public WaveFormat WaveFormat
    {
        get => _capture!.WaveFormat;
        set => _capture!.WaveFormat = value;
    }

    public event EventHandler<WaveInEventArgs>? DataAvailable
    {
        add => _capture!.DataAvailable += value;
        remove => _capture!.DataAvailable -= value;
    }

    public event EventHandler<StoppedEventArgs>? RecordingStopped
    {
        add => _capture!.RecordingStopped += value;
        remove => _capture!.RecordingStopped -= value;
    }

    public void StartRecording() => _capture!.StartRecording();
    public void StopRecording() => _capture!.StopRecording();

    public void Dispose()
    {
        try
        {
            // Dispose joins the capture thread before releasing the endpoint or notifications.
            _capture?.Dispose();
        }
        finally
        {
            try
            {
                if (_registered)
                {
                    _registered = false;
                    Marshal.ThrowExceptionForHR(_enumerator.UnregisterEndpointNotificationCallback(_notifications));
                }
            }
            finally
            {
                try { _device?.Dispose(); }
                finally { _enumerator.Dispose(); }
            }
        }
    }
}
