using System.Text.Json;

namespace ATeam.Dashboard;

/// <summary>The ways something becomes your move. A move into Triage isn't one: nothing waits on it.</summary>
public enum Arrival
{
    Pitch,
    Review,
    Question,
}

/// <summary>One line of a team's record, as <c>a-team board &lt;team&gt; moves</c> reports it.</summary>
public sealed record Move(int Item, DateTimeOffset At, string Who, string What)
{
    /// <summary>The gate the move brings the item to you at, or null where it brings nothing.</summary>
    internal Arrival? Arrives => Who == "you" ? null
        : What == "handed back with a question" ? Arrival.Question
        : Into("Pitched") ? Arrival.Pitch
        : Into("In review") ? Arrival.Review
        : null;

    /// <summary>Whether it takes the item out of whatever gate it was at.</summary>
    internal bool Leaves =>
        What.Contains(" → ", StringComparison.Ordinal) || What.StartsWith("added as ", StringComparison.Ordinal)
        || What.StartsWith("accepted · ", StringComparison.Ordinal) || What.StartsWith("closed", StringComparison.Ordinal)
        || What is "declined" or "question answered";

    private bool Into(string status) =>
        What.EndsWith($" → {status}", StringComparison.Ordinal) || What == $"added as {status}";

    public static IReadOnlyList<Move> Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return [];
            return [.. document.RootElement.EnumerateArray().Select(move => new Move(move.GetProperty("item").GetInt32(),
                move.GetProperty("at").GetDateTimeOffset(), move.GetProperty("who").GetString() ?? "",
                move.GetProperty("what").GetString() ?? ""))];
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return [];
        }
    }
}

/// <summary>An item that has become your move since the app started.</summary>
public sealed record NewItem(string Team, int Number, Arrival Kind, DateTimeOffset At)
{
    /// <summary>Whether it waits in Work's <paramref name="gate"/>: a pitch with an open question waits in Blocked.</summary>
    internal bool WaitsIn(string gate) => Kind switch
    {
        Arrival.Pitch => gate is "Pitches" or "Blocked",
        Arrival.Question => gate == "Blocked",
        _ => gate == "Review",
    };

    internal Icon Icon => Kind switch
    {
        Arrival.Pitch => Icon.Pitch,
        Arrival.Question => Icon.Question,
        _ => Icon.Task,
    };
}

/// <summary>What has become your move on any team since <paramref name="since"/>, read from the record, until you
/// open it or it leaves its gate.</summary>
public sealed class WhatsNew(DateTimeOffset since)
{
    private readonly Dictionary<string, IReadOnlyList<NewItem>> _arrived = [];
    private readonly Dictionary<(string Team, int Number), DateTimeOffset> _cleared = [];

    /// <summary>When the app started, to the second the record keeps.</summary>
    public DateTimeOffset Since { get; } = since.AddTicks(-(since.Ticks % TimeSpan.TicksPerSecond));

    /// <summary>Newest first, across every team.</summary>
    public IReadOnlyList<NewItem> Items =>
        [.. _arrived.Values.SelectMany(items => items).Where(Uncleared).OrderByDescending(item => item.At)];

    public NewItem? Newest => Items.FirstOrDefault();

    /// <summary>Takes <paramref name="team"/>'s moves since start, and says whether they brought something new.</summary>
    public bool Read(string team, IReadOnlyList<Move> moves)
    {
        var before = _arrived.GetValueOrDefault(team, []).Where(Uncleared).ToList();
        _arrived[team] = [.. moves.Where(move => move.At >= Since).GroupBy(move => move.Item)
            .Select(item => item.Aggregate((NewItem?)null, (now, move) =>
                move.Arrives is { } kind ? new NewItem(team, move.Item, kind, move.At) : move.Leaves ? null : now))
            .OfType<NewItem>()];
        return _arrived[team].Where(Uncleared).Except(before).Any();
    }

    /// <summary>Work's read of <paramref name="team"/>, asked for at <paramref name="asked"/>: an item that arrived
    /// before it and isn't in its gate there has left it.</summary>
    public void Read(string team, IEnumerable<WaitingItem> waiting, DateTimeOffset asked)
    {
        var at = waiting.Where(item => item.Team == team).ToList();
        foreach (var item in _arrived.GetValueOrDefault(team, []).Where(item => item.At < asked && Uncleared(item)))
            if (!at.Any(each => each.Number == item.Number && item.WaitsIn(WorkView.GateOf(each))))
                _cleared[(team, item.Number)] = asked;
    }

    /// <summary>You've opened it: it's no longer new, until it arrives at a gate again.</summary>
    public void Seen(string team, int number, DateTimeOffset at) => _cleared[(team, number)] = at;

    /// <summary>The status bar's alert: the newest's icon and number, and how many others there are.</summary>
    public string Alert(IconStyle style)
    {
        var items = Items;
        if (items.Count == 0)
            return "";
        var others = items.Count - 1;
        return $"{Icons.Glyph(items[0].Icon, style)} #{items[0].Number}{(others == 0 ? "" : $" +{others}")}";
    }

    private bool Uncleared(NewItem item) =>
        !_cleared.TryGetValue((item.Team, item.Number), out var cleared) || cleared < item.At;
}
