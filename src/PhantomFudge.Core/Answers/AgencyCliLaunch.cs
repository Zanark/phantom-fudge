using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace PhantomFudge.Core.Answers;

internal static class AgencyCliLaunch
{
    internal static bool HasVersionIdentity(string output) => Regex.IsMatch(output.Trim(),
        @"\Aagency [0-9]+(?:\.[0-9]+){2,3}(?:[-+][A-Za-z0-9.-]+)? \(commit: [0-9a-fA-F]{7,64}\)(?: [^\r\n]+)?\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    internal static ProcessStartInfo Create(string executable, AnswerSettings settings, string workingDirectory,
        string home, string profile, string logDirectory, bool versionOnly = false)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false, true),
            StandardOutputEncoding = new UTF8Encoding(false, true),
            StandardErrorEncoding = new UTF8Encoding(false, true),
            WorkingDirectory = workingDirectory
        };
        SanitizeEnvironment(info, home, profile);
        if (versionOnly)
        {
            info.ArgumentList.Add("--version");
            return info;
        }
        foreach (var argument in new[] { "--verbosity", "error", "--log-dir", logDirectory })
            info.ArgumentList.Add(argument);
        foreach (var argument in AgencyAnswerProfile.CandidateArguments)
            info.ArgumentList.Add(argument);
        info.ArgumentList.Add("--model");
        info.ArgumentList.Add(settings.Model ?? AgencyAnswerProfile.DefaultModel);
        return info;
    }

    internal static void SanitizeEnvironment(ProcessStartInfo info, string home, string profile)
    {
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SYSTEMROOT", "WINDIR", "SYSTEMDRIVE", "COMSPEC", "PATHEXT", "TEMP", "TMP",
            "APPDATA", "LOCALAPPDATA", "PROCESSOR_ARCHITECTURE", "NUMBER_OF_PROCESSORS", "PATH"
        };
        foreach (var name in info.Environment.Keys.ToArray())
            if (!keep.Contains(name)) info.Environment.Remove(name);
        var path = info.Environment.TryGetValue("PATH", out var value) ? value : "";
        info.Environment["PATH"] = string.Join(Path.PathSeparator, (path ?? "").Split(Path.PathSeparator)
            .Where(p => Path.IsPathFullyQualified(p) &&
                !string.Equals(Path.TrimEndingDirectorySeparator(p), Environment.CurrentDirectory, StringComparison.OrdinalIgnoreCase)));
        // Agency, not the app, manages its existing cached installation and authentication.
        // Unlike direct Copilot, APPDATA/LOCALAPPDATA must remain real (observed probe).
        info.Environment["HOME"] = home;
        info.Environment["USERPROFILE"] = home;
        info.Environment["COPILOT_HOME"] = profile;
        info.Environment["COPILOT_AUTO_UPDATE"] = "false";
        info.Environment["OTEL_SDK_DISABLED"] = "true";
        info.Environment["NO_COLOR"] = "1";
    }
}
