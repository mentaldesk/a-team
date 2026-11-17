using System.Text;
using System.Text.Json.Nodes;

namespace ATeam.Core;

/// <summary>What a card's record says before a reconcile.</summary>
internal sealed record Known(string Status, bool Shut);

/// <summary>What reading the repo's open issues and PRs means for the record: the SQL, those it brings onto the board and
/// where, and the nodes of those Done on it, to be read again as they are now.</summary>
internal sealed record ArrivalPlan(string Sql, IReadOnlyList<(string Number, string To)> Arrived, IReadOnlyList<string> Reopened);

/// <summary>What seeing some issues and PRs on GitHub means for the record. No I/O, so every case can be tested in process.</summary>
internal sealed record ReconcilePlan(string Sql, IReadOnlyList<(string Number, string? Type)> Reopened);

internal static class Reconciler
{
    internal static string Q(string? text) => text is null ? "NULL" : "'" + text.Replace("'", "''") + "'";

    internal static ReconcilePlan Plan(JsonArray nodes, IReadOnlyDictionary<string, Known> had, string team, string repo, string now)
    {
        var sql = new StringBuilder();
        var reopened = new List<(string, string?)>();
        var teamQ = Q(team);

        foreach (var c in nodes)
        {
            if (c is null || Json.Text(c["repository"]?["nameWithOwner"]) != repo) continue;
            var number = c["number"]!.ToJsonString();
            if (!had.TryGetValue(number, out var row)) continue;
            var cState = Json.Text(c["state"]);
            var cReason = Json.Text(c["stateReason"]);
            var shut = cState is not null ? cState is "CLOSED" or "MERGED" : (cReason ?? "REOPENED") != "REOPENED";
            sql.Append($"UPDATE board SET content = {Content(c, shut, cReason)}, node = {Q(Json.Text(c["id"]))} WHERE team = {teamQ} AND item = {number};\n");
            if (shut && row.Status != "Done")
                sql.Append(StatusRoute.Set(teamQ, number, "Done", now));
            if (!shut && row.Status == "Done")
                reopened.Add((number, Json.Text(c["__typename"])));
        }
        return new ReconcilePlan(sql.ToString(), reopened);
    }

    /// <summary>What an issue or PR's record keeps of it.</summary>
    private static string Content(JsonNode c, bool shut, string? cReason)
    {
        var labels = new JsonArray();
        foreach (var label in c["labels"]?["nodes"]?.AsArray() ?? [])
            labels.Add((JsonNode?)label?["name"]?.DeepClone());
        var content = new JsonObject
        {
            ["type"] = c["__typename"]?.DeepClone(),
            ["title"] = c["title"]?.DeepClone(),
            ["url"] = c["url"]?.DeepClone(),
            ["labels"] = labels,
            ["blockedBy"] = Json.Truthy(c["issueDependenciesSummary"]?["blockedBy"]) ? c["issueDependenciesSummary"]!["blockedBy"]!.DeepClone() : 0,
            ["parent"] = c["parent"]?["number"]?.DeepClone(),
        };
        if (shut) content["shut"] = true;
        if (shut && cReason is not null) content["closed"] = cReason;
        var login = Json.Text(c["author"]?["login"]);
        if (login == "dependabot") content["dependabot"] = true;
        if (Json.Text(c["__typename"]) == "Issue" && login is not null)
            content["author"] = Json.Text(c["author"]?["__typename"]) == "Bot" ? $"{login}[bot]" : login;
        return Q(Json.Compact(content));
    }

    /// <summary>Each open issue of <paramref name="repo"/> the record hasn't got arrives as an Idea, and each open PR that
    /// closes none of its cards at the lane its draft says; one it has with no status gets the same. One Done on the board
    /// is read again, whatever the record saw of it closing. Every other card stays as it is.</summary>
    internal static ArrivalPlan Arrivals(JsonArray issues, IReadOnlyDictionary<string, Known> had, string team, string repo, string now)
    {
        var sql = new StringBuilder();
        var arrived = new List<(string, string)>();
        var reopened = new List<string>();
        var teamQ = Q(team);
        var onBoard = had.Keys.Concat(issues.OfType<JsonNode>()
            .Where(c => Json.Text(c["__typename"]) == "Issue" && Json.Text(c["state"]) == "OPEN" && Json.Text(c["repository"]?["nameWithOwner"]) == repo)
            .Select(c => c["number"]!.ToJsonString())).ToHashSet();
        foreach (var c in issues)
        {
            if (c is null || Json.Text(c["state"]) != "OPEN") continue;
            if (Json.Text(c["repository"]?["nameWithOwner"]) != repo) continue;
            var number = c["number"]!.ToJsonString();
            var node = Json.Text(c["id"]);
            if (ArrivesAt(c, onBoard, repo) is not { } to) continue;
            if (!had.TryGetValue(number, out var row))
            {
                sql.Append(StatusRoute.Found(teamQ, number, Q(node), to, now, Content(c, false, null)));
                arrived.Add((number, to));
            }
            else if (row.Status == StatusRoute.None && !row.Shut)
            {
                sql.Append(StatusRoute.Set(teamQ, number, to, now));
                arrived.Add((number, to));
            }
            else if ((row.Shut || row.Status == "Done") && node is not null)
            {
                reopened.Add(node);
            }
        }
        return new ArrivalPlan(sql.ToString(), arrived, reopened);
    }

    /// <summary>Where an open issue or PR comes onto the board: an issue as an Idea, a PR in the lane its draft says. Null for
    /// a PR that closes a card on the board, or arriving with it, which shows on that card instead.</summary>
    internal static string? ArrivesAt(JsonNode c, IReadOnlySet<string> onBoard, string repo) => Json.Text(c["__typename"]) switch
    {
        "Issue" => "Idea",
        "PullRequest" when !Closes(c, repo).Any(onBoard.Contains) => Json.Truthy(c["isDraft"]) || Json.Text(c["author"]?["login"]) == "dependabot" ? "In progress" : "In review",
        _ => null,
    };

    /// <summary>The issues of <paramref name="repo"/> a PR closes when it merges.</summary>
    internal static IEnumerable<string> Closes(JsonNode pr, string repo) =>
        (pr["closingIssuesReferences"]?["nodes"]?.AsArray() ?? []).OfType<JsonNode>()
            .Where(issue => string.Equals(Json.Text(issue["repository"]?["nameWithOwner"]), repo, StringComparison.OrdinalIgnoreCase))
            .Select(issue => issue["number"]!.ToJsonString());

    /// <summary>Where an event's words say a card landed; null where they don't say.</summary>
    internal static string? Landed(string what)
    {
        if (what.StartsWith("filed as Ready", StringComparison.Ordinal)) return "Ready";
        if (what.StartsWith("added as ", StringComparison.Ordinal)) return what[9..];
        if (what.StartsWith("moved to ", StringComparison.Ordinal)) return null;
        if (!what.Contains(" → ", StringComparison.Ordinal)) return null;
        var parts = what.Split(" · ")[0].Split(" → ");
        return parts.Length > 1 ? parts[1] : null;
    }
}
