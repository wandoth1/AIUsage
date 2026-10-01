using static AIUsage.Core.L10n;

namespace AIUsage.Core;

/// Local-disk boundary. This is not an OS sandbox and cannot defeat malicious filesystem races.
public static class LocalPaths
{
    // WinNT.h: reparse points and data that may require a remote-storage recall.
    private const FileAttributes NotResident = (FileAttributes)(0x00040000 | 0x00400000);
    public static bool IsResident(FileAttributes attributes) =>
        (attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline | NotResident)) == 0;
    public static bool IsLocalDrive(DriveType type) =>
        type is DriveType.Fixed or DriveType.Removable or DriveType.Ram or DriveType.CDRom;

    // Reject network/device syntax before querying file attributes. Drive mappings are checked next.
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith(@"\\", StringComparison.Ordinal) ||
            path.StartsWith("//", StringComparison.Ordinal) || path.Contains("://", StringComparison.Ordinal) ||
            !Path.IsPathFullyQualified(path)) throw Blocked();
        string full = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows())
        {
            if (full.Length < 3 || !char.IsAsciiLetter(full[0]) || full[1] != ':' || full[2] != '\\' ||
                full.AsSpan(2).Contains(':')) throw Blocked(); // No device paths or alternate streams.
            if (!IsLocalDrive(new DriveInfo(full[..3]).DriveType)) throw Blocked();
        }
        return full;
    }
    public static string Require(string path)
    {
        string full = Normalize(path);
        string root = Path.GetPathRoot(full)!;
        string current = root;
        Check(current);
        foreach (string part in full[root.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            try { Check(current); }
            catch (FileNotFoundException) { break; }
            catch (DirectoryNotFoundException) { break; }
        }
        return full;
    }
    private static void Check(string path)
    {
        if (!IsResident(File.GetAttributes(path))) throw Blocked();
    }
    private static LocalPathException Blocked() => new();
}

public sealed class LocalPathException : IOException
{
    public LocalPathException() : base(T("LocalPathBlocked")) { }
}
