using System.Runtime.InteropServices;
using System.Text;

namespace AIUsage.Core;

/// Minimal read-only access to a SQLite file through winsqlite3.dll, the SQLite build that ships with Windows.
/// No third-party package and no native code in the AIUsage folder. Only these entry points are imported (the
/// offline tests pin the list), databases are opened with SQLITE_OPEN_READONLY, and no statement that writes is
/// ever prepared: this type has no way to modify a database.
internal sealed class WinSqlite : IDisposable
{
    private const string Library = "winsqlite3.dll";
    private const int Ok = 0, Row = 100, Done = 101, OpenReadOnly = 0x1, OpenUri = 0x40, Text = 3;
    private IntPtr db, statement;

    [DllImport(Library), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);
    [DllImport(Library), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int sqlite3_close_v2(IntPtr db);
    [DllImport(Library), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int sqlite3_busy_timeout(IntPtr db, int milliseconds);
    [DllImport(Library), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int bytes, out IntPtr statement, IntPtr tail);
    [DllImport(Library), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int sqlite3_bind_int64(IntPtr statement, int index, long value);
    [DllImport(Library), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int sqlite3_step(IntPtr statement);
    [DllImport(Library), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int sqlite3_column_type(IntPtr statement, int column);
    [DllImport(Library), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern long sqlite3_column_int64(IntPtr statement, int column);
    [DllImport(Library), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr sqlite3_column_text(IntPtr statement, int column);
    [DllImport(Library), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int sqlite3_column_bytes(IntPtr statement, int column);
    [DllImport(Library), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int sqlite3_finalize(IntPtr statement);

    /// Opens <paramref name="path"/> read-only. With <paramref name="immutable"/> SQLite takes no locks and
    /// creates no -shm/-wal side files; use it only when no write-ahead log exists.
    public WinSqlite(string path, bool immutable)
    {
        // A file: URI lets "immutable" be requested; only the characters SQLite's URI parser treats specially are escaped.
        string uri = "file:///" + path.Replace("%", "%25").Replace("?", "%3f").Replace("#", "%23").Replace('\\', '/') + (immutable ? "?immutable=1" : "");
        int code;
        try { code = sqlite3_open_v2(Utf8(uri), out db, OpenReadOnly | OpenUri, IntPtr.Zero); }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        { throw new IOException("Windows SQLite (winsqlite3.dll) is not available.", e); }
        if (code != Ok) { Dispose(); throw new IOException("SQLite could not open the database (code " + code + ")."); }
        sqlite3_busy_timeout(db, 1500);
    }
    public void Prepare(string sql)
    {
        Finish();
        byte[] bytes = Utf8(sql);
        int code = sqlite3_prepare_v2(db, bytes, bytes.Length, out statement, IntPtr.Zero);
        if (code != Ok) throw new IOException("SQLite could not prepare the query (code " + code + ").");
    }
    public void Bind(int index, long value)
    {
        if (sqlite3_bind_int64(statement, index, value) != Ok) throw new IOException("SQLite could not bind a parameter.");
    }
    /// True while rows remain; busy, corrupt or interrupted databases surface as IOException.
    public bool Read()
    {
        int code = sqlite3_step(statement);
        if (code == Row) return true;
        if (code == Done) return false;
        throw new IOException("SQLite could not read the database (code " + code + ").");
    }
    public long Integer(int column) => sqlite3_column_int64(statement, column);
    public bool IsText(int column) => sqlite3_column_type(statement, column) == Text;
    /// UTF-8 length of a text column. sqlite3_column_text is called first so the length is for UTF-8.
    public int TextLength(int column) { sqlite3_column_text(statement, column); return sqlite3_column_bytes(statement, column); }
    /// Copies a text column's UTF-8 bytes; the pointer SQLite returns is only valid until the next step.
    public int CopyText(int column, byte[] destination)
    {
        IntPtr pointer = sqlite3_column_text(statement, column);
        int length = sqlite3_column_bytes(statement, column);
        if (pointer == IntPtr.Zero || length <= 0) return 0;
        if (length > destination.Length) throw new ArgumentException("Destination is smaller than the column.");
        Marshal.Copy(pointer, destination, 0, length);
        return length;
    }
    private void Finish() { if (statement != IntPtr.Zero) { sqlite3_finalize(statement); statement = IntPtr.Zero; } }
    public void Dispose() { Finish(); if (db != IntPtr.Zero) { sqlite3_close_v2(db); db = IntPtr.Zero; } }
    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text + "\0");
}
