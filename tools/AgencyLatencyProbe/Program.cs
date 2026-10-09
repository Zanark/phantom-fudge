using System.Diagnostics;
using System.Text;
using System.Text.Json;
using PhantomFudge.Core.Answers;
using PhantomFudge.Core.Diagnostics;

if (args.Length is < 2 or > 3 ||
    args[0] is not ("--allow-cloud" or "--prepare-only" or "--allow-cloud-suite" or "--allow-cloud-prepared-suite" or "--allow-cloud-transport-suite" or "--allow-cloud-candidate-suite") ||
    (args.Length == 3 && args[2] is not ("gpt-5.4-mini" or "claude-haiku-5.5" or "gpt-5.6-sol-fast")))
{
    Console.Error.WriteLine("Use --prepare-only, --allow-cloud (one fixed question), --allow-cloud[-prepared|-transport]-suite (six trials), or --allow-cloud-candidate-suite (three prepared trials), followed by <receipt.json>.");
    return 2;
}

var selectedModel = args.Length == 3 ? args[2] : AgencyCliAnswerProvider.DefaultModel;
if (args[0] is "--allow-cloud-suite" or "--allow-cloud-prepared-suite" or "--allow-cloud-transport-suite" or "--allow-cloud-candidate-suite")
    return await RunSuiteAsync(args[1], args[0] is "--allow-cloud-prepared-suite" or "--allow-cloud-candidate-suite",
        args[0] == "--allow-cloud-transport-suite", args.Length == 3 ? selectedModel : null,
        args[0] == "--allow-cloud-candidate-suite" ? 3 : 6);

const string question = "okay so right mean the solution for the lead good problem to some";
var total = Stopwatch.StartNew();
var text = new StringBuilder();
long? warmedMs = null, firstMs = null, completedMs = null;
var attempts = 0;
var failure = (string?)null;
var completed = false;
try
{
    await using var provider = new AgencyCliAnswerProvider(new(AnswerProviderMode.Agency, Model: args.Length == 3 ? selectedModel : null));
    await provider.WarmUpAsync();
    warmedMs = total.ElapsedMilliseconds;
    var ask = Stopwatch.StartNew();
    if (args[0] == "--prepare-only")
    {
        await provider.WarmUpAsync();
        completed = true;
        completedMs = ask.ElapsedMilliseconds;
    }
    else
    {
        attempts++;
        await foreach (var item in provider.StreamAsync(new(Guid.NewGuid(), question)))
        {
            if (item.Kind == AnswerEventKind.TextDelta)
            {
                firstMs ??= ask.ElapsedMilliseconds;
                text.Append(item.Text);
            }
            else if (item.Kind == AnswerEventKind.Completed)
            {
                completed = true;
                completedMs = ask.ElapsedMilliseconds;
            }
        }
    }
}
catch (AnswerProviderException error) { failure = error.Code; }
catch (OperationCanceledException) { failure = "cancelled"; }
finally { LocalDiagnostics.FlushAndStop(); }

var receipt = new
{
    mode = args[0] == "--prepare-only" ? "agency_acp_empty_session_preparation" : "agency_acp_production_provider",
    model = selectedModel,
    reasoningEffort = "low",
    question = attempts == 0 ? null : question,
    modelAttempts = attempts,
    submittedQuestionRequests = attempts,
    warmupMs = warmedMs,
    repeatedPreparationMs = args[0] == "--prepare-only" ? completedMs : null,
    askToFirstTextMs = firstMs,
    askToCompletionMs = args[0] == "--prepare-only" ? null : completedMs,
    totalIncludingCleanupMs = total.ElapsedMilliseconds,
    completed,
    failure,
    answer = text.ToString(),
    limitations = "Preparation creates one empty session but submits no model prompt. Question counts are client submissions; opaque upstream retries are unobservable. Synthetic cases do not establish real transcription accuracy, p50/p95, or a latency guarantee."
};
var output = JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true });
var destination = Path.GetFullPath(args[1]);
Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
await File.WriteAllTextAsync(destination, output, new UTF8Encoding(false));
Console.WriteLine(output);
return completed && failure is null ? 0 : 1;

