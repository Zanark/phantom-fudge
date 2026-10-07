using System.Net.Http;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace PhantomFudge.App.Audio;

/// <summary>A single-use, in-memory PCM session with a local transcription service.</summary>
public sealed class TranscriptionSocket : IAsyncDisposable
{
    public const int SampleRate = 16000;
    public const int BytesPerSecond = SampleRate * 2;
    public const int MaximumFrameBytes = 32000;
    public const int MaximumPendingBytes = BytesPerSecond * 2;
    internal const int MaximumMessageBytes = 32768;

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(15);
    private static readonly byte[] StartMessage =
        """{"type":"start","sampleRate":16000,"channels":1,"encoding":"pcm_s16le"}"""u8.ToArray();
    private static readonly byte[] StopMessage = """{"type":"stop"}"""u8.ToArray();

    private readonly ClientWebSocket _socket = new();
    private readonly HttpMessageInvoker _http = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseProxy = false
    });
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly CancellationTokenSource _abort = new();
    private readonly object _queueLock = new();
    private readonly object _resourceLock = new();
    private readonly Channel<byte[]> _audio = Channel.CreateBounded<byte[]>(
        new BoundedChannelOptions(MaximumPendingBytes / 2)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
    private readonly TaskCompletionSource<bool> _ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _stopped =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Task _sender = Task.CompletedTask;
    private Task _receiver = Task.CompletedTask;
    private int _pendingBytes;
    private long _submittedBytes;
    private int _lastSegmentId;
    private int _faulted;
    private int _stopSent;
    private int _hasReady;
    private bool _acceptAudio;
    private bool _used;
    private bool _closed;
    private bool _disposed;

    public event Action<TranscriptEvent>? TranscriptReceived;
    public event Action<string>? Faulted;
    public bool IsReady => Volatile.Read(ref _hasReady) != 0 && Volatile.Read(ref _faulted) == 0;

    public static void ValidateEndpoint(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        // OriginalString rejects alternate numeric IP spellings, escaped paths and URI normalization.
        if (!endpoint.IsAbsoluteUri ||
            !Regex.IsMatch(endpoint.OriginalString,
                @"\Aws://(?:127\.0\.0\.1|\[::1\])(?::[0-9]{1,5})?/v1/transcribe\z",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
                TimeSpan.FromMilliseconds(100)) ||
            endpoint.Port is < 1 or > 65535 ||
            endpoint.AbsolutePath != "/v1/transcribe" ||
            endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0)
        {
            throw new ArgumentException(
                "Use ws://127.0.0.1:8765/v1/transcribe or the literal IPv6 loopback equivalent.",
                nameof(endpoint));
        }
    }

    public async Task ConnectAsync(Uri endpoint, string token, CancellationToken cancellationToken = default)
    {
        ValidateEndpoint(endpoint);
        if (string.IsNullOrWhiteSpace(token) || token.Length > 1024 ||
            !Regex.IsMatch(token, @"\A[A-Za-z0-9._~+/\-]+=*\z", RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100)))
        {
            throw new ArgumentException("Enter a valid local service token.", nameof(token));
        }

        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_used || _closed)
                throw new InvalidOperationException("Create a new transport for each transcription session.");
            _used = true;
            _socket.Options.SetRequestHeader("Authorization", "Bearer " + token);
            _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
            _socket.Options.KeepAliveTimeout = TimeSpan.FromSeconds(10);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _abort.Token);
            deadline.CancelAfter(ConnectTimeout);
            try
            {
                await _socket.ConnectAsync(endpoint, _http, deadline.Token).ConfigureAwait(false);
                await _socket.SendAsync(StartMessage.AsMemory(), WebSocketMessageType.Text, true, deadline.Token)
                    .ConfigureAwait(false);
                _receiver = ReceiveAsync();
                if (!await _ready.Task.WaitAsync(deadline.Token).ConfigureAwait(false))
                    throw new InvalidOperationException("The local service did not accept this session.");

                lock (_queueLock)
                {
                    if (_faulted != 0)
                        throw new InvalidOperationException("The local service disconnected during startup.");
                    _acceptAudio = true;
                }
                _sender = SendAudioAsync();
            }
            catch (Exception)
            {
                Fail(cancellationToken.IsCancellationRequested
                    ? "Local transcription startup was canceled."
                    : "Cannot start local transcription. Check the service, token, and installed model.");
                throw new InvalidOperationException(
                    "Cannot start local transcription. Check the service, token, and installed model.");
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <summary>Copies one PCM16 mono frame. A full queue faults the session; audio is never silently dropped.</summary>
    public bool TryEnqueueAudio(ReadOnlySpan<byte> pcm)
    {
        lock (_queueLock)
        {
            if (_closed || _disposed)
                return false;
        }
        if (pcm.Length == 0 || pcm.Length > MaximumFrameBytes || (pcm.Length & 1) != 0)
        {
            Fail("Invalid PCM audio frame. Stop and restart local transcription.");
            return false;
        }

        bool overflow;
        lock (_queueLock)
        {
            if (!_acceptAudio)
                return false;
            overflow = pcm.Length > MaximumPendingBytes - _pendingBytes;
            if (!overflow)
            {
                byte[] copy = pcm.ToArray();
                _pendingBytes += copy.Length;
                if (_audio.Writer.TryWrite(copy))
                {
                    _submittedBytes += copy.Length;
                    return true;
                }
                _pendingBytes -= copy.Length;
                Array.Clear(copy);
                overflow = true;
            }
        }

        if (overflow)
            Fail("Local transcription cannot keep up with output audio. Session stopped; no audio was silently skipped.");
        return false;
    }

    /// <summary>Drains queued PCM, sends stop, then receives final text and the stopped acknowledgement.</summary>
    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_closed || _disposed)
                return;
            _closed = true;
            lock (_queueLock)
            {
                _acceptAudio = false;
                _audio.Writer.TryComplete();
            }

            using var deadline = new CancellationTokenSource(FlushTimeout);
            try
            {
                if (_hasReady != 0 && _faulted == 0)
                {
                    await _sender.WaitAsync(deadline.Token).ConfigureAwait(false);
                    if (!await _stopped.Task.WaitAsync(deadline.Token).ConfigureAwait(false))
                        return;
                    await _receiver.WaitAsync(deadline.Token).ConfigureAwait(false);
                    if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                    {
                        await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "stopped", deadline.Token)
                            .ConfigureAwait(false);
                    }
                }
            }
            catch (Exception)
            {
                Fail("The local service did not finish cleanly. Some final text may be unavailable.");
            }
            finally
            {
                AbortCore();
                await Task.WhenAll(_sender, _receiver).ConfigureAwait(false);
                ClearQueuedAudio();
                Volatile.Write(ref _hasReady, 0);
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    internal void Abort(string safeMessage) => Fail(safeMessage);

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_resourceLock)
            {
                if (_disposed)
                    return;
                _disposed = true;
                _socket.Dispose();
                _http.Dispose();
                _abort.Dispose();
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private async Task SendAudioAsync()
    {
        try
        {
            await foreach (byte[] frame in _audio.Reader.ReadAllAsync(_abort.Token).ConfigureAwait(false))
            {
                try
                {
                    await _socket.SendAsync(frame.AsMemory(), WebSocketMessageType.Binary, true, _abort.Token)
                        .ConfigureAwait(false);
                }
                finally
                {
                    lock (_queueLock)
                        _pendingBytes -= frame.Length;
                    Array.Clear(frame);
                }
            }
            Volatile.Write(ref _stopSent, 1);
            await _socket.SendAsync(StopMessage.AsMemory(), WebSocketMessageType.Text, true, _abort.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_abort.IsCancellationRequested)
        {
            // Abort has already published the failure, or completed a normal stop.
        }
        catch (Exception)
        {
            Fail("The local audio connection failed. Check the service and restart transcription.");
        }
    }

    private async Task ReceiveAsync()
    {
        byte[] message = new byte[MaximumMessageBytes + 1];
        try
        {
            while (!_abort.IsCancellationRequested)
            {
                int length = 0;
                ValueWebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(message.AsMemory(length), _abort.Token).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                        throw new WebSocketException();
                    if (result.MessageType != WebSocketMessageType.Text ||
                        result.Count > MaximumMessageBytes - length)
                        throw new JsonException();
                    length += result.Count;
                    if (!result.EndOfMessage && length == MaximumMessageBytes)
                        throw new JsonException();
                } while (!result.EndOfMessage);

                using JsonDocument document = JsonDocument.Parse(message.AsMemory(0, length),
                    new JsonDocumentOptions { MaxDepth = 8 });
                if (ProcessMessage(document.RootElement))
                    return;
            }
        }
        catch (OperationCanceledException) when (_abort.IsCancellationRequested)
        {
            // Abort has already published the failure, or completed a normal stop.
        }
        catch (JsonException)
        {
            Fail("The local service sent an invalid response. Check that client and service versions match.");
        }
        catch (Exception)
        {
            Fail("The local transcription connection ended unexpectedly. Check the service and restart.");
        }
        finally
        {
            Array.Clear(message);
        }
    }

    private bool ProcessMessage(JsonElement message)
    {
        if (message.ValueKind != JsonValueKind.Object)
            throw new JsonException();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in message.EnumerateObject())
            if (!names.Add(property.Name))
                throw new JsonException();
        string type = RequiredString(message, "type", 32);
        if (type == "error")
        {
            RequireProperties(names, "type", "code", "message");
            _ = RequiredString(message, "code", 64);
            _ = RequiredString(message, "message", 1024);
            // Do not trust a remote error to be free of model paths, secrets, or transcript text.
            Fail("The local recognizer rejected the session. Check its configuration, model, and token.");
            return true;
        }
        if (_hasReady == 0)
        {
            if (type != "ready" ||
                !message.TryGetProperty("sampleRate", out var rate) || !rate.TryGetInt32(out int hz) || hz != SampleRate ||
                !message.TryGetProperty("channels", out var channels) || !channels.TryGetInt32(out int count) || count != 1 ||
                RequiredString(message, "encoding", 32) != "pcm_s16le")
                throw new JsonException();
            Volatile.Write(ref _hasReady, 1);
            _ready.TrySetResult(true);
            return false;
        }
        switch (type)
        {
            case "partial":
                RequireProperties(names, "type", "text");
                string partial = RequiredString(message, "text", 16384);
                TranscriptReceived?.Invoke(new TranscriptEvent(type, partial));
                return false;
            case "final":
                RequireProperties(names, "type", "text", "segmentId", "startSeconds", "endSeconds");
                string text = RequiredString(message, "text", 16384);
                if (!message.TryGetProperty("segmentId", out var id) || !id.TryGetInt32(out int segmentId) ||
                    segmentId <= _lastSegmentId)
                    throw new JsonException();
                double start = RequiredSeconds(message, "startSeconds");
                double end = RequiredSeconds(message, "endSeconds");
                long submittedBytes;
                lock (_queueLock)
                    submittedBytes = _submittedBytes;
                if (end < start || end > (double)submittedBytes / BytesPerSecond + 1)
                    throw new JsonException();
                _lastSegmentId = segmentId;
                TranscriptReceived?.Invoke(new TranscriptEvent(type, text, segmentId, start, end));
                return false;
            case "stopped":
                RequireProperties(names, "type");
                if (Volatile.Read(ref _stopSent) == 0)
                    throw new JsonException();
                _stopped.TrySetResult(true);
                return true;
            default:
                throw new JsonException();
        }
    }

    private static string RequiredString(JsonElement message, string name, int maximumLength)
    {
        if (!message.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new JsonException();
        string text = value.GetString()!;
        if (text.Length > maximumLength)
            throw new JsonException();
        return text;
    }

    private static double RequiredSeconds(JsonElement message, string name)
    {
        if (!message.TryGetProperty(name, out var value) || !value.TryGetDouble(out double seconds) ||
            !double.IsFinite(seconds) || seconds < 0)
            throw new JsonException();
        return seconds;
    }

    private static void RequireProperties(HashSet<string> actual, params string[] expected)
    {
        if (!actual.SetEquals(expected))
            throw new JsonException();
    }

    private void Fail(string safeMessage)
    {
        if (Volatile.Read(ref _disposed))
            return;
        if (Interlocked.CompareExchange(ref _faulted, 1, 0) != 0)
            return;
        lock (_queueLock)
        {
            _acceptAudio = false;
            _audio.Writer.TryComplete();
        }
        AbortCore();
        _ready.TrySetResult(false);
        _stopped.TrySetResult(false);
        Faulted?.Invoke(safeMessage);
    }

    private void AbortCore()
    {
        lock (_resourceLock)
        {
            if (_disposed)
                return;
            _abort.Cancel();
            _socket.Abort();
        }
    }

    private void ClearQueuedAudio()
    {
        while (_audio.Reader.TryRead(out byte[]? frame))
        {
            lock (_queueLock)
                _pendingBytes -= frame.Length;
            Array.Clear(frame);
        }
    }
}
