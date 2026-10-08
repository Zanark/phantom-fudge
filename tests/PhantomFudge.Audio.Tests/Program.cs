using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using PhantomFudge.App.Audio;

return await AudioTests.RunAsync(args);

internal static partial class AudioTests
{
    internal const string Token = "synthetic-test-token-not-a-real-secret";
    private const string Ready = """{"type":"ready","sampleRate":16000,"channels":1,"encoding":"pcm_s16le"}""";
    private static int _passed;

    public static async Task<int> RunAsync(string[] filters)
    {
        if (filters.Length != 0 && filters[0] == "--service")
            return await RunSyntheticServiceCheckAsync(filters);
        if (filters.Length != 0 && filters[0] == "--service-v2")
            return await RunDualSyntheticServiceCheckAsync(filters);
        if (filters.Length != 0 && filters[0] == "--playback")
            return await PlaybackCheck.RunAsync(filters);
        (string Name, Func<Task> Test)[] tests =
        [
            ("literal loopback URI allowlist", EndpointValidationAsync),
            ("PCM RMS level", LevelAsync),
            ("synthetic silence cadence suppresses recent real packets", SilenceCadenceAsync),
            ("selected-device loss only, without default switching", DeviceNotificationAsync),
            ("ordered PCM, fragmented replies, partials and final drain", HappyPathAsync),
            ("wrong bearer token", WrongTokenAsync),
            ("HTTP redirects are not followed", RedirectAsync),
            ("invalid ready before audio capture", InvalidReadyAsync),
            ("startup cancellation", CancellationAsync),
            ("Stop cancels pending and queued Start", () => StopDuringStartupAsync(dispose: false)),
            ("Dispose cancels pending Start", () => StopDuringStartupAsync(dispose: true)),
            ("malformed protocol schemas", MalformedMessagesAsync),
            ("duplicate final IDs preserve previous finals", DuplicateFinalAsync),
            ("oversized inbound message", OversizedMessageAsync),
            ("binary server frames are rejected", () => AssertFaultResponseAsync("synthetic", WebSocketMessageType.Binary)),
            ("disconnect stops visibly", DisconnectAsync),
            ("byte-budget backpressure", BackpressureAsync),
            ("invalid PCM frames", InvalidPcmAsync),
            ("missing stopped acknowledgement times out", StopTimeoutAsync),
            ("repeated stop and dispose", IdempotentDisposeAsync),
            .. DualSourceTests()
        ];
        foreach (var test in tests)
        {
            if (filters.Length != 0 && !filters.Any(filter => test.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                continue;
            try
            {
                await test.Test().WaitAsync(TimeSpan.FromSeconds(30));
                Console.WriteLine($"PASS {test.Name}");
                _passed++;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"FAIL {test.Name}: {error.GetType().Name} — {error.Message}");
                return 1;
            }
        }
        Console.WriteLine($"{_passed} synthetic audio/transport checks passed. No audio endpoint was captured.");
        return 0;
    }

    private static async Task<int> RunSyntheticServiceCheckAsync(string[] arguments)
    {
        if (arguments.Length != 6 ||
            (arguments[2] != "--synthetic-wave" && arguments[2] != "--synthetic-pcm") ||
            arguments[4] != "--expect")
        {
            Console.Error.WriteLine("Use --service <local-ws-uri> --synthetic-wave <16k-mono-pcm16.wav> --expect <phrase>.");
            Console.Error.WriteLine("Or use --synthetic-pcm - for in-memory, raw PCM16 mono 16 kHz on stdin.");
            return 1;
        }
        string stage = "fixture";
        string? faultMessage = null;
        var finals = new ConcurrentQueue<string>();
        int partials = 0;
        int faults = 0;
        try
        {
            string token = Environment.GetEnvironmentVariable("PHANTOM_AUDIO_TEST_TOKEN") ??
                throw new InvalidOperationException("Set PHANTOM_AUDIO_TEST_TOKEN for this process.");
            using Stream pcm = arguments[2] == "--synthetic-pcm"
                ? await ReadSyntheticPcmAsync(arguments[3])
                : new WaveFileReader(arguments[3]);
            if (pcm is WaveFileReader wave)
            {
                Check(wave.WaveFormat.Encoding == WaveFormatEncoding.Pcm && wave.WaveFormat.SampleRate == 16000 &&
                      wave.WaveFormat.Channels == 1 && wave.WaveFormat.BitsPerSample == 16,
                    "The synthetic WAV must be PCM16 mono 16 kHz.");
            }
            Check(pcm.Length is > 0 and <= TranscriptionSocket.BytesPerSecond * 60 && pcm.Length % 2 == 0 &&
                  !string.IsNullOrWhiteSpace(arguments[5]),
                "Use an owner-approved synthetic PCM16 mono 16 kHz fixture of at most 60 seconds.");
            await using var client = new TranscriptionSocket();
            client.TranscriptReceived += transcript =>
            {
                if (transcript.Type == "final")
                    finals.Enqueue(transcript.Text);
                else
                    Interlocked.Increment(ref partials);
            };
            client.Faulted += message => { faultMessage = message; Interlocked.Increment(ref faults); };
            stage = "connect";
            await client.ConnectAsync(new Uri(arguments[1]), token);
            stage = "PCM streaming";
            byte[] frame = new byte[3200];
            int read;
            while ((read = pcm.Read(frame, 0, frame.Length)) != 0)
            {
                Check(client.TryEnqueueAudio(frame.AsSpan(0, read)), "The service could not accept synthetic PCM.");
                await Task.Delay(TimeSpan.FromSeconds((double)read / TranscriptionSocket.BytesPerSecond));
            }
            Array.Clear(frame);
            stage = "final drain";
            await client.StopAsync();
            stage = "expected phrase";
            string normalized = string.Join(' ', finals);
            Check(faults == 0 && normalized.Contains(arguments[5], StringComparison.OrdinalIgnoreCase),
                "No matching final text, or the local service faulted.");
            Console.WriteLine($"PASS local recognizer: {finals.Count} finals, {partials} partials; expected synthetic phrase found.");
            Console.WriteLine("Fixture transport only; no WASAPI capture or Docker execution is established by this check.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"FAIL local synthetic fixture transport at {stage} ({error.GetType().Name}); {finals.Count} finals, {partials} partials.");
            if (faultMessage is not null)
                Console.Error.WriteLine(faultMessage);
            return 1;
        }
    }

    private static async Task<MemoryStream> ReadSyntheticPcmAsync(string path)
    {
        Check(path == "-", "Synthetic raw PCM must come from stdin, not an audio recording.");
        var memory = new MemoryStream();
        try
        {
            using Stream input = Console.OpenStandardInput();
            byte[] buffer = new byte[8192];
            int count;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while ((count = await input.ReadAsync(buffer, deadline.Token)) != 0)
            {
                Check(memory.Length + count <= TranscriptionSocket.BytesPerSecond * 60,
                    "The synthetic PCM fixture exceeds 60 seconds.");
                memory.Write(buffer, 0, count);
            }
            Array.Clear(buffer);
            memory.Position = 0;
            return memory;
        }
        catch
        {
            memory.Dispose();
            throw;
        }
    }

    private static Task EndpointValidationAsync()
    {
        foreach (string endpoint in new[]
        {
            "ws://127.0.0.1:8765/v1/transcribe", "ws://[::1]:8765/v1/transcribe",
            "ws://127.0.0.1/v1/transcribe"
        })
            TranscriptionSocket.ValidateEndpoint(new Uri(endpoint));
        foreach (string endpoint in new[]
        {
            "wss://127.0.0.1:8765/v1/transcribe", "ws://localhost:8765/v1/transcribe",
            "ws://example.com/v1/transcribe", "ws://127.0.0.2/v1/transcribe",
            "ws://127.1/v1/transcribe", "ws://2130706433/v1/transcribe",
            "ws://127.0.0.1:0/v1/transcribe", "ws://127.0.0.1/v1/transcribe?token=synthetic",
            "ws://127.0.0.1/v1/transcribe#fragment", "ws://user@127.0.0.1/v1/transcribe",
            "ws://127.0.0.1/wrong", "ws://127.0.0.1/other/../v1/transcribe",
            "ws://127.0.0.1/v1/%74ranscribe", "ws://[::ffff:127.0.0.1]/v1/transcribe"
        })
        {
            bool rejected = false;
            try { TranscriptionSocket.ValidateEndpoint(new Uri(endpoint)); }
            catch (ArgumentException) { rejected = true; }
            Check(rejected, "A noncanonical endpoint was accepted.");
        }
        return Task.CompletedTask;
    }

    private static Task LevelAsync()
    {
        Check(LoopbackTranscriber.CalculateLevel([]) == 0, "Empty level is not zero.");
        Check(LoopbackTranscriber.CalculateLevel(new byte[3200]) == 0, "Silent level is not zero.");
        Check(LoopbackTranscriber.CalculateLevel([0, 128, 0, 128]) == 1, "Full scale RMS differs.");
        Check(Math.Abs(LoopbackTranscriber.CalculateLevel([0, 64, 0, 192]) - 0.5f) < 0.0001f,
            "Signed PCM level differs.");
        return Task.CompletedTask;
    }

    private static Task SilenceCadenceAsync()
    {
        static long At(int milliseconds) => Stopwatch.Frequency * milliseconds / 1000;
        var cadence = new SilenceCadence();
        cadence.ObservePacket(At(1000));
        Check(!cadence.TrySchedule(At(1249)), "Silence was inserted while packets were recent.");
        Check(cadence.TrySchedule(At(1250)), "Idle endpoint did not receive silence.");
        Check(!cadence.TrySchedule(At(1250)) && !cadence.TrySchedule(At(1349)), "Silence repeated too quickly.");
        Check(cadence.TrySchedule(At(1350)), "Silence cadence did not continue.");
        cadence.ObservePacket(At(1400));
        Check(!cadence.TrySchedule(At(1600)), "Real output did not suppress synthetic silence.");
        Check(cadence.TrySchedule(At(1700)), "A second idle gap was not filled.");
        Check(cadence.TrySchedule(At(10000)) && !cadence.TrySchedule(At(10000)), "A delayed tick backfilled a burst.");
        return Task.CompletedTask;
    }

    private static async Task DeviceNotificationAsync()
    {
        var notified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int faults = 0;
        var listener = new RenderEndpointNotification("synthetic-render", () =>
        {
            Interlocked.Increment(ref faults);
            notified.TrySetResult();
        });
        listener.OnDefaultDeviceChanged(DataFlow.Render, Role.Communications, "synthetic-other-render");
        listener.OnDeviceRemoved("synthetic-unrelated");
        listener.OnDeviceStateChanged("synthetic-render", DeviceState.Active);
        Check(faults == 0, "An unrelated endpoint or default change changed the selected session.");
        listener.OnDeviceStateChanged("synthetic-render", DeviceState.Disabled);
        listener.OnDeviceRemoved("synthetic-render");
        await notified.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Check(faults == 1, "Selected-device loss was not reported exactly once.");
    }

    private static async Task HappyPathAsync()
    {
        byte[] fixture = Enumerable.Range(0, 9600).Select(i => (byte)(i % 251)).ToArray();
        var received = new List<byte>();
        await using var server = await TestServer.StartAsync(async (socket, cancellation) =>
        {
            await ExpectStartAsync(socket, cancellation);
            await SendAsync(socket, Ready, cancellation);
            await SendAsync(socket, """{"type":"partial","text":"unfinished"}""", cancellation);
            await SendFragmentedAsync(socket, """{"type":"partial","text":"replacement"}""", cancellation);
            while (true)
            {
                var message = await ReceiveAsync(socket, cancellation);
                if (message.Type == WebSocketMessageType.Text)
                {
                    Check(Encoding.UTF8.GetString(message.Bytes) == """{"type":"stop"}""", "Expected stop after PCM.");
                    break;
                }
                Check(message.Bytes.Length is > 0 and <= 32000 && message.Bytes.Length % 2 == 0, "Invalid outgoing PCM.");
                received.AddRange(message.Bytes);
            }
            await SendAsync(socket,
                """{"type":"final","text":"first synthetic sentence","segmentId":1,"startSeconds":0,"endSeconds":0.1}""",
                cancellation);
            await SendAsync(socket,
                """{"type":"final","text":"second synthetic sentence","segmentId":2,"startSeconds":0.1,"endSeconds":0.3}""",
                cancellation);
            await SendAsync(socket, """{"type":"stopped"}""", cancellation);
        });
        await using var client = new TranscriptionSocket();
        var transcripts = new ConcurrentQueue<TranscriptEvent>();
        var faults = new ConcurrentQueue<string>();
        client.TranscriptReceived += transcripts.Enqueue;
        client.Faulted += faults.Enqueue;
        await client.ConnectAsync(server.Endpoint, Token);
        Check(client.TryEnqueueAudio(fixture.AsSpan(0, 3200)), "First PCM frame rejected.");
        Check(client.TryEnqueueAudio(fixture.AsSpan(3200)), "Second PCM frame rejected.");
        await client.StopAsync();
        await server.Completed;
        Check(received.SequenceEqual(fixture), "PCM changed, disappeared, or duplicated.");
        Check(faults.IsEmpty, "Healthy session faulted.");
        Check(transcripts.Select(t => t.Text).SequenceEqual(
            new[] { "unfinished", "replacement", "first synthetic sentence", "second synthetic sentence" }),
            "Partial/final ordering differs.");
        Check(transcripts.Where(t => t.Type == "final").Select(t => t.SegmentId).SequenceEqual(new int?[] { 1, 2 }),
            "Final IDs changed.");
        Check(!client.IsReady, "Stopped transport is still ready.");
    }

    private static async Task WrongTokenAsync()
    {
        await using var server = await TestServer.StartAsync((_, _) => Task.CompletedTask);
        await using var client = new TranscriptionSocket();
        var faults = new ConcurrentQueue<string>();
        client.Faulted += faults.Enqueue;
        bool rejected = false;
        try { await client.ConnectAsync(server.Endpoint, "wrong-synthetic-token"); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected && !client.IsReady && faults.Count == 1, "Wrong token did not fail exactly once.");
        Check(!faults.Any(s => s.Contains("wrong-synthetic-token", StringComparison.Ordinal)), "Token escaped in an error.");
    }

    private static async Task RedirectAsync()
    {
        await using var server = await TestServer.StartAsync((_, _) => Task.CompletedTask, redirect: true);
        await using var client = new TranscriptionSocket();
        bool rejected = false;
        try { await client.ConnectAsync(server.Endpoint, Token); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected && server.RedirectFollowed == 0, "The WebSocket client followed an HTTP redirect.");
    }

    private static async Task InvalidReadyAsync()
    {
        await using var server = await TestServer.StartAsync(async (socket, cancellation) =>
        {
            await ExpectStartAsync(socket, cancellation);
            await SendAsync(socket, """{"type":"ready","sampleRate":48000,"channels":2,"encoding":"float"}""", cancellation);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellation);
        });
        await using var transcriber = new LoopbackTranscriber();
        var faults = new ConcurrentQueue<string>();
        transcriber.Faulted += faults.Enqueue;
        bool rejected = false;
        try { await transcriber.StartAsync("synthetic-device-never-opened", server.Endpoint, Token); }
        catch (InvalidOperationException) { rejected = true; }
        await transcriber.StopAsync();
        Check(rejected && !transcriber.IsRunning && faults.Count == 1, "Invalid ready did not stop startup exactly once.");
    }

    private static async Task CancellationAsync()
    {
        await using var server = await TestServer.StartAsync(async (socket, cancellation) =>
        {
            await ExpectStartAsync(socket, cancellation);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellation);
        });
        await using var client = new TranscriptionSocket();
        var faults = new ConcurrentQueue<string>();
        client.Faulted += faults.Enqueue;
        using var canceled = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        bool rejected = false;
        try { await client.ConnectAsync(server.Endpoint, Token, canceled.Token); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected && !client.IsReady && faults.Count == 1, "Startup cancellation did not fault exactly once.");
    }