static async Task<int> RunSuiteAsync(string path, bool preparedFirst, bool transportFirst, string? configuredModel, int trialCount)
{
    var model = configuredModel ?? AgencyCliAnswerProvider.DefaultModel;
    string[] questions =
    [
        "Write me the solution for the leet code problem two sum",
        "okay so right mean the solution for the lead good problem to some"
    ];
    var trials = new List<object>();
    var total = Stopwatch.StartNew();
    var failure = (string?)null;
    var submitted = 0;
    try
    {
        await using var provider = new AgencyCliAnswerProvider(new(AnswerProviderMode.Agency, Model: configuredModel));
        for (var index = 0; index < trialCount; index++)
        {
            var preparation = Stopwatch.StartNew();
            if (index == 0 && transportFirst) await provider.WarmUpTransportAsync();
            else if (index != 0 || preparedFirst) await provider.WarmUpAsync();
            var preparationMs = index == 0 && !preparedFirst && !transportFirst ? 0 : preparation.ElapsedMilliseconds;
            var ask = Stopwatch.StartNew();
            var answer = new StringBuilder();
            long? firstText = null, terminal = null;
            var succeeded = false;
            var trialFailure = (string?)null;
            submitted++;
            try
            {
                await foreach (var item in provider.StreamAsync(new(Guid.NewGuid(), questions[index % questions.Length])))
                {
                    if (item.Kind == AnswerEventKind.TextDelta)
                    {
                        firstText ??= ask.ElapsedMilliseconds;
                        answer.Append(item.Text);
                    }
                    else
                    {
                        succeeded = true;
                        terminal = ask.ElapsedMilliseconds;
                    }
                }
            }
            catch (AnswerProviderException error) { trialFailure = error.Code; }
            catch (OperationCanceledException) { trialFailure = "cancelled"; }
            var value = answer.ToString();
            var useful = succeeded && value.Contains("```csharp", StringComparison.OrdinalIgnoreCase) &&
                value.Contains("Dictionary", StringComparison.Ordinal) && value.Contains("O(n)", StringComparison.OrdinalIgnoreCase) &&
                !System.Text.RegularExpressions.Regex.IsMatch(value,
                    @"please (paste|provide|share)|can you (provide|share)|could you|I need the actual|Get-ChildItem|working directory",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            trials.Add(new
            {
                trial = index + 1,
                state = index == 0 && transportFirst ? "transport_ready_without_session" :
                    index == 0 && !preparedFirst ? "cold_process" : "prepared_fresh_session",
                question = questions[index % questions.Length], preparationBeforeAskMs = preparationMs,
                firstProviderTextMs = firstText, completedAndSessionClosedMs = terminal,
                streamCleanupMs = ask.ElapsedMilliseconds, completed = succeeded, failure = trialFailure,
                preliminaryUsefulAnswer = useful,
                completeUsefulUnder5000Ms = useful && terminal is < 5000,
                answer = value
            });
            await PersistAsync();
            Console.WriteLine($"Trial {index + 1}: first={firstText}ms complete={terminal}ms preliminaryUseful={useful} failure={trialFailure ?? "none"}");
            if (trialFailure is not null)
            {
                failure = trialFailure;
                break;
            }
        }
    }
    catch (AnswerProviderException error) { failure = error.Code; }
    catch (OperationCanceledException) { failure = "cancelled"; }
    finally
    {
        LocalDiagnostics.FlushAndStop();
        await PersistAsync();
    }
    return failure is null ? 0 : 1;

    async Task PersistAsync()
    {
        var result = new
        {
            model, configuredModel, reasoningEffort = "low",
            profile = "concise-standalone-prepared-session",
            startup = transportFirst ? "transport_only_no_session" : preparedFirst ? "prepared_empty_session" : "cold_on_ask",
            questionSource = "User-provided public-safe intended and ASR strings, preserved exactly",
            providerAssemblySha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                await File.ReadAllBytesAsync(typeof(AgencyCliAnswerProvider).Assembly.Location))),
            submittedQuestionRequests = submitted, failure, totalMs = total.ElapsedMilliseconds,
            trials,
            limitations = "Synthetic cases only. All failed/slow trials retained. Prepared-session timing excludes measured pre-Ask initialization. Preliminary usefulness requires separate code review and execution checks. ACP stays open as a protocol transport, not an interactive question prompt."
        };
        var destination = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllTextAsync(destination, JsonSerializer.Serialize(result,
            new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
    }
}
