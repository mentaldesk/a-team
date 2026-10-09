using System.Globalization;
using System.Text.Json;

namespace ATeam.Dashboard;

/// <summary>Whose line a card is on: the mark its chip wears says which.</summary>
public enum CardKind
{
    Pitch,
    Task,
    Docs,
    Yours,
}

/// <summary>One card on a team's board, as <c>a-team board &lt;team&gt; overview</c> reports it, and when it entered
/// the column it's in.</summary>
public sealed record BoardCard(
    string Team, int Number, string Title, string Url, string Status, string Priority, CardKind Kind,
    DateTimeOffset? Since, int? Parent = null)
{
    public string Mark => Kind switch
    {
        CardKind.Pitch => "◆",
        CardKind.Task => "●",
        CardKind.Docs => "✎",
        _ => "○",
    };

    public string KindName => Kind switch
    {
        CardKind.Pitch => "Pitch",
        CardKind.Task => "Task",
        CardKind.Docs => "Docs PR",
        _ => "Yours",
    };

    /// <summary>How long it has sat in its column; nothing where the board didn't say when it got there.</summary>
    public TimeSpan? Age(DateTimeOffset now) => Since is { } since ? now - since : null;

    /// <summary>What that command printed. Anything that isn't an item with a number is left out.</summary>
    public static IReadOnlyList<BoardCard> Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return [];
            return
            [
                .. document.RootElement.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.Object && Numbered(item, "number") is not null)
                    .Select(item => new BoardCard(
                        Text(item, "team"), Numbered(item, "number")!.Value, Text(item, "title"), Text(item, "url"),
                        Text(item, "status"), Text(item, "priority"), KindOf(Text(item, "kind")), Time(item, "since"),
                        Numbered(item, "parent")))
            ];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static CardKind KindOf(string kind) => kind switch
    {
        "pitch" => CardKind.Pitch,
        "task" => CardKind.Task,
        "docs" => CardKind.Docs,
        _ => CardKind.Yours,
    };

    private static int? Numbered(JsonElement item, string name) =>
        item.TryGetProperty(name, out var number) && number.ValueKind == JsonValueKind.Number && number.TryGetInt32(out var value)
            ? value
            : null;

    private static DateTimeOffset? Time(JsonElement item, string name) =>
        DateTimeOffset.TryParse(Text(item, name), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)
            ? at
            : null;

    private static string Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
}

/// <summary>How long a card has waited: in its largest unit on a chip, in full in the details.</summary>
public static class Ages
{
    public static string Short(TimeSpan age) =>
        age.TotalHours < 1 ? $"{Math.Max(0, (int)age.TotalMinutes)}m"
        : age.TotalDays < 1 ? $"{(int)age.TotalHours}h"
        : $"{(int)age.TotalDays}d";

    public static string Full(TimeSpan age)
    {
        if (age < TimeSpan.FromMinutes(1))
            return "0m";
        string[] parts =
        [
            age.Days > 0 ? $"{age.Days}d" : "",
            age.Days > 0 || age.Hours > 0 ? $"{age.Hours}h" : "",
            $"{age.Minutes}m",
        ];
        return string.Join(" ", parts.Where(part => part.Length > 0));
    }
}

/// <summary>How long a card may sit in a column before Overseer highlights it, one optional entry per column.</summary>
public static class ColumnLimits
{
    /// <summary>A limit as Settings takes it: a whole number and a unit, <c>30m</c>, <c>2h</c>, <c>3d</c> or
    /// <c>2w</c>. Blank is no limit; anything else is refused.</summary>
    public static bool TryParse(string text, out TimeSpan? limit)
    {
        limit = null;
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
            return true;
        if (trimmed.Length < 2 || !int.TryParse(trimmed[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count == 0)
            return false;
        limit = char.ToLowerInvariant(trimmed[^1]) switch
        {
            'm' => TimeSpan.FromMinutes(count),
            'h' => TimeSpan.FromHours(count),
            'd' => TimeSpan.FromDays(count),
            'w' => TimeSpan.FromDays(7 * count),
            _ => null,
        };
        return limit is not null;
    }

    /// <summary>Whether <paramref name="card"/> has sat in its column past that column's limit. A column with no
    /// limit never is.</summary>
    public static bool IsOver(BoardCard card, IReadOnlyDictionary<string, string> limits, DateTimeOffset now) =>
        Limit(limits, card.Status) is { } limit && card.Age(now) is { } age && age > limit;

    public static TimeSpan? Limit(IReadOnlyDictionary<string, string> limits, string column) =>
        limits.TryGetValue(column, out var text) && TryParse(text, out var limit) ? limit : null;
}
