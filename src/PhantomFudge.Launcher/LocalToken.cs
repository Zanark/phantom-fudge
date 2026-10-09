using System.IO;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace PhantomFudge.Launcher;

[SupportedOSPlatform("windows")]
internal static class LocalToken
{
    internal static string ReadOrCreate(InstallPaths paths)
    {
        InstallPaths.RejectReparsePoints(paths.Token);
        Directory.CreateDirectory(paths.Local);
        InstallPaths.RejectReparsePoints(paths.Token);
        if (!File.Exists(paths.Token))
        {
            using var identity = WindowsIdentity.GetCurrent();
            var sid = identity.User ?? throw new InvalidOperationException("Current-user identity is unavailable.");
            var security = new FileSecurity();
            security.SetOwner(sid);
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, AccessControlType.Allow));
            try
            {
                // CreateNew plus the explicit DACL prevents an initially public token or overwrite race.
                using var stream = new FileInfo(paths.Token).Create(FileMode.CreateNew,
                    FileSystemRights.Read | FileSystemRights.Write, FileShare.None, 4096, FileOptions.None, security);
                var bytes = Encoding.ASCII.GetBytes(LauncherContract.CreateToken());
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            catch (IOException) when (File.Exists(paths.Token))
            {
                // Another same-user startup may have won creation; only accept its validated file.
            }
        }
        return Read(paths.Token);
    }

    internal static string Read(string path)
    {
        InstallPaths.RequireRegularFile(path);
        if (new FileInfo(path).Length > 1024)
            throw new InvalidDataException("The local token is invalid.");
        VerifyPrivateAcl(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bytes = new byte[1025];
        var count = 0;
        while (count < bytes.Length)
        {
            var read = stream.Read(bytes, count, bytes.Length - count);
            if (read == 0)
                break;
            count += read;
        }
        if (count > 1024 || bytes.AsSpan(0, count).ContainsAnyExceptInRange((byte)0, (byte)127))
            throw new InvalidDataException("The local token is invalid.");
        var token = Encoding.ASCII.GetString(bytes, 0, count).Trim();
        if (!LauncherContract.IsValidToken(token))
            throw new InvalidDataException("The local token is invalid.");
        return token;
    }

    internal static void VerifyPrivateAcl(string path)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User ?? throw new InvalidOperationException("Current-user identity is unavailable.");
        var security = new FileInfo(path).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
        if (!sid.Equals(security.GetOwner(typeof(SecurityIdentifier))) || !security.AreAccessRulesProtected)
            throw new InvalidDataException("The local token needs a private current-user ACL.");
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier));
        var hasUserRead = false;
        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.AccessControlType != AccessControlType.Allow)
                continue;
            if (!sid.Equals(rule.IdentityReference))
                throw new InvalidDataException("The local token needs a private current-user ACL.");
            hasUserRead |= (rule.FileSystemRights & FileSystemRights.ReadData) != 0;
        }
        if (!hasUserRead)
            throw new InvalidDataException("The local token is not readable by its owner.");
    }
}
