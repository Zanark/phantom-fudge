using System.Diagnostics;
using System.Text;

namespace PhantomFudge.Core.Answers;

internal static class CopilotCliLaunch
{
    internal static readonly string[] SupportedVersions = ["GitHub Copilot CLI 1.0.81-0.", "GitHub Copilot CLI 1.0.91."];
    internal static readonly string[] SafetyArguments =
    [
        "--no-auto-update", "--no-custom-instructions", "--disable-builtin-mcps",
        "--no-ask-user", "--no-remote", "--no-remote-export", "--no-experimental",
        "--no-bash-env", "--disallow-temp-dir",
        // In 1.0.81-0 both bare and empty-valued allowlists restore ALL tools.
        // An explicitly nonexistent name is the offline-verified zero-tool selection.
        "--available-tools=phantom_fudge_no_tools",
        "--deny-tool=shell", "--deny-tool=write", "--deny-tool=url",
        "--output-format", "json", "--stream", "on", "--no-color", "--log-level", "none"
    ];

    internal static string ResolveExecutable(string? explicitPath, string? searchPath = null)
    {
        if (explicitPath is not null)
        {
            if (IsExecutable(explicitPath)) return Path.GetFullPath(explicitPath);
            throw Missing();
        }
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var standalone = Path.Combine(local, "Microsoft", "WinGet", "Links", "copilot.exe");
        if (IsExecutable(standalone)) return standalone;
        foreach (var path in (searchPath ?? Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (!Path.IsPathFullyQualified(path) ||
                string.Equals(Path.TrimEndingDirectorySeparator(path), Environment.CurrentDirectory, StringComparison.OrdinalIgnoreCase))
                continue;
            var candidate = Path.Combine(path, "copilot.exe");
            if (IsExecutable(candidate)) return candidate;
            // Resolve npm's known package layout, never read or evaluate its shell shims.
            var architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
            {
                System.Runtime.InteropServices.Architecture.X64 => "x64",
                System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
                _ => ""
            };
            candidate = Path.Combine(path, "node_modules", "@github", "copilot", "node_modules",
                "@github", "copilot-win32-" + architecture, "copilot.exe");
            if (architecture.Length != 0 && IsExecutable(candidate)) return candidate;
        }
        throw Missing();
    }

    private static bool IsExecutable(string path) =>
        Path.IsPathFullyQualified(path) && Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path);

    private static AnswerProviderException Missing() => new("copilot_not_found",
        "Choose an installed standalone copilot.exe (absolute path). Shell commands, .cmd, and .ps1 wrappers are not accepted.");

    internal static ProcessStartInfo Create(string executable, AnswerSettings settings, string profile, string workingDirectory,
        string isolatedHome, bool versionOnly = false)
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
        SanitizeEnvironment(info, profile, isolatedHome);
        if (versionOnly)
        {
            info.ArgumentList.Add("--no-auto-update");
            info.ArgumentList.Add("--version");
            info.Environment["COPILOT_OFFLINE"] = "true";
            return info;
        }
        foreach (var arg in SafetyArguments) info.ArgumentList.Add(arg);
        if (settings.Model is not null)
            info.Environment["COPILOT_MODEL"] = settings.Model;
        if (settings.Mode == AnswerProviderMode.LocalModel)
        {
            info.Environment["COPILOT_OFFLINE"] = "true";
            info.Environment["COPILOT_PROVIDER_BASE_URL"] = settings.LocalEndpoint;
            info.Environment["COPILOT_PROVIDER_TYPE"] = "openai";
            info.Environment["COPILOT_PROVIDER_WIRE_API"] = "completions";
            info.Environment["COPILOT_PROVIDER_TRANSPORT"] = "http";
        }
        return info;
    }

    internal static void SanitizeEnvironment(ProcessStartInfo info, string profile, string home)
    {
        string[] ordinary =
        [
            "SystemRoot", "WINDIR", "SystemDrive", "COMSPEC", "PATHEXT",
            "PROCESSOR_ARCHITECTURE", "NUMBER_OF_PROCESSORS", "TEMP", "TMP"
        ];
        var retained = ordinary.Where(info.Environment.ContainsKey).ToDictionary(k => k, k => info.Environment[k]);
        var path = info.Environment.TryGetValue("PATH", out var inheritedPath) ? inheritedPath : "";
        info.Environment.Clear();
        foreach (var (key, value) in retained) info.Environment[key] = value;
        info.Environment["PATH"] = string.Join(Path.PathSeparator,
            (path ?? "").Split(Path.PathSeparator).Where(p => Path.IsPathFullyQualified(p) &&
                !string.Equals(Path.TrimEndingDirectorySeparator(p), Environment.CurrentDirectory, StringComparison.OrdinalIgnoreCase)));
        info.Environment["COPILOT_HOME"] = profile;
        info.Environment["HOME"] = home;
        info.Environment["USERPROFILE"] = home;
        info.Environment["APPDATA"] = Path.Combine(home, "AppData", "Roaming");
        info.Environment["LOCALAPPDATA"] = Path.Combine(home, "AppData", "Local");
        info.Environment["COPILOT_AUTO_UPDATE"] = "false";
        info.Environment["OTEL_SDK_DISABLED"] = "true";
        info.Environment["NO_COLOR"] = "1";
    }
}
