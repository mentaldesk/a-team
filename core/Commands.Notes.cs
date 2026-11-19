using System.Text.Json.Nodes;

namespace ATeam.Core;

/// <summary>What each run noticed: kept by the dispatcher, listed in the Lead's brief, and handled by the Lead.</summary>
internal static partial class Commands
{
    private static Dictionary<string, Handler> Noting() => new()
    {
        ["notes"] = KeepNotes,
        ["noticed"] = Noticed,
    };

    internal static int KeepNotes(Board board, string[] args)
    {
        if (args.Length != 1 || !long.TryParse(args[0], out var run))
            throw new Die($"usage: board.sh {board.Team} notes <run>");
        var log = board.EndedRunLog(run) ?? throw new Die($"run {run} isn't one of {board.Team}'s that has ended");
        var kept = board.KeepNotes(run, Notes.Parse(Notes.FinalSummary(log)));
        if (kept > 0) board.Say($"run {run}: kept {kept} {(kept == 1 ? "note" : "notes")}");
        return 0;
    }

    private const string NoticedUsage = """
        lead file <title> <file> <note>... | add <n> <file> <note>... | answer <file> <note>... | drop <file> <note>...
        """;

    internal static int Noticed(Board board, string[] args)
    {
        if (args.Length == 0)
        {
            var groups = Notes.Group(board.UnhandledNotes());
            if (groups.Count > 0) Console.Out.Write(Notes.Brief(board.Team, groups, IdeasRoom(board, board.AllCards())));
            return 0;
        }
        if (args.Length < 2)
            throw new Die($"usage: board.sh {board.Team} noticed [{NoticedUsage}]");
        Alone("lead", args[0], $"{args[0]} may not handle notes; the Lead handles what every run noticed");
        return args[1] switch
        {
            "file" when args.Length >= 5 => FileNotes(board, args[2], args[3], args[4..]),
            "add" when args.Length >= 5 => AddNotes(board, args[2], args[3], args[4..]),
            "answer" when args.Length >= 4 => SettleNotes(board, "answered", args[2], args[3..]),
            "drop" when args.Length >= 4 => SettleNotes(board, "dropped", args[2], args[3..]),
            _ => throw new Die($"usage: board.sh {board.Team} noticed {NoticedUsage}"),
        };
    }

    private static int IdeasRoom(Board board, List<JsonNode> all) =>
        (int.TryParse(board.Text("wip", "ideas"), out var limit) ? limit : 0) - board.WaitingForPriority(all);

    private static string OneLine(string file, string what)
    {
        NeedsFile(file);
        var said = File.ReadAllText(file).Split('\n').Where(HasText).ToList();
        if (said.Count != 1) throw new Die($"{what} is one line: {file} has {Math.Max(said.Count, 1)}");
        return said[0].Trim();
    }

    private static string Evidence(List<Note> notes) => Notes.Evidence(new NoteGroup(notes));

    private static int FileNotes(Board board, string title, string file, string[] ids)
    {
        if (Blank(title)) throw new Die("an Idea needs a title");
        NeedsFile(file);
        var notes = board.NotesToHandle(ids);
        var room = IdeasRoom(board, board.AllCards());
        if (room <= 0)
            throw new Die($"no room for another Idea (wip.ideas is {board.Text("wip", "ideas")}): the notes stay in your next brief");
        var body = Notes.WithEvidence(Signed(Board.FileText(file), "lead"), Evidence(notes));
        var url = OpenIssue(board, title, body);
        var n = "new";
        var node = "";
        if (!board.DryRun)
        {
            n = url[(url.LastIndexOf('/') + 1)..];
            node = Board.Must(board.Read("api", $"repos/{board.Repo}/issues/{n}", "--jq", ".node_id")).Said;
        }
        board.AddToBoard(n, node);
        board.Mark(n, "idea", "true");
        board.SetStatus(n, StatusRoute.None, "Idea", "lead");
        board.HandleNotes(notes, "filed", board.DryRun ? null : n, null);
        board.Log("lead", n, $"filed as an Idea from {notes.Count} {(notes.Count == 1 ? "note" : "notes")}");
        Console.Out.Write(n + "\n");
        return 0;
    }

    private static int AddNotes(Board board, string n, string file, string[] ids)
    {
        NeedsFile(file);
        var it = Must(board.Item(n), $"#{n} is not on the board");
        if (!Cards.IsIssue(it) || Json.Truthy(it["closed"]) || !(Cards.Status(it) == "Idea" || Cards.Pitch(it) && Cards.Status(it) != "Done"))
            throw new Die($"notes are added only to an open Idea or pitch (#{n} is '{Cards.Status(it)}')");
        var notes = board.NotesToHandle(ids);
        var body = $"{Board.FileText(file)}\n\n{Evidence(notes)}.\n\n<!-- a-team:lead -->";
        board.Show(body);
        Commented(board, n, body);
        board.HandleNotes(notes, "added", n, null);
        board.Log("lead", n, $"added {notes.Count} {(notes.Count == 1 ? "note" : "notes")}");
        return 0;
    }

    private static int SettleNotes(Board board, string how, string file, string[] ids)
    {
        var why = OneLine(file, how == "dropped" ? "why you dropped them" : "the answer");
        var notes = board.NotesToHandle(ids);
        board.HandleNotes(notes, how, null, why);
        board.Say($"notes {string.Join(", ", notes.Select(note => note.Id))}: {how}");
        return 0;
    }
}
