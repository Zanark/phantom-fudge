using System.Diagnostics;
using PhantomFudge.Core.Answers;

namespace PhantomFudge.Core.Tests;

public sealed class CopilotOwnedProcessTests
{
    private static ProcessStartInfo StartInfo(string script)
    {
        var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        info.ArgumentList.Add("-NoLogo");
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-NonInteractive");
        info.ArgumentList.Add("-Command");
        info.ArgumentList.Add(script);
        return info;
    }

    [Fact]
    public async Task StandardInputIsNotAnArgumentAndOutputIsDrained()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var process = new CopilotOwnedProcess(StartInfo(
            "$value=[Console]::In.ReadToEnd(); [Console]::Out.Write($value.Length); [Console]::Error.Write('synthetic stderr')"), timeout.Token);
        const string text = "synthetic question with \"quotes\" & parameters";
        await process.SendAsync(text, timeout.Token);
        var output = new char[100];
        var count = await process.Process.StandardOutput.ReadAsync(output.AsMemory(), timeout.Token);
        Assert.Equal(text.Length.ToString(), new string(output, 0, count));
        Assert.Equal(0, await process.WaitAsync(timeout.Token));
    }

    [Fact]
    public async Task CancellationStopsOnlyOwnedParentAndDescendant()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var script = "$child=Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -ArgumentList '-NoProfile','-NonInteractive','-Command','Start-Sleep -Seconds 90' -PassThru -WindowStyle Hidden; [Console]::Out.WriteLine($child.Id); [Console]::Out.Flush(); Start-Sleep -Seconds 90";
        var process = new CopilotOwnedProcess(StartInfo(script), timeout.Token);
        var parentId = process.Process.Id;
        int childId = 0;
        try
        {
            var line = await process.Process.StandardOutput.ReadLineAsync(timeout.Token);
            Assert.True(int.TryParse(line, out childId));
            timeout.Cancel();
        }
        finally { await process.DisposeAsync(); }
        Assert.False(IsRunning(parentId));
        Assert.False(IsRunning(childId));
    }

    [Fact]
    public async Task StderrFloodIsBoundedAndNeverIncludedInErrors()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var process = new CopilotOwnedProcess(StartInfo(
            "while($true) { [Console]::Error.Write(('SYNTHETIC_PRIVATE_VALUE' * 512)); [Console]::Error.Flush() }"), timeout.Token);
        process.Process.StandardInput.Close();
        var error = await Assert.ThrowsAsync<AnswerProviderException>(() => process.WaitAsync(timeout.Token));
        Assert.Equal("copilot_limit", error.Code);
        Assert.DoesNotContain("SYNTHETIC_PRIVATE_VALUE", error.Message);
    }

    [Fact]
    public async Task StdoutFloodWithoutNewlinesIsBoundedAndProcessIsReaped()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var process = new CopilotOwnedProcess(StartInfo(
            "while($true) { [Console]::Out.Write(('x' * 4096)); [Console]::Out.Flush() }"), timeout.Token);
        var pid = process.Process.Id;
        try
        {
            process.Process.StandardInput.Close();
            var error = await Assert.ThrowsAsync<AnswerProviderException>(async () =>
            {
                await foreach (var unused in CopilotJsonProtocol.ReadRecordsAsync(process.Process.StandardOutput, timeout.Token)) { }
            });
            Assert.Equal("copilot_limit", error.Code);
        }
        finally { await process.DisposeAsync(); }
        Assert.False(IsRunning(pid));
    }

    private static bool IsRunning(int pid)
    {
        if (pid == 0) return false;
        try { using var process = Process.GetProcessById(pid); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }
}
