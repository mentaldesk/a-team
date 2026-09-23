using System.Text.Json;

namespace ATeam.Dashboard;

/// <summary>One item at a gate, as <c>a-team board &lt;team&gt; waiting</c> reports it.</summary>
public sealed record WaitingItem(
    int Number, string Title, string Status, string Url, string Team, string Turn = "", string Reason = "")
{
    /// <summary>Whether the next move is the reviewer's. An item the board said nothing about is theirs.</summary>
    public bool Mine => Turn.Length == 0 || Turn == "you";

    /// <summary>The whole of it for the message bar, with the role in front when the move isn't the reviewer's.</summary>
    public string Line => string.Join(" · ", new[] { $"#{Number}", Mine ? "" : Turn, Reason }.Where(part => part.Length > 0));

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
                        Text(item, "team"), Text(item, "turn"), Text(item, "reason")))
            ];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static int? Numbered(JsonElement item) =>
        item.TryGetProperty("number", out var number) && number.TryGetInt32(out var value) ? value : null;

    private static string Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
}
