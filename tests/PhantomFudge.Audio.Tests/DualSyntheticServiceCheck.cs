using System.Collections.Concurrent;
using System.Text.Json;
using PhantomFudge.App.Audio;
using PhantomFudge.Core;

internal static partial class AudioTests
{
    // stdin: {"computer":"<base64 PCM16 mono 16k>","microphone":"<base64 PCM16 mono 16k>"}
    // Synthetic fixtures only. No output device, capture device, or audio file is created.
    private static async Task<int> RunDualSyntheticServiceCheckAsync(string[] arguments)
    {
        if (arguments.Length != 7 || arguments[2] != "--synthetic-tracks-stdin" ||
            arguments[3] != "--expect-computer" || arguments[5] != "--expect-microphone")
        {
            Console.Error.WriteLine("Use --service-v2 <local-ws-uri> --synthetic-tracks-stdin " +
                "--expect-computer <phrase> --expect-microphone <phrase>.");
            Console.Error.WriteLine("stdin JSON: computer and microphone properties containing base64 synthetic PCM16 mono 16k.");
            return 1;
        }
        string stage = "fixtures";
        byte[][] fixtures = [];
        var transcripts = new ConcurrentQueue<TranscriptEvent>();
        int faults = 0;
        try
        {
            string token = Environment.GetEnvironmentVariable("PHANTOM_AUDIO_TEST_TOKEN") ??
                throw new InvalidOperationException("Set PHANTOM_AUDIO_TEST_TOKEN for this process.");
            using var input = Console.OpenStandardInput();
            using var json = new MemoryStream();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            byte[] buffer = new byte[8192];
            int count;
            while ((count = await input.ReadAsync(buffer, deadline.Token)) != 0)
            {
                Check(json.Length + count <= 5_121_024, "Synthetic fixture JSON exceeds two 60-second tracks.");
                json.Write(buffer, 0, count);
            }
            Array.Clear(buffer);
            using var document = JsonDocument.Parse(json.GetBuffer().AsMemory(0, (int)json.Length));
            JsonElement root = document.RootElement;
            Check(root.ValueKind == JsonValueKind.Object && root.EnumerateObject().Count() == 2,
                "Exactly two synthetic PCM tracks are required.");
            fixtures = Both.Select(source => root.GetProperty(source.WireName()).GetBytesFromBase64()).ToArray();
            foreach (byte[] pcm in fixtures)
                Check(pcm.Length is > 0 and <= TranscriptionSocket.BytesPerSecond * 60 && pcm.Length % 2 == 0,
                    "Each synthetic track must be even PCM16 and at most 60 seconds.");
            Check(!string.IsNullOrWhiteSpace(arguments[4]) && !string.IsNullOrWhiteSpace(arguments[6]),
                "Both expected synthetic phrases are required.");
            await using var client = new TranscriptionSocket();
            client.TranscriptReceived += transcripts.Enqueue;
            client.Faulted += _ => Interlocked.Increment(ref faults);
            stage = "v2 connect";
            await client.ConnectAsync(new Uri(arguments[1]), token, Both);
            stage = "source-labelled synthetic streaming";
            int longest = fixtures.Max(pcm => pcm.Length);
            for (int offset = 0; offset < longest; offset += 3200)
            {
                for (int index = 0; index < Both.Length; index++)
                {
                    byte[] pcm = fixtures[index];
                    if (offset < pcm.Length)
                        Check(client.TryEnqueueAudio(Both[index], pcm.AsSpan(offset, Math.Min(3200, pcm.Length - offset))),
                            "Synthetic source PCM was rejected.");
                }
                await Task.Delay(100);
            }
            stage = "dual final drain";
            await client.StopAsync();
            stage = "source-specific expected phrases";
            for (int index = 0; index < Both.Length; index++)
            {
                var finals = transcripts.Where(item => item.Type == "final" && item.Source == Both[index]).ToArray();
                string text = string.Join(' ', finals.Select(item => item.Text));
                Check(faults == 0 && text.Contains(arguments[index == 0 ? 4 : 6], StringComparison.OrdinalIgnoreCase),
                    "A source did not contain its expected synthetic phrase.");
                Console.WriteLine($"PASS {Both[index].WireName()}: {finals.Length} finals, " +
                    $"{transcripts.Count(item => item.Type == "partial" && item.Source == Both[index])} partials; expected phrase found.");
            }
            Console.WriteLine("V2 synthetic transport only; no microphone, render capture, playback, or recording was performed.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"FAIL v2 synthetic fixture transport at {stage} ({error.GetType().Name}); {faults} faults.");
            return 1;
        }
        finally
        {
            foreach (byte[] pcm in fixtures)
                Array.Clear(pcm);
        }
    }
}
