using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ATeam.Core;

/// <summary>What only reads: the board's counts, what's been said on a card, its history, and the trends the app draws.</summary>
internal static partial class Commands
{
    private static Dictionary<string, Handler> Reading() => new()
    {
        ["wip"] = Wip,
        ["mine"] = Mine,
        ["next"] = Next,
        ["children"] = Children,
        ["body"] = Body,
        ["conversation"] = Conversation,
        ["history"] = History,
        ["moves"] = MovesSince,
        ["today"] = Today,
        ["trend"] = Trend,
        ["trends"] = TrendsNow,
        ["spending"] = Spending,
        ["decisions"] = Decisions,
        ["pr"] = Pr,
        ["checks"] = ChecksOn,
        ["criteria"] = Criteria,
        ["feedback"] = Feedback,
    };

    private static int SayPretty(JsonNode? node)
    {
        Console.Out.Write(Json.Pretty(node) + "\n");
        return 0;
    }

    private static int SayCompact(JsonNode? node)
    {
        Console.Out.Write(Json.Compact(node) + "\n");
        return 0;
    }

    private static int SayLine(string line)
    {
        Console.Out.Write(line + "\n");
        return 0;
    }

    private static JsonObject Counts(IEnumerable<JsonNode> cards)
    {
        var counts = new JsonObject();
        foreach (var group in cards.GroupBy(Cards.Status).OrderBy(group => group.Key, StringComparer.Ordinal))
            counts[group.Key] = group.Count();
        return counts;
    }

    internal static int Wip(Board board, string[] _)
    {
        var all = board.AllCards();
        static bool OpenBlocked(JsonNode card) => Cards.Status(card) != "Done" && Cards.Blocked(card);
        var dev = all.Where(card => Cards.Marked(card, "dev")).ToList();
        var counted = Counts(dev.Where(card => !OpenBlocked(card)));
        counted["blocked"] = dev.Count(OpenBlocked);
        return SayPretty(new JsonObject
        {
            ["pitches"] = Counts(all.Where(Cards.Pitch)),
            ["dev"] = counted,
            ["stakeholders"] = Counts(all.Where(card => !Cards.Pitch(card) && !Cards.Marked(card, "dev") && !Cards.Marked(card, "customer"))),
        });
    }

    internal static int Mine(Board board, string[] args)
    {
        if (args.Length < 1)
            throw new Die($"usage: board.sh {board.Team} mine <role> [STATUS...]");
        var role = args[0];
        CheckRole(role);
        var mine = new JsonArray();
        foreach (var card in board.AllCards())
            if (Cards.Own(card, role) && (args.Length == 1 || args[1..].Contains(Cards.Status(card))))
                mine.Add((JsonNode?)card.DeepClone());
        return SayPretty(mine);
    }

    internal static int Next(Board board, string[] _) =>
        SayPretty(board.Startable(board.AllCards()).FirstOrDefault());

    internal static int Children(Board board, string[] args)
    {
        if (args.Length != 1)
            throw new Die($"usage: board.sh {board.Team} children <n>");
        var read = board.Read("api", "--paginate", $"repos/{board.Repo}/issues/{args[0]}/sub_issues");
        var all = board.AllCards();
        Console.Error.Write(read.Err);
        var pages = Json.ParseMany(read.Out);
        if (pages.Any(page => page is not JsonArray))
            throw new Stop(5, "jq: error (at <stdin>:0): Cannot index string with \"number\"\n");
        var children = new JsonArray();
        foreach (var sub in pages.SelectMany(page => page!.AsArray()))
            children.Add((JsonNode?)Child(sub!, all));
        SayPretty(children);
        return read.Code;
    }

