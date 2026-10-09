using System.IO.Pipes;
using System.Security.Principal;

namespace PhantomFudge.Launcher;

internal sealed class InstanceCoordination : IDisposable
{
    private readonly Mutex operation;
    private readonly string pipeName;
    private bool ownsOperation;

    internal InstanceCoordination() : this(CurrentUserSuffix())
    {
    }

    internal InstanceCoordination(string diagnosticSuffix)
    {
        pipeName = "PhantomFudge-" + diagnosticSuffix;
        operation = new Mutex(false, @"Local\PhantomFudge-Launcher-" + diagnosticSuffix);
    }

    private static string CurrentUserSuffix()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value ?? throw new InvalidOperationException("Current-user identity is unavailable.");
        return LauncherContract.UserSuffix(sid);
    }

    internal bool AppIsRunning()
    {
        if (!Mutex.TryOpenExisting(@"Local\" + pipeName, out var appMutex))
            return false;
        appMutex.Dispose();
        return true;
    }

    internal bool TryEnter()
    {
        try
        {
            ownsOperation = operation.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            ownsOperation = true;
        }
        return ownsOperation;
    }

    internal async Task RevealAsync()
    {
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await client.ConnectAsync(timeout.Token).ConfigureAwait(false);
        await client.WriteAsync("REVEAL"u8.ToArray(), timeout.Token).ConfigureAwait(false);
        await client.FlushAsync(timeout.Token).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (ownsOperation)
            operation.ReleaseMutex();
        operation.Dispose();
    }
}
