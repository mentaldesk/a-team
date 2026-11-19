using System.Globalization;
using System.Text.Json.Nodes;

namespace ATeam.Core;

/// <summary>What each run noticed, kept in the record until the Lead handles it.</summary>
internal sealed partial class Board
{
    private const string NoteColumns = """
        notes.id, notes.run, runs.role, runs.started, notes.text,
            (SELECT CASE WHEN COUNT(*) = 1 THEN MIN(item) END FROM run_items WHERE run_items.run = runs.id) AS card
        """;

    /// <summary>The log of the team's run <paramref name="run"/> once it has ended, or null.</summary>
    internal string? EndedRunLog(long run) =>
        _record.Scalar($"SELECT log FROM runs WHERE id = {run} AND team = {TeamQ} AND ended IS NOT NULL;");

    /// <summary>Keeps <paramref name="notes"/> against run <paramref name="run"/>, each once however often it's asked.</summary>
    internal int KeepNotes(long run, IReadOnlyList<string> notes)
    {
        if (notes.Count == 0) return 0;
        var had = int.Parse(_record.Scalar($"SELECT COUNT(*) FROM notes WHERE run = {run};") ?? "0", CultureInfo.InvariantCulture);
        if (Would($"keep {notes.Count} notes from run {run}")) return notes.Count;
        var values = string.Join(", ", notes.Select((text, i) => $"({run}, {i + 1}, {Reconciler.Q(text)})"));
        _record.Exec($"INSERT OR IGNORE INTO notes (run, n, text) VALUES {values};");
        return int.Parse(_record.Scalar($"SELECT COUNT(*) FROM notes WHERE run = {run};") ?? "0", CultureInfo.InvariantCulture) - had;
    }

    private static Note NoteOf(Row row) => new(
        long.Parse(row["id"]!, CultureInfo.InvariantCulture), long.Parse(row["run"]!, CultureInfo.InvariantCulture), row["role"]!,
        row["card"] is { } card ? long.Parse(card, CultureInfo.InvariantCulture) : null, row["started"]!, row["text"]!);

    /// <summary>The team's notes no Lead run has handled yet, oldest first.</summary>
    internal List<Note> UnhandledNotes() =>
        [.. _record.Query($"""
            SELECT {NoteColumns} FROM notes JOIN runs ON runs.id = notes.run
                WHERE runs.team = {TeamQ} AND notes.handled IS NULL ORDER BY notes.id;
            """).Select(NoteOf)];

    /// <summary>The team's unhandled notes <paramref name="ids"/>, refusing any that isn't one.</summary>
    internal List<Note> NotesToHandle(IReadOnlyList<string> ids)
    {
        if (ids.Count == 0) throw new Die("name the notes, by the numbers the brief gives them");
        var bad = ids.FirstOrDefault(id => !long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out _));
        if (bad is not null) throw new Die($"'{bad}' isn't a note's number");
        var unhandled = UnhandledNotes().ToDictionary(note => note.Id);
        var picked = new List<Note>();
        foreach (var id in ids.Select(id => long.Parse(id, CultureInfo.InvariantCulture)).Distinct())
        {
            if (unhandled.TryGetValue(id, out var note))
            {
                picked.Add(note);
                continue;
            }
            var handled = _record.Scalar($"SELECT notes.handled FROM notes JOIN runs ON runs.id = notes.run WHERE notes.id = {id} AND runs.team = {TeamQ};");
            throw new Die(handled is null ? $"note {id} isn't one of {_team}'s" : $"note {id} is {handled} already");
        }
        return picked;
    }

    /// <summary>Marks the notes handled, how and why, so they leave the Lead's brief and stay on their run's card.</summary>
    internal void HandleNotes(IEnumerable<Note> notes, string how, string? reference, string? why)
    {
        var ids = string.Join(", ", notes.Select(note => note.Id));
        if (Would($"mark notes {ids} {how}")) return;
        _record.Exec($"""
            UPDATE notes SET handled = {Reconciler.Q(how)}, ref = {reference ?? "NULL"}, why = {Reconciler.Q(why)}, at = {Reconciler.Q(Now())}
                WHERE id IN ({ids}) AND handled IS NULL;
            """);
    }

    /// <summary>Each of <paramref name="runs"/>' notes, as Overseer shows them on its card: what it said and what the Lead did with it.</summary>
    internal Dictionary<long, JsonArray> NotesOfRuns(IEnumerable<long> runs)
    {
        var found = new Dictionary<long, JsonArray>();
        var list = string.Join(", ", runs.Distinct());
        if (list.Length == 0) return found;
        foreach (var row in _record.Query($"SELECT run, text, handled, ref, why FROM notes WHERE run IN ({list}) ORDER BY run, n;"))
        {
            var run = long.Parse(row["run"]!, CultureInfo.InvariantCulture);
            if (!found.TryGetValue(run, out var notes)) found[run] = notes = [];
            var note = new JsonObject { ["text"] = row["text"] };
            if (row["handled"] is { } handled) note["handled"] = handled;
            if (row["ref"] is { } reference) note["ref"] = long.Parse(reference, CultureInfo.InvariantCulture);
            if (row["why"] is { } why) note["why"] = why;
            notes.Add((JsonNode?)note);
        }
        return found;
    }
}
