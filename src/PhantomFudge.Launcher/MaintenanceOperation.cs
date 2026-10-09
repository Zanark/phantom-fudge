using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text.Json;

namespace PhantomFudge.Launcher;

internal sealed record RuntimeReceipt(string InstanceId, uint? Pid, string? ProcessCreatedAt)
{
    internal static RuntimeReceipt Parse(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("schemaVersion", out var schema) || schema.ValueKind != JsonValueKind.Number ||
            !schema.TryGetInt32(out var version) || version != 1 ||
            !root.TryGetProperty("instanceId", out var instance) || instance.ValueKind != JsonValueKind.String ||
            !Guid.TryParseExact(instance.GetString(), "N", out var id) || id.ToString("N") != instance.GetString())
            throw new InvalidDataException("The local runtime receipt is invalid.");
        uint? pid = null;
        if (root.TryGetProperty("pid", out var pidValue) && pidValue.ValueKind != JsonValueKind.Null)
        {
            if (pidValue.ValueKind != JsonValueKind.Number || !pidValue.TryGetUInt32(out var parsedPid) || parsedPid == 0)
                throw new InvalidDataException("The local runtime receipt is invalid.");
            pid = parsedPid;
        }
        string? created = null;
        if (root.TryGetProperty("processCreatedAt", out var timeValue))
        {
            if (pid is null || timeValue.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("The local runtime receipt is invalid.");
            created = timeValue.GetString();
            if (created is null || created.Length is < 1 or > 20 || created[0] == '0' ||
                !created.All(char.IsAsciiDigit) ||
                !ulong.TryParse(created, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                throw new InvalidDataException("The local runtime receipt is invalid.");
        }
        return new(id.ToString("N"), pid, created);
    }
}

[SupportedOSPlatform("windows")]
internal static class MaintenanceOperation
{
    internal static async Task<bool> RunAsync(InstallPaths paths, IMaintenanceService service,
        Func<uint, string?>? inspectProcess = null, TimeSpan? shutdownTimeout = null,
        Func<Task<bool>>? portIsListening = null)
    {
        InstallPaths.RejectReparsePoints(paths.Local);
        // An untouched installation must not even probe a foreign source-workspace listener.
        if (!Directory.Exists(paths.Local) || (!File.Exists(paths.Token) && !File.Exists(paths.Receipt)))
            return true;
        InstallPaths.RejectReparsePoints(paths.RuntimeLock);
        using var lifecycleLock = new FileStream(paths.RuntimeLock, FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.ReadWrite);
        try
        {
            lifecycleLock.Lock(0, 1);
        }
        catch (IOException)
        {
            return false;
        }
        // Closing the handle also releases the byte lock used by the Python manager.
        if (!File.Exists(paths.Token))
            return !File.Exists(paths.Receipt);
        var token = LocalToken.Read(paths.Token);
        if (!File.Exists(paths.Receipt))
        {
            // A normal stop preserves the token. With no receipt, never send credentials or shutdown.
            return !await (portIsListening ?? PortIsListeningAsync)().ConfigureAwait(false);
        }
        InstallPaths.RequireRegularFile(paths.Receipt);
        var original = InstallPaths.ReadBounded(paths.Receipt, 16384);
        var receipt = RuntimeReceipt.Parse(original);
        inspectProcess ??= ProcessIdentity.Read;
        if (receipt.Pid is { } recordedPid)
        {
            var currentCreation = inspectProcess(recordedPid);
            if (currentCreation is null ||
                (receipt.ProcessCreatedAt is { } recordedCreation && currentCreation != recordedCreation))
                return RemoveUnchangedReceipt(paths, original);
        }

        var runtime = await service.ReadRuntimeAsync(token).ConfigureAwait(false);
        if (runtime.InstanceId != receipt.InstanceId || runtime.Active ||
            (receipt.Pid is { } expectedPid && runtime.Pid != expectedPid))
            return false;
        var liveCreation = inspectProcess(runtime.Pid);
        if (liveCreation is null)
            return false;
        if (receipt.ProcessCreatedAt is { } expectedCreation && liveCreation != expectedCreation)
            return RemoveUnchangedReceipt(paths, original);
        if (!await service.StopAsync(token, receipt.InstanceId).ConfigureAwait(false))
            return false;

        var watch = Stopwatch.StartNew();
        var timeout = shutdownTimeout ?? TimeSpan.FromSeconds(15);
        while (true)
        {
            if (inspectProcess(runtime.Pid) != liveCreation)
                return RemoveUnchangedReceipt(paths, original);
            if (watch.Elapsed >= timeout)
                return false;
            await Task.Delay(100).ConfigureAwait(false);
        }
    }

    private static bool RemoveUnchangedReceipt(InstallPaths paths, byte[] original)
    {
        InstallPaths.RequireRegularFile(paths.Receipt);
        if (!InstallPaths.ReadBounded(paths.Receipt, 16384).AsSpan().SequenceEqual(original))
            return false;
        File.Delete(paths.Receipt);
        return true;
    }

    private static async Task<bool> PortIsListeningAsync()
    {
        using var client = new TcpClient(AddressFamily.InterNetwork);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, 8765, deadline.Token).ConfigureAwait(false);
            return true;
        }
        catch (SocketException error) when (error.SocketErrorCode == SocketError.ConnectionRefused)
        {
            return false;
        }
    }
}
