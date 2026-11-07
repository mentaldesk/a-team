using System.Text.Json.Nodes;

namespace ATeam.Core;

/// <summary>What a card on the board, as `list` prints it, says about itself.</summary>
internal static class Cards
{
    internal static readonly string[] Ranks = ["Urgent", "High", "Medium", "Low"];

    internal static string N(JsonNode card) => card["number"]!.ToJsonString();

    internal static string Status(JsonNode card) => Json.Text(card["status"]) ?? "";

    internal static bool IsIssue(JsonNode card) => Json.Text(card["type"]) == "Issue";

    internal static bool IsPr(JsonNode card) => Json.Text(card["type"]) == "PullRequest";

    internal static bool Pitch(JsonNode card) =>
        card["labels"]?.AsArray().Any(label => Json.Text(label) == "pitch") == true;

    internal static bool Marked(JsonNode card, string mark) => Json.Truthy(card["marks"]?[mark]);

    internal static bool Dependabot(JsonNode card) => Json.Truthy(card["dependabot"]);

    internal static bool Blocked(JsonNode card) =>
        Marked(card, "held") || Marked(card, "upstream") || Json.Long(card["blockedBy"]) > 0;

    internal static bool ReadyTask(JsonNode card) => Status(card) == "Ready" && IsIssue(card) && !Pitch(card);

    internal static bool Startable(JsonNode card) => ReadyTask(card) && !Blocked(card);

    /// <summary>A Ready task on hold, or a Dependabot PR the Dev can't make build, which waits the same way.</summary>
    internal static bool Held(JsonNode card) =>
        (ReadyTask(card) || (Status(card) == "Ready" && Dependabot(card))) && Marked(card, "held");

    internal static bool Fix(JsonNode card) => Marked(card, "fix");

    internal static bool Fileable(JsonNode card) => Pitch(card) && Status(card) is "Approved" or "Building";

    /// <summary>A task of the Dev's that takes up one of wip.worktrees.</summary>
    internal static bool Working(JsonNode card) =>
        Marked(card, "dev") && Status(card) is "In progress" or "In review" && !Blocked(card);

    internal static bool Own(JsonNode card, string role) => role switch
    {
        "lead" => Pitch(card),
        "dev" => card["marks"]?["dev"]?.GetValueKind() == System.Text.Json.JsonValueKind.True,
        "customer" => card["marks"]?["customer"]?.GetValueKind() == System.Text.Json.JsonValueKind.True,
        _ => false,
    };

    private static int Rank(string? rank)
    {
        var at = Array.IndexOf(Ranks, rank);
        return at < 0 ? Ranks.Length : at;
    }

    /// <summary>Highest Priority first, unset last, and within each by the Lead's recommendation.</summary>
    internal static List<JsonNode> ByPriority(IEnumerable<JsonNode> cards) =>
        cards.OrderBy(card => Rank(Json.Text(card["priority"]))).ThenBy(card => Rank(Json.Text(card["marks"]?["rank"]))).ToList();

    /// <summary>Which drafts go up to Pitched, and which pitches a higher-priority draft displaces, under wip.pitched.</summary>
    internal static (List<JsonNode> Promote, List<JsonNode> Demote) PitchSwap(IEnumerable<JsonNode> cards, int limit)
    {
        var drafts = ByPriority(cards.Where(card => Pitch(card) && IsIssue(card) && Status(card) is "Pitched" or "Exploring"));
        var priorities = drafts.Select(card => Json.Text(card["priority"])).ToList();
        var sorted = drafts.OrderBy(card => priorities.IndexOf(Json.Text(card["priority"])))
            .ThenBy(card => Status(card) != "Pitched").ToList();
        var pitched = sorted.Count(card => Status(card) == "Pitched");
        if (pitched < limit)
            return (sorted.Where(card => Status(card) == "Exploring").Take(limit - pitched).ToList(), []);
        var promote = sorted.Take(limit).Where(card => Status(card) == "Exploring").ToList();
        var displaced = sorted.Skip(limit).Where(card => Status(card) == "Pitched").ToList();
        return (promote, displaced.Skip(displaced.Count - promote.Count).ToList());
    }
}
