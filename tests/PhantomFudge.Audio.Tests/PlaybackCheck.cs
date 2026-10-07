using System.Collections.Concurrent;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using PhantomFudge.App.Audio;

internal static class PlaybackCheck
{
    public static async Task<int> RunAsync(string[] arguments)
    {
        if (arguments.Length != 4 || arguments[2] != "--expect")
        {
            Console.Error.WriteLine("Use --playback <local-ws-uri> --expect <synthetic-phrase> with PCM16 mono16k stdin.");
            return 64;
        }
        var faults = new ConcurrentQueue<string>();
        var finals = new ConcurrentQueue<string>();
        var partials = 0;
        var metered = 0;
        try
        {
            var token = Environment.GetEnvironmentVariable("PHANTOM_AUDIO_TEST_TOKEN") ??
                throw new InvalidOperationException("Local token missing.");
            using var pcm = new MemoryStream();
            using (var input = Console.OpenStandardInput())
            {
                var buffer = new byte[8192];
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                int count;
                while ((count = await input.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    if (pcm.Length + count > 16000 * 2 * 20)
                        throw new InvalidOperationException("Synthetic playback fixture exceeds 20 seconds.");
                    pcm.Write(buffer, 0, count);
                }
            }
            if (pcm.Length == 0 || pcm.Length % 2 != 0)
                throw new InvalidOperationException("Invalid synthetic PCM.");
            pcm.Position = 0;
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            await using var capture = new LoopbackTranscriber();
            capture.Faulted += message => faults.Enqueue(message);
            capture.LevelChanged += level =>
            {
                if (level > 0.001f)
                    Interlocked.Increment(ref metered);
            };
            capture.TranscriptReceived += segment =>
            {
                if (segment.Type == "final")
                    finals.Enqueue(segment.Text);
                else if (segment.Text.Length > 0)
                    Interlocked.Increment(ref partials);
            };
            await capture.StartAsync(device.ID, new Uri(arguments[1]), token);
            try
            {
                using var stream = new RawSourceWaveStream(pcm, new WaveFormat(16000, 16, 1));
                using var output = new WasapiOut(device, AudioClientShareMode.Shared, false, 100);
                var finished = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
                output.PlaybackStopped += (_, args) => finished.TrySetResult(args.Exception);
                output.Init(stream);
                output.Play();
                var playbackError = await finished.Task.WaitAsync(TimeSpan.FromSeconds(25));
                if (playbackError is not null)
                    throw new InvalidOperationException("Synthetic output playback failed.");
                await Task.Delay(1600);
            }
            finally
            {
                await capture.StopAsync();
            }
            var matched = string.Join(" ", finals).Contains(arguments[3], StringComparison.OrdinalIgnoreCase);
            var passed = matched && faults.IsEmpty && metered > 0 && partials > 0 && !capture.IsRunning;
            Console.WriteLine($"{(passed ? "PASS" : "FAIL")} real render-loopback: {partials} partials, " +
                $"{finals.Count} finals, {metered} nonzero-meter updates, expected phrase match={matched}, " +
                $"faults={faults.Count}, stopped={!capture.IsRunning}.");
            Console.WriteLine("Local native service and default output only; no microphone or audio file. Not Docker or meeting qualification.");
            return passed ? 0 : 2;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Synthetic playback-loopback failed ({error.GetType().Name}); " +
                $"partials={partials}, finals={finals.Count}, faults={faults.Count}.");
            return 1;
        }
    }
}
