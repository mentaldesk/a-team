using System.Globalization;
using System.Text.Json.Nodes;

namespace ATeam.Core;

/// <summary>What moves a card: `move` and `add`, `claim`, and the commands behind the stakeholders' gates.</summary>
internal static partial class Commands
{
    internal delegate int Handler(Board board, string[] args);

    /// <summary>Every command the core runs, each group's from its own file.</summary>
    internal static Dictionary<string, Handler> All()
    {
        Dictionary<string, Handler>[] groups = [Moving(), Gating(), Arriving(), Writes()];
        return groups.SelectMany(group => group).ToDictionary();
    }

    private static Dictionary<string, Handler> Moving() => new()
    {
        ["list"] = List,
        ["move"] = Move,
        ["add"] = Add,
        ["claim"] = Claim,
    };

    private static void CheckRole(string role)
    {
        if (!StatusRoute.Roles.Contains(role))
            throw new Die($"unknown role '{role}' (lead | dev | customer | reviewer)");
    }

    private static void CheckState(string status)
    {
        if (!StatusRoute.Statuses.Contains(status))
            throw new Die($"unknown status '{status}'");
    }

    private static JsonNode Must(JsonNode? card, string message) => card ?? throw new Die(message);

    internal static int List(Board board, string[] statuses)
    {
        var items = board.Items();
        if (statuses.Length > 0)
        {
            var wanted = new JsonArray();
            foreach (var item in items)
                if (statuses.Contains(Json.Text(item?["status"])))
                    wanted.Add((JsonNode?)item!.DeepClone());
            items = wanted;
        }
        Console.Out.Write(Json.Pretty(items) + "\n");
        return 0;
    }

    internal static int Move(Board board, string[] args)
    {
        if (args.Length != 3)
            throw new Die($"usage: board.sh {board.Team} move <role> <n> <status>");
        var (role, n, to) = (args[0], args[1], args[2]);
        CheckRole(role);
        CheckState(to);
        var it = Must(board.Item(n), $"#{n} is not on the board (use add)");
        var from = Cards.Status(it);
        if (!StatusRoute.Allowed(role, from, to))
            throw new Die($"{role} may not move #{n} from '{from}' to '{to}'");
        board.OwnTask(role, n);
        board.OwnDocs(role, n);
        if (role == "dev" && Cards.Pitch(it))
            throw new Die("dev does not move pitches");
        if ((role, to) is ("dev", "In review") && Cards.Dependabot(it))
            throw new Die($"a Dependabot PR merges itself once it's green: leave #{n} In progress");
        var swap = (role, from, to) is ("lead", "Pitched", "Exploring");
        if (swap && !Displaced(board, n))
            throw new Die($"lead may move #{n} out of Pitched only when a higher-priority draft displaces it (nothing in Exploring out-ranks #{n})");

        if ((role, from) is ("dev", "Ready"))
            board.Mark(n, "dev", "true");
        else if ((role, from) is ("lead", "Idea"))
            Board.Must(board.Write($"label #{n} pitch", "issue", "edit", n, "-R", board.Repo, "--add-label", "pitch"));
        else if (!Cards.Own(it, role))
            throw new Die($"#{n} isn't {role}'s; leave it to the stakeholders");
        if ((role, from, to) is ("lead", "Idea", "Exploring") && Cards.Marked(it, "shelved"))
            board.ClearMark(n, "shelved");
        if (swap && !Cards.Marked(it, "displaced"))
            board.Mark(n, "displaced", "true");

        board.SetStatus(Cards.N(it), from, to, role);
        board.Log(role, n, $"{from} → {to}");
        if ((role, from, to) is ("dev", "In progress", "Ready") && Cards.Marked(it, "held"))
            board.Log(role, n, "handed back with a question");
        board.Say($"#{n}: {from} -> {to}");
        return 0;
    }

    private static bool Displaced(Board board, string n)
    {
        if (!int.TryParse(board.Text("wip", "pitched"), out var limit)) return false;
        return Cards.PitchSwap(board.AllCards(), limit).Demote.Any(card => Cards.N(card) == n);
    }

    private static bool RoleWrote(GhResult body, string marker) => body.Ok && body.Out.Contains(marker, StringComparison.Ordinal);

