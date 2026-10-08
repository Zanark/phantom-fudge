using System.Text;
using System.Text.Json;

namespace PhantomFudge.Core.Answers;

public static class CopilotCliProfile
{
    private const string Marker = "PhantomFudge.CopilotProfile.v1";
    internal const string MarkerFile = ".phantom-fudge-profile";
    internal const string SettingsJson = """
        {
          "disableAllHooks": true,
          "memory": false,
          "ide": { "autoConnect": false },
          "customAgents": { "defaultLocalOnly": true },
          "autoUpdate": false,
          "bashEnv": false,
          "beep": false,
          "notifications": false,
          "experimental": false
        }
        """;

    // Explicit setup only: never adopt, overwrite, or copy an existing user's profile.
    public static void Initialize(string profileDirectory)
    {
        var path = CheckPath(profileDirectory);
        try
        {
            if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any())
            {
                Validate(path);
                return;
            }
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "settings.json"), SettingsJson, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(path, MarkerFile), Marker, new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw Invalid();
        }
    }

    internal static string CheckPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw Invalid();
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        for (var dir = new DirectoryInfo(full); dir is not null; dir = dir.Parent)
            if (string.Equals(dir.Name, ".copilot", StringComparison.OrdinalIgnoreCase) ||
                (dir.Exists && dir.Attributes.HasFlag(FileAttributes.ReparsePoint)))
                throw Invalid();
        return full;
    }

    internal static void Validate(string profileDirectory)
    {
        var path = CheckPath(profileDirectory);
        try
        {
            if (ReadBounded(Path.Combine(path, MarkerFile), 128) != Marker)
                throw Invalid();
            var count = 0;
            foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos())
            {
                if (++count > 32 || entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    throw Invalid();
                if (entry is DirectoryInfo directory)
                {
                    if (entry.Name is "installed-plugins" or "skills" or "agents" or "hooks")
                    {
                        if (directory.EnumerateFileSystemInfos().Any())
                            throw Invalid();
                    }
                    else if (entry.Name is not ("session-state" or "logs" or "AppData"))
                        throw Invalid();
                }
                else if (entry.Name is not (MarkerFile or "settings.json" or "config.json" or
                    "session-store.db" or "session-store.db-shm" or "session-store.db-wal"))
                    throw Invalid();
            }

            using var settings = JsonDocument.Parse(ReadBounded(Path.Combine(path, "settings.json"), 16_384));
            var root = settings.RootElement;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw Invalid();
                switch (property.Name)
                {
                    case "disableAllHooks":
                        if (property.Value.ValueKind != JsonValueKind.True) throw Invalid();
                        break;
                    case "memory": case "autoUpdate": case "bashEnv": case "beep":
                    case "notifications": case "experimental":
                        if (property.Value.ValueKind != JsonValueKind.False) throw Invalid();
                        break;
                    case "ide":
                        RequireOnlyBoolean(property.Value, "autoConnect", false);
                        break;
                    case "customAgents":
                        RequireOnlyBoolean(property.Value, "defaultLocalOnly", true);
                        break;
                    default: throw Invalid();
                }
            }
            if (root.EnumerateObject().Count() != 9) throw Invalid();

            var configPath = Path.Combine(path, "config.json");
            if (File.Exists(configPath))
            {
                using var config = JsonDocument.Parse(ReadBounded(configPath, 65_536),
                    new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, MaxDepth = 16 });
                foreach (var property in config.RootElement.EnumerateObject())
                    if (property.Name is not ("firstLaunchAt" or "last_logged_in_user" or "logged_in_users" or "copilot_tokens"))
                        throw Invalid();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or DecoderFallbackException)
        {
            throw Invalid();
        }
    }

    private static void RequireOnlyBoolean(JsonElement value, string name, bool expected)
    {
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() != 1 ||
            !value.TryGetProperty(name, out var property) ||
            property.ValueKind != (expected ? JsonValueKind.True : JsonValueKind.False))
            throw Invalid();
    }

    private static string ReadBounded(string path, int limit)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (stream.Length > limit) throw Invalid();
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
        var chars = new char[limit + 1];
        var count = reader.ReadBlock(chars, 0, chars.Length);
        if (count > limit) throw Invalid();
        return new string(chars, 0, count);
    }

    internal static AnswerProviderException Invalid() => new("copilot_profile",
        "Initialize a separate Phantom Fudge Copilot profile and sign in there. Unmanaged settings, plugins, hooks, or profiles are not accepted.");
}
