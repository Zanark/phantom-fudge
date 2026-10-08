using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PhantomFudge.Core.Answers;

internal sealed class CopilotOwnedProcess : IAsyncDisposable
{
    internal const int MaximumErrorCharacters = 65_536;
    internal Process Process { get; }
    private readonly CancellationTokenSource stopped;
    private readonly CancellationTokenRegistration cancellation;
    private readonly Task stderr;
    private readonly SafeFileHandle? job;
    private readonly Func<string, AnswerProviderException?>? inspectError;
    private AnswerProviderException? observedFailure;
    private int errorOverflow;

    internal CopilotOwnedProcess(ProcessStartInfo startInfo, CancellationToken cancellationToken,
        Func<string, AnswerProviderException?>? inspectError = null)
    {
        this.inspectError = inspectError;
        cancellationToken.ThrowIfCancellationRequested();
        stopped = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Process = new Process { StartInfo = startInfo };
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (OperatingSystem.IsWindows())
            {
                job = CreateJobObject(IntPtr.Zero, null);
                if (job.IsInvalid) throw new Win32Exception();
                var limits = new JobExtendedLimit { Basic = new JobBasicLimit { Flags = 0x2000 } };
                if (!SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<JobExtendedLimit>()))
                    throw new Win32Exception();
            }
            if (!Process.Start()) throw new Win32Exception();
            if (job is not null && !AssignProcessToJobObject(job, Process.Handle))
                throw new Win32Exception();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Kill();
            job?.Dispose();
            try { Process.WaitForExit(5000); }
            catch (InvalidOperationException) { }
            Process.Dispose();
            stopped.Dispose();
            throw new AnswerProviderException("copilot_start", "Copilot could not be started safely. Check the installed executable and application permissions.");
        }
        cancellation = stopped.Token.Register(Kill);
        stderr = DrainErrorAsync();
    }

    private async Task DrainErrorAsync()
    {
        var buffer = new char[2048];
        var total = 0;
        try
        {
            while (true)
            {
                var count = await Process.StandardError.ReadAsync(buffer.AsMemory(), stopped.Token).ConfigureAwait(false);
                if (count == 0) return;
                total += count;
                var failure = inspectError?.Invoke(new string(buffer, 0, count));
                if (failure is not null)
                {
                    Interlocked.CompareExchange(ref observedFailure, failure, null);
                    stopped.Cancel();
                    return;
                }
                if (total > MaximumErrorCharacters)
                {
                    Interlocked.Exchange(ref errorOverflow, 1);
                    stopped.Cancel();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (stopped.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or DecoderFallbackException)
        {
            Interlocked.Exchange(ref errorOverflow, 1);
            stopped.Cancel();
        }
    }

    internal void CheckErrorLimit()
    {
        if (Volatile.Read(ref observedFailure) is { } failure) throw failure;
        if (Volatile.Read(ref errorOverflow) != 0) throw CopilotJsonProtocol.Limit();
    }

    internal async Task SendAsync(string prompt, CancellationToken cancellationToken)
    {
        try
        {
            await Process.StandardInput.WriteAsync(prompt.AsMemory(), cancellationToken).ConfigureAwait(false);
            await Process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            Process.StandardInput.Close();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or EncoderFallbackException)
        {
            CheckErrorLimit();
            cancellationToken.ThrowIfCancellationRequested();
            throw new AnswerProviderException("copilot_stdin", "Copilot could not receive the question securely through standard input.");
        }
    }

    internal async Task<int> WaitAsync(CancellationToken cancellationToken)
    {
        await Process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        job?.Dispose();
        await stderr.WaitAsync(cancellationToken).ConfigureAwait(false);
        CheckErrorLimit();
        return Process.ExitCode;
    }

    private void Kill()
    {
        try { if (!Process.HasExited) Process.Kill(entireProcessTree: true); }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException) { }
        job?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        stopped.Cancel();
        job?.Dispose(); // Also closes descendants which outlived their direct parent.
        try
        {
            await Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            await stderr.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new AnswerProviderException("copilot_cleanup", "Copilot process cleanup did not finish within its deadline.");
        }
        finally
        {
            cancellation.Dispose();
            Process.Dispose();
            stopped.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobBasicLimit
    {
        public long PerProcessTime, PerJobTime;
        public uint Flags;
        public UIntPtr MinimumWorkingSet, MaximumWorkingSet;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobExtendedLimit
    {
        public JobBasicLimit Basic;
        public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemory, PeakJobMemory;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int infoClass, ref JobExtendedLimit info, uint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
}
