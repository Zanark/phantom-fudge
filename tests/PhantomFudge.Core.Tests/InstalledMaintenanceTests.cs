using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using PhantomFudge.Launcher;

namespace PhantomFudge.Core.Tests;

[SupportedOSPlatform("windows")]
public sealed class InstalledMaintenanceTests : IDisposable
{
    private const string Instance = "00112233445566778899aabbccddeeff";
    private readonly InstallPaths paths;

    public InstalledMaintenanceTests()
    {
        var root = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ".local", "installer-release",
            "launcher-maintenance-tests-" + Guid.NewGuid().ToString("N")));
        paths = new(Path.Combine(root, "damaged install"), Path.Combine(root, "data"));
    }

    public void Dispose()
    {
        var fixture = Path.GetDirectoryName(paths.DataRoot)!;
        if (Directory.Exists(fixture))
            Directory.Delete(fixture, recursive: true);
    }

    [Theory]
    [InlineData("absent", false, true)]
    [InlineData("empty", false, true)]
    [InlineData("token-only", false, true)]
    [InlineData("token-only", true, false)]
    [InlineData("receipt-only", false, false)]
    public async Task IncompleteOwnershipNeverAuthenticatesOrCreatesToken(string fixture, bool listening, bool expected)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (fixture == "empty")
            Directory.CreateDirectory(paths.Local);
        else if (fixture == "token-only")
            _ = LocalToken.ReadOrCreate(paths);
        else if (fixture == "receipt-only")
        {
            Directory.CreateDirectory(paths.Local);
            File.WriteAllText(paths.Receipt, "preserved receipt");
        }
        var service = new FakeService();
        var probes = 0;
        Assert.Equal(expected, await MaintenanceOperation.RunAsync(paths, service, portIsListening: () =>
        {
            probes++;
            return Task.FromResult(listening);
        }));
        Assert.Equal(fixture == "token-only" ? 1 : 0, probes);
        Assert.Equal(0, service.Reads + service.Stops);
        Assert.Equal(fixture == "token-only", File.Exists(paths.Token));
        Assert.Equal(fixture != "absent", Directory.Exists(paths.Local));
        if (fixture == "receipt-only")
            Assert.Equal("preserved receipt", File.ReadAllText(paths.Receipt));
    }

    [Fact]
    public async Task TokenOnlyUncertainListenerRefusesWithoutAuthentication()
    {
        if (!OperatingSystem.IsWindows()) return;
        var token = LocalToken.ReadOrCreate(paths);
        var service = new FakeService();
        await Assert.ThrowsAsync<OperationCanceledException>(() => MaintenanceOperation.RunAsync(paths, service,
            portIsListening: () => Task.FromException<bool>(new OperationCanceledException())));
        Assert.Equal(0, service.Reads + service.Stops);
        Assert.Equal(token, LocalToken.Read(paths.Token));
    }

    [Fact]
    public async Task AuthenticatedIdleStopNeedsNoOldExecutableOrRuntime()
    {
        if (!OperatingSystem.IsWindows()) return;
        CreateOwnership();
        var service = new FakeService();
        var inspections = 0;
        Assert.True(await MaintenanceOperation.RunAsync(paths, service, _ => ++inspections <= 2 ? "100" : null));
        Assert.Equal(1, service.Reads);
        Assert.Equal(1, service.Stops);
        Assert.False(File.Exists(paths.Receipt));
        Assert.True(File.Exists(paths.Token));
        Assert.False(Directory.Exists(paths.Root));
    }

    [Theory]
    [InlineData("active")]
    [InlineData("foreign-instance")]
    [InlineData("different-pid")]
    public async Task ActiveOrForeignRuntimeIsNeverStopped(string state)
    {
        if (!OperatingSystem.IsWindows()) return;
        CreateOwnership();
        var before = File.ReadAllBytes(paths.Receipt);
        var service = new FakeService
        {
            Identity = new(state == "foreign-instance" ? Guid.NewGuid().ToString("N") : Instance,
                state == "different-pid" ? 999u : 1234u, state == "active")
        };
        Assert.False(await MaintenanceOperation.RunAsync(paths, service, _ => "100"));
        Assert.Equal(0, service.Stops);
        Assert.Equal(before, File.ReadAllBytes(paths.Receipt));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedOrIncompleteShutdownRetainsReceipt(bool accepted)
    {
        if (!OperatingSystem.IsWindows()) return;
        CreateOwnership();
        var service = new FakeService { StopAccepted = accepted };
        Assert.False(await MaintenanceOperation.RunAsync(paths, service, _ => "100", TimeSpan.Zero));
        Assert.Equal(1, service.Stops);
        Assert.True(File.Exists(paths.Receipt));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("200")]
    public async Task ProvenDeadOrReusedPidClearsOnlyReceiptWithoutNetwork(string? identity)
    {
        if (!OperatingSystem.IsWindows()) return;
        CreateOwnership();
        var service = new FakeService();
        Assert.True(await MaintenanceOperation.RunAsync(paths, service, _ => identity));
        Assert.Equal(0, service.Reads + service.Stops);
        Assert.False(File.Exists(paths.Receipt));
        Assert.True(File.Exists(paths.Token));
    }

    [Fact]
    public async Task UninspectableProcessFailsClosedWithoutNetworkOrDeletion()
    {
        if (!OperatingSystem.IsWindows()) return;
        CreateOwnership();
        var service = new FakeService();
        await Assert.ThrowsAsync<Win32Exception>(() =>
            MaintenanceOperation.RunAsync(paths, service, _ => throw new Win32Exception(5)));
        Assert.True(File.Exists(paths.Receipt));
        Assert.Equal(0, service.Reads + service.Stops);
    }

    [Fact]
    public async Task UnreachableLegacyReceiptIsRetained()
    {
        if (!OperatingSystem.IsWindows()) return;
        CreateOwnership();
        File.WriteAllText(paths.Receipt, JsonSerializer.Serialize(new { schemaVersion = 1, instanceId = Instance, pid = 1234 }));
        var service = new FakeService { Unreachable = true };
        await Assert.ThrowsAsync<HttpRequestException>(() => MaintenanceOperation.RunAsync(paths, service, _ => "100"));
        Assert.Equal(0, service.Stops);
        Assert.True(File.Exists(paths.Receipt));
    }

    [Fact]
    public async Task PidOnlyReceiptClearsOnlyWhenProcessIsConfirmedAbsent()
    {
        if (!OperatingSystem.IsWindows()) return;
        CreateOwnership();
        File.WriteAllText(paths.Receipt, JsonSerializer.Serialize(new { schemaVersion = 1, instanceId = Instance, pid = 1234 }));
        var service = new FakeService();
        Assert.True(await MaintenanceOperation.RunAsync(paths, service, _ => null));
        Assert.Equal(0, service.Reads + service.Stops);
        Assert.False(File.Exists(paths.Receipt));
        Assert.True(File.Exists(paths.Token));
    }

    [Fact]
    public async Task InvalidReceiptIsRetainedWithoutNetwork()
    {
        if (!OperatingSystem.IsWindows()) return;
        CreateOwnership();
        File.WriteAllText(paths.Receipt, "{}");
        var service = new FakeService();
        await Assert.ThrowsAsync<InvalidDataException>(() => MaintenanceOperation.RunAsync(paths, service, _ => "100"));
        Assert.Equal("{}", File.ReadAllText(paths.Receipt));
        Assert.Equal(0, service.Reads + service.Stops);
    }

    [Fact]
    public async Task PythonCompatibleByteLockRefusesConcurrentManager()
    {
        if (!OperatingSystem.IsWindows()) return;
        CreateOwnership();
        using var held = new FileStream(paths.RuntimeLock, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
        held.Lock(0, 1);
        var service = new FakeService();
        Assert.False(await MaintenanceOperation.RunAsync(paths, service, _ => "100"));
        Assert.Equal(0, service.Reads + service.Stops);
        Assert.True(File.Exists(paths.Receipt));
    }

    [Fact]
    public async Task ConcurrentlyChangedReceiptIsNotDeletedAfterConfirmedStop()
    {
        if (!OperatingSystem.IsWindows()) return;
        CreateOwnership();
        var service = new FakeService { OnStop = () => File.WriteAllText(paths.Receipt, "replacement receipt") };
        var inspections = 0;
        Assert.False(await MaintenanceOperation.RunAsync(paths, service, _ => ++inspections <= 2 ? "100" : null));
        Assert.Equal("replacement receipt", File.ReadAllText(paths.Receipt));
    }

    [Fact]
    public void OwnershipMarkerAllowsRepairDespiteDamagedManifestAndMissingBinaries()
    {
        Directory.CreateDirectory(paths.Root);
        File.WriteAllText(paths.Marker, "phantom-fudge.install.v1");
        File.WriteAllText(paths.Manifest, "damaged");
        paths.ValidateOwnershipMarker();
        Assert.Throws<FileNotFoundException>(() => paths.Validate(requireSpeech: true));
        File.WriteAllText(paths.Marker, "unrelated");
        Assert.Throws<InvalidDataException>(paths.ValidateOwnershipMarker);
    }

    [Fact]
    public void InstalledValidationChecksCompactManifestAndRequiredLayoutWithoutStartingAnything()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(paths.App)!);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.Python)!);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.Manager)!);
        Directory.CreateDirectory(Path.Combine(paths.Models, "vosk-model-small-en-us-0.15"));
        Directory.CreateDirectory(Path.Combine(paths.Models, "faster-whisper-small.en"));
        foreach (var file in new[] { paths.App, paths.Python, paths.Manager })
            File.WriteAllText(file, "synthetic placeholder, never executed");
        File.WriteAllText(paths.Marker, "phantom-fudge.install.v1");
        File.WriteAllText(paths.Manifest, "{\"version\":\"0.1.0-preview.1\",\"schemaVersion\":1}");
        paths.ValidateOwnershipMarker();
        paths.Validate(requireSpeech: true);
        Assert.False(Directory.Exists(paths.DataRoot));
        File.WriteAllText(paths.Manifest, "{\"version\":\"0.1.0\",\"files\":\"" + new string('x', 16384) + "\"}");
        Assert.Throws<InvalidDataException>(() => paths.Validate(requireSpeech: true));
    }

    [Fact]
    public void CreationIdentityWithoutPidCannotProveADeadProcess()
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            schemaVersion = 1, instanceId = Instance, processCreatedAt = "100"
        }));
        Assert.Throws<InvalidDataException>(() => RuntimeReceipt.Parse(bytes));
    }

    [Fact]
    public void ProcessIdentityUsesExactWindowsFileTimeWithoutSignaling()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var process = Process.GetCurrentProcess();
        Assert.Equal(process.StartTime.ToUniversalTime().ToFileTimeUtc().ToString(System.Globalization.CultureInfo.InvariantCulture),
            ProcessIdentity.Read((uint)process.Id));
    }

    private void CreateOwnership()
    {
        _ = LocalToken.ReadOrCreate(paths);
        File.WriteAllText(paths.Receipt, JsonSerializer.Serialize(new
        {
            schemaVersion = 1, instanceId = Instance, pid = 1234, processCreatedAt = "100"
        }));
    }

    private sealed class FakeService : IMaintenanceService
    {
        internal RuntimeIdentity Identity { get; init; } = new(Instance, 1234, false);
        internal bool StopAccepted { get; init; } = true;
        internal bool Unreachable { get; init; }
        internal Action? OnStop { get; init; }
        internal int Reads { get; private set; }
        internal int Stops { get; private set; }

        public Task<RuntimeIdentity> ReadRuntimeAsync(string token)
        {
            Reads++;
            return Unreachable ? Task.FromException<RuntimeIdentity>(new HttpRequestException()) : Task.FromResult(Identity);
        }

        public Task<bool> StopAsync(string token, string instance)
        {
            Assert.Equal(Instance, instance);
            Stops++;
            OnStop?.Invoke();
            return Task.FromResult(StopAccepted);
        }
    }
}

