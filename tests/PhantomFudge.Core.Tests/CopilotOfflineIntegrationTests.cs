using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using PhantomFudge.Core.Answers;

namespace PhantomFudge.Core.Tests;

public sealed class CopilotOfflineIntegrationTests
{
    // Explicit opt-in to an installed binary; this fixture only serves synthetic loopback SSE.
    [Theory]
    [InlineData("plain")]
    [InlineData("file")]
    [InlineData("quoted-file")]
    [InlineData("slash")]
    public async Task InstalledCliStreamsFromOfflineMockWithoutToolsOrDefaultProfile(string inputKind)
    {
        var executable = Environment.GetEnvironmentVariable("PHANTOMFUDGE_COPILOT_TEST_EXE");
        if (string.IsNullOrEmpty(executable)) return;
        var root = Path.Combine(Environment.CurrentDirectory, "CopilotIntegration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var sentinelPath = Path.Combine(root, "attachment-sentinel.txt");
        const string sentinelText = "PHANTOM_SYNTHETIC_ATTACHMENT_SENTINEL_9b3821";
        File.WriteAllText(sentinelPath, sentinelText);
        var question = inputKind switch
        {
            "file" => "Give a synthetic response. @" + sentinelPath,
            "quoted-file" => "Give a synthetic response. @\"" + sentinelPath + "\"",
            "slash" => "/help",
            _ => "Give a synthetic response."
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = ServeAsync(listener, finish.Task, question, sentinelText, timeout.Token);
        try
        {
            var provider = new CopilotCliAnswerProvider(
                new(AnswerProviderMode.LocalModel, executable, "synthetic", $"http://127.0.0.1:{port}/v1"),
                Path.Combine(root, "unused-cloud-profile"), Path.Combine(root, "requests"), TimeSpan.FromSeconds(25));
            var request = new AnswerRequest(Guid.NewGuid(), question);
            await using var stream = provider.StreamAsync(request, timeout.Token).GetAsyncEnumerator(timeout.Token);
            Assert.True(await stream.MoveNextAsync());
            Assert.Equal(AnswerEventKind.TextDelta, stream.Current.Kind);
            Assert.Equal("Synthetic ", stream.Current.Text);
            finish.SetResult();
            var events = new List<AnswerEvent> { stream.Current };
            while (await stream.MoveNextAsync()) events.Add(stream.Current);
            Assert.Equal("Synthetic answer 🙂.", string.Concat(events.Select(e => e.Text)));
            Assert.Equal(AnswerEventKind.Completed, events[^1].Kind);
            Assert.All(events, e => Assert.Equal(request.RequestId, e.RequestId));
            Assert.Equal(Enumerable.Range(1, events.Count).Select(i => (long)i), events.Select(e => e.Sequence));
            await server;
            Assert.False(Directory.Exists(Path.Combine(root, "unused-cloud-profile")));
            Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(root, "requests")));
        }
        finally
        {
            finish.TrySetResult();
            timeout.Cancel();
            listener.Stop();
            try { await server; } catch (OperationCanceledException) { }
            finally { Directory.Delete(root, recursive: true); }
        }
    }

