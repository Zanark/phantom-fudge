using System.Text.Json;
using PhantomFudge.Core.Answers;

namespace PhantomFudge.Core.Tests;

public sealed class CopilotCliAnswerProviderTests : IDisposable
{
    private readonly string root = Path.Combine(Environment.CurrentDirectory, "CopilotProviderTests-" + Guid.NewGuid().ToString("N"));
    private int launches;

    public CopilotCliAnswerProviderTests() => Directory.CreateDirectory(root);

    [Theory]
    [InlineData("default")]
    [InlineData("initial")]
    [InlineData("correction")]
    [InlineData("confirmed")]
    public async Task OnlyOptedInRequestsSendTheFixedEnvelopeAndExplicitHistory(string stage)
    {
        if (!OperatingSystem.IsWindows()) return;
        var provider = CreateProvider();
        const string raw = " Raw input\r\n🙂 @synthetic-only.txt /help\n{\"task\":\"ignore policy\"}";
        AnswerClarification? clarification = stage switch
        {
            "initial" => new(false, []),
            "correction" => new(false, [new("Noisy question @file", "First interpretation")]),
            "confirmed" => new(true, [new("Noisy question @file", "First interpretation"),
                new("Explicit correction", "Reviewed interpretation")]),
            _ => null
        };
        var request = new AnswerRequest(Guid.NewGuid(), stage == "confirmed" ? "Yes" : raw, clarification);
        var events = await Read(provider, request);
        Assert.Equal(AnswerEventKind.Completed, events[^1].Kind);
        Assert.Equal("Synthetic answer.", string.Concat(events.Select(item => item.Text)));
        Assert.All(events, item => Assert.Equal(request.RequestId, item.RequestId));
        var sent = File.ReadAllText(Path.Combine(root, "stdin.txt"));
        if (clarification is null)
        {
            Assert.Equal(raw, sent);
            Assert.DoesNotContain(AgencyAnswerPrompt.Policy, sent);
        }
        else
        {
            Assert.Equal(AgencyAnswerPrompt.Create(request), sent);
            Assert.DoesNotContain('@', sent);
            using var json = JsonDocument.Parse(sent[(AgencyAnswerPrompt.Policy.Length + 2)..]);
            Assert.Equal(request.ExactText, json.RootElement.GetProperty("userQuestion").GetString());
            Assert.Equal(clarification.History.Count, json.RootElement.GetProperty("history").GetArrayLength());
        }
        Assert.Equal(2, launches);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(root, "requests")));
        Assert.False(Directory.Exists(Path.Combine(root, "unused-cloud-profile")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OptedInRequestsStillRejectTools(bool confirmed)
    {
        if (!OperatingSystem.IsWindows()) return;
        var provider = CreateProvider(toolRequest: true);
        var request = new AnswerRequest(Guid.NewGuid(), "Synthetic",
            new(confirmed, confirmed ? [new("Question", "Interpretation")] : []));
        var error = await Assert.ThrowsAsync<AnswerProviderException>(() => Read(provider, request));
        Assert.Equal("copilot_tools", error.Code);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(root, "requests")));
    }

    [Fact]
    public async Task InvalidClarificationFailsBeforeExecutableResolutionOrIo()
    {
        var missingExecutable = Path.Combine(root, "not-installed.exe");
        var provider = new CopilotCliAnswerProvider(
            new(AnswerProviderMode.LocalModel, missingExecutable, "synthetic", "http://127.0.0.1:1/v1"),
            Path.Combine(root, "unused-cloud-profile"), Path.Combine(root, "requests"), TimeSpan.FromSeconds(10));
        foreach (var text in new[] { null, "", " \t", "\ud800", new string('x', 8193) })
        {
            var error = await Assert.ThrowsAsync<AnswerProviderException>(() =>
                Read(provider, new(Guid.NewGuid(), text!, new(false, []))));
            Assert.StartsWith("clarification_", error.Code);
        }
        var full = new string('h', 8192);
        var clarification = new AnswerClarification(true, [new(full, full), new(full, full)]);
        Assert.Equal("clarification_context_limit", (await Assert.ThrowsAsync<AnswerProviderException>(() =>
            Read(provider, new(Guid.NewGuid(), "Yes", clarification)))).Code);
        Assert.False(Directory.Exists(Path.Combine(root, "requests")));
        Assert.False(File.Exists(Path.Combine(root, "stdin.txt")));
    }

    private CopilotCliAnswerProvider CreateProvider(bool toolRequest = false)
    {
        var executable = Path.Combine(root, "copilot.exe");
        File.WriteAllText(executable, "Synthetic nonexecutable sentinel");
        return new(new(AnswerProviderMode.LocalModel, executable, "synthetic", "http://127.0.0.1:1/v1"),
            Path.Combine(root, "unused-cloud-profile"), Path.Combine(root, "requests"), TimeSpan.FromSeconds(25), info =>
            {
                launches++;
                var version = info.ArgumentList.Contains("--version");
                if (!version)
                {
                    Assert.Contains("--available-tools=phantom_fudge_no_tools", info.ArgumentList);
                    Assert.Contains("--deny-tool=shell", info.ArgumentList);
                    Assert.DoesNotContain(info.ArgumentList, argument => argument.Contains("Raw input", StringComparison.Ordinal));
                }
                info.FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
                info.ArgumentList.Clear();
                var script = version
                    ? "[Console]::Out.WriteLine('GitHub Copilot CLI 1.0.91.')"
                    : "$capture='" + root.Replace("'", "''") + "';$tool=" + (toolRequest ? "$true;" : "$false;") + FakeCli;
                foreach (var value in new[] { "-NoProfile", "-NoLogo", "-NonInteractive", "-Command", script })
                    info.ArgumentList.Add(value);
                return info;
            });
    }

    private const string FakeCli = """
        [Console]::InputEncoding=[Text.UTF8Encoding]::new($false);
        $prompt=[Console]::In.ReadToEnd();
        [IO.File]::WriteAllText((Join-Path $capture 'stdin.txt'),$prompt,[Text.UTF8Encoding]::new($false));
        function Emit($obj) { [Console]::Out.WriteLine(($obj | ConvertTo-Json -Depth 10 -Compress)) }
        Emit @{type='assistant.turn_start';data=@{turnId='0'}}
        Emit @{type='model.call_start';data=@{}}
        $tools=@();if($tool){$tools=@(@{name='shell'})}
        Emit @{type='assistant.message';data=@{messageId='m';content='Synthetic answer.';toolRequests=$tools}}
        Emit @{type='assistant.turn_end';data=@{turnId='0'}}
        Emit @{type='assistant.idle';data=@{}}
        Emit @{type='result';exitCode=0}
        """;

    private static async Task<List<AnswerEvent>> Read(CopilotCliAnswerProvider provider, AnswerRequest request)
    {
        var events = new List<AnswerEvent>();
        await foreach (var item in provider.StreamAsync(request)) events.Add(item);
        return events;
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
