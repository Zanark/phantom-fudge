using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using PhantomFudge.App.Audio;
using PhantomFudge.Core;

internal static partial class AudioTests
{
    private static readonly AudioSource[] Both = [AudioSource.Computer, AudioSource.Microphone];
    private static readonly AudioInputSelection BothInputs = new("synthetic-render", "synthetic-mic");

    private static (string Name, Func<Task> Test)[] DualSourceTests() =>
    [
        ("v2 computer-only framing and drain", () => V2FramingAsync([AudioSource.Computer])),
        ("v2 mic-only framing and drain", () => V2FramingAsync([AudioSource.Microphone])),
        ("v2 both framing, independent partials, and global finals", () => V2FramingAsync(Both)),
        ("v2 synthetic recognizer helper stdin and source-specific expectations", V2HelperAsync),
        ("v2 invalid selections before hardware", V2InvalidSelectionAsync),
        ("v2 strict ready rejects downgrade before hardware", V2InvalidReadyAsync),
        ("v2 source schema and bounded inbound messages", V2MalformedAsync),
        ("v2 global IDs and per-source timestamp bounds", V2TimestampsAsync),
        ("v2 stop requires a final from every source", V2MissingFinalAsync),
        ("v2 rejects undeclared and invalid PCM sources", V2InvalidPcmAsync),
        ("v2 byte budgets independent and bounded in total", V2BackpressureAsync),
        ("v2 socket Stop and Dispose cancel pending handshake", V2SocketStopAsync),
        ("v2 cancellation before hardware", V2CancelAsync),
        ("v2 Stop invalidates queued adapter starts", () => V2QueuedStartAsync(false)),
        ("v2 Dispose invalidates queued adapter starts", () => V2QueuedStartAsync(true)),
        ("dual input endpoint direction and silence independence", DualInputPolicyAsync),
        ("single synthetic microphone adapter and v1 computer compatibility", SingleCaptureAsync),
        ("dual repeated sessions detach callbacks and reset state", DualRepeatedSessionsAsync),
        ("dual synthetic startup gate, packet copy, meters, silence and drain", DualCaptureAsync),
        ("dual input construction failure releases both", () => DualFailureAsync(true)),
        ("dual input startup failure releases both", () => DualFailureAsync(false)),
        ("dual selected-device loss and callback failure stop both", DualDeviceLossAsync),
        ("dual canceled hung startup retains ownership", DualHungStartupAsync),
        ("dual hung stop releases responsive input without blocking UI", DualHungStopAsync),
        ("dual disposal failure blocks unsafe restart", DualDisposeFailureAsync),
        ("dual failed construction cleanup blocks unsafe restart", DualConstructionCleanupAsync)
    ];

    private static string V2Ready(IEnumerable<AudioSource> sources) => JsonSerializer.Serialize(new
    {
        type = "ready", protocolVersion = 2, sampleRate = 16000, channels = 1,
        encoding = "pcm_s16le", sources = sources.Select(source => source.WireName()).ToArray()
    });

    private static string Final(AudioSource source, int id, double start = 0, double end = 0, string text = "") =>
        JsonSerializer.Serialize(new
        {
            type = "final", source = source.WireName(), text, segmentId = id,
            startSeconds = start, endSeconds = end
        });

    private static async Task ExpectV2StartAsync(WebSocket socket, CancellationToken cancellation, AudioSource[] sources)
    {
        var message = await ReceiveAsync(socket, cancellation);
        Check(message.Type == WebSocketMessageType.Text, "V2 start must be text.");
        using var document = JsonDocument.Parse(message.Bytes);
        JsonElement start = document.RootElement;
        Check(start.EnumerateObject().Count() == 6 && start.GetProperty("type").GetString() == "start" &&
              start.GetProperty("protocolVersion").GetInt32() == 2 &&
              start.GetProperty("sampleRate").GetInt32() == 16000 &&
              start.GetProperty("channels").GetInt32() == 1 &&
              start.GetProperty("encoding").GetString() == "pcm_s16le" &&
              start.GetProperty("sources").EnumerateArray().Select(value => value.GetString())
                  .SequenceEqual(sources.Select(source => source.WireName())), "V2 handshake did not match canonical sources.");
    }

    private static async Task DrainV2Async(WebSocket socket, CancellationToken cancellation, AudioSource[] sources,
        ConcurrentQueue<(AudioSource Source, byte[] Pcm)>? received = null)
    {
        while (true)
        {
            var message = await ReceiveAsync(socket, cancellation);
            if (message.Type == WebSocketMessageType.Text)
            {
                Check(Encoding.UTF8.GetString(message.Bytes) == """{"type":"stop"}""", "Stop did not follow audio.");
                break;
            }
            Check(message.Type == WebSocketMessageType.Binary && message.Bytes.Length is >= 3 and <= 32001 &&
                message.Bytes.Length % 2 == 1 && sources.Contains((AudioSource)message.Bytes[0]), "Invalid source framing.");
            received?.Enqueue(((AudioSource)message.Bytes[0], message.Bytes[1..]));
        }
        int id = 0;
        foreach (AudioSource source in sources)
            await SendAsync(socket, Final(source, ++id), cancellation);
        await SendAsync(socket, """{"type":"stopped"}""", cancellation);
    }

