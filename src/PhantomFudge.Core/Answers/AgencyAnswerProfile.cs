namespace PhantomFudge.Core.Answers;

internal static class AgencyAnswerProfile
{
    internal const string Name = "phantom-fudge-answer";
    internal const string DefaultModel = "gpt-5.6-sol-fast";
    internal const string ReasoningEffort = "low";
    internal const string AgentFileName = Name + ".agent.md";
    // Agent YAML tools: [] disables tools; the CLI allowlist must still use a nonempty sentinel.
    internal static string AgentDefinition => """
        ---
        name: phantom-fudge-answer
        description: Direct standalone technical answers without tools or workspace actions.
        tools: []
        ---

        """ + AgencyAnswerPrompt.Policy + "\n";

    // Accepted by `agency config check --skip-remotes <explicit-file>`.
    // This validates configuration only; it is not an authorization or launch gate.
    internal const string Toml = """
        [profiles.phantom-fudge-answer]
        [profiles.phantom-fudge-answer.mcps]
        [profiles.phantom-fudge-answer.plugins]
        default = []

        """;

    internal static string[] CandidateArguments =>
    [
        "copilot", "--profile-only", Name, "--no-default-mcps", "--no-config-plugins",
        "--no-aec", "--no-input-processing", "--",
        "--no-auto-update", "--no-custom-instructions", "--disable-builtin-mcps",
        "--no-ask-user", "--no-remote", "--no-remote-export", "--no-experimental",
        "--no-bash-env", "--disallow-temp-dir", "--available-tools=phantom_fudge_no_tools",
        "--deny-tool=shell", "--deny-tool=write", "--deny-tool=read", "--deny-tool=url",
        "--no-color", "--log-level", "none", "--no-eager-powershell-resolution",
        "--acp", "--reasoning-effort", ReasoningEffort, "--agent", Name
    ];
}
