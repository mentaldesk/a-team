using System.Runtime.InteropServices;
using System.Text;

namespace ATeam.Core;

/// <summary>The system's SQLite, so a release has no native library of its own to ship.</summary>
internal static class Sqlite
{
    private const string Lib = "sqlite3";
    internal const int Row = 100;
    internal const int Null = 5;

    static Sqlite()
    {
        NativeLibrary.SetDllImportResolver(typeof(Sqlite).Assembly, (_, _, _) =>
        {
            var brew = Environment.GetEnvironmentVariable("HOMEBREW_PREFIX");
            string?[] candidates =
            [
                "/usr/lib/libsqlite3.dylib", "libsqlite3.so.0", "libsqlite3.so", "libsqlite3.dylib",
                brew is null ? null : $"{brew}/opt/sqlite/lib/libsqlite3.dylib",
                brew is null ? null : $"{brew}/opt/sqlite/lib/libsqlite3.so",
            ];
            foreach (var candidate in candidates)
                if (candidate is not null && NativeLibrary.TryLoad(candidate, out var handle))
                    return handle;
            return IntPtr.Zero;
        });
    }

    [DllImport(Lib)] internal static extern int sqlite3_open_v2(byte[] file, out IntPtr db, int flags, IntPtr vfs);
    [DllImport(Lib)] internal static extern int sqlite3_busy_timeout(IntPtr db, int ms);
    [DllImport(Lib)] internal static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int bytes, out IntPtr stmt, IntPtr tail);
    [DllImport(Lib)] internal static extern int sqlite3_bind_text(IntPtr stmt, int at, byte[] text, int bytes, IntPtr destructor);
    [DllImport(Lib)] internal static extern int sqlite3_bind_null(IntPtr stmt, int at);
    [DllImport(Lib)] internal static extern int sqlite3_step(IntPtr stmt);
    [DllImport(Lib)] internal static extern int sqlite3_column_count(IntPtr stmt);
    [DllImport(Lib)] internal static extern IntPtr sqlite3_column_name(IntPtr stmt, int column);
    [DllImport(Lib)] internal static extern int sqlite3_column_type(IntPtr stmt, int column);
    [DllImport(Lib)] internal static extern IntPtr sqlite3_column_text(IntPtr stmt, int column);
    [DllImport(Lib)] internal static extern int sqlite3_finalize(IntPtr stmt);
    [DllImport(Lib)] internal static extern int sqlite3_exec(IntPtr db, byte[] sql, IntPtr callback, IntPtr arg, IntPtr error);
    [DllImport(Lib)] internal static extern IntPtr sqlite3_errmsg(IntPtr db);
    [DllImport(Lib)] internal static extern int sqlite3_close(IntPtr db);

    internal static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text + "\0");
}

internal static class Libc
{
    [DllImport("libc", SetLastError = true)] private static extern int execvp(IntPtr file, IntPtr[] argv);

    /// <summary>Becomes the program named, so it keeps this process, its terminal and its signals. Only returns on failure.</summary>
    internal static int Exec(string file, IReadOnlyList<string> args)
    {
        var argv = new IntPtr[args.Count + 2];
        argv[0] = Marshal.StringToCoTaskMemUTF8(file);
        for (var i = 0; i < args.Count; i++)
            argv[i + 1] = Marshal.StringToCoTaskMemUTF8(args[i]);
        argv[^1] = IntPtr.Zero;
        return execvp(argv[0], argv);
    }
}
