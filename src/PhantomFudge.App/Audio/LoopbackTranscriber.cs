using System.Buffers.Binary;
using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using PhantomFudge.Core;
using PhantomFudge.Core.Diagnostics;

namespace PhantomFudge.App.Audio;

/// <summary>Captures explicitly selected computer and/or microphone endpoints as independent tracks.</summary>
public sealed class LoopbackTranscriber : IAsyncDisposable
{
    private static readonly TimeSpan CaptureStopTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CaptureStartTimeout = TimeSpan.FromSeconds(10);
    private static readonly byte[] SilenceFrame = new byte[TranscriptionSocket.BytesPerSecond / 10];
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _requestLock = new();
    private readonly Func<AudioSource, string, Action, IWaveIn> _openInput;
    private long _stopVersion;
    private Session? _session;
    private Task _pendingCaptureRelease = Task.CompletedTask;
    private int _captureReleaseFailed;
    private bool _disposed;

    public LoopbackTranscriber() : this((source, id, disconnected) => new WasapiInput(source, id, disconnected)) { }

    // Tests substitute synthetic IWaveIn objects; production always owns an explicit WASAPI endpoint.
    internal LoopbackTranscriber(Func<AudioSource, string, Action, IWaveIn> openInput) => _openInput = openInput;

    public bool IsRunning => Volatile.Read(ref _session)?.Running == true;
    public event Action<TranscriptEvent>? TranscriptReceived;
    public event Action<string>? StatusChanged;
    public event Action<string>? Faulted;
    /// <summary>Maximum of the latest levels from all selected inputs; zero after stop.</summary>
    public event Action<float>? LevelChanged;
    public event Action<AudioSource, float>? SourceLevelChanged;

    public static IReadOnlyList<RenderDevice> GetRenderDevices() =>
        EnumerateDevices(AudioSource.Computer).Select(device => new RenderDevice(device.Id, device.Name)).ToArray();

    public static IReadOnlyList<MicrophoneDevice> GetMicrophoneDevices() =>
        EnumerateDevices(AudioSource.Microphone).Select(device => new MicrophoneDevice(device.Id, device.Name)).ToArray();

