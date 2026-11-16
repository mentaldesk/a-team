namespace ATeam.Dashboard;

/// <summary>A card that's your move and holds up other work, with every task it frees, nearest first.</summary>
public sealed record InboxRow(WaitingItem Item, string Move, IReadOnlyList<(string Team, int Number)> Frees)
{
    /// <summary>The card as the row names it: a PR by its own number.</summary>
    public string Card => Item.Dependency || Item.Pr == Item.Number ? $"PR #{Item.Number}" : $"#{Item.Number}";

    /// <summary>What it frees, each team named once before its own numbers: <c>frees a-team #672 · #673</c>.</summary>
    public string Freed => "frees " + string.Join(" · ", Frees.GroupBy(freed => freed.Team)
        .Select(team => $"{team.Key} {string.Join(" · ", team.Select(freed => $"#{freed.Number}"))}"));

    /// <summary>The whole chain for the status bar, from the card to the last task it frees.</summary>
    public string Chain => string.Join(" → ", new[] { $"{Item.Team} {Card}" }.Concat(Frees.Select(freed => $"{freed.Team} #{freed.Number}")));
}

/// <summary>Which of your moves hold up other work, on any team, ranked by how much each frees.</summary>
public static class Inbox
{
    /// <summary>Of cards <see cref="Holds.Tag"/> has tagged, those a column shows as your move and something waits on,
    /// the one freeing the most tasks, directly or further down a chain, first.</summary>
    public static IReadOnlyList<InboxRow> Rows(IReadOnlyList<WaitingItem> items) =>
    [
        .. items.Where(item => item.Mine && item.WaitsOnYou && item.Holding.Count > 0 && WorkView.Gates.Any(gate => gate.Holds(item)))
            .Select(item => new InboxRow(item, Move(item), Freed(item, items)))
            .OrderByDescending(row => row.Frees.Count)
            .ThenByDescending(row => row.Item.Holding.Count)
            .ThenByDescending(row => Priorities.Of(row.Item))
            .ThenBy(row => row.Item.Team, StringComparer.Ordinal)
            .ThenBy(row => row.Item.Number),
    ];

    /// <summary>A line per row, the team, the card and the move each lined up under the row above's.</summary>
    public static IReadOnlyList<string> Lines(IReadOnlyList<InboxRow> rows)
    {
        var team = rows.Select(row => row.Item.Team.Length).DefaultIfEmpty().Max();
        var card = rows.Select(row => row.Card.Length).DefaultIfEmpty().Max();
        var move = rows.Select(row => row.Move.Length).DefaultIfEmpty().Max();
        return [.. rows.Select(row => $"{row.Item.Team.PadRight(team)}  {row.Card.PadRight(card)}  {row.Move.PadRight(move)}  {row.Freed}")];
    }

    /// <summary>What you do to free it, as the row says it.</summary>
    internal static string Move(WaitingItem item) =>
        item.Secret is not null ? "give secret"
        : item.Held || item.Question.Length > 0 ? "answer"
        : item.Status == "In review" ? "review"
        : WorkView.ToRank(item) ? "rank"
        : item.Status == "Pitched" ? "approve"
        : "decide";

    /// <summary>Every task waiting on the card, then each one waiting on those, once each, nearest first.</summary>
    private static IReadOnlyList<(string Team, int Number)> Freed(WaitingItem card, IReadOnlyList<WaitingItem> items)
    {
        var freed = new List<(string Team, int Number)>();
        var seen = new HashSet<(string, int)> { (card.Team, card.Number) };
        var next = new Queue<WaitingItem>([card]);
        while (next.TryDequeue(out var holder))
            foreach (var hold in holder.Holding)
            {
                if (!seen.Add((hold.Team, hold.Number)))
                    continue;
                freed.Add((hold.Team, hold.Number));
                if (items.FirstOrDefault(item => item.Team == hold.Team && item.Number == hold.Number) is { } task)
                    next.Enqueue(task);
            }
        return freed;
    }
}
