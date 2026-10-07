using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace PhantomFudge.App.Audio;

/// <summary>Captures all audio on one explicitly selected render endpoint, never a microphone.</summary>
public sealed class LoopbackTranscriber : IAsyncDisposable
{
    private static readonly TimeSpan CaptureStopTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CaptureStartTimeout = TimeSpan.FromSeconds(10);
    private static readonly byte[] SilenceFrame = new byte[TranscriptionSocket.BytesPerSecond / 10];
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _requestLock = new();
    private long _stopVersion;
    private Session? _session;
    private Task _pendingCaptureRelease = Task.CompletedTask;
    private int _captureReleaseFailed;
    private bool _disposed;

    public bool IsRunning => Volatile.Read(ref _session)?.Running == true;
    public event Action<TranscriptEvent>? TranscriptReceived;
    public event Action<string>? StatusChanged;
    public event Action<string>? Faulted;
    public event Action<float>? LevelChanged;

    public static IReadOnlyList<RenderDevice> GetRenderDevices()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var result = new List<RenderDevice>();
            foreach (MMDevice device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using (device)
                    result.Add(new RenderDevice(device.ID, device.FriendlyName));
            }
            return result;
        }
        catch (Exception)
        {
            throw new InvalidOperationException("Cannot list playback devices. Check Windows sound settings.");
        }
    }

    public async Task StartAsync(
        string deviceId,
        Uri endpoint,
        string token,
        CancellationToken cancellationToken = default)
    {
        long startVersion = Interlocked.Read(ref _stopVersion);
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_session is not null)
                throw new InvalidOperationException("Stop the current transcription session before starting another.");
            if (!_pendingCaptureRelease.IsCompleted || Volatile.Read(ref _captureReleaseFailed) != 0)
                throw new InvalidOperationException("The previous audio device is still stopping. Close the app before retrying.");

            Session session;
            lock (_requestLock)
            {
                if (startVersion != _stopVersion)
                    throw new InvalidOperationException("Local transcription startup was canceled.");
                session = new Session();
                Volatile.Write(ref _session, session);
            }
            session.Transport.TranscriptReceived += transcript => TranscriptReceived?.Invoke(transcript);
            session.Transport.Faulted += message => SignalFault(session, message);
            session.Cancellation = cancellationToken.Register(() =>
                SignalFault(session, "Local transcription was canceled. Start again when ready."));

            try
            {
                if (string.IsNullOrWhiteSpace(deviceId))
                    throw new InvalidOperationException("Select an active playback device.");
                StatusChanged?.Invoke("Connecting to the local transcription service...");
                await session.Transport.ConnectAsync(endpoint, token, session.StartCancellation.Token).ConfigureAwait(false);
                session.StartCancellation.Token.ThrowIfCancellationRequested();

                // NAudio remembers the creation SynchronizationContext for RecordingStopped.
                // Always construct it on a worker, so shutdown never depends on a blocked WPF dispatcher.
                session.CaptureStartup = Task.Run(() => StartCapture(session, deviceId), session.StartCancellation.Token);
                await session.CaptureStartup.WaitAsync(CaptureStartTimeout, session.StartCancellation.Token).ConfigureAwait(false);
                session.StartCancellation.Token.ThrowIfCancellationRequested();
                session.SilenceTask = SupplySilenceAsync(session);
                if (session.Running)
                {
                    StatusChanged?.Invoke(
                        "Transcribing all output on the selected playback device (not meeting-only). Times are audio-relative.");
                }
            }
            catch (Exception)
            {
                SignalFault(session, cancellationToken.IsCancellationRequested || session.StopRequested
                    ? "Local transcription startup was canceled."
                    : "Cannot start output transcription. Check the selected playback device, local service, token, and model.");
                await StopSessionAsync(session).ConfigureAwait(false);
                throw new InvalidOperationException(session.Failure ?? "Local transcription could not start.");
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task StopAsync()
    {
        // Cancel pending startup before waiting for the lifecycle lock; hiding must not start capture later.
        RequestStop();
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_session is { } session)
                await StopSessionAsync(session).ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        RequestStop();
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_session is { } session)
                await StopSessionAsync(session).ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private void RequestStop()
    {
        lock (_requestLock)
        {
            Interlocked.Increment(ref _stopVersion);
            Volatile.Read(ref _session)?.RequestStop();
        }
    }

    private void StartCapture(Session session, string deviceId)
    {
        session.Enumerator = new MMDeviceEnumerator();
        session.Notifications = new RenderEndpointNotification(deviceId, () =>
        {
            if (!session.StopRequested)
                SignalFault(session, "The selected playback device disconnected. Select an active output and restart.");
        });
        Marshal.ThrowExceptionForHR(session.Enumerator.RegisterEndpointNotificationCallback(session.Notifications));
        session.NotificationsRegistered = true;
        foreach (MMDevice candidate in session.Enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            bool selected = false;
            try
            {
                if (string.Equals(candidate.ID, deviceId, StringComparison.Ordinal))
                {
                    session.Device = candidate;
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
        if (session.Device is null)
            throw new InvalidOperationException("The selected playback device is no longer active.");
        session.StartCancellation.Token.ThrowIfCancellationRequested();
        session.Capture = new WasapiLoopbackCapture(session.Device)
        {
            WaveFormat = new WaveFormat(TranscriptionSocket.SampleRate, 16, 1)
        };
        session.DataHandler = (_, data) => OnAudio(session, data);
        session.StoppedHandler = (_, stopped) => OnRecordingStopped(session, stopped);
        session.Capture.DataAvailable += session.DataHandler;
        session.Capture.RecordingStopped += session.StoppedHandler;

        lock (session.AudioGate)
        {
            session.StartCancellation.Token.ThrowIfCancellationRequested();
            session.LastMeter = Stopwatch.GetTimestamp();
            session.Silence.ObservePacket(session.LastMeter);
            session.AcceptAudio = true;
        }
        session.Capture.StartRecording();
        session.StartSucceeded = true;
        lock (session.AudioGate)
            session.Running = session.Failure is null && !session.StopRequested;
        if (session.StopRequested)
            session.Capture.StopRecording();
    }

    private void OnAudio(Session session, WaveInEventArgs data)
    {
        float? level = null;
        try
        {
            lock (session.AudioGate)
            {
                if (!session.AcceptAudio || data.BytesRecorded == 0)
                    return;
                if (data.BytesRecorded < 0 || data.BytesRecorded > data.Buffer.Length || (data.BytesRecorded & 1) != 0)
                    throw new InvalidOperationException();
                long now = Stopwatch.GetTimestamp();
                session.Silence.ObservePacket(now);
                for (int offset = 0; offset < data.BytesRecorded; offset += TranscriptionSocket.MaximumFrameBytes)
                {
                    int length = Math.Min(TranscriptionSocket.MaximumFrameBytes, data.BytesRecorded - offset);
                    if (!session.Transport.TryEnqueueAudio(data.Buffer.AsSpan(offset, length)))
                        return;
                }
                if (Stopwatch.GetElapsedTime(session.LastMeter, now).TotalMilliseconds >= 100)
                {
                    session.LastMeter = now;
                    level = CalculateLevel(data.Buffer.AsSpan(0, data.BytesRecorded));
                }
            }
            if (level is { } rms)
                LevelChanged?.Invoke(rms);
        }
        catch (Exception)
        {
            SignalFault(session, "Output audio could not be processed. Check the playback device and restart transcription.");
        }
    }

    internal static float CalculateLevel(ReadOnlySpan<byte> pcm)
    {
        if (pcm.IsEmpty)
            return 0;
        double sum = 0;
        for (int i = 0; i < pcm.Length; i += 2)
        {
            double sample = BinaryPrimitives.ReadInt16LittleEndian(pcm.Slice(i, 2)) / 32768.0;
            sum += sample * sample;
        }
        return (float)Math.Clamp(Math.Sqrt(sum / (pcm.Length / 2)), 0, 1);
    }

    private async Task SupplySilenceAsync(Session session)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        try
        {
            while (await timer.WaitForNextTickAsync(session.SilenceCancellation.Token).ConfigureAwait(false))
            {
                bool emitted = false;
                lock (session.AudioGate)
                {
                    if (!session.AcceptAudio || session.StopRequested)
                        return;
                    long now = Stopwatch.GetTimestamp();
                    if (session.Silence.TrySchedule(now))
                    {
                        // WASAPI may send no packet when playback is idle. Do not backfill wall-clock
                        // gaps or race real packets; decoder times describe this supplied PCM stream.
                        if (!session.Transport.TryEnqueueAudio(SilenceFrame))
                            return;
                        session.LastMeter = now;
                        emitted = true;
                    }
                }
                if (emitted)
                    LevelChanged?.Invoke(0);
            }
        }
        catch (OperationCanceledException) when (session.SilenceCancellation.IsCancellationRequested)
        {
            // Explicit stop or an already reported fault ends synthetic silence.
        }
        catch (Exception)
        {
            SignalFault(session, "The selected playback device is unavailable. Check Windows sound settings and restart.");
        }
    }

    private void OnRecordingStopped(Session session, StoppedEventArgs stopped)
    {
        try
        {
            if (stopped.Exception is not null || !session.StopRequested)
                SignalFault(session, "Output capture stopped unexpectedly. Check the selected playback device and restart.");
        }
        finally
        {
            session.Running = false;
            session.RecordingStopped.TrySetResult();
        }
    }

    private void SignalFault(Session session, string safeMessage)
    {
        if (Interlocked.CompareExchange(ref session.Failure, safeMessage, null) is not null)
            return;
        lock (session.AudioGate)
        {
            session.AcceptAudio = false;
            session.Running = false;
        }
        session.RequestStop();
        session.Transport.Abort(safeMessage);
        // Never await this from NAudio's callback or the transport receiver: cleanup joins both.
        session.FaultCleanup = Task.Run(async () =>
        {
            await _lifecycle.WaitAsync().ConfigureAwait(false);
            try
            {
                if (ReferenceEquals(_session, session))
                    await StopSessionAsync(session).ConfigureAwait(false);
            }
            finally
            {
                _lifecycle.Release();
            }
        });
        Faulted?.Invoke(safeMessage);
    }

    private async Task StopSessionAsync(Session session)
    {
        session.RequestStop();
        try
        {
            if (session.Failure is null)
                StatusChanged?.Invoke("Stopping output capture and flushing final text...");
            await session.SilenceTask.ConfigureAwait(false);
            if (session.CaptureStartup.IsCompleted && session.Capture is { } capture)
            {
                capture.StopRecording();
                if (session.StartSucceeded)
                {
                    try
                    {
                        await session.RecordingStopped.Task.WaitAsync(CaptureStopTimeout).ConfigureAwait(false);
                    }
                    catch (TimeoutException)
                    {
                        SignalFault(session, "The playback device did not stop promptly. Close the app before restarting.");
                    }
                }
            }
        }
        catch (Exception)
        {
            SignalFault(session, "Output capture could not stop cleanly. Close the app before restarting.");
        }
        finally
        {
            lock (session.AudioGate)
            {
                session.AcceptAudio = false;
                session.Running = false;
            }

            // Dispose joins NAudio's capture thread. On a hung driver, leave ownership with this
            // worker rather than disposing COM objects under a callback or hanging the UI thread.
            _pendingCaptureRelease = Task.Run(async () =>
            {
                try
                {
                    await session.CaptureStartup.ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Startup may have failed after its caller timed out; it still owns the device.
                    SignalFault(session, "Output capture startup failed. Check the playback device and restart.");
                }
                ReleaseCapture(session);
            });
            try
            {
                await _pendingCaptureRelease.WaitAsync(CaptureStopTimeout).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                SignalFault(session, "The playback driver is not responding. Close the app before restarting transcription.");
            }

            try
            {
                await session.Transport.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                session.Cancellation.Dispose();
                if (ReferenceEquals(_session, session))
                    Volatile.Write(ref _session, null);
                LevelChanged?.Invoke(0);
                if (session.Failure is null)
                    StatusChanged?.Invoke("Transcription stopped.");
            }
        }
    }

    private void ReleaseCapture(Session session)
    {
        try
        {
            session.Capture?.Dispose();
        }
        catch (Exception)
        {
            Interlocked.Exchange(ref _captureReleaseFailed, 1);
            SignalFault(session, "The playback device could not be released cleanly. Close the app before restarting.");
        }
        finally
        {
            if (session.Capture is { } capture)
            {
                capture.DataAvailable -= session.DataHandler;
                capture.RecordingStopped -= session.StoppedHandler;
            }
            try
            {
                if (session.NotificationsRegistered)
                {
                    Marshal.ThrowExceptionForHR(session.Enumerator!
                        .UnregisterEndpointNotificationCallback(session.Notifications));
                }
            }
            catch (Exception)
            {
                Interlocked.Exchange(ref _captureReleaseFailed, 1);
                SignalFault(session, "Playback notifications could not be released cleanly. Close the app before restarting.");
            }
            finally
            {
                try
                {
                    try
                    {
                        session.Device?.Dispose();
                    }
                    finally
                    {
                        session.Enumerator?.Dispose();
                    }
                }
                catch (Exception)
                {
                    Interlocked.Exchange(ref _captureReleaseFailed, 1);
                    SignalFault(session, "The playback device could not be released cleanly. Close the app before restarting.");
                }
                finally
                {
                    session.DisposeCancellation();
                }
            }
        }
    }

    private sealed class Session
    {
        private readonly object _cancellationLock = new();
        private bool _cancellationDisposed;
        public readonly object AudioGate = new();
        public readonly SilenceCadence Silence = new();
        public readonly TranscriptionSocket Transport = new();
        public readonly CancellationTokenSource StartCancellation = new();
        public readonly CancellationTokenSource SilenceCancellation = new();
        public readonly TaskCompletionSource RecordingStopped =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationTokenRegistration Cancellation;
        public MMDevice? Device;
        public MMDeviceEnumerator? Enumerator;
        public RenderEndpointNotification? Notifications;
        public bool NotificationsRegistered;
        public WasapiLoopbackCapture? Capture;
        public EventHandler<WaveInEventArgs>? DataHandler;
        public EventHandler<StoppedEventArgs>? StoppedHandler;
        public Task SilenceTask = Task.CompletedTask;
        public Task CaptureStartup = Task.CompletedTask;
        public Task FaultCleanup = Task.CompletedTask;
        public volatile bool Running;
        public volatile bool StopRequested;
        public bool AcceptAudio;
        public bool StartSucceeded;
        public string? Failure;
        public long LastMeter;

        public void RequestStop()
        {
            lock (_cancellationLock)
            {
                StopRequested = true;
                if (!_cancellationDisposed)
                {
                    StartCancellation.Cancel();
                    SilenceCancellation.Cancel();
                }
            }
        }

        public void DisposeCancellation()
        {
            lock (_cancellationLock)
            {
                _cancellationDisposed = true;
                StartCancellation.Dispose();
                SilenceCancellation.Dispose();
            }
        }
    }
}
