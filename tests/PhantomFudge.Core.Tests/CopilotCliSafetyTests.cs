using System.Diagnostics;
using PhantomFudge.Core.Answers;

namespace PhantomFudge.Core.Tests;

public sealed class CopilotCliSafetyTests : IDisposable
{
    private readonly string root = Path.Combine(Environment.CurrentDirectory, "CopilotTests-" + Guid.NewGuid().ToString("N"));

    public CopilotCliSafetyTests() => Directory.CreateDirectory(root);

    [Fact]
    public void InitializeIsExplicitIdempotentAndRejectsUnmanagedData()
    {
        var profile = Path.Combine(root, "profile");
        CopilotCliProfile.Initialize(profile);
        var before = File.ReadAllText(Path.Combine(profile, "settings.json"));
        CopilotCliProfile.Initialize(profile);
        CopilotCliProfile.Validate(profile);
        Assert.Equal(before, File.ReadAllText(Path.Combine(profile, "settings.json")));
        var unmanaged = Path.Combine(root, "unmanaged");
        Directory.CreateDirectory(unmanaged);
        File.WriteAllText(Path.Combine(unmanaged, "settings.json"), "{}");
        Assert.Throws<AnswerProviderException>(() => CopilotCliProfile.Initialize(unmanaged));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(unmanaged, "settings.json")));
    }

    [Theory]
    [InlineData("mcp-config.json")]
    [InlineData("copilot-instructions.md")]
    [InlineData("plugin.json")]
    [InlineData("hooks.json")]
    public void UnexpectedProfileConfigurationIsNeverLoaded(string filename)
    {
        CopilotCliProfile.Initialize(root);
        File.WriteAllText(Path.Combine(root, filename), "synthetic");
        Assert.Throws<AnswerProviderException>(() => CopilotCliProfile.Validate(root));
    }

    [Fact]
    public void SettingsChangesAndPopulatedPluginDirectoryAreRejected()
    {
        CopilotCliProfile.Initialize(root);
        File.WriteAllText(Path.Combine(root, "settings.json"), CopilotCliProfile.SettingsJson.Replace("\"disableAllHooks\": true", "\"disableAllHooks\": false"));
        Assert.Throws<AnswerProviderException>(() => CopilotCliProfile.Validate(root));
        File.WriteAllText(Path.Combine(root, "settings.json"), CopilotCliProfile.SettingsJson);
        Directory.CreateDirectory(Path.Combine(root, "installed-plugins"));
        File.WriteAllText(Path.Combine(root, "installed-plugins", "plugin.json"), "{}");
        Assert.Throws<AnswerProviderException>(() => CopilotCliProfile.Validate(root));
    }

    [Theory]
    [InlineData(".agents")]
    [InlineData(".claude")]
    [InlineData(".github")]
    [InlineData("skills")]
    public void DedicatedProfileCannotContainPersonalDiscoveryDirectories(string directory)
    {
        CopilotCliProfile.Initialize(root);
        var path = Path.Combine(root, directory, "synthetic");
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "SKILL.md"), "Synthetic definition, never load.");
        Assert.Throws<AnswerProviderException>(() => CopilotCliProfile.Validate(root));
    }

    [Fact]
    public void DefaultCopilotDirectoryIsNotAccepted()
    {
        Assert.Throws<AnswerProviderException>(() => CopilotCliProfile.Initialize(Path.Combine(root, ".copilot")));
        Assert.Throws<AnswerProviderException>(() => CopilotCliProfile.Initialize(".\\profile"));
    }

    [Fact]
    public void LaunchUsesNoShellOrPromptArgumentsAndAllVerifiedSafetyFlags()
    {
        var info = CopilotCliLaunch.Create(Path.Combine(root, "copilot.exe"), new(AnswerProviderMode.Copilot), root, root, root);
        Assert.False(info.UseShellExecute);
        Assert.True(info.CreateNoWindow);
        Assert.True(info.RedirectStandardInput && info.RedirectStandardOutput && info.RedirectStandardError);
        Assert.Contains("--available-tools=phantom_fudge_no_tools", info.ArgumentList);
        Assert.Contains("--deny-tool=shell", info.ArgumentList);
        Assert.Contains("--deny-tool=write", info.ArgumentList);
        Assert.Contains("--deny-tool=url", info.ArgumentList);
        Assert.Contains("--no-custom-instructions", info.ArgumentList);
        Assert.Contains("--disable-builtin-mcps", info.ArgumentList);
        Assert.Contains("--no-remote-export", info.ArgumentList);
        Assert.DoesNotContain(info.ArgumentList, arg => arg.StartsWith("--allow-all", StringComparison.Ordinal) || arg is "--yolo" or "--prompt" or "-p");
    }

    [Fact]
    public void EnvironmentAllowlistDropsSecretsRuntimeInjectionAndProviderOverrides()
    {
        var info = new ProcessStartInfo();
        string[] names = ["GH_TOKEN", "GITHUB_TOKEN", "COPILOT_PROVIDER_BASE_URL", "COPILOT_CUSTOM_INSTRUCTIONS_DIRS",
            "OTEL_EXPORTER_OTLP_ENDPOINT", "NODE_OPTIONS", "OPENAI_API_KEY", "AZURE_CLIENT_SECRET",
            "ANTHROPIC_AUTH_TOKEN", "HTTP_PROXY", "HTTPS_PROXY", "BASH_ENV", "PYTHONPATH", "RANDOM_PRIVATE_DATA"];
        foreach (var name in names) info.Environment[name] = "must-not-leave-parent";
        info.Environment["PATH"] = "." + Path.PathSeparator + root + Path.PathSeparator + "relative";
        CopilotCliLaunch.SanitizeEnvironment(info, root, root);
        foreach (var name in names) Assert.False(info.Environment.ContainsKey(name));
        Assert.Equal(root, info.Environment["PATH"]);
        Assert.Equal(root, info.Environment["COPILOT_HOME"]);
        Assert.Equal("true", info.Environment["OTEL_SDK_DISABLED"]);
    }

    [Fact]
    public void InheritedHomeAndSkillDiscoveryRootsCannotReachTheChild()
    {
        var inheritedHome = Path.Combine(root, "synthetic-parent-home");
        var isolatedHome = Path.Combine(root, "synthetic-request-home");
        var profile = Path.Combine(root, "managed-profile");
        Directory.CreateDirectory(Path.Combine(inheritedHome, ".agents", "skills", "synthetic"));
        File.WriteAllText(Path.Combine(inheritedHome, ".agents", "skills", "synthetic", "SKILL.md"),
            "Synthetic parent-only definition; never load into the request.");
        var info = new ProcessStartInfo();
        foreach (var name in new[] { "HOME", "USERPROFILE", "APPDATA", "LOCALAPPDATA", "COPILOT_HOME",
            "COPILOT_CUSTOM_INSTRUCTIONS_DIRS", "CLAUDE_CONFIG_DIR", "XDG_CONFIG_HOME", "XDG_DATA_HOME" })
            info.Environment[name] = inheritedHome;
        CopilotCliLaunch.SanitizeEnvironment(info, profile, isolatedHome);
        Assert.Equal(isolatedHome, info.Environment["HOME"]);
        Assert.Equal(isolatedHome, info.Environment["USERPROFILE"]);
        Assert.Equal(Path.Combine(isolatedHome, "AppData", "Roaming"), info.Environment["APPDATA"]);
        Assert.Equal(Path.Combine(isolatedHome, "AppData", "Local"), info.Environment["LOCALAPPDATA"]);
        Assert.Equal(profile, info.Environment["COPILOT_HOME"]);
        Assert.DoesNotContain(inheritedHome, info.Environment.Values);
        Assert.False(info.Environment.ContainsKey("COPILOT_CUSTOM_INSTRUCTIONS_DIRS"));
        Assert.False(info.Environment.ContainsKey("CLAUDE_CONFIG_DIR"));
        Assert.False(info.Environment.ContainsKey("XDG_CONFIG_HOME"));
    }

    [Fact]
    public void LocalModeHasExplicitOfflineLoopbackProviderAndNoCredentials()
    {
        var settings = new AnswerSettings(AnswerProviderMode.LocalModel, Model: "synthetic", LocalEndpoint: "http://127.0.0.1:23456/v1");
        var info = CopilotCliLaunch.Create(Path.Combine(root, "copilot.exe"), settings, root, root, root);
        Assert.Equal("true", info.Environment["COPILOT_OFFLINE"]);
        Assert.Equal(settings.LocalEndpoint, info.Environment["COPILOT_PROVIDER_BASE_URL"]);
        Assert.Equal("openai", info.Environment["COPILOT_PROVIDER_TYPE"]);
        Assert.Equal("completions", info.Environment["COPILOT_PROVIDER_WIRE_API"]);
        Assert.False(info.Environment.ContainsKey("COPILOT_PROVIDER_API_KEY"));
        Assert.False(info.Environment.ContainsKey("GITHUB_TOKEN"));
    }

    [Theory]
    [InlineData("copilot.exe")]
    [InlineData("copilot --allow-all")]
    [InlineData("C:\\missing\\copilot.exe")]
    [InlineData("C:\\installed\\copilot.cmd")]
    [InlineData("C:\\installed\\copilot.ps1")]
    public void UnsafeOrMissingExplicitExecutablesFailWithoutShellFallback(string path) =>
        Assert.Throws<AnswerProviderException>(() => CopilotCliLaunch.ResolveExecutable(path));

    [Fact]
    public void ExplicitExistingExeIsPassedAsOneFileName()
    {
        var path = Path.Combine(root, "with spaces", "copilot.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "synthetic; never executed");
        Assert.Equal(path, CopilotCliLaunch.ResolveExecutable(path));
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
