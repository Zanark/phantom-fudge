using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace PhantomFudge.App.Native;

internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly string pipeName;
    private readonly CancellationTokenSource shutdown = new();
    private Task? listener;
    private bool disposed;
    public bool IsOwner { get; }

    public SingleInstance()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value ?? throw new InvalidOperationException("Current-user identity is unavailable.");
        var suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sid)))[..24];
        pipeName = "PhantomFudge-" + suffix;
        mutex = new Mutex(true, @"Local\" + pipeName, out var created);
        IsOwner = created;
    }

    public async Task RevealExistingAsync()
    {
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await client.ConnectAsync(timeout.Token);
        await client.WriteAsync("REVEAL"u8.ToArray(), timeout.Token);
        await client.FlushAsync(timeout.Token);
    }

    public void Listen(Action reveal, Action<string> reportFailure)
    {
        if (!IsOwner || listener is not null)
            throw new InvalidOperationException("Instance recovery listener cannot be started.");
        listener = ListenAsync(reveal, reportFailure);
    }

    private async Task ListenAsync(Action reveal, Action<string> reportFailure)
    {
        while (!shutdown.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(shutdown.Token).ConfigureAwait(false);
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
                requestTimeout.CancelAfter(TimeSpan.FromSeconds(2));
                var request = new byte[6];
                await server.ReadExactlyAsync(request, requestTimeout.Token).ConfigureAwait(false);
                if (request.AsSpan().SequenceEqual("REVEAL"u8))
                    reveal();
                else
                    reportFailure("An invalid local recovery request was rejected.");
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException)
            {
                reportFailure("A local recovery request timed out.");
            }
            catch (IOException)
            {
                reportFailure("A local recovery connection failed. The hotkey remains available.");
                await Task.Delay(250, shutdown.Token).ConfigureAwait(false);
            }
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        shutdown.Cancel();
        if (IsOwner)
            mutex.ReleaseMutex();
        mutex.Dispose();
        // A listener may still observe cancellation; do not dispose its token source underneath it.
        if (listener is null)
            shutdown.Dispose();
        else
            _ = listener.ContinueWith(_ => shutdown.Dispose(), TaskScheduler.Default);
    }
}
