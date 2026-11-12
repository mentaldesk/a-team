using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace ATeam.Core;

/// <summary>A row of a query, by column name. A column's value is its text, or null.</summary>
internal sealed class Row(Dictionary<string, string?> columns, string? first)
{
    public string? this[string column] => columns[column];

    public string? First => first;
}

/// <summary>history.db: the board's own record, shared with board.sh.</summary>
internal sealed class Record : IDisposable
{
    private const int ReadWriteCreate = 6;
    private const string Schema = """
        CREATE TABLE IF NOT EXISTS events (
            id INTEGER PRIMARY KEY, team TEXT NOT NULL, item INTEGER NOT NULL, at TEXT NOT NULL,
            who TEXT NOT NULL, what TEXT NOT NULL);
          CREATE TABLE IF NOT EXISTS runs (id INTEGER PRIMARY KEY, team TEXT NOT NULL, role TEXT NOT NULL,
            pid INTEGER NOT NULL, log TEXT NOT NULL, started TEXT NOT NULL, ended TEXT, cost REAL, outcome TEXT);
          CREATE TABLE IF NOT EXISTS run_items (run INTEGER NOT NULL, item INTEGER NOT NULL);
          CREATE TABLE IF NOT EXISTS caught_up (team TEXT PRIMARY KEY, started TEXT NOT NULL, at TEXT NOT NULL);
          CREATE TABLE IF NOT EXISTS seen (team TEXT NOT NULL, item INTEGER NOT NULL, node TEXT NOT NULL,
            status TEXT NOT NULL, PRIMARY KEY (team, item));
          CREATE TABLE IF NOT EXISTS github (team TEXT NOT NULL, id TEXT NOT NULL, event INTEGER,
            PRIMARY KEY (team, id));
          CREATE TABLE IF NOT EXISTS queue (team TEXT NOT NULL, at TEXT NOT NULL, waiting INTEGER NOT NULL);
          CREATE TABLE IF NOT EXISTS spent (team TEXT NOT NULL, item INTEGER, at TEXT NOT NULL, seconds INTEGER NOT NULL,
            gate TEXT NOT NULL, what TEXT NOT NULL);
          CREATE TABLE IF NOT EXISTS board (team TEXT NOT NULL, item INTEGER NOT NULL, node TEXT NOT NULL, status TEXT NOT NULL,
            priority TEXT, since TEXT NOT NULL, content TEXT NOT NULL DEFAULT '{}', mirror TEXT, mirrored_status TEXT,
            mirrored_priority TEXT, marks TEXT, mirrored_rank TEXT, mirrored_theme TEXT, PRIMARY KEY (team, item));
          CREATE TABLE IF NOT EXISTS boards (team TEXT PRIMARY KEY, project TEXT NOT NULL, read TEXT, failed TEXT, retry TEXT, repaired TEXT);
          CREATE TABLE IF NOT EXISTS details (team TEXT PRIMARY KEY, detail TEXT, synced TEXT, failed TEXT);
          CREATE TABLE IF NOT EXISTS formats (team TEXT PRIMARY KEY, format INTEGER NOT NULL, version TEXT NOT NULL, moved TEXT);
          CREATE TABLE IF NOT EXISTS format_moves (team TEXT PRIMARY KEY, pid INTEGER NOT NULL);
        """;

    private readonly IntPtr _db;

    private Record(IntPtr db) => _db = db;

    internal static Record Open(string path)
    {
        if (Sqlite.sqlite3_open_v2(Sqlite.Utf8(path), out var db, ReadWriteCreate, IntPtr.Zero) != 0)
            throw new Die($"can't open {path}");
        Sqlite.sqlite3_busy_timeout(db, 5000);
        var record = new Record(db);
        record.Exec(Schema);
        return record;
    }

