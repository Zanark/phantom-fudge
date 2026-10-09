using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace PhantomFudge.Launcher;

internal sealed record RuntimeIdentity(string InstanceId, uint Pid, bool Active);

internal interface IMaintenanceService
{
    Task<RuntimeIdentity> ReadRuntimeAsync(string token);
    Task<bool> StopAsync(string token, string instance);
}

internal sealed class OwnedServiceClient : IMaintenanceService, IDisposable
{
    private readonly HttpClient client;

    internal OwnedServiceClient(HttpMessageHandler? handler = null)
    {
        client = new HttpClient(handler ?? new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false,
            MaxResponseHeadersLength = 8
        })
        {
            BaseAddress = new Uri("http://127.0.0.1:8765"),
            Timeout = TimeSpan.FromSeconds(3)
        };
    }

    public async Task<RuntimeIdentity> ReadRuntimeAsync(string token)
    {
        using var json = await RequestAsync(HttpMethod.Get, "/v1/runtime", token, null).ConfigureAwait(false);
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("service", out var name) || name.ValueKind != JsonValueKind.String ||
            name.GetString() != "phantom-fudge-transcription" ||
            !root.TryGetProperty("protocolVersion", out var protocol) || protocol.ValueKind != JsonValueKind.Number ||
            !protocol.TryGetInt32(out var version) || version != 1 ||
            !root.TryGetProperty("instanceId", out var instance) || instance.ValueKind != JsonValueKind.String ||
            !Guid.TryParseExact(instance.GetString(), "N", out var id) || id.ToString("N") != instance.GetString() ||
            !root.TryGetProperty("pid", out var pid) || pid.ValueKind != JsonValueKind.Number ||
            !pid.TryGetUInt32(out var processId) || processId == 0 ||
            !root.TryGetProperty("active", out var active) || active.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("The service identity is invalid.");
        return new(id.ToString("N"), processId, active.GetBoolean());
    }

    public async Task<bool> StopAsync(string token, string instance)
    {
        using var json = await RequestAsync(HttpMethod.Post, "/v1/shutdown", token, instance).ConfigureAwait(false);
        return json.RootElement.ValueKind == JsonValueKind.Object &&
            json.RootElement.TryGetProperty("status", out var status) &&
            status.ValueKind == JsonValueKind.String && status.GetString() == "stopping";
    }

    private async Task<JsonDocument> RequestAsync(HttpMethod method, string path, string token, string? instance)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (instance is not null)
            request.Headers.Add("X-Phantom-Instance", instance);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 4096)
            throw new InvalidDataException("The local service response is too large.");
        await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
        var bytes = new byte[4097];
        var count = 0;
        while (count < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(count), deadline.Token).ConfigureAwait(false);
            if (read == 0)
                break;
            count += read;
        }
        if (count > 4096)
            throw new InvalidDataException("The local service response is too large.");
        return JsonDocument.Parse(bytes.AsMemory(0, count), new JsonDocumentOptions { MaxDepth = 8 });
    }

    public void Dispose() => client.Dispose();
}