    private static JsonObject Child(JsonNode sub, List<JsonNode> all)
    {
        var number = Json.Long(sub["number"]);
        var on = all.FirstOrDefault(card => card["number"]!.GetValue<long>() == number);
        var names = new JsonArray();
        foreach (var label in sub["labels"]?.AsArray() ?? [])
            names.Add(label?["name"]?.DeepClone());
        var status = on?["status"]?.DeepClone();
        var leftInIdea = Json.Text(status) == "Idea" && !names.Any(name => Json.Text(name) == "pitch") && !Cards.Marked(on!, "idea")
            && (Json.Text(sub["body"]) ?? "").Contains("<!-- a-team:lead -->", StringComparison.Ordinal);
        return new JsonObject
        {
            ["number"] = sub["number"]?.DeepClone(),
            ["title"] = sub["title"]?.DeepClone(),
            ["state"] = sub["state"]?.DeepClone(),
            ["labels"] = names,
            ["status"] = status,
            ["leftInIdea"] = leftInIdea,
        };
    }

    internal static int Body(Board board, string[] args)
    {
        if (args.Length != 1)
            throw new Die($"usage: board.sh {board.Team} body <n>");
        var read = board.Read("api", $"repos/{board.Repo}/issues/{args[0]}", "--jq", """{number, title, body: (.body // "")}""");
        if (!read.Ok) throw new Die($"can't read #{args[0]} ({Board.Refusal(read)})");
        var issues = Json.ParseMany(read.Out).OfType<JsonObject>().ToList();
        board.WithImages(issues);
        return SayPretty(issues.FirstOrDefault());
    }

    internal static int Conversation(Board board, string[] args)
    {
        if (args.Length != 1)
            throw new Die($"usage: board.sh {board.Team} conversation <n>");
        return SayPretty(board.ConversationOn(args[0]));
    }

    internal static int History(Board board, string[] args)
    {
        if (args.Length != 1)
            throw new Die($"usage: board.sh {board.Team} history <n>");
        if (!Regex.IsMatch(args[0], @"^[0-9]+\z"))
            throw new Die($"#{args[0]} isn't an item number");
        return Board.Recorded ? SayPretty(board.HistoryOf(args[0])) : SayLine("""{"since": null, "events": []}""");
    }

    internal static int MovesSince(Board board, string[] args)
    {
        if (args.Length != 1)
            throw new Die($"usage: board.sh {board.Team} moves <since>");
        if (!Regex.IsMatch(args[0], @"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z\z"))
            throw new Die($"'{args[0]}' isn't a time like 2026-10-11T09:30:00Z");
        return Board.Recorded ? SayCompact(board.EventsSince(args[0])) : SayLine("[]");
    }

    internal static int Today(Board board, string[] args)
    {
        if (args.Length != 0)
            throw new Die($"usage: board.sh {board.Team} today");
        return SayCompact(board.BudgetToday());
    }

    internal static int Trend(Board board, string[] args)
    {
        if (args.Length != 0)
            throw new Die($"usage: board.sh {board.Team} trend");
        return Board.Recorded ? SayPretty(board.TrendNow())
            : SayLine("""{"since": null, "weekAgo": null, "accepted": 0, "spentSince": null, "spent": 0}""");
    }

    internal static int TrendsNow(Board board, string[] args)
    {
        if (args.Length != 0)
            throw new Die($"usage: board.sh {board.Team} trends");
        return Board.Recorded ? SayPretty(board.TrendsOver())
            : SayLine("""{"since": null, "queue": [], "accepted": [], "cycles": [], "cost": 0, "spentSince": null, "spent": [], "touches": [], "finished": []}""");
    }

    internal static int Spending(Board board, string[] args)
    {
        if (args.Length != 0)
            throw new Die($"usage: board.sh {board.Team} spending");
        var runs = Board.Recorded ? board.RunsWithItems() : [];
        if (runs.Count == 0)
            return SayLine("""{"since": null, "runs": [], "items": []}""");
        return SayPretty(SpentOn(runs, board.AllCards()));
    }

