using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PhantomFudge.Launcher;

internal enum LaunchMode { Normal, NotesOnly, Maintenance, ValidateInstall, SelfTest, Invalid }

internal static class LauncherContract
{
    internal const string ServiceUri = "ws://127.0.0.1:8765/v1/transcribe";

    internal static LaunchMode ParseArguments(string[] arguments) => arguments switch
    {
        [] => LaunchMode.Normal,
        ["--notes-only"] => LaunchMode.NotesOnly,
        ["--maintenance"] => LaunchMode.Maintenance,
        ["--maintenance", var root] when !string.IsNullOrWhiteSpace(root) => LaunchMode.Maintenance,
        ["--validate-install", var root] when !string.IsNullOrWhiteSpace(root) => LaunchMode.ValidateInstall,
        ["--self-test"] => LaunchMode.SelfTest,
        _ => LaunchMode.Invalid
    };

    internal static string UserSuffix(string sid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sid);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sid)))[..24];
    }

    internal static bool IsValidToken(string token) =>
        token.Length is >= 32 and <= 512 &&
        token.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    internal static string CreateToken() =>
        Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    internal static string ReadVersion(string json)
    {
        if (json.Length > 16384)
            throw new InvalidDataException("Installation manifest is too large.");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("version", out var property) ||
            property.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Installation version is missing.");
        var version = property.GetString()!;
        if (version.Length is < 1 or > 80 ||
            !version.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '+'))
            throw new InvalidDataException("Installation version is invalid.");
        // No manifest field is ever used as an executable, command argument, or data directory.
        return version;
    }
}

internal sealed record InstallPaths(string Root, string DataRoot)
{
    internal string App => Path.Combine(Root, "app", "PhantomFudge.exe");
    internal string Python => Path.Combine(Root, "runtime", "python", "python.exe");
    internal string Manager => Path.Combine(Root, "services", "transcription", "manage.py");
    internal string Models => Path.Combine(Root, "models");
    internal string Manifest => Path.Combine(Root, "install-manifest.json");
    internal string Marker => Path.Combine(Root, "app-installation.id");
    internal string Local => Path.Combine(DataRoot, ".local");
    internal string Token => Path.Combine(Local, "transcription.token");
    internal string Receipt => Path.Combine(Local, "transcription-runtime.json");
    internal string RuntimeLock => Path.Combine(Local, "transcription-runtime.lock");

    internal static InstallPaths FromLauncherDirectory(string directory, string localAppData)
    {
        if (!Path.IsPathFullyQualified(directory) || !Path.IsPathFullyQualified(localAppData))
            throw new InvalidDataException("Installation requires absolute local paths.");
        var launcher = new DirectoryInfo(Path.GetFullPath(directory));
        if (!launcher.Name.Equals("launcher", StringComparison.OrdinalIgnoreCase) || launcher.Parent is null)
            throw new InvalidDataException("Start the installed launcher, not a relocated executable.");
        return FromInstallRoot(launcher.Parent.FullName, localAppData);
    }

    internal static InstallPaths FromInstallRoot(string root, string localAppData)
    {
        if (!Path.IsPathFullyQualified(root) || !Path.IsPathFullyQualified(localAppData))
            throw new InvalidDataException("Installation requires absolute local paths.");
        return new(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)),
            Path.Combine(Path.GetFullPath(localAppData), "PhantomFudge", "InstalledRuntime"));
    }

    internal void ValidateOwnershipMarker()
    {
        RequireRegularFile(Marker);
        if (Encoding.ASCII.GetString(ReadBounded(Marker, 64)).Trim() != "phantom-fudge.install.v1")
            throw new InvalidDataException("The installation ownership marker is invalid.");
    }

    internal void Validate(bool requireSpeech)
    {
        RequireRegularFile(App);
        RequireRegularFile(Manifest);
        _ = LauncherContract.ReadVersion(new UTF8Encoding(false, true).GetString(ReadBounded(Manifest, 16384)));
        if (requireSpeech)
        {
            RequireRegularFile(Python);
            RequireRegularFile(Manager);
            RequireDirectory(Models);
            RequireDirectory(Path.Combine(Models, "vosk-model-small-en-us-0.15"));
            RequireDirectory(Path.Combine(Models, "faster-whisper-small.en"));
        }
    }

    internal static void RequireRegularFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("A required installed file is missing.");
        RejectReparsePoints(path);
    }

    internal static byte[] ReadBounded(string path, int maximumBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bytes = new byte[maximumBytes + 1];
        var count = 0;
        while (count < bytes.Length)
        {
            var read = stream.Read(bytes, count, bytes.Length - count);
            if (read == 0)
                break;
            count += read;
        }
        if (count > maximumBytes)
            throw new InvalidDataException("A local metadata file is too large.");
        return bytes[..count];
    }

    private static void RequireDirectory(string path)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException("A required installed directory is missing.");
        RejectReparsePoints(path);
    }

    internal static void RejectReparsePoints(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current);
            current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked installation or runtime paths are not supported.");
        }
    }
}

internal static class InstallerMaintenanceGate
{
    internal const string Name = @"Local\PhantomFudge-Installer-Maintenance";

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal static bool Blocks(LaunchMode mode, string mutexName = Name)
    {
        if (mode is not (LaunchMode.Normal or LaunchMode.NotesOnly) ||
            !Mutex.TryOpenExisting(mutexName, out var gate))
            return false;
        gate.Dispose();
        return true;
    }
}