    [Theory]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(303)]
    [InlineData(307)]
    [InlineData(308)]
    public async Task InstalledCliRejectsRedirectsWithoutForwardingOrRetrying(int status)
    {
        var executable = Environment.GetEnvironmentVariable("PHANTOMFUDGE_COPILOT_TEST_EXE");
        if (string.IsNullOrEmpty(executable)) return;
        var root = Path.Combine(Environment.CurrentDirectory, "CopilotRedirect-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var origin = new TcpListener(IPAddress.Loopback, 0);
        using var destination = new TcpListener(IPAddress.Loopback, 0);
        origin.Start();
        destination.Start();
        var port = ((IPEndPoint)origin.LocalEndpoint).Port;
        var redirectPort = ((IPEndPoint)destination.LocalEndpoint).Port;
        var server = RedirectAsync(origin, redirectPort, status, timeout.Token);
        try
        {
            var provider = new CopilotCliAnswerProvider(
                new(AnswerProviderMode.LocalModel, executable, "synthetic", $"http://127.0.0.1:{port}/v1"),
                Path.Combine(root, "unused-cloud-profile"), Path.Combine(root, "requests"), TimeSpan.FromSeconds(25));
            var error = await Assert.ThrowsAsync<AnswerProviderException>(async () =>
            {
                await foreach (var unused in provider.StreamAsync(new(Guid.NewGuid(), "Give a synthetic response."), timeout.Token))
                    Assert.Fail("Redirected model responses must never produce answer events.");
            });
            Assert.Equal("copilot_model_error", error.Code);
            await server;
            Assert.False(destination.Pending());
            Assert.False(origin.Pending());
            Assert.False(Directory.Exists(Path.Combine(root, "unused-cloud-profile")));
            Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(root, "requests")));
        }
        finally
        {
            timeout.Cancel();
            origin.Stop();
            destination.Stop();
            try { await server; } catch (OperationCanceledException) { }
            finally { Directory.Delete(root, recursive: true); }
        }
    }

    private static async Task RedirectAsync(TcpListener listener, int destinationPort, int status, CancellationToken cancellationToken)
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var stream = client.GetStream();
        await ReadSyntheticRequestAsync(stream, cancellationToken);
        await stream.WriteAsync(Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} Redirect\r\nLocation: http://127.0.0.1:{destinationPort}/v1/chat/completions\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), cancellationToken);
    }

    private static async Task ServeAsync(TcpListener listener, Task finish, string question, string sentinel, CancellationToken cancellationToken)
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var stream = client.GetStream();
        await ReadSyntheticRequestAsync(stream, cancellationToken, question, sentinel);
        await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nConnection: close\r\n\r\n"), cancellationToken);
        await WriteChunk(stream, new { role = "assistant" }, null, cancellationToken);
        await WriteChunk(stream, new { content = "Synthetic " }, null, cancellationToken);
        // The consumer must see the first delta while the provider has not sent a terminal.
        await finish.WaitAsync(cancellationToken);
        await WriteChunk(stream, new { content = "answer 🙂." }, null, cancellationToken);
        await WriteChunk(stream, new { }, "stop", cancellationToken);
        await stream.WriteAsync(Encoding.ASCII.GetBytes("data: [DONE]\n\n"), cancellationToken);
    }

    private static async Task ReadSyntheticRequestAsync(NetworkStream stream, CancellationToken cancellationToken,
        string question = "Give a synthetic response.", string? forbiddenSentinel = null)
    {
        var header = new List<byte>();
        var single = new byte[1];
        while (header.Count < 32_768)
        {
            Assert.Equal(1, await stream.ReadAsync(single, cancellationToken));
            header.Add(single[0]);
            if (header.Count >= 4 && header.TakeLast(4).SequenceEqual(new byte[] { 13, 10, 13, 10 })) break;
        }
        Assert.True(header.Count < 32_768);
        var headers = Encoding.ASCII.GetString(header.ToArray()).Split("\r\n");
        Assert.StartsWith("POST /v1/chat/completions HTTP/", headers[0]);
        var length = int.Parse(headers.Single(h => h.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)).Split(':')[1]);
        Assert.InRange(length, 1, 262_144);
        var body = new byte[length];
        await stream.ReadExactlyAsync(body, cancellationToken);
        if (forbiddenSentinel is not null)
            Assert.DoesNotContain(forbiddenSentinel, Encoding.UTF8.GetString(body));
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal("synthetic", root.GetProperty("model").GetString());
        Assert.True(!root.TryGetProperty("tools", out var tools) || tools.GetArrayLength() == 0);
        var users = root.GetProperty("messages").EnumerateArray().Where(m => m.GetProperty("role").GetString() == "user").ToArray();
        Assert.Single(users);
        Assert.EndsWith("\n\n" + question, users[0].GetProperty("content").GetString());
    }

    private static Task WriteChunk(NetworkStream stream, object delta, string? finishReason, CancellationToken cancellationToken)
    {
        var payload = new { id = "synthetic", @object = "chat.completion.chunk", created = 1, model = "synthetic",
            choices = new[] { new { index = 0, delta, finish_reason = finishReason } } };
        return stream.WriteAsync(Encoding.UTF8.GetBytes("data: " + JsonSerializer.Serialize(payload) + "\n\n"), cancellationToken).AsTask();
    }
}
