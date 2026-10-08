using System.Text.Json;

namespace ATeam.Dashboard;

/// <summary>One item at a gate, as <c>a-team board &lt;team&gt; waiting</c> reports it.</summary>
public sealed record WaitingItem(
    int Number, string Title, string Status, string Url, string Team, string Turn = "", string Reason = "",
    int Pr = 0, string PrUrl = "", string Trouble = "", string Priority = "", bool Pitch = false,
    string Question = "", string Unready = "", string Base = "", int Tasks = 0, int OpenTasks = 0, string Role = "")
{
    /// <summary>Whether this is the Customer lead's docs PR, which is its own PR to merge.</summary>
    public bool Docs => Role == "customer";

    /// <summary>Whether this is a pitch the reviewer can approve now.</summary>
    public bool Approvable => Pitch && Status == "Pitched";

    /// <summary>Whether this is a task whose PR the reviewer could merge, or a validated pitch they could close,
    /// trouble or not.</summary>
    public bool Acceptable => Status == "In review";

    /// <summary>Whether there's something to try: a task's PR, or the default branch for a validated pitch.</summary>
    public bool Triable => Pr > 0 || Pitch && Acceptable;

    /// <summary>Why it can't be accepted yet, for the message bar, or nothing when it can.</summary>
    public string Unacceptable =>
        Holdup.Length == 0 ? "" : Pitch ? $"#{Number} {Holdup}" : $"#{Number} · {Holdup}";

    /// <summary>What stands in the way of accepting it, without its number, or nothing when it can be.</summary>
    public string Holdup =>
        Pitch ? OpenTasks == 0 ? "" : $"has {OpenTasks} open task{(OpenTasks == 1 ? "" : "s")}"
        : Pr == 0 ? "no PR to merge"
        : Unready;

    /// <summary>Whether the next move is the reviewer's. An item the board said nothing about is theirs.</summary>
    public bool Mine => Turn.Length == 0 || Turn == "you";

    /// <summary>The whole of it for the message bar, with the role in front when the move isn't the reviewer's,
    /// and the PR it hands over to at the end.</summary>
    public string Line => string.Join(" · ",
        new[] { $"#{Number}", Mine ? "" : Turn, Reason, Pr == 0 ? "" : $"PR #{Pr}" }.Where(part => part.Length > 0));

    /// <summary>What that command printed. Anything that isn't an item with a number is left out.</summary>
    public static IReadOnlyList<WaitingItem> Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return [];
            return
            [
                .. document.RootElement.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.Object && Numbered(item) is not null)
                    .Select(item => new WaitingItem(
                        Numbered(item)!.Value, Text(item, "title"), Text(item, "status"), Text(item, "url"),
                        Text(item, "team"), Text(item, "turn"), Text(item, "reason"),
                        Numbered(item, "pr") ?? 0, Text(item, "prUrl"), Text(item, "trouble"),
                        Text(item, "priority"), Flag(item, "pitch"), Text(item, "question"),
                        Text(item, "unready"), Text(item, "base"),
                        Numbered(item, "tasks") ?? 0, Numbered(item, "openTasks") ?? 0, Text(item, "role")))
            ];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static int? Numbered(JsonElement item) => Numbered(item, "number");

    private static int? Numbered(JsonElement item, string name) =>
        item.TryGetProperty(name, out var number) && number.TryGetInt32(out var value) ? value : null;

    private static bool Flag(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
}
