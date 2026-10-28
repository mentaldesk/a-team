using System.Text.Json;

namespace ATeam.Dashboard;

/// <summary>A team's red default branch, as <c>a-team board &lt;team&gt; waiting</c> reports it alongside the items,
/// and the fix task for it where the Lead has filed one.</summary>
public sealed record RedMain(string Team, string Branch, string Since, int Fix = 0, string FixStatus = "")
{
    /// <summary>The line Work shows above the team's columns.</summary>
    public string Line => string.Join(" · ", new[]
    {
        $"⚠ {Branch} is red since {Since[..Math.Min(7, Since.Length)]}",
        Fix == 0 ? "" : FixStatus == "In review" ? $"fix #{Fix} is waiting for you" : $"fix #{Fix} is with the Dev",
    }.Where(part => part.Length > 0));

    /// <summary>The red branches in what that command printed, one a team at most.</summary>
    public static IReadOnlyList<RedMain> Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return [];
            return
            [
                .. document.RootElement.EnumerateArray()
                    .Where(entry => entry.ValueKind == JsonValueKind.Object
                        && entry.TryGetProperty("red", out var red) && red.ValueKind == JsonValueKind.Object)
                    .Select(entry => (Team: Text(entry, "team"), Red: entry.GetProperty("red")))
                    .Select(entry => new RedMain(entry.Team, Text(entry.Red, "branch"), Text(entry.Red, "since"),
                        entry.Red.TryGetProperty("fix", out var fix) && fix.ValueKind == JsonValueKind.Object
                            && fix.TryGetProperty("number", out var number) && number.TryGetInt32(out var n) ? n : 0,
                        entry.Red.TryGetProperty("fix", out var task) && task.ValueKind == JsonValueKind.Object ? Text(task, "status") : ""))
                    .Where(red => red.Team.Length > 0 && red.Branch.Length > 0)
                    .DistinctBy(red => red.Team)
            ];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
}
