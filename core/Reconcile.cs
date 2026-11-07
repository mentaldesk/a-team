using System.Text;
using System.Text.Json.Nodes;

namespace ATeam.Core;

/// <summary>What a card's record says before a reconcile.</summary>
internal sealed record Known(string Status, string? Priority, string Since, string? MirroredStatus, string? MirroredPriority, bool Shut);

/// <summary>What seeing some nodes on GitHub means for the record. No I/O, so every case can be tested in process.</summary>
internal sealed record ReconcilePlan(
    string Sql,
    IReadOnlyList<(string Number, string To, string From)> Bounces,
    IReadOnlyList<(string Number, string? Type)> Reopened,
    IReadOnlyList<(string Number, string To)> Ranks);

internal static class Reconciler
{
    internal static string Q(string? text) => text is null ? "NULL" : "'" + text.Replace("'", "''") + "'";

    /// <summary>What a status on the mirror means here: a statusMap's own name, one of ours, or "?" and whatever it says.</summary>
    internal static string State(string? shown, IReadOnlyDictionary<string, string> reverseMap)
    {
        if (shown is null) return StatusRoute.None;
        if (reverseMap.TryGetValue(shown, out var mapped)) return mapped;
        return StatusRoute.Statuses.Contains(shown) ? shown : "?" + shown;
    }

    internal static ReconcilePlan Plan(
        JsonArray nodes, IReadOnlyDictionary<string, Known> had, string team, string repo, string priorityField, string mirror,
        IReadOnlyDictionary<string, string> reverseMap, string now, bool adopt)
    {
        var inserts = new StringBuilder();
        var updates = new StringBuilder();
        var closings = new StringBuilder();
        var bounces = new List<(string, string, string)>();
        var reopened = new List<(string, string?)>();
        var ranks = new List<(string, string)>();
        var teamQ = Q(team);

        foreach (var node in nodes)
        {
            var c = node?["content"];
            if (Json.Text(c?["repository"]?["nameWithOwner"]) != repo) continue;
            var number = c!["number"]!.ToJsonString();
            had.TryGetValue(number, out var row);
            var cState = Json.Text(c["state"]);
            var cReason = Json.Text(c["stateReason"]);
            var shut = cState is not null ? cState is "CLOSED" or "MERGED" : (cReason ?? "REOPENED") != "REOPENED";
            var closes = shut && row is not null && row.Status != "Done";
            var reopens = !shut && row is { Shut: true, Status: "Done" };
            var project = node!["project"];
            var shown = project is not null ? State(Json.Text(project["status"]), reverseMap) : null;
            var item = Json.Text(project?["id"]);
            var changed = Json.Text(project?["since"]);
            var since = changed ?? now;
            string? ranked = null;
            foreach (var value in c["issueFieldValues"]?["nodes"]?.AsArray() ?? [])
            {
                if (Json.Text(value?["field"]?["name"]) != priorityField) continue;
                ranked = Json.Text(value?["name"]);
                break;
            }
            var type = Json.Text(c["__typename"]);
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
            if (Json.Text(c["author"]?["login"]) == "dependabot") content["dependabot"] = true;
            var contentQ = Q(Json.Compact(content));

            if (row is null && shown is not null)
            {
                inserts.Append($"INSERT INTO board (team, item, node, status, priority, since, content, mirror, mirrored_status,\n"
                    + $" mirrored_priority) VALUES ({teamQ}, {number}, {Q(Json.Text(c["id"]))}, {Q(shown)},\n"
                    + $" {Q(ranked)}, {Q(since)}, {contentQ}, {Q(item)}, {Q(shown)}, {Q(ranked ?? "none")});\n");
            }
            if (row is not null)
            {
                var r = ranked ?? "none";
                updates.Append($"UPDATE board SET content = {contentQ}, node = {Q(Json.Text(c["id"]))}");
                if (item is not null)
                    updates.Append($", mirror = {Q(item)}");
                else if (mirror.Length > 0)
                    updates.Append(", mirror = NULL, mirrored_status = NULL, mirrored_rank = NULL, mirrored_theme = NULL");
                if (shown is not null)
                    updates.Append($", mirrored_status = {Q(shown)}");
                if (type == "Issue" && (adopt || row.MirroredPriority is null || r == (row.Priority ?? "none")))
                    updates.Append($", mirrored_priority = {Q(r)}");
                if (adopt && shown is not null)
                    updates.Append($", status = {Q(shown)}");
                if (adopt)
                    updates.Append($", priority = {Q(ranked)}");
                if (!adopt && row.Status == StatusRoute.None && shown is not null && shown != StatusRoute.None)
                    updates.Append($", status = {Q(shown)}, since = {Q(now)}");
                updates.Append($"\n WHERE team = {teamQ} AND item = {number};\n");

                if (!adopt)
                {
                    if (!(closes || reopens || row.Status == StatusRoute.None) && shown is not null && row.MirroredStatus is not null
                        && shown != row.MirroredStatus && shown != row.Status && string.CompareOrdinal(changed, row.Since) >= 0 && changed is not null)
                        bounces.Add((number, row.Status, shown.TrimStart('?')));
                    if (reopens)
                        reopened.Add((number, type));
                    if (type == "Issue" && ranked is not null && row.MirroredPriority is not null
                        && ranked != row.MirroredPriority && ranked != row.Priority)
                        ranks.Add((number, ranked));
                }
            }
            if (closes)
                closings.Append($"UPDATE board SET status = 'Done', since = {Q(now)} WHERE team = {teamQ} AND item = {number};\n");
        }
        return new ReconcilePlan(inserts.ToString() + updates + closings, bounces, reopened, ranks);
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