    private static async Task StopDuringStartupAsync(bool dispose)
    {
        var handshake = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int starts = 0;
        await using var server = await TestServer.StartAsync(async (socket, cancellation) =>
        {
            await ExpectStartAsync(socket, cancellation);
            Interlocked.Increment(ref starts);
            handshake.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellation);
        });
        await using var transcriber = new LoopbackTranscriber();
        int faults = 0;
        transcriber.Faulted += _ => Interlocked.Increment(ref faults);
        Task first = transcriber.StartAsync("synthetic-device-never-opened", server.Endpoint, Token);
        await handshake.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task queued = transcriber.StartAsync("synthetic-device-never-opened", server.Endpoint, Token);
        Task stop = dispose ? transcriber.DisposeAsync().AsTask() : transcriber.StopAsync();
        await stop.WaitAsync(TimeSpan.FromSeconds(3));
        foreach (Task start in new[] { first, queued })
        {
            bool rejected = false;
            try { await start.WaitAsync(TimeSpan.FromSeconds(3)); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "A pending Start survived Stop/Dispose.");
        }
        Check(starts == 1 && faults == 1 && !transcriber.IsRunning,
            "Startup race opened another session or failed to stop exactly once.");
    }

    private static async Task MalformedMessagesAsync()
    {
        string[] malformed =
        [
            """{"type":"partial","text":7}""",
            """{"type":"partial","type":"partial","text":"synthetic"}""",
            """{"type":"partial","text":"synthetic","unexpected":true}""",
            """{"type":"ready","sampleRate":16000,"channels":1,"encoding":"pcm_s16le"}""",
            """{"type":"stopped"}""",
            """{"type":"unrecognized"}""",
            """{"type":"final","text":"synthetic","segmentId":0,"startSeconds":0,"endSeconds":0}""",
            """{"type":"final","text":"synthetic","segmentId":1,"startSeconds":-1,"endSeconds":0}""",
            """{"type":"final","text":"synthetic","segmentId":1,"startSeconds":1,"endSeconds":0}""",
            """{"type":"final","text":"synthetic","segmentId":1,"startSeconds":0,"endSeconds":100000}""",
            """{"type":"final","text":"synthetic","segmentId":1,"startSeconds":"invalid","endSeconds":0}""",
            """{"type":"error","code":"decoder_failed","message":"C:\\synthetic-private-model\\secret"}""",
            """not JSON""",
            "[]"
        ];
        for (int i = 0; i < malformed.Length; i++)
        {
            try
            {
                await AssertFaultResponseAsync(malformed[i]).WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (Exception error)
            {
                throw new InvalidOperationException($"Malformed case {i + 1}: {error.GetType().Name}", error);
            }
        }
    }

    private static async Task DuplicateFinalAsync()
    {
        var transcripts = new ConcurrentQueue<TranscriptEvent>();
        await using var server = await TestServer.StartAsync(async (socket, cancellation) =>
        {
            await ExpectStartAsync(socket, cancellation);
            await SendAsync(socket, Ready, cancellation);
            await SendAsync(socket,
                """{"type":"final","text":"retained","segmentId":1,"startSeconds":0,"endSeconds":0}""", cancellation);
            await SendAsync(socket,
                """{"type":"final","text":"duplicate","segmentId":1,"startSeconds":0,"endSeconds":0}""", cancellation);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellation);
        });
        await using var client = new TranscriptionSocket();
        var fault = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.TranscriptReceived += transcripts.Enqueue;
        client.Faulted += _ => fault.TrySetResult();
        try { await client.ConnectAsync(server.Endpoint, Token); }
        catch (InvalidOperationException) { }
        await fault.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(transcripts.Count == 1 && transcripts.Single().Text == "retained", "Final replay replaced or duplicated text.");
    }

    private static Task OversizedMessageAsync() =>
        AssertFaultResponseAsync("{\"type\":\"partial\",\"text\":\"" + new string('x', 33000) + "\"}");

    private static async Task DisconnectAsync()
    {
        await using var server = await TestServer.StartAsync(async (socket, cancellation) =>
        {
            await ExpectStartAsync(socket, cancellation);
            await SendAsync(socket, Ready, cancellation);
            socket.Abort();
        });
        await using var client = new TranscriptionSocket();
        var fault = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int count = 0;
        client.Faulted += _ => { Interlocked.Increment(ref count); fault.TrySetResult(); };
        try { await client.ConnectAsync(server.Endpoint, Token); }
        catch (InvalidOperationException) { }
        await fault.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(!client.IsReady && count == 1, "Disconnect did not stop the session exactly once.");
    }

    private static async Task BackpressureAsync()
    {
        await using var server = await TestServer.StartAsync(async (socket, cancellation) =>
        {
            await ExpectStartAsync(socket, cancellation);
            await SendAsync(socket, Ready, cancellation);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellation);
        });
        await using var client = new TranscriptionSocket();
        var faults = new ConcurrentQueue<string>();
        client.Faulted += faults.Enqueue;
        await client.ConnectAsync(server.Endpoint, Token);
        byte[] pcm = new byte[TranscriptionSocket.MaximumFrameBytes];
        bool accepted = true;
        for (int i = 0; i < 4096 && accepted; i++)
            accepted = client.TryEnqueueAudio(pcm);
        Check(!accepted && !client.IsReady, "Unconsumed audio did not reach the bounded byte budget.");
        Check(faults.Count == 1 && faults.Single().Contains("cannot keep up", StringComparison.Ordinal),
            "Backpressure was not reported explicitly once.");
    }

    private static async Task InvalidPcmAsync()
    {
        foreach (int size in new[] { 0, 1, 32002 })
        {
            await using var server = await TestServer.StartAsync(async (socket, cancellation) =>
            {
                await ExpectStartAsync(socket, cancellation);
                await SendAsync(socket, Ready, cancellation);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellation);
            });
            await using var client = new TranscriptionSocket();
            int faults = 0;
            client.Faulted += _ => Interlocked.Increment(ref faults);
            await client.ConnectAsync(server.Endpoint, Token);
            Check(!client.TryEnqueueAudio(new byte[size]) && faults == 1, "Invalid PCM was not rejected visibly.");
        }
    }

    private static async Task StopTimeoutAsync()
    {
        await using var server = await TestServer.StartAsync(async (socket, cancellation) =>
        {
            await ExpectStartAsync(socket, cancellation);
            await SendAsync(socket, Ready, cancellation);
            _ = await ReceiveAsync(socket, cancellation);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellation);
        });
        await using var client = new TranscriptionSocket();
        int faults = 0;
        client.Faulted += _ => Interlocked.Increment(ref faults);
        await client.ConnectAsync(server.Endpoint, Token);
        await client.StopAsync().WaitAsync(TimeSpan.FromSeconds(20));
        Check(faults == 1 && !client.IsReady, "Missing stopped acknowledgement was not bounded and faulted.");
    }

    private static async Task IdempotentDisposeAsync()
    {
        var client = new TranscriptionSocket();
        await client.StopAsync();
        await client.StopAsync();
        await client.DisposeAsync();
        await client.DisposeAsync();
        Check(!client.TryEnqueueAudio([]), "Disposed transport accepted PCM.");
        var transcriber = new LoopbackTranscriber();
        await transcriber.StopAsync();
        await transcriber.DisposeAsync();
        await transcriber.DisposeAsync();
        Check(!transcriber.IsRunning, "Disposed transcriber is running.");
    }

    private static async Task AssertFaultResponseAsync(string response, WebSocketMessageType type = WebSocketMessageType.Text)
    {
        await using var server = await TestServer.StartAsync(async (socket, cancellation) =>
        {
            await ExpectStartAsync(socket, cancellation);
            await SendAsync(socket, Ready, cancellation);
            await socket.SendAsync(Encoding.UTF8.GetBytes(response).AsMemory(), type, true, cancellation);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellation);
        });
        await using var client = new TranscriptionSocket();
        var fault = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        int faults = 0;
        client.Faulted += message => { Interlocked.Increment(ref faults); fault.TrySetResult(message); };
        try { await client.ConnectAsync(server.Endpoint, Token); }
        catch (InvalidOperationException) { }
        string safeMessage = await fault.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await client.StopAsync();
        Check(faults == 1 && !client.IsReady, "Malformed response did not stop the session exactly once.");
        Check(!safeMessage.Contains("synthetic-private", StringComparison.Ordinal), "Remote private path was surfaced.");
    }

    private static async Task ExpectStartAsync(WebSocket socket, CancellationToken cancellation)
    {
        var message = await ReceiveAsync(socket, cancellation);
        Check(message.Type == WebSocketMessageType.Text, "Start must be text.");
        using var document = JsonDocument.Parse(message.Bytes);
        JsonElement start = document.RootElement;
        Check(start.GetProperty("type").GetString() == "start" &&
              start.GetProperty("sampleRate").GetInt32() == 16000 &&
              start.GetProperty("channels").GetInt32() == 1 &&
              start.GetProperty("encoding").GetString() == "pcm_s16le", "Invalid start handshake.");
    }

    private static async Task<(WebSocketMessageType Type, byte[] Bytes)> ReceiveAsync(
        WebSocket socket, CancellationToken cancellation)
    {
        byte[] buffer = new byte[64000];
        int offset = 0;
        ValueWebSocketReceiveResult received;
        do
        {
            received = await socket.ReceiveAsync(buffer.AsMemory(offset), cancellation);
            offset += received.Count;
            Check(received.MessageType != WebSocketMessageType.Close, "Unexpected client close.");
        } while (!received.EndOfMessage);
        return (received.MessageType, buffer[..offset]);
    }

    private static ValueTask SendAsync(WebSocket socket, string message, CancellationToken cancellation) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(message).AsMemory(), WebSocketMessageType.Text, true, cancellation);

    private static async Task SendFragmentedAsync(WebSocket socket, string message, CancellationToken cancellation)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(message);
        await socket.SendAsync(bytes.AsMemory(0, 7), WebSocketMessageType.Text, false, cancellation);
        await socket.SendAsync(bytes.AsMemory(7), WebSocketMessageType.Text, true, cancellation);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class TestServer : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly CancellationTokenSource _stop = new();
        private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Uri Endpoint { get; private set; } = null!;
        public Task Completed => _completed.Task;
        public int RedirectFollowed;

        private TestServer(WebApplication app) => _app = app;

        public static async Task<TestServer> StartAsync(Func<WebSocket, CancellationToken, Task> handler, bool redirect = false)
        {
            WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            WebApplication app = builder.Build();
            var server = new TestServer(app);
            app.UseWebSockets();
            app.Map("/redirected", context =>
            {
                Interlocked.Increment(ref server.RedirectFollowed);
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            });
            app.Map("/v1/transcribe", async context =>
            {
                if (redirect)
                {
                    context.Response.StatusCode = StatusCodes.Status302Found;
                    context.Response.Headers.Location = "http://" + context.Request.Host + "/redirected";
                    return;
                }
                if (context.Request.Headers.Authorization != "Bearer " + Token)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }
                try
                {
                    using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
                    await handler(socket, server._stop.Token);
                    server._completed.TrySetResult();
                }
                catch (OperationCanceledException) when (server._stop.IsCancellationRequested)
                {
                    server._completed.TrySetResult();
                }
                catch (WebSocketException)
                {
                    // Negative protocol cases intentionally abort the client.
                    server._completed.TrySetResult();
                }
                catch (Exception error)
                {
                    server._completed.TrySetException(error);
                }
            });
            await app.StartAsync();
            var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!;
            server.Endpoint = new Uri(addresses.Addresses.Single().Replace("http://", "ws://", StringComparison.Ordinal) + "/v1/transcribe");
            return server;
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await _app.StopAsync(deadline.Token);
            await _app.DisposeAsync();
            _stop.Dispose();
        }
    }
}
