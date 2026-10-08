using System.Diagnostics;
using PhantomFudge.Core.Answers;

if (args is not ["--synthetic-request"] and not ["--synthetic-request", "--allow-cloud"])
{
    Console.Error.WriteLine("Use --synthetic-request, adding --allow-cloud only for an explicitly approved Copilot cloud test.");
    return 2;
}

try
{
    var settings = AnswerConfiguration.Load();
    if (settings.Mode == AnswerProviderMode.SnapshotOnly)
        throw new InvalidOperationException("Configure a provider first with Initialize-Answers.ps1.");
    if (settings.Mode is AnswerProviderMode.Copilot or AnswerProviderMode.Agency && !args.Contains("--allow-cloud"))
        throw new InvalidOperationException("This provider uses remote inference. The smoke prompt requires explicit --allow-cloud.");
    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(120));
    var request = new AnswerRequest(Guid.NewGuid(), "Reply with exactly PHANTOM_OK and no other text.");
    IAnswerProvider provider = settings.Mode == AnswerProviderMode.Agency
        ? new AgencyCliAnswerProvider(settings)
        : new CopilotCliAnswerProvider(settings, AnswerConfiguration.CopilotProfilePath);
    var text = new System.Text.StringBuilder();
    var completed = false;
    var started = Stopwatch.StartNew();
    double? firstTextMs = null;
    await foreach (var value in provider.StreamAsync(request, cancellation.Token))
    {
        if (value.Kind == AnswerEventKind.TextDelta)
        {
            firstTextMs ??= started.Elapsed.TotalMilliseconds;
            if (text.Length + value.Text.Length > AnswerRun.MaximumCharacters)
                throw new InvalidOperationException("Synthetic response exceeded the limit.");
            text.Append(value.Text);
        }
        else if (value.Kind == AnswerEventKind.Completed)
            completed = true;
    }
    if (!completed || text.ToString().Trim() != "PHANTOM_OK")
        throw new InvalidOperationException("Synthetic response did not match the expected marker.");
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
    {
        passed = true,
        provider = settings.Mode.ToString(),
        firstTextMs,
        totalMs = started.Elapsed.TotalMilliseconds,
        output = "PHANTOM_OK",
        synthetic = true,
        meetingDataSent = false
    }));
    return 0;
}
catch (Exception error) when (error is AnswerProviderException or InvalidOperationException or
    ArgumentException or IOException or OperationCanceledException)
{
    Console.Error.WriteLine(error is AnswerProviderException providerError ? $"{providerError.Code}: {providerError.Message}" :
        "Synthetic answer probe failed. Check provider setup, login and model availability; no retry was sent.");
    return 1;
}
