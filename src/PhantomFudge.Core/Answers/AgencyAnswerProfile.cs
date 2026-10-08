namespace PhantomFudge.Core.Answers;

internal static class AgencyAnswerProfile
{
    internal const string Name = "phantom-fudge-answer";

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
        "--output-format", "json", "--stream", "on", "--no-color", "--log-level", "none"
    ];
}