    /// <summary>Every run, and the cards they ran on with the pitches over those.</summary>
    internal static JsonObject SpentOn(JsonArray runs, List<JsonNode> all)
    {
        var on = runs.SelectMany(run => run!["items"]!.AsArray()).Select(Json.Long).ToHashSet();
        var over = all.Where(card => on.Contains(card["number"]!.GetValue<long>()) && card["parent"] is not null)
            .Select(card => Json.Long(card["parent"])).ToHashSet();
        var items = new JsonArray();
        foreach (var card in all.Where(card => on.Contains(card["number"]!.GetValue<long>()) || over.Contains(card["number"]!.GetValue<long>())))
            items.Add((JsonNode?)new JsonObject
            {
                ["number"] = card["number"]?.DeepClone(),
                ["title"] = card["title"]?.DeepClone(),
                ["url"] = card["url"]?.DeepClone(),
                ["status"] = card["status"]?.DeepClone(),
                ["parent"] = card["parent"]?.DeepClone(),
                ["pitch"] = Cards.IsIssue(card) && Cards.Pitch(card),
                ["theme"] = card["marks"]?["theme"]?.DeepClone(),
            });
        return new JsonObject { ["since"] = runs[0]!["started"]?.DeepClone(), ["runs"] = runs, ["items"] = items };
    }

    internal static int Decisions(Board board, string[] args)
    {
        var live = args.FirstOrDefault() != "--replaced";
        var words = live ? args : args[1..];
        return Board.Recorded ? SayCompact(board.DecisionsMatching(words, live)) : SayLine("[]");
    }

    internal static int Pr(Board board, string[] args)
    {
        if (args.Length != 1)
            throw new Die($"usage: board.sh {board.Team} pr <n>");
        var n = args[0];
        var all = board.AllCards();
        var card = long.TryParse(n, out var number) ? all.FirstOrDefault(it => it["number"]!.GetValue<long>() == number) : null;
        if (card is not null && Cards.Dependabot(card))
            return BumpPr(board, n);
        var (read, pr) = board.PrFor(n);
        Console.Error.Write(read.Err);
        if (read.Out.Trim().Length > 0) SayCompact(pr);
        return read.Code;
    }

    private static int BumpPr(Board board, string n)
    {
        if (board.BumpView(n) is { } pr) SayCompact(pr);
        return 0;
    }

    internal static int ChecksOn(Board board, string[] args)
    {
        if (args.Length != 1)
            throw new Die($"usage: board.sh {board.Team} checks <pr>");
        return SayPretty(board.Ci(args[0]));
    }

    internal static int Criteria(Board board, string[] args)
    {
        if (args.Length is < 1 or > 2)
            throw new Die($"usage: board.sh {board.Team} criteria <n> [<pr>]");
        var n = args[0];
        var pr = args.Length == 2 && args[1].Length > 0 ? args[1] : ItsPr(board, n);
        var issue = board.Read("api", $"repos/{board.Repo}/issues/{n}", "--jq", ".body // \"\"");
        Console.Error.Write(issue.Err);
        if (!issue.Ok) throw new Die($"can't read #{n}");
        var said = "";
        if (pr.Length > 0)
        {
            var pull = board.Read("api", $"repos/{board.Repo}/pulls/{pr}", "--jq", ".body // \"\"");
            Console.Error.Write(pull.Err);
            if (!pull.Ok) throw new Die($"can't read PR #{pr}");
            said = pull.Said;
        }
        return SayPretty(Checklist.Read(issue.Said + "\n", said));
    }

    private static string ItsPr(Board board, string n)
    {
        var (read, pr) = board.PrFor(n);
        Board.Must(read);
        return pr?["number"]?.ToJsonString() ?? "";
    }

    internal static int Feedback(Board board, string[] args)
    {
        if (args.Length != 2)
            throw new Die($"usage: board.sh {board.Team} feedback <role> <n>");
        var (role, n) = (args[0], args[1]);
        CheckRole(role);
        var (all, code) = board.CommentsSoFar(n);
        if (all is null) return code;
        var left = board.Unanswered(role, all);
        foreach (var comment in left)
        {
            comment.Remove("id");
            comment.Remove("eyes");
        }
        board.WithImages(left);
        var shown = new JsonArray();
        foreach (var comment in left)
            shown.Add((JsonNode?)comment);
        SayPretty(shown);
        return code;
    }
}