    internal static int Add(Board board, string[] args)
    {
        if (args.Length != 3)
            throw new Die($"usage: board.sh {board.Team} add <role> <n> <status>");
        var (role, n, to) = (args[0], args[1], args[2]);
        if (role != "you") CheckRole(role);
        CheckState(to);
        if (!StatusRoute.Allowed(role, StatusRoute.None, to))
            throw new Die($"{role} may not add items as '{to}'");
        var issue = $"repos/{board.Repo}/issues/{n}";
        var noCustomer = $"{board.Team} has no Customer lead (roles.customer in {board.Team}.json)";
        switch (role, to)
        {
            case ("customer", "In review"):
            {
                if (!board.CustomerOn) throw new Die(noCustomer);
                if (!Board.SaysTrue(board.Read("api", issue, "--jq", """.pull_request != null and .state == "open" and ((.body // "") | contains("<!-- a-team:customer -->"))""")))
                    throw new Die($"customer may only add its own open docs PR (#{n} isn't one)");
                var other = board.AllCards().FirstOrDefault(card => Cards.Marked(card, "customer") && Cards.Status(card) == "In review" && Cards.N(card) != n);
                if (other is not null)
                    throw new Die($"customer's docs PR #{Cards.N(other)} is still open: add to it rather than opening another");
                if (!board.DocsKnown())
                    throw new Die($"{board.Text("docs")} isn't in {board.Repo} yet: propose the docs before writing any");
                break;
            }
            case ("customer", "Pitched"):
            {
                if (!board.CustomerOn) throw new Die(noCustomer);
                if (!Board.SaysTrue(board.Read("api", $"repos/{board.Repo}/pulls/{n}", "--jq", """.state == "open" and .draft and ((.body // "") | contains("<!-- a-team:customer -->"))""")))
                    throw new Die($"customer may only put its own open draft docs proposal in Pitched (#{n} isn't one)");
                if (board.DocsKnown())
                    throw new Die($"{board.Text("docs")} is already in {board.Repo}: no docs proposal needed");
                var other = board.AllCards().FirstOrDefault(card => Cards.Marked(card, "customer") && Cards.Status(card) is "In review" or "Pitched" && Cards.N(card) != n);
                if (other is not null)
                    throw new Die($"customer's #{Cards.N(other)} is still open: no docs proposal beside it");
                break;
            }
            case ("customer", "Idea"):
            {
                if (!board.CustomerOn) throw new Die(noCustomer);
                if (!Board.SaysTrue(board.Read("api", issue, "--jq", """.pull_request == null and .state == "open" and ((.body // "") | contains("<!-- a-team:customer -->"))""")))
                    throw new Die($"customer may only add an open issue it opened as an Idea (#{n} isn't one)");
                var found = board.AllCards().Count(card => Cards.Marked(card, "idea") && Cards.Status(card) == "Idea" && Cards.N(card) != n);
                var limit = int.TryParse(board.Text("wip", "ideas"), out var ideas) ? ideas : 0;
                if (found >= limit)
                    throw new Die($"{found} discovered Ideas are waiting for triage (wip.ideas is {limit}): leave #{n} off the board");
                break;
            }
            case ("lead", "Ready"):
            {
                var parent = board.ParentOf(n);
                if (parent.Length == 0 || !board.AllCards().Any(card => Cards.N(card) == parent && Cards.Fileable(card)))
                    throw new Die($"#{n} isn't a sub-issue of an Approved or Building pitch: file a task with: board.sh {board.Team} task lead <pitch> <title> <file>");
                break;
            }
        }
        var existing = board.Item(n);
        if (existing is not null)
        {
            if (Cards.Status(existing) is not (StatusRoute.None or "Idea"))
                throw new Die($"#{n} is already on the board (use move)");
            var body = board.Read("api", issue, "--jq", ".body");
            if (role == "you")
            {
                if (RoleWrote(body, "<!-- a-team:"))
                    throw new Die($"#{n} is already on the board and is a role's, not yours (use move)");
            }
            else if (!RoleWrote(body, $"<!-- a-team:{role} -->"))
            {
                throw new Die($"#{n} is already on the board and isn't {role}'s (use move)");
            }
        }
        else if (role == "dev" && !RoleWrote(board.Read("api", issue, "--jq", ".body"), "<!-- a-team:dev -->"))
        {
            throw new Die($"#{n} has no dev marker: dev adds only the follow-ups it opened");
        }
        var kind = Board.Must(board.Read("api", issue, "--jq", "if .pull_request then \"pulls\" else \"issues\" end")).Said;
        if ((role, to) is ("lead", "Exploring") or ("lead", "Pitched"))
            Board.Must(board.Write($"label #{n} pitch", "api", "-X", "POST", $"{issue}/labels", "-f", "labels[]=pitch"));
        if (existing is null)
            board.AddToBoard(n, Board.Must(board.Read("api", $"repos/{board.Repo}/{kind}/{n}", "--jq", ".node_id")).Said);
        if ((role, to) is ("lead", "Idea") or ("customer", "Idea"))
            board.Mark(n, "idea", "true");
        else if ((role, to) is ("customer", "In review") or ("customer", "Pitched"))
            board.Mark(n, "customer", "true");

        board.SetStatus(existing is null ? n : Cards.N(existing), StatusRoute.None, to, role);
        board.Log(role, n, $"added as {to}");
        board.Say($"#{n}: added as {to}");
        return 0;
    }

    internal static int Claim(Board board, string[] args)
    {
        if (args.Length is < 1 or > 2)
            throw new Die($"usage: board.sh {board.Team} claim dev [<n>]");
        if (args[0] != "dev")
            throw new Die("only dev claims a task");
        var all = board.AllCards();
        var startable = board.Startable(all);
        JsonNode? it;
        if (args.Length == 2)
        {
            if (!long.TryParse(args[1], out _))
                throw new Stop(2, "jq: Invalid JSON text passed to --argjson\n");
            it = Must(startable.FirstOrDefault(card => Cards.N(card) == args[1]), $"#{args[1]} isn't a Ready task the Dev can start: it's claimed, blocked or not Ready");
        }
        else
        {
            it = startable.FirstOrDefault();
            if (it is null)
            {
                Console.Out.Write("null\n");
                return 0;
            }
        }
        var n = Cards.N(it);
        var used = all.Count(Cards.Working);
        var limit = int.TryParse(board.Text("wip", "worktrees"), out var worktrees) ? worktrees : (int?)null;
        if (!(used < limit) && !(used < (limit ?? 0) + 1 && (Json.Text(it["priority"]) == "Urgent" || Cards.Fix(it))))
            throw new Die($"no free worktree for #{n} ({used} in use, wip.worktrees is {limit})");
        board.Mark(n, "dev", "true");
        board.SetStatus(n, "Ready", "In progress", "dev");
        board.Log("dev", n, "Ready → In progress");
        Console.Out.Write(Json.Compact(new JsonObject { ["number"] = it["number"]!.DeepClone(), ["title"] = it["title"]?.DeepClone() }) + "\n");
        return 0;
    }

    private static string LocalDate(DateTimeOffset at, string format) =>
        Libc.Local(at).ToString(format, CultureInfo.InvariantCulture);
}
