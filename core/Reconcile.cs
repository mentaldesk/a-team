using System.Text;
using System.Text.Json.Nodes;

namespace ATeam.Core;

/// <summary>What a card's record says before a reconcile.</summary>
internal sealed record Known(string Status, bool Shut);

/// <summary>What reading the repo's open issues means for the record: the SQL, the issues it brings onto the board, and
/// the nodes of those closed on it, to be read again as they are now.</summary>
internal sealed record ArrivalPlan(string Sql, IReadOnlyList<string> Arrived, IReadOnlyList<string> Reopened);

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
            if (!shut && row is { Shut: true, Status: "Done" })
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

    /// <summary>Each open issue of <paramref name="repo"/> the record hasn't got arrives as an Idea; one
    /// it has with no status becomes an Idea; an issue or PR it has closed is read again. Every other card stays as it is.</summary>
    internal static ArrivalPlan Arrivals(JsonArray issues, IReadOnlyDictionary<string, Known> had, string team, string repo, string now)
    {
        var sql = new StringBuilder();
        var arrived = new List<string>();
        var reopened = new List<string>();
        var teamQ = Q(team);
        foreach (var c in issues)
        {
            if (c is null || Json.Text(c["state"]) != "OPEN") continue;
            if (Json.Text(c["repository"]?["nameWithOwner"]) != repo) continue;
            var number = c["number"]!.ToJsonString();
            var node = Json.Text(c["id"]);
            if (Json.Text(c["__typename"]) == "PullRequest")
            {
                if (had.TryGetValue(number, out var pull) && pull.Shut && node is not null) reopened.Add(node);
                continue;
            }
            if (Json.Text(c["__typename"]) != "Issue") continue;
            if (!had.TryGetValue(number, out var row))
            {
                sql.Append(StatusRoute.Found(teamQ, number, Q(node), now, Content(c, false, null)));
                arrived.Add(number);
            }
            else if (row.Status == StatusRoute.None && !row.Shut)
            {
                sql.Append(StatusRoute.Set(teamQ, number, "Idea", now));
                arrived.Add(number);
            }
            else if (row.Shut && node is not null)
            {
                reopened.Add(node);
            }
        }
        return new ArrivalPlan(sql.ToString(), arrived, reopened);
    }

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
