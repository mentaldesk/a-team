using System.Text.Json.Nodes;

namespace ATeam.Core;

internal static class Commands
{
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

    private static bool HasPitch(JsonNode item) =>
        item["labels"]?.AsArray().Any(label => Json.Text(label) == "pitch") == true;

    private static bool Own(JsonNode item, string role) => role switch
    {
        "lead" => HasPitch(item),
        "dev" => item["marks"]?["dev"]?.GetValueKind() == System.Text.Json.JsonValueKind.True,
        "customer" => item["marks"]?["customer"]?.GetValueKind() == System.Text.Json.JsonValueKind.True,
        _ => false,
    };

    private static JsonNode? Find(JsonArray items, string n) =>
        items.FirstOrDefault(item => item?["number"]?.ToJsonString() == n);

    internal static int Move(Board board, string[] args)
    {
        if (args.Length != 3)
            throw new Die($"usage: board.sh {board.Team} move <role> <n> <status>");
        var (role, n, to) = (args[0], args[1], args[2]);
        if (!StatusRoute.Roles.Contains(role))
            throw new Die($"unknown role '{role}' (lead | dev | customer | reviewer)");
        if (!StatusRoute.Statuses.Contains(to))
            throw new Die($"unknown status '{to}'");
        if (!long.TryParse(n, out _))
            throw new Fallback("a number that isn't one");
        var items = board.Items();
        var item = Find(items, n) ?? throw new Die($"#{n} is not on the board (use add)");
        var from = Json.Text(item["status"])!;
        if (!StatusRoute.Allowed(role, from, to))
            throw new Die($"{role} may not move #{n} from '{from}' to '{to}'");
        if (role is "dev" or "reviewer"
            && Environment.GetEnvironmentVariable("A_TEAM_RUN_TASK") is { Length: > 0 } task && n != task)
            throw new Fallback("a run for another task");
        if (role == "customer" && item["marks"]?["customer"] is null)
            throw new Die($"customer only touches its own docs PR (#{n} isn't it)");
        if (role == "dev" && HasPitch(item))
            throw new Die("dev does not move pitches");
        if ((role, to) is ("dev", "In review") && Json.Truthy(item["dependabot"]))
            throw new Die($"a Dependabot PR merges itself once it's green: leave #{n} In progress");
        if ((role, from, to) is ("lead", "Pitched", "Exploring"))
            throw new Fallback("a swap in Pitched, which lead-next decides");

        if ((role, from) is ("dev", "Ready"))
        {
            board.Mark(n, "dev", "true");
        }
        else if ((role, from) is ("lead", "Idea"))
        {
            var labelled = board.Label(n);
            if (!labelled.Ok)
                throw new Fallback("the label wouldn't go on");
        }
        else if (!Own(item, role))
        {
            throw new Die($"#{n} isn't {role}'s; leave it to the stakeholders");
        }
        if ((role, from, to) is ("lead", "Idea", "Exploring") && Json.Truthy(item["marks"]?["shelved"]))
            board.ClearMark(n, "shelved");

        board.Committed = true;
        board.SetStatus(item["number"]!.ToJsonString(), to);
        board.Log(role, n, $"{from} → {to}");
        if ((role, from, to) is ("dev", "In progress", "Ready") && Json.Truthy(item["marks"]?["held"]))
            board.Log(role, n, "handed back with a question");
        Console.Out.Write($"#{n}: {from} -> {to}\n");
        return 0;
    }

    internal static int Add(Board board, string[] args, JsonObject config)
    {
        if (args.Length != 3)
            throw new Die($"usage: board.sh {board.Team} add <role> <n> <status>");
        var (role, n, to) = (args[0], args[1], args[2]);
        if (role != "you" && !StatusRoute.Roles.Contains(role))
            throw new Die($"unknown role '{role}' (lead | dev | customer | reviewer)");
        if (!StatusRoute.Statuses.Contains(to))
            throw new Die($"unknown status '{to}'");
        if (!StatusRoute.Allowed(role, StatusRoute.None, to))
            throw new Die($"{role} may not add items as '{to}'");
        if (role == "customer" || (role, to) is ("lead", "Ready"))
            throw new Fallback("an add with more checks than ported so far");
        if (!long.TryParse(n, out _))
            throw new Fallback("a number that isn't one");

        var existing = Find(board.Items(), n);
        if (existing is not null)
        {
            if (Json.Text(existing["status"]) is not (StatusRoute.None or "Idea"))
                throw new Die($"#{n} is already on the board (use move)");
            var body = board.Issue(n, ".body");
            if (role == "you")
            {
                if (body.Ok && body.Out.Contains("<!-- a-team:", StringComparison.Ordinal))
                    throw new Die($"#{n} is already on the board and is a role's, not yours (use move)");
            }
            else if (!body.Ok || !body.Out.Contains($"<!-- a-team:{role} -->", StringComparison.Ordinal))
            {
                throw new Die($"#{n} is already on the board and isn't {role}'s (use move)");
            }
        }
        else if (role == "dev")
        {
            var body = board.Issue(n, ".body");
            if (!body.Ok || !body.Out.Contains("<!-- a-team:dev -->", StringComparison.Ordinal))
                throw new Die($"#{n} has no dev marker: dev adds only the follow-ups it opened");
        }
        var kind = board.Issue(n, "if .pull_request then \"pulls\" else \"issues\" end");
        if (!kind.Ok)
            throw new Fallback("the issue wouldn't read");
        if ((role, to) is ("lead", "Exploring") or ("lead", "Pitched") && !board.LabelNew(n).Ok)
            throw new Fallback("the label wouldn't go on");
        if (existing is null)
        {
            var node = board.Content(kind.Said, n, ".node_id");
            if (!node.Ok)
                throw new Fallback("the node id wouldn't read");
            board.AddToBoard(n, node.Said);
        }
        if ((role, to) is ("lead", "Idea"))
            board.Mark(n, "idea", "true");

        board.Committed = true;
        board.SetStatus(existing?["number"]?.ToJsonString() ?? n, to);
        board.Log(role, n, $"added as {to}");
        Console.Out.Write($"#{n}: added as {to}\n");
        return 0;
    }
}
