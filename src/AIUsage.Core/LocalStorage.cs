using System.Text;
using static AIUsage.Core.L10n;

namespace AIUsage.Core;

/// AIUsage-owned files only. No shell handlers, browser, external editor, or network transport.
public static class LocalStorage
{
    public static string ReadText(string path, int maxBytes)
    {
        path = LocalPaths.Require(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var bytes = new byte[checked(maxBytes + 1)];
        int n = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
        if (n > maxBytes) throw new IOException(T("LocalFileTooLarge"));
        int bom = n >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191 ? 3 : 0;
        try { return new UTF8Encoding(false, true).GetString(bytes, bom, n - bom); }
        catch (DecoderFallbackException e) { throw new IOException("Invalid UTF-8 in local data file.", e); }
    }
    public static void SavePrices(string path, string json)
    {
        PriceCatalog.ValidateOverrides(json);
        AtomicJson.WriteText(path, json);
    }
    public static string ExportCsv(string dataDirectory, string csv)
    {
        string directory = LocalPaths.Require(Path.Combine(dataDirectory, "exports"));
        Directory.CreateDirectory(directory);
        string path = LocalPaths.Require(Path.Combine(directory, "AIUsage-" +
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N") + ".csv"));
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(true));
        writer.Write(csv);
        return path;
    }
}