    internal void TryRollback() => Sqlite.sqlite3_exec(_db, Sqlite.Utf8("ROLLBACK;"), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

    internal void Exec(string sql)
    {
        if (Sqlite.sqlite3_exec(_db, Sqlite.Utf8(sql), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) != 0)
            throw new InvalidOperationException(Error());
    }

    internal List<Row> Query(string sql, params string?[] args)
    {
        if (Sqlite.sqlite3_prepare_v2(_db, Sqlite.Utf8(sql), -1, out var stmt, IntPtr.Zero) != 0)
            throw new InvalidOperationException(Error());
        var rows = new List<Row>();
        try
        {
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] is { } text)
                    Sqlite.sqlite3_bind_text(stmt, i + 1, Sqlite.Utf8(text), -1, new IntPtr(-1));
                else
                    Sqlite.sqlite3_bind_null(stmt, i + 1);
            }
            var columns = Sqlite.sqlite3_column_count(stmt);
            int step;
            while ((step = Sqlite.sqlite3_step(stmt)) == Sqlite.Row)
            {
                var row = new Dictionary<string, string?>(columns);
                string? first = null;
                for (var c = 0; c < columns; c++)
                {
                    var name = Marshal.PtrToStringUTF8(Sqlite.sqlite3_column_name(stmt, c))!;
                    var value = Sqlite.sqlite3_column_type(stmt, c) == Sqlite.Null
                        ? null
                        : Marshal.PtrToStringUTF8(Sqlite.sqlite3_column_text(stmt, c));
                    row[name] = value;
                    if (c == 0) first = value;
                }
                rows.Add(new Row(row, first));
            }
            if (step != Sqlite.Done)
                throw new InvalidOperationException(Error());
        }
        finally
        {
            Sqlite.sqlite3_finalize(stmt);
        }
        return rows;
    }

    /// <summary>The rows as `sqlite3 -json` prints them, each column in its own JSON type.</summary>
    internal JsonArray Rows(string sql)
    {
        if (Sqlite.sqlite3_prepare_v2(_db, Sqlite.Utf8(sql), -1, out var stmt, IntPtr.Zero) != 0)
            throw new InvalidOperationException(Error());
        var rows = new JsonArray();
        try
        {
            var columns = Sqlite.sqlite3_column_count(stmt);
            int step;
            while ((step = Sqlite.sqlite3_step(stmt)) == Sqlite.Row)
            {
                var row = new JsonObject();
                for (var c = 0; c < columns; c++)
                    row[Marshal.PtrToStringUTF8(Sqlite.sqlite3_column_name(stmt, c))!] = Cell(stmt, c);
                rows.Add((JsonNode?)row);
            }
            if (step != Sqlite.Done)
                throw new InvalidOperationException(Error());
        }
        finally
        {
            Sqlite.sqlite3_finalize(stmt);
        }
        return rows;
    }

    private static string CellText(IntPtr stmt, int column) => Marshal.PtrToStringUTF8(Sqlite.sqlite3_column_text(stmt, column))!;

    private JsonNode? Cell(IntPtr stmt, int column) => Sqlite.sqlite3_column_type(stmt, column) switch
    {
        Sqlite.Null => null,
        Sqlite.Integer => JsonNode.Parse(CellText(stmt, column)),
        Sqlite.Float => JsonNode.Parse(Real(Sqlite.sqlite3_column_double(stmt, column))),
        _ => CellText(stmt, column),
    };

    /// <summary>A real number as `sqlite3 -json` spells it.</summary>
    private string Real(double value)
    {
        var whole = (long)value;
        if (value == whole) return $"{whole.ToString(CultureInfo.InvariantCulture)}.0";
        if (Sqlite.sqlite3_prepare_v2(_db, Sqlite.Utf8("SELECT printf('%!.20g', ?1);"), -1, out var stmt, IntPtr.Zero) != 0)
            throw new InvalidOperationException(Error());
        try
        {
            Sqlite.sqlite3_bind_double(stmt, 1, value);
            Sqlite.sqlite3_step(stmt);
            return CellText(stmt, 0);
        }
        finally
        {
            Sqlite.sqlite3_finalize(stmt);
        }
    }

    internal string? Scalar(string sql, params string?[] args)
    {
        var rows = Query(sql, args);
        return rows.Count == 0 ? null : rows[0].First;
    }

    private string Error() => Marshal.PtrToStringUTF8(Sqlite.sqlite3_errmsg(_db)) ?? "sqlite failed";

    public void Dispose() => Sqlite.sqlite3_close(_db);
}
