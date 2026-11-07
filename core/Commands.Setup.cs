namespace ATeam.Core;

/// <summary>Whether a team can run, what setting one up changes, and the gate on its own.</summary>
internal static partial class Commands
{
    private static Dictionary<string, Handler> SettingUp() => new()
    {
        ["check"] = Check,
        ["setup"] = Setup,
        ["format"] = FormatOnly,
    };

    internal static int FormatOnly(Board board, string[] args)
    {
        if (args.Length != 0) throw new Die($"usage: board.sh {board.Team} format");
        return 0;
    }

    /// <summary>Exits 1 when the team can't run, and 2 when it can but something it uses is missing.</summary>
    internal static int Check(Board board, string[] args)
    {
        var found = new Findings();
        board.CheckFormat(found);
        var reached = board.CheckRepo(found);
        board.CheckWorkdir(found);
        if (reached) board.CheckVision(found);
        board.CheckDocs(found, reached);
        board.CheckRelease(found, reached);
        var unreachable = board.CheckProject(found);
        board.CheckWorkflows(found, unreachable);
        if (reached) board.CheckLabels(found);
        board.CheckIdentity(found, unreachable);
        return found.Code;
    }

    internal static int CheckUnreadable(string team, string file, string? trouble)
    {
        var found = new Findings();
        found.Problem("config", Unreadable(team, file, trouble));
        return found.Code;
    }

    /// <summary>Why a team's config can't be read, in jq's words, or a parser's if jq has none.</summary>
    internal static string Unreadable(string team, string file, string? trouble)
    {
        var said = Gh.Which("jq") is { } jq ? Proc.Capture(jq, "empty", file).Said : "";
        return UnreadableWords(team, said.Length > 0 ? said : trouble ?? "");
    }

    internal static string UnreadableWords(string team, string said)
    {
        if (said.StartsWith("jq: ", StringComparison.Ordinal)) said = said["jq: ".Length..];
        if (said.StartsWith("parse error: ", StringComparison.Ordinal)) said = said["parse error: ".Length..];
        return said.Length > 0 ? $"can't read {team}.json: {said}" : $"{team}.json isn't a JSON object";
    }

    internal static int Setup(Board board, string[] args)
    {
        board.Quiet = true;
        board.SetUpProject();
        board.SetUpLabels();
        return 0;
    }
}

/// <summary>What `check` has found: each as a line as it's found, and the exit code they come to.</summary>
internal sealed class Findings
{
    private bool _fatal;
    private bool _minor;

    internal int Code => _fatal ? 1 : _minor ? 2 : 0;

    internal void Problem(string topic, string detail)
    {
        _fatal = true;
        Line("problem", topic, detail);
    }

    internal void Note(string topic, string detail)
    {
        _minor = true;
        Line("note", topic, detail);
    }

    private static void Line(string severity, string topic, string detail) =>
        Console.Out.Write($"{severity,-9}{topic,-10}{detail}\n");
}