public sealed class InstalledMaintenanceTransportTests
{
    private const string Instance = "00112233445566778899aabbccddeeff";
    private const string RuntimeJson = "{\"service\":\"phantom-fudge-transcription\",\"protocolVersion\":1,\"instanceId\":\"" +
        Instance + "\",\"pid\":1234,\"active\":false}";

    [Fact]
    public async Task HttpTransportUsesOnlyFixedLoopbackAndAuthenticatedInstanceShutdown()
    {
        var calls = 0;
        using var service = new OwnedServiceClient(new Handler(request =>
        {
            Assert.Equal("http://127.0.0.1:8765", request.RequestUri!.GetLeftPart(UriPartial.Authority));
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal(new string('x', 64), request.Headers.Authorization.Parameter);
            Assert.Null(request.Content);
            calls++;
            if (calls == 1)
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal("/v1/runtime", request.RequestUri.AbsolutePath);
                return Response(RuntimeJson);
            }
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/v1/shutdown", request.RequestUri.AbsolutePath);
            Assert.Equal(Instance, Assert.Single(request.Headers.GetValues("X-Phantom-Instance")));
            return Response("{\"status\":\"stopping\"}");
        }));
        Assert.Equal(new RuntimeIdentity(Instance, 1234, false), await service.ReadRuntimeAsync(new string('x', 64)));
        Assert.True(await service.StopAsync(new string('x', 64), Instance));
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(302)]
    [InlineData(401)]
    [InlineData(409)]
    public async Task HttpFailuresNeverBecomeSuccessfulMaintenance(int status)
    {
        var calls = 0;
        using var service = new OwnedServiceClient(new Handler(_ =>
        {
            calls++;
            var response = Response("{}");
            response.StatusCode = (HttpStatusCode)status;
            response.Headers.Location = new Uri("https://unrelated.invalid/");
            return response;
        }));
        await Assert.ThrowsAsync<HttpRequestException>(() => service.ReadRuntimeAsync(new string('x', 64)));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"service\":\"other\"}")]
    public async Task InvalidRuntimeIdentityIsRejected(string response)
    {
        using var service = new OwnedServiceClient(new Handler(_ => Response(response)));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ReadRuntimeAsync(new string('x', 64)));
    }

    [Fact]
    public async Task OversizedResponseIsRejectedBeforeJsonParsing()
    {
        using var service = new OwnedServiceClient(new Handler(_ => Response(new string('x', 4097))));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ReadRuntimeAsync(new string('x', 64)));
    }

    [Fact]
    public async Task ShutdownRequiresExplicitStoppingAcknowledgment()
    {
        using var service = new OwnedServiceClient(new Handler(_ => Response("{\"status\":\"ready\"}")));
        Assert.False(await service.StopAsync(new string('x', 64), Instance));
    }

    private static HttpResponseMessage Response(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