    private static async Task V2FramingAsync(AudioSource[] sources)
    {
        byte[] original = Enumerable.Range(0, 32000).Select(i => (byte)(i % 251)).ToArray();
        var received = new ConcurrentQueue<(AudioSource Source, byte[] Pcm)>();
        await using var server = await TestServer.StartAsync(async (socket, cancellation) =>
        {
            await ExpectV2StartAsync(socket, cancellation, sources);
            await SendFragmentedAsync(socket, V2Ready(sources), cancellation);
            foreach (AudioSource source in sources)
                await SendAsync(socket, JsonSerializer.Serialize(new
                {
                    type = "partial", source = source.WireName(), text = source.WireName() + " pending"
                }), cancellation);
            await DrainV2Async(socket, cancellation, sources, received);
        });
        await using var client = new TranscriptionSocket();
        var transcripts = new ConcurrentQueue<TranscriptEvent>();
        int faults = 0;
        client.TranscriptReceived += transcripts.Enqueue;
        client.Faulted += _ => Interlocked.Increment(ref faults);
        // Out-of-order input lists are copied and canonicalized, not written onto the wire.
        AudioSource[] mutableSources = sources.Reverse().ToArray();
        await client.ConnectAsync(server.Endpoint, Token, mutableSources);
        Array.Fill(mutableSources, (AudioSource)255);
        foreach (AudioSource source in sources)
        {
            byte[] mutable = original.ToArray();
            Check(client.TryEnqueueAudio(source, mutable), "V2 maximum frame rejected.");
            Array.Fill(mutable, (byte)0);
            Check(client.TryEnqueueAudio(source, new byte[] { 25, 44 }), "V2 minimum frame rejected.");
        }
        await Task.WhenAll(client.StopAsync(), client.StopAsync());
        await server.Completed;
        Check(faults == 0 && !client.IsReady, "Valid v2 drain faulted.");
        foreach (AudioSource source in sources)
        {
            var frames = received.Where(frame => frame.Source == source).ToArray();
            Check(frames.Length == 2 && frames[0].Pcm.SequenceEqual(original) &&
                frames[1].Pcm.SequenceEqual(new byte[] { 25, 44 }), "V2 changed or mixed copied sample bytes.");
            Check(transcripts.Any(item => item.Type == "partial" && item.Source == source) &&
                transcripts.Any(item => item.Type == "final" && item.Source == source), "Source partial/final lost.");
        }
        Check(transcripts.Where(item => item.Type == "final").Select(item => item.SegmentId)
            .SequenceEqual(Enumerable.Range(1, sources.Length).Select(id => (int?)id)), "Global IDs changed.");
    }

    private static async Task V2InvalidSelectionAsync()
    {
        var endpoint = new Uri("ws://127.0.0.1:1/v1/transcribe");
        foreach (AudioSource[] sources in new AudioSource[][] { [], [0], [(AudioSource)255],
                     [AudioSource.Computer, AudioSource.Computer], [.. Both, AudioSource.Microphone] })
        {
            await using var client = new TranscriptionSocket();
            bool rejected = false;
            try { await client.ConnectAsync(endpoint, Token, sources); }
            catch (ArgumentException) { rejected = true; }
            Check(rejected, "Invalid source declaration reached networking.");
        }
        int opened = 0;
        await using var transcriber = new LoopbackTranscriber((_, _, _) =>
        {
            Interlocked.Increment(ref opened);
            throw new InvalidOperationException("Hardware seam must not be reached.");
        });
        foreach (AudioInputSelection selection in new AudioInputSelection[]
        {
            new(null, null), new("", null), new(null, " "), new("render", ""), new("same", "same")
        })
        {
            bool rejected = false;
            try { await transcriber.StartAsync(selection, endpoint, Token); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && !transcriber.IsRunning, "Invalid input selection did not fail closed.");
        }
        Check(opened == 0, "Invalid selection opened hardware.");
        bool emptyIdentityRejected = false;
        try { await transcriber.StartAsync(BothInputs, endpoint, Token, captureSessionId: Guid.Empty); }
        catch (ArgumentException) { emptyIdentityRejected = true; }
        Check(emptyIdentityRejected && opened == 0, "An empty session identity reached networking or hardware.");
    }

    private static async Task V2HelperAsync()
    {
        byte[][] fixtures = [[0, 0, 1, 0], [2, 0]];
        await using var server = await TestServer.StartAsync(async (socket, cancellation) =>
        {
            await ExpectV2StartAsync(socket, cancellation, Both);
            await SendAsync(socket, V2Ready(Both), cancellation);
            for (int index = 0; index < Both.Length; index++)
            {
                var message = await ReceiveAsync(socket, cancellation);
                Check(message.Type == WebSocketMessageType.Binary && message.Bytes[0] == (byte)Both[index] &&
                    message.Bytes.AsSpan(1).SequenceEqual(fixtures[index]), "Helper changed source PCM.");
            }
            var stop = await ReceiveAsync(socket, cancellation);
            Check(stop.Type == WebSocketMessageType.Text &&
                Encoding.UTF8.GetString(stop.Bytes) == """{"type":"stop"}""", "Helper did not drain both tracks.");
            await SendAsync(socket, Final(AudioSource.Computer, 1, text: "computer synthetic phrase"), cancellation);
            await SendAsync(socket, Final(AudioSource.Microphone, 2, text: "microphone synthetic phrase"), cancellation);
            await SendAsync(socket, """{"type":"stopped"}""", cancellation);
        });
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (string argument in new[]
        {
            typeof(AudioTests).Assembly.Location, "--service-v2", server.Endpoint.ToString(), "--synthetic-tracks-stdin",
            "--expect-computer", "computer synthetic phrase", "--expect-microphone", "microphone synthetic phrase"
        })
            start.ArgumentList.Add(argument);
        start.Environment["PHANTOM_AUDIO_TEST_TOKEN"] = Token;
        using Process child = Process.Start(start)!;
        try
        {
            await child.StandardInput.WriteAsync(JsonSerializer.Serialize(new
            {
                computer = Convert.ToBase64String(fixtures[0]), microphone = Convert.ToBase64String(fixtures[1])
            }));
            child.StandardInput.Close();
            Task<string> output = child.StandardOutput.ReadToEndAsync();
            Task<string> errors = child.StandardError.ReadToEndAsync();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Check(child.ExitCode == 0 && (await output).Contains("PASS microphone:", StringComparison.Ordinal) &&
                string.IsNullOrWhiteSpace(await errors), "Synthetic recognizer CLI helper failed.");
            await server.Completed;
        }
        finally
        {
            if (!child.HasExited)
                child.Kill(entireProcessTree: true);
        }
    }