    private static IReadOnlyList<(string Id, string Name)> EnumerateDevices(AudioSource source)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var result = new List<(string, string)>();
            DataFlow direction = WasapiInput.Direction(source);
            foreach (MMDevice device in enumerator.EnumerateAudioEndPoints(direction, DeviceState.Active))
            {
                using (device)
                    if (device.DataFlow == direction && device.State == DeviceState.Active)
                        result.Add((device.ID, device.FriendlyName));
            }
            return result;
        }
        catch (Exception)
        {
            throw new InvalidOperationException("Cannot list the selected audio inputs. Check Windows sound settings.");
        }
    }

    public Task StartAsync(string deviceId, Uri endpoint, string token, CancellationToken cancellationToken = default) =>
        StartCoreAsync(new AudioInputSelection(deviceId, null), endpoint, token, false, cancellationToken, Guid.NewGuid());

    public Task StartAsync(AudioInputSelection selection, Uri endpoint, string token,
        CancellationToken cancellationToken = default, Guid? captureSessionId = null) =>
        StartCoreAsync(selection, endpoint, token, true, cancellationToken, captureSessionId ?? Guid.NewGuid());

    private async Task StartCoreAsync(AudioInputSelection selection, Uri endpoint, string token, bool version2,
        CancellationToken cancellationToken, Guid captureSessionId)
    {
        if (captureSessionId == Guid.Empty)
            throw new ArgumentException("A capture session must have an identity.", nameof(captureSessionId));
        long startVersion = Interlocked.Read(ref _stopVersion);
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_session is not null)
                throw new InvalidOperationException("Stop the current transcription session before starting another.");
            if (!_pendingCaptureRelease.IsCompleted || Volatile.Read(ref _captureReleaseFailed) != 0)
                throw new InvalidOperationException("The previous audio device is still stopping. Close the app before retrying.");
            Input[] inputs = ValidateSelection(selection);
            Session session;
            lock (_requestLock)
            {
                if (startVersion != _stopVersion)
                    throw new InvalidOperationException("Local transcription startup was canceled.");
                session = new Session(inputs, captureSessionId);
                Volatile.Write(ref _session, session);
            }
            session.Transport.TranscriptReceived += transcript =>
                TranscriptReceived?.Invoke(transcript with { CaptureSessionId = session.Id });
            session.Transport.Faulted += message => SignalFault(session, message);
            session.Cancellation = cancellationToken.Register(() =>
                SignalFault(session, "Local transcription was canceled. Start again when ready."));
            try
            {
                StatusChanged?.Invoke("Connecting to the local transcription service...");
                if (version2)
                    await session.Transport.ConnectAsync(endpoint, token, inputs.Select(input => input.Source).ToArray(),
                        session.StartCancellation.Token).ConfigureAwait(false);
                else
                    await session.Transport.ConnectAsync(endpoint, token, session.StartCancellation.Token).ConfigureAwait(false);
                session.StartCancellation.Token.ThrowIfCancellationRequested();

                // NAudio captures SynchronizationContext at construction. No native construction
                // or join may depend on a WPF dispatcher, including a late-returning hung startup.
                foreach (Input input in inputs)
                    input.Startup = Task.Run(() => StartCapture(session, input), session.StartCancellation.Token);
                await Task.WhenAll(inputs.Select(input => input.Startup))
                    .WaitAsync(CaptureStartTimeout, session.StartCancellation.Token).ConfigureAwait(false);
                session.StartCancellation.Token.ThrowIfCancellationRequested();
                lock (session.StateGate)
                {
                    session.Running = session.Failure is null && !session.StopRequested &&
                        inputs.All(input => input.StartSucceeded && !input.RecordingStopped.Task.IsCompleted);
                }
                if (!session.Running)
                    throw new InvalidOperationException("The selected inputs did not start.");
                foreach (Input input in inputs)
                    input.SilenceTask = SupplySilenceAsync(session, input);
                StatusChanged?.Invoke(version2
                    ? "Transcribing the selected inputs independently. Computer audio includes all selected output; times are per input."
                    : "Transcribing all output on the selected playback device (not meeting-only). Times are audio-relative.");
            }
            catch (Exception)
            {
                SignalFault(session, cancellationToken.IsCancellationRequested || session.StopRequested
                    ? "Local transcription startup was canceled."
                    : "Cannot start transcription. Check the selected inputs, Windows microphone access, local service, token, and model.");
                await StopSessionAsync(session).ConfigureAwait(false);
                throw new InvalidOperationException(session.Failure ?? "Local transcription could not start.");
            }
        }
        finally { _lifecycle.Release(); }
    }

    private static Input[] ValidateSelection(AudioInputSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if ((selection.ComputerDeviceId is null && selection.MicrophoneDeviceId is null) ||
            (selection.ComputerDeviceId is not null && string.IsNullOrWhiteSpace(selection.ComputerDeviceId)) ||
            (selection.MicrophoneDeviceId is not null && string.IsNullOrWhiteSpace(selection.MicrophoneDeviceId)) ||
            (selection.ComputerDeviceId is not null &&
                string.Equals(selection.ComputerDeviceId, selection.MicrophoneDeviceId, StringComparison.Ordinal)))
            throw new InvalidOperationException("Select a distinct active device for each requested input.");
        var inputs = new List<Input>(2);
        if (selection.ComputerDeviceId is { } computer)
            inputs.Add(new Input(AudioSource.Computer, computer));
        if (selection.MicrophoneDeviceId is { } microphone)
            inputs.Add(new Input(AudioSource.Microphone, microphone));
        return inputs.ToArray();
    }

    public async Task StopAsync()
    {
        RequestStop();
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_session is { } session)
                await StopSessionAsync(session).ConfigureAwait(false);
        }
        finally { _lifecycle.Release(); }
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
        finally { _lifecycle.Release(); }
    }

    private void RequestStop()
    {
        lock (_requestLock)
        {
            Interlocked.Increment(ref _stopVersion);
            Volatile.Read(ref _session)?.RequestStop();
        }
    }

    private void StartCapture(Session session, Input input)
    {
        try
        {
            session.StartCancellation.Token.ThrowIfCancellationRequested();
            input.Capture = _openInput(input.Source, input.DeviceId, () =>
            {
                if (!session.StopRequested)
                    SignalFault(session, "A selected audio device disconnected. Check both selections and restart.");
            });
            session.StartCancellation.Token.ThrowIfCancellationRequested();
            input.DataHandler = (_, data) => OnAudio(session, input, data);
            input.StoppedHandler = (_, stopped) => OnRecordingStopped(session, input, stopped);
            input.Capture.DataAvailable += input.DataHandler;
            input.Capture.RecordingStopped += input.StoppedHandler;
            lock (input.AudioGate)
            {
                session.StartCancellation.Token.ThrowIfCancellationRequested();
                input.LastMeter = Stopwatch.GetTimestamp();
                input.Silence.ObservePacket(input.LastMeter);
                input.AcceptAudio = true;
            }
            input.Capture.StartRecording();
            input.StartSucceeded = true;
        }
        catch (CaptureReleaseException)
        {
            Interlocked.Exchange(ref _captureReleaseFailed, 1);
            SignalFault(session, "An audio input could not be released after startup failed. Close the app before restarting.");
            throw;
        }
        catch (OperationCanceledException) when (session.StopRequested) { throw; }
        catch
        {
            SignalFault(session, "A selected audio input could not start. Check its device and Windows microphone access.");
            throw;
        }
    }

    private void OnAudio(Session session, Input input, WaveInEventArgs data)
    {
        float? level = null;
        try
        {
            lock (input.AudioGate)
            {
                if (!input.AcceptAudio || data.BytesRecorded == 0)
                    return;
                if (data.BytesRecorded < 0 || data.BytesRecorded > data.Buffer.Length || (data.BytesRecorded & 1) != 0)
                    throw new InvalidOperationException();
                long now = Stopwatch.GetTimestamp();
                if (input.StatisticsStarted == 0)
                    input.StatisticsStarted = now;
                input.StatisticsSamples += data.BytesRecorded / 2;
                for (var sampleOffset = 0; sampleOffset < data.BytesRecorded; sampleOffset += 2)
                {
                    var sample = BinaryPrimitives.ReadInt16LittleEndian(data.Buffer.AsSpan(sampleOffset, 2));
                    if (sample is >= 32760 or <= -32760)
                        input.StatisticsClipped++;
                }
                if (Stopwatch.GetElapsedTime(input.StatisticsStarted, now).TotalSeconds >= 1)
                {
                    var elapsed = (long)Stopwatch.GetElapsedTime(input.StatisticsStarted, now).TotalMilliseconds;
                    LocalDiagnostics.Record(DiagnosticEvent.AudioSamples, session.Id, elapsed,
                        input.StatisticsSamples, input.Source.WireName(), "Audio");
                    LocalDiagnostics.Record(DiagnosticEvent.AudioClippedSamples, session.Id, elapsed,
                        input.StatisticsClipped, input.Source.WireName(), "Audio");
                    input.StatisticsStarted = now;
                    input.StatisticsSamples = 0;
                    input.StatisticsClipped = 0;
                }
                input.Silence.ObservePacket(now);
                for (int offset = 0; offset < data.BytesRecorded; offset += TranscriptionSocket.MaximumFrameBytes)
                {
                    int length = Math.Min(TranscriptionSocket.MaximumFrameBytes, data.BytesRecorded - offset);
                    if (!session.Transport.TryEnqueueAudio(input.Source, data.Buffer.AsSpan(offset, length)))
                        return;
                }
                if (Stopwatch.GetElapsedTime(input.LastMeter, now).TotalMilliseconds >= 100)
                {
                    input.LastMeter = now;
                    level = CalculateLevel(data.Buffer.AsSpan(0, data.BytesRecorded));
                }
            }
            if (level is { } rms)
                PublishLevel(session, input, rms);
        }
        catch (Exception)
        {
            SignalFault(session, "Selected audio could not be processed. Check the inputs and restart transcription.");
        }
    }

    private void PublishLevel(Session session, Input input, float level)
    {
        float aggregate;
        lock (session.StateGate)
        {
            input.Level = level;
            aggregate = session.Inputs.Max(item => item.Level);
        }
        SourceLevelChanged?.Invoke(input.Source, level);
        LevelChanged?.Invoke(aggregate);
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

    private async Task SupplySilenceAsync(Session session, Input input)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        try
        {
            while (await timer.WaitForNextTickAsync(session.SilenceCancellation.Token).ConfigureAwait(false))
            {
                bool emitted = false;
                lock (input.AudioGate)
                {
                    if (!input.AcceptAudio || session.StopRequested)
                        return;
                    long now = Stopwatch.GetTimestamp();
                    if (input.Silence.TrySchedule(now))
                    {
                        // Only this track's idle interval matters. Never backfill a delayed tick,
                        // or insert silence while this input has delivered a recent real packet.
                        if (!session.Transport.TryEnqueueAudio(input.Source, SilenceFrame))
                            return;
                        input.LastMeter = now;
                        emitted = true;
                    }
                }
                if (emitted)
                    PublishLevel(session, input, 0);
            }
        }
        catch (OperationCanceledException) when (session.SilenceCancellation.IsCancellationRequested) { }
        catch (Exception)
        {
            SignalFault(session, "A selected audio input is unavailable. Check Windows sound settings and restart.");
        }
    }

    private void OnRecordingStopped(Session session, Input input, StoppedEventArgs stopped)
    {
        try
        {
            if (stopped.Exception is not null || !session.StopRequested)
                SignalFault(session, "An audio input stopped unexpectedly. Check the selected devices and restart.");
        }
        finally
        {
            lock (session.StateGate)
                session.Running = false;
            input.RecordingStopped.TrySetResult();
        }
    }

    private void SignalFault(Session session, string safeMessage)
    {
        if (Interlocked.CompareExchange(ref session.Failure, safeMessage, null) is not null)
            return;
        // Do not acquire another track's AudioGate from a callback: simultaneous failures
        // would deadlock. Transport admission and each input's volatile flag fail closed.
        foreach (Input input in session.Inputs)
            input.AcceptAudio = false;
        lock (session.StateGate)
            session.Running = false;
        session.RequestStop();
        session.Transport.Abort(safeMessage);
        _ = Task.Run(async () =>
        {
            await _lifecycle.WaitAsync().ConfigureAwait(false);
            try
            {
                if (ReferenceEquals(_session, session))
                    await StopSessionAsync(session).ConfigureAwait(false);
            }
            finally { _lifecycle.Release(); }
        });
        Faulted?.Invoke(safeMessage);
    }

    private async Task StopSessionAsync(Session session)
    {
        session.RequestStop();
        try
        {
            if (session.Failure is null)
                StatusChanged?.Invoke("Stopping selected audio inputs and flushing final text...");
        }
        finally
        {
            await FinishSessionAsync(session).ConfigureAwait(false);
        }
    }

    private async Task FinishSessionAsync(Session session)
    {
        try { await Task.WhenAll(session.Inputs.Select(input => input.SilenceTask)).ConfigureAwait(false); }
        catch (Exception)
        {
            SignalFault(session, "Audio processing could not stop cleanly. Check the selected inputs before restarting.");
        }
        // Each worker retains its endpoint until late startup/driver joins finish. Neither a
        // hung input nor its cleanup may prevent the other input from stopping and releasing.
        _pendingCaptureRelease = Task.Run(async () =>
        {
            try
            {
                await Task.WhenAll(session.Inputs.Select(input => Task.Run(() => ReleaseInputAsync(session, input))))
                    .ConfigureAwait(false);
            }
            finally { session.DisposeCancellation(); }
        });
        try
        {
            await _pendingCaptureRelease.WaitAsync(CaptureStopTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            SignalFault(session, "An audio driver is not responding. Close the app before restarting transcription.");
        }
        finally
        {
            foreach (Input input in session.Inputs)
                input.AcceptAudio = false;
            session.Running = false;
            try
            {
                await session.Transport.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                session.Cancellation.Dispose();
                if (ReferenceEquals(_session, session))
                    Volatile.Write(ref _session, null);
                foreach (Input input in session.Inputs)
                    PublishLevel(session, input, 0);
                if (session.Failure is null)
                    StatusChanged?.Invoke("Transcription stopped.");
            }
        }
    }

    private async Task ReleaseInputAsync(Session session, Input input)
    {
        try
        {
            try { await input.Startup.ConfigureAwait(false); }
            catch (OperationCanceledException) when (session.StopRequested) { }
            catch (Exception)
            {
                SignalFault(session, "Audio input startup failed. Check the selected devices and restart.");
            }
            if (input.Capture is { } capture)
            {
                capture.StopRecording();
                if (input.StartSucceeded)
                {
                    try { await input.RecordingStopped.Task.WaitAsync(CaptureStopTimeout).ConfigureAwait(false); }
                    catch (TimeoutException)
                    {
                        SignalFault(session, "An audio input did not stop promptly. Close the app before restarting.");
                    }
                }
            }
        }
        catch (Exception)
        {
            SignalFault(session, "Audio capture could not stop cleanly. Close the app before restarting.");
        }
        finally
        {
            input.AcceptAudio = false;
            try { input.Capture?.Dispose(); }
            catch (Exception)
            {
                Interlocked.Exchange(ref _captureReleaseFailed, 1);
                SignalFault(session, "An audio device could not be released cleanly. Close the app before restarting.");
            }
            finally
            {
                if (input.Capture is { } capture)
                {
                    capture.DataAvailable -= input.DataHandler;
                    capture.RecordingStopped -= input.StoppedHandler;
                }
            }
        }
    }

    private sealed class Input(AudioSource source, string deviceId)
    {
        public AudioSource Source { get; } = source;
        public string DeviceId { get; } = deviceId;
        public readonly object AudioGate = new();
        public readonly SilenceCadence Silence = new();
        public readonly TaskCompletionSource RecordingStopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IWaveIn? Capture;
        public EventHandler<WaveInEventArgs>? DataHandler;
        public EventHandler<StoppedEventArgs>? StoppedHandler;
        public Task Startup = Task.CompletedTask;
        public Task SilenceTask = Task.CompletedTask;
        public volatile bool AcceptAudio;
        public bool StartSucceeded;
        public long LastMeter;
        public long StatisticsStarted;
        public long StatisticsSamples;
        public long StatisticsClipped;
        public float Level;
    }

    private sealed class Session(Input[] inputs, Guid id)
    {
        private readonly object _cancellationLock = new();
        private bool _cancellationDisposed;
        public Input[] Inputs { get; } = inputs;
        public Guid Id { get; } = id;
        public readonly object StateGate = new();
        public readonly TranscriptionSocket Transport = new();
        public readonly CancellationTokenSource StartCancellation = new();
        public readonly CancellationTokenSource SilenceCancellation = new();
        public CancellationTokenRegistration Cancellation;
        public volatile bool Running;
        public volatile bool StopRequested;
        public string? Failure;

        public void RequestStop()
        {
            lock (StateGate)
            {
                StopRequested = true;
                Running = false;
            }
            lock (_cancellationLock)
            {
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