    private static async Task V2InvalidReadyAsync()
    {
        string ready = V2Ready(Both);
        string[] invalid =
        [
            Ready,
            ready.Replace("\"protocolVersion\":2", "\"protocolVersion\":1"),
            ready.Replace("\"sampleRate\":16000", "\"sampleRate\":48000"),
            ready.Replace("\"channels\":1", "\"channels\":2"),
            ready.Replace("pcm_s16le", "float"),
            ready.Replace("[\"computer\",\"microphone\"]", "[\"microphone\",\"computer\"]"),
            ready.Replace("[\"computer\",\"microphone\"]", "[\"computer\"]"),
            ready.Replace("[\"computer\",\"microphone\"]", "[\"computer\",\"computer\"]"),
            ready.Replace("microphone", "unknown"),
            ready.Replace("\"sources\":", "\"unexpected\":true,\"sources\":"),
            ready.Replace("\"type\":\"ready\"", "\"type\":\"ready\",\"type\":\"ready\"")
        ];
        foreach (string response in invalid)
        {
            int opened = 0, faults = 0;
            await using var server = await TestServer.StartAsync(async (socket, cancellation) =>
            {
                await ExpectV2StartAsync(socket, cancellation, Both);
                await SendAsync(socket, response, cancellation);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellation);
            });
            await using var transcriber = new LoopbackTranscriber((_, _, _) =>
            {
                Interlocked.Increment(ref opened);
                return new SyntheticInput();
            });
            transcriber.Faulted += _ => Interlocked.Increment(ref faults);
            bool rejected = false;
            try { await transcriber.StartAsync(BothInputs, server.Endpoint, Token); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && faults == 1 && opened == 0, "V2 invalid ready reached capture or did not fault once.");
        }
    }

    private static async Task V2MalformedAsync()
    {
        string[] invalid =
        [
            """{"type":"partial","text":"v1 downgrade"}""",
            """{"type":"partial","source":"unknown","text":"x"}""",
            """{"type":"partial","source":"Microphone","text":"x"}""",
            """{"type":"partial","source":2,"text":"x"}""",
            """{"type":"partial","source":"microphone","text":false}""",
            """{"type":"partial","source":"microphone","source":"computer","text":"x"}""",
            """{"type":"final","text":"v1 downgrade","segmentId":1,"startSeconds":0,"endSeconds":0}""",
            """{"type":"final","source":"microphone","text":"x","segmentId":1,"startSeconds":-1,"endSeconds":0}""",
            """{"type":"final","source":"microphone","text":"x","segmentId":1,"startSeconds":0,"endSeconds":1e400}""",
            """{"type":"final","source":"microphone","text":"x","segmentId":1,"startSeconds":1,"endSeconds":0}""",
            """{"type":"error","code":"decoder_failed","message":"synthetic-private-secret"}""",
            "{\"type\":\"partial\",\"source\":\"microphone\",\"text\":\"" + new string('x', 33000) + "\"}"
        ];
        foreach (string response in invalid)
            await AssertV2FaultAsync([response]);
        await AssertV2FaultAsync(["synthetic"], WebSocketMessageType.Binary);
        await AssertV2FaultAsync(
            ["""{"type":"partial","source":"computer","text":"undeclared"}"""], sources: [AudioSource.Microphone]);
    }

    private static async Task AssertV2FaultAsync(string[] responses,
        WebSocketMessageType messageType = WebSocketMessageType.Text, AudioSource[]? sources = null,
        Action<TranscriptionSocket>? submit = null, int expectedEvents = 0)
    {
        sources ??= Both;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = await TestServer.StartAsync(async (socket, cancellation) =>
        {
            await ExpectV2StartAsync(socket, cancellation, sources);
            await SendAsync(socket, V2Ready(sources), cancellation);
            await release.Task.WaitAsync(cancellation);
            foreach (string response in responses)
                await socket.SendAsync(Encoding.UTF8.GetBytes(response).AsMemory(), messageType, true, cancellation);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellation);
        });
        await using var client = new TranscriptionSocket();
        var fault = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var received = new ConcurrentQueue<TranscriptEvent>();
        int faults = 0;
        client.TranscriptReceived += received.Enqueue;
        client.Faulted += message => { Interlocked.Increment(ref faults); fault.TrySetResult(message); };
        await client.ConnectAsync(server.Endpoint, Token, sources);
        submit?.Invoke(client);
        release.TrySetResult();
        string safe = await fault.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await client.StopAsync();
        Check(faults == 1 && !client.IsReady && !safe.Contains("synthetic-private", StringComparison.Ordinal) &&
            received.Count == expectedEvents, "V2 fault leaked private text, invalid events, or repeated.");
    }

    private static async Task V2TimestampsAsync()
    {
        await AssertV2FaultAsync([Final(AudioSource.Computer, 1), Final(AudioSource.Microphone, 1)], expectedEvents: 1);
        await AssertV2FaultAsync([Final(AudioSource.Microphone, 2), Final(AudioSource.Computer, 1)], expectedEvents: 1);
        await AssertV2FaultAsync(
            [Final(AudioSource.Computer, 1, 0, 0.75), Final(AudioSource.Microphone, 2),
             Final(AudioSource.Computer, 3, 0.5, 0.8)], expectedEvents: 2);
        await AssertV2FaultAsync([Final(AudioSource.Microphone, 1, 0, 1.5)],
            submit: client => Check(client.TryEnqueueAudio(AudioSource.Computer, new byte[32000]), "PCM rejected."));
        await AssertV2FaultAsync([Final(AudioSource.Computer, 1, 0, 1.5)],
            submit: client => Check(client.TryEnqueueAudio(AudioSource.Microphone, new byte[32000]), "PCM rejected."));
        await using var server = await TestServer.StartAsync(async (socket, cancellation) =>
        {
            await ExpectV2StartAsync(socket, cancellation, Both);
            await SendAsync(socket, V2Ready(Both), cancellation);
            _ = await ReceiveAsync(socket, cancellation);
            await SendAsync(socket, Final(AudioSource.Computer, 1, 0, 0.8), cancellation);
            await SendAsync(socket, Final(AudioSource.Microphone, 2, 0, 0.1), cancellation);
            await SendAsync(socket, """{"type":"stopped"}""", cancellation);
        });
        await using var valid = new TranscriptionSocket();
        int faults = 0;
        valid.Faulted += _ => faults++;
        await valid.ConnectAsync(server.Endpoint, Token, Both);
        await valid.StopAsync();
        await server.Completed;
        Check(faults == 0, "Independent source clocks were compared to each other.");
    }

    private static async Task V2MissingFinalAsync()
    {
        await using var server = await TestServer.StartAsync(async (socket, cancellation) =>
        {
            await ExpectV2StartAsync(socket, cancellation, Both);
            await SendAsync(socket, V2Ready(Both), cancellation);
            _ = await ReceiveAsync(socket, cancellation);
            await SendAsync(socket, Final(AudioSource.Computer, 1), cancellation);
            await SendAsync(socket, """{"type":"stopped"}""", cancellation);
        });
        await using var client = new TranscriptionSocket();
        int faults = 0;
        client.Faulted += _ => faults++;
        await client.ConnectAsync(server.Endpoint, Token, Both);
        await client.StopAsync();
        Check(faults == 1, "Stopped without the microphone final was accepted.");
    }

    private static async Task V2InvalidPcmAsync()
    {
        foreach (var invalid in new (AudioSource Source, int Length)[]
        {
            (AudioSource.Computer, 2), ((AudioSource)0, 2), ((AudioSource)3, 2),
            (AudioSource.Microphone, 0), (AudioSource.Microphone, 1), (AudioSource.Microphone, 32002)
        })
        {
            AudioSource[] sources = [AudioSource.Microphone];
            await using var server = await TestServer.StartAsync(async (socket, cancellation) =>
            {
                await ExpectV2StartAsync(socket, cancellation, sources);
                await SendAsync(socket, V2Ready(sources), cancellation);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellation);
            });
            await using var client = new TranscriptionSocket();
            int faults = 0;
            client.Faulted += _ => faults++;
            await client.ConnectAsync(server.Endpoint, Token, sources);
            Check(!client.TryEnqueueAudio(invalid.Source, new byte[invalid.Length]) && faults == 1 && !client.IsReady,
                "Invalid PCM/source did not visibly fault v2.");
        }
    }

    private static async Task V2BackpressureAsync()
    {
        foreach (AudioSource saturated in Both)
        {
            await using var server = await TestServer.StartAsync(async (socket, cancellation) =>
            {
                await ExpectV2StartAsync(socket, cancellation, Both);
                await SendAsync(socket, V2Ready(Both), cancellation);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellation);
            });
            await using var client = new TranscriptionSocket();
            int faults = 0;
            client.Faulted += _ => faults++;
            await client.ConnectAsync(server.Endpoint, Token, Both);
            // Hold the existing admission lock to keep sender completions from releasing budget.
            // No production hook or real socket-buffer timing assumption is needed.
            object gate = typeof(TranscriptionSocket).GetField("_queueLock", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(client)!;
            lock (gate)
            {
                foreach (AudioSource source in Both)
                    for (int frame = 0; frame < 2; frame++)
                        Check(client.TryEnqueueAudio(source, new byte[32000]), "One source consumed the other's PCM budget.");
                int pending = (int)typeof(TranscriptionSocket)
                    .GetField("_pendingBytes", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(client)!;
                Check(pending == 128000, "Wire prefix was counted as PCM or total budget changed.");
                Check(!client.TryEnqueueAudio(saturated, new byte[2]), "A source exceeded its 64000-byte budget.");
            }
            Check(faults == 1 && !client.IsReady, "Backpressure did not stop both tracks visibly.");
        }
    }

    private static async Task V2SocketStopAsync()
    {
        foreach (bool dispose in new[] { false, true })
        {
            var handshake = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var server = await TestServer.StartAsync(async (socket, cancellation) =>
            {
                await ExpectV2StartAsync(socket, cancellation, Both);
                handshake.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellation);
            });
            await using var client = new TranscriptionSocket();
            int faults = 0;
            client.Faulted += _ => faults++;
            Task start = client.ConnectAsync(server.Endpoint, Token, Both);
            await handshake.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await (dispose ? client.DisposeAsync().AsTask() : client.StopAsync()).WaitAsync(TimeSpan.FromSeconds(3));
            bool rejected = false;
            try { await start; }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && faults == 1 && !client.IsReady, "V2 Stop/Dispose waited for handshake timeout.");
        }
    }

    private static async Task V2CancelAsync()
    {
        await using var server = await TestServer.StartAsync(async (socket, cancellation) =>
        {
            await ExpectV2StartAsync(socket, cancellation, Both);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellation);
        });
        int opened = 0, faults = 0;
        await using var transcriber = new LoopbackTranscriber((_, _, _) =>
        {
            opened++;
            return new SyntheticInput();
        });
        transcriber.Faulted += _ => faults++;
        using var canceled = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        bool rejected = false;
        try { await transcriber.StartAsync(BothInputs, server.Endpoint, Token, canceled.Token); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected && opened == 0 && faults == 1, "V2 cancellation opened an input.");
    }

    private static async Task V2QueuedStartAsync(bool dispose)
    {
        var handshake = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int starts = 0, opened = 0, faults = 0;
        await using var server = await TestServer.StartAsync(async (socket, cancellation) =>
        {
            await ExpectV2StartAsync(socket, cancellation, Both);
            Interlocked.Increment(ref starts);
            handshake.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellation);
        });
        await using var transcriber = new LoopbackTranscriber((_, _, _) => { opened++; return new SyntheticInput(); });
        transcriber.Faulted += _ => Interlocked.Increment(ref faults);
        Task first = transcriber.StartAsync(BothInputs, server.Endpoint, Token);
        await handshake.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Task queued = transcriber.StartAsync(BothInputs, server.Endpoint, Token);
        await (dispose ? transcriber.DisposeAsync().AsTask() : transcriber.StopAsync()).WaitAsync(TimeSpan.FromSeconds(3));
        foreach (Task start in new[] { first, queued })
        {
            bool rejected = false;
            try { await start; }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "A queued dual-input start survived Stop/Dispose.");
        }
        Check(starts == 1 && opened == 0 && faults == 1, "Stop allowed another handshake or input.");
    }

    private static Task DualInputPolicyAsync()
    {
        Check(WasapiInput.MatchesSelection("x", AudioSource.Computer, "x", DataFlow.Render, DeviceState.Active) &&
            WasapiInput.MatchesSelection("x", AudioSource.Microphone, "x", DataFlow.Capture, DeviceState.Active),
            "Correct direction was rejected.");
        Check(!WasapiInput.MatchesSelection("x", AudioSource.Microphone, "x", DataFlow.Render, DeviceState.Active) &&
            !WasapiInput.MatchesSelection("x", AudioSource.Computer, "x", DataFlow.Capture, DeviceState.Active) &&
            !WasapiInput.MatchesSelection("x", AudioSource.Microphone, "x", DataFlow.Capture, DeviceState.Disabled) &&
            !WasapiInput.MatchesSelection("x", AudioSource.Microphone, "y", DataFlow.Capture, DeviceState.Active),
            "Wrong endpoint direction, identity, or state was accepted.");
        static long At(int ms) => Stopwatch.Frequency * ms / 1000;
        var computer = new SilenceCadence();
        var microphone = new SilenceCadence();
        computer.ObservePacket(At(1000));
        microphone.ObservePacket(At(1000));
        computer.ObservePacket(At(1240));
        Check(!computer.TrySchedule(At(1250)) && microphone.TrySchedule(At(1250)),
            "Computer packets suppressed microphone idle silence.");
        microphone.ObservePacket(At(1300));
        Check(computer.TrySchedule(At(1500)) && !microphone.TrySchedule(At(1500)),
            "Microphone packets suppressed computer idle silence.");
        return Task.CompletedTask;
    }

    private static async Task DualCaptureAsync()
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handshake = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var frames = new ConcurrentQueue<(AudioSource Source, byte[] Pcm)>();
        using var micStart = new ManualResetEventSlim();
        var inputs = new ConcurrentDictionary<AudioSource, SyntheticInput>();
        var meters = new ConcurrentQueue<(AudioSource Source, float Level)>();
        var aggregates = new ConcurrentQueue<float>();
        int opened = 0, faults = 0;
        await using var server = await TestServer.StartAsync(async (socket, cancellation) =>
        {
            await ExpectV2StartAsync(socket, cancellation, Both);
            handshake.TrySetResult();
            await ready.Task.WaitAsync(cancellation);
            await SendAsync(socket, V2Ready(Both), cancellation);
            await DrainV2Async(socket, cancellation, Both, frames);
        });
        await using var transcriber = new LoopbackTranscriber((source, id, _) =>
        {
            Check(SynchronizationContext.Current is null, "Native construction inherited a UI context.");
            Check(id == (source == AudioSource.Computer ? BothInputs.ComputerDeviceId : BothInputs.MicrophoneDeviceId),
                "Selected endpoint was substituted.");
            Interlocked.Increment(ref opened);
            var input = new SyntheticInput { StartGate = source == AudioSource.Microphone ? micStart : null };
            inputs[source] = input;
            return input;
        });
        transcriber.Faulted += _ => Interlocked.Increment(ref faults);
        transcriber.SourceLevelChanged += (source, level) => meters.Enqueue((source, level));
        transcriber.LevelChanged += aggregates.Enqueue;
        try
        {
            Task startup = transcriber.StartAsync(BothInputs, server.Endpoint, Token);
            await handshake.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Check(opened == 0 && !transcriber.IsRunning, "Capture opened before v2 ready.");
            ready.TrySetResult();
            await WaitUntilAsync(() => inputs.Count == 2 && inputs.Values.All(input => input.StartEntered.IsCompleted));
            Check(!transcriber.IsRunning, "Running became true before both inputs started.");
            micStart.Set();
            await startup;
            Check(transcriber.IsRunning && inputs.Values.All(input => input.StartCount == 1), "Both inputs did not start.");
            bool switchingRejected = false;
            try { await transcriber.StartAsync(new AudioInputSelection(null, "other-mic"), server.Endpoint, Token); }
            catch (InvalidOperationException) { switchingRejected = true; }
            Check(switchingRejected && opened == 2, "Selection switched without stopping.");
            await Task.Delay(120);
            byte[] computerPcm = Enumerable.Repeat(new byte[] { 0, 128 }, 1600).SelectMany(value => value).ToArray();
            byte[] micPcm = Enumerable.Repeat(new byte[] { 0, 64 }, 1600).SelectMany(value => value).ToArray();
            inputs[AudioSource.Computer].Emit(computerPcm);
            inputs[AudioSource.Microphone].Emit(micPcm);
            Array.Clear(computerPcm);
            Array.Clear(micPcm);
            // Keep computer packets active while the microphone becomes independently idle.
            byte[] active = [1, 0];
            for (int tick = 0; tick < 12; tick++)
            {
                inputs[AudioSource.Computer].Emit(active);
                await Task.Delay(50);
            }
            await Task.WhenAll(transcriber.StopAsync(), transcriber.StopAsync());
            await server.Completed;
            Check(faults == 0 && !transcriber.IsRunning && inputs.Values.All(input =>
                input.Disposed && input.StopCount == 1 && input.HandlerCount == 0), "Dual stop did not join/release both inputs.");
            Check(frames.Any(frame => frame.Source == AudioSource.Computer && frame.Pcm.Length == 3200 &&
                frame.Pcm[1] == 128) && frames.Any(frame => frame.Source == AudioSource.Microphone &&
                frame.Pcm.Length == 3200 && frame.Pcm[1] == 64), "Reusable capture buffers were not copied per source.");
            Check(frames.Any(frame => frame.Source == AudioSource.Microphone && frame.Pcm.All(value => value == 0)),
                "Idle mic did not get independent silence.");
            Check(!frames.Any(frame => frame.Source == AudioSource.Computer && frame.Pcm.All(value => value == 0)),
                "Computer silence was injected over recent real packets.");
            Check(meters.Any(meter => meter.Source == AudioSource.Computer && meter.Level == 1) &&
                meters.Any(meter => meter.Source == AudioSource.Microphone && Math.Abs(meter.Level - 0.5) < 0.001) &&
                aggregates.Contains(1) && aggregates.Last() == 0, "Source or max/latest aggregate meters differ.");
        }
        finally { micStart.Set(); }
    }

    private static async Task SingleCaptureAsync()
    {
        foreach (AudioSource source in Both)
        {
            bool v1 = source == AudioSource.Computer;
            var input = new SyntheticInput();
            var frames = new ConcurrentQueue<(AudioSource Source, byte[] Pcm)>();
            var transcripts = new ConcurrentQueue<TranscriptEvent>();
            int opened = 0, faults = 0;
            await using var server = await TestServer.StartAsync(async (socket, cancellation) =>
            {
                if (!v1)
                {
                    await ExpectV2StartAsync(socket, cancellation, [source]);
                    await SendAsync(socket, V2Ready([source]), cancellation);
                    await DrainV2Async(socket, cancellation, [source], frames);
                    return;
                }
                await ExpectStartAsync(socket, cancellation);
                await SendAsync(socket, Ready, cancellation);
                var audio = await ReceiveAsync(socket, cancellation);
                Check(audio.Type == WebSocketMessageType.Binary &&
                    audio.Bytes.SequenceEqual(new byte[] { 16, 32 }), "V1 capture was source-prefixed.");
                _ = await ReceiveAsync(socket, cancellation);
                await SendAsync(socket,
                    """{"type":"final","text":"legacy","segmentId":1,"startSeconds":0,"endSeconds":0}""", cancellation);
                await SendAsync(socket, """{"type":"stopped"}""", cancellation);
            });
            await using var transcriber = new LoopbackTranscriber((actual, id, _) =>
            {
                Check(actual == source && id == "synthetic-selected", "Single-input selection changed.");
                Interlocked.Increment(ref opened);
                return input;
            });
            transcriber.TranscriptReceived += transcripts.Enqueue;
            transcriber.Faulted += _ => Interlocked.Increment(ref faults);
            if (v1)
                await transcriber.StartAsync("synthetic-selected", server.Endpoint, Token);
            else
                await transcriber.StartAsync(new AudioInputSelection(null, "synthetic-selected"), server.Endpoint, Token);
            input.Emit([16, 32]);
            await transcriber.StopAsync();
            await server.Completed;
            Check(opened == 1 && input.Disposed && faults == 0 && !transcriber.IsRunning,
                "Single-input capture did not stop cleanly.");
            Check(transcripts.Single().Source == (v1 ? null : source), "V1/v2 source compatibility changed.");
            Check(transcripts.Single().CaptureSessionId is { } id && id != Guid.Empty,
                "Adapter omitted capture-session identity.");
            if (!v1)
                Check(frames.Count == 1 && frames.Single().Source == source &&
                    frames.Single().Pcm.SequenceEqual(new byte[] { 16, 32 }), "Mic-only adapter lost source PCM.");
        }
    }

    private static async Task DualRepeatedSessionsAsync()
    {
        var inputs = new ConcurrentBag<SyntheticInput>();
        var transcripts = new ConcurrentQueue<TranscriptEvent>();
        var identities = new HashSet<Guid>();
        int faults = 0;
        await using var server = await TestServer.StartAsync(async (socket, cancellation) =>
        {
            await ExpectV2StartAsync(socket, cancellation, Both);
            await SendAsync(socket, V2Ready(Both), cancellation);
            await DrainV2Async(socket, cancellation, Both);
        });
        await using var transcriber = new LoopbackTranscriber((_, _, _) =>
        {
            var input = new SyntheticInput();
            inputs.Add(input);
            return input;
        });
        transcriber.Faulted += _ => Interlocked.Increment(ref faults);
        transcriber.TranscriptReceived += transcripts.Enqueue;
        for (int iteration = 0; iteration < 3; iteration++)
        {
            using var cancellation = new CancellationTokenSource();
            Guid? requestedIdentity = iteration == 1 ? Guid.NewGuid() : null;
            await transcriber.StartAsync(BothInputs, server.Endpoint, Token, cancellation.Token, requestedIdentity);
            Check(transcriber.IsRunning, "Fresh dual session failed to become running.");
            await transcriber.StopAsync();
            var current = transcripts.ToArray().Skip(iteration * 2).ToArray();
            Check(current.Length == 2 && current[0].CaptureSessionId is { } identity && identity != Guid.Empty
                && current.All(item => item.CaptureSessionId == identity) && identities.Add(identity)
                && (requestedIdentity is null || requestedIdentity == identity),
                "Repeated sessions mixed/reused identities or ignored the caller's identity.");
            cancellation.Cancel();
            foreach (SyntheticInput input in inputs)
                input.UnexpectedStop();
            Check(!transcriber.IsRunning && faults == 0 &&
                inputs.All(input => input.Disposed && input.HandlerCount == 0),
                "Stopped callbacks or token cancellation leaked into another session.");
        }
        await Task.WhenAll(transcriber.DisposeAsync().AsTask(), transcriber.DisposeAsync().AsTask());
        Check(inputs.Count == 6 && inputs.All(input => input.StartCount == 1 && input.StopCount == 1),
            "Repeated sessions reused or double-released inputs.");
    }

    private static async Task DualFailureAsync(bool construction)
    {
        var computer = new SyntheticInput();
        var microphone = new SyntheticInput { ThrowOnStart = !construction };
        await using var server = await TestServer.StartAsync(async (socket, cancellation) =>
        {
            await ExpectV2StartAsync(socket, cancellation, Both);
            await SendAsync(socket, V2Ready(Both), cancellation);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellation);
        });
        int faults = 0;
        await using var transcriber = new LoopbackTranscriber((source, _, _) =>
        {
            if (source == AudioSource.Computer)
                return computer;
            Check(computer.StartEntered.Wait(TimeSpan.FromSeconds(3)), "Computer did not start for failure test.");
            if (construction)
                throw new InvalidOperationException("Synthetic denied microphone.");
            return microphone;
        });
        transcriber.Faulted += _ => Interlocked.Increment(ref faults);
        bool rejected = false;
        try { await transcriber.StartAsync(BothInputs, server.Endpoint, Token); }
        catch (InvalidOperationException) { rejected = true; }
        await transcriber.StopAsync();
        Check(rejected && faults == 1 && !transcriber.IsRunning && computer.Disposed &&
            (construction || microphone.Disposed), "One failed input did not release the entire session.");
    }

    private static async Task DualDeviceLossAsync()
    {
        foreach (bool deviceLoss in new[] { false, true })
        {
            var inputs = new ConcurrentDictionary<AudioSource, SyntheticInput>();
            var notifications = new ConcurrentDictionary<AudioSource, Action>();
            await using var server = await TestServer.StartAsync(async (socket, cancellation) =>
            {
                await ExpectV2StartAsync(socket, cancellation, Both);
                await SendAsync(socket, V2Ready(Both), cancellation);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellation);
            });
            int faults = 0;
            await using var transcriber = new LoopbackTranscriber((source, _, lost) =>
            {
                notifications[source] = lost;
                return inputs[source] = new SyntheticInput();
            });
            transcriber.Faulted += _ => Interlocked.Increment(ref faults);
            await transcriber.StartAsync(BothInputs, server.Endpoint, Token);
            await Task.WhenAll(Both.Select(source => Task.Run(() =>
            {
                if (deviceLoss) notifications[source]();
                else inputs[source].UnexpectedStop();
            })));
            await transcriber.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));
            Check(!transcriber.IsRunning && faults == 1 && inputs.Values.All(input => input.Disposed),
                "Concurrent callback/device faults deadlocked or left the other input running.");
        }
    }

    private static async Task DualHungStartupAsync()
    {
        using var blocked = new ManualResetEventSlim();
        var computer = new SyntheticInput();
        var mic = new SyntheticInput { StartGate = blocked };
        await using var server = await TestServer.StartAsync(async (socket, cancellation) =>
        {
            await ExpectV2StartAsync(socket, cancellation, Both);
            await SendAsync(socket, V2Ready(Both), cancellation);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellation);
        });
        int faults = 0;
        await using var transcriber = new LoopbackTranscriber((source, _, _) =>
            source == AudioSource.Computer ? computer : mic);
        transcriber.Faulted += _ => Interlocked.Increment(ref faults);
        using var canceled = new CancellationTokenSource();
        Task startup = transcriber.StartAsync(BothInputs, server.Endpoint, Token, canceled.Token);
        try
        {
            await Task.WhenAll(computer.StartEntered, mic.StartEntered).WaitAsync(TimeSpan.FromSeconds(3));
            canceled.Cancel();
            await transcriber.StopAsync().WaitAsync(TimeSpan.FromSeconds(7));
            bool rejected = false;
            try { await startup; }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && !transcriber.IsRunning && computer.Disposed && !mic.Disposed && faults == 1,
                "Canceled startup lost ownership or failed to release the responsive input.");
            rejected = false;
            try { await transcriber.StartAsync(BothInputs, server.Endpoint, Token); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Another session opened while a capture startup worker still owned the microphone.");
        }
        finally { blocked.Set(); }
        await WaitUntilAsync(() => mic.Disposed && mic.HandlerCount == 0);
    }

    private static async Task DualHungStopAsync()
    {
        using var blocked = new ManualResetEventSlim();
        var computer = new SyntheticInput();
        var mic = new SyntheticInput { StopGate = blocked };
        await using var server = await TestServer.StartAsync(async (socket, cancellation) =>
        {
            await ExpectV2StartAsync(socket, cancellation, Both);
            await SendAsync(socket, V2Ready(Both), cancellation);
            await DrainV2Async(socket, cancellation, Both);
        });
        int faults = 0;
        await using var transcriber = new LoopbackTranscriber((source, _, _) =>
            source == AudioSource.Computer ? computer : mic);
        transcriber.Faulted += _ => faults++;
        await transcriber.StartAsync(BothInputs, server.Endpoint, Token);
        try
        {
            await transcriber.StopAsync().WaitAsync(TimeSpan.FromSeconds(7));
            Check(computer.Disposed && !mic.Disposed && !transcriber.IsRunning && faults == 1,
                "Hung microphone stop blocked the other input or the caller.");
        }
        finally { blocked.Set(); }
        await WaitUntilAsync(() => mic.Disposed);
    }

    private static async Task DualDisposeFailureAsync()
    {
        var computer = new SyntheticInput();
        var mic = new SyntheticInput { ThrowOnDispose = true };
        await using var server = await TestServer.StartAsync(async (socket, cancellation) =>
        {
            await ExpectV2StartAsync(socket, cancellation, Both);
            await SendAsync(socket, V2Ready(Both), cancellation);
            await DrainV2Async(socket, cancellation, Both);
        });
        int faults = 0;
        await using var transcriber = new LoopbackTranscriber((source, _, _) =>
            source == AudioSource.Computer ? computer : mic);
        transcriber.Faulted += _ => faults++;
        await transcriber.StartAsync(BothInputs, server.Endpoint, Token);
        await transcriber.StopAsync();
        bool rejected = false;
        try { await transcriber.StartAsync(BothInputs, server.Endpoint, Token); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected && computer.Disposed && mic.Disposed && faults == 1, "Release failure allowed unsafe restart.");
    }

    private static async Task DualConstructionCleanupAsync()
    {
        int opened = 0, faults = 0;
        await using var server = await TestServer.StartAsync(async (socket, cancellation) =>
        {
            await ExpectV2StartAsync(socket, cancellation, Both);
            await SendAsync(socket, V2Ready(Both), cancellation);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellation);
        });
        await using var transcriber = new LoopbackTranscriber((_, _, _) =>
        {
            Interlocked.Increment(ref opened);
            throw new CaptureReleaseException();
        });
        transcriber.Faulted += _ => Interlocked.Increment(ref faults);
        try { await transcriber.StartAsync(BothInputs, server.Endpoint, Token); }
        catch (InvalidOperationException) { }
        int attempts = opened;
        bool rejected = false;
        try { await transcriber.StartAsync(BothInputs, server.Endpoint, Token); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected && attempts > 0 && opened == attempts && faults == 1 && !transcriber.IsRunning,
            "Constructor cleanup failure did not retain the unsafe-restart guard.");
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!predicate())
            await Task.Delay(10, deadline.Token);
    }

    private sealed class SyntheticInput : IWaveIn
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public WaveFormat WaveFormat { get; set; } = new(16000, 16, 1);
        public event EventHandler<WaveInEventArgs>? DataAvailable;
        public event EventHandler<StoppedEventArgs>? RecordingStopped;
        public Task StartEntered => _started.Task;
        public ManualResetEventSlim? StartGate { get; init; }
        public ManualResetEventSlim? StopGate { get; init; }
        public bool ThrowOnStart { get; init; }
        public bool ThrowOnDispose { get; init; }
        public volatile bool Disposed;
        public int StartCount, StopCount;
        public int HandlerCount => (DataAvailable?.GetInvocationList().Length ?? 0) +
            (RecordingStopped?.GetInvocationList().Length ?? 0);

        public void StartRecording()
        {
            Interlocked.Increment(ref StartCount);
            _started.TrySetResult();
            StartGate?.Wait();
            if (ThrowOnStart)
                throw new InvalidOperationException("Synthetic denied input.");
        }

        public void StopRecording()
        {
            Interlocked.Increment(ref StopCount);
            StopGate?.Wait();
            RecordingStopped?.Invoke(this, new StoppedEventArgs());
        }

        public void Emit(byte[] pcm) => DataAvailable?.Invoke(this, new WaveInEventArgs(pcm, pcm.Length));
        public void UnexpectedStop() => RecordingStopped?.Invoke(this, new StoppedEventArgs(new InvalidOperationException()));
        public void Dispose()
        {
            Disposed = true;
            if (ThrowOnDispose)
                throw new InvalidOperationException("Synthetic disposal failure.");
        }
    }
}
