namespace ATeam.Dashboard;

/// <summary>One link of the chain a task waits on: the team working where it is, the numbers of that team's cards
/// that stand for it, what it is, and how far it has got where no card says so.</summary>
public sealed record ChainLink(string Team, IReadOnlyList<int> Refs, string What, string Shown);

/// <summary>A task a card holds up, and the links still to go after the card's own.</summary>
public sealed record Hold(string Team, int Number, IReadOnlyList<ChainLink> Then);

/// <summary>Which cards hold up a task, on any team's tab, and what that task's own card says it waits on.</summary>
public static class Holds
{
    /// <summary><paramref name="items"/> with every card that a task waits on now carrying that task, and every task
    /// waiting on another repo saying which link holds it.</summary>
    public static IReadOnlyList<WaitingItem> Tag(IReadOnlyList<WaitingItem> items)
    {
        var held = items.SelectMany(task => (task.Chains ?? []).Select(chain => (Task: task, Chain: chain))).ToList();
        return
        [
            .. items.Select(item =>
            {
                IReadOnlyList<Hold> holding =
                [
                    .. held.Where(each => Stands(each.Chain[0], item))
                        .Select(each => new Hold(each.Task.Team, each.Task.Number, [.. each.Chain.Skip(1)])),
                    .. (item.Frees ?? []).Select(number => new Hold(item.Team, number, [])),
                ];
                var waitsOn = item.Chains is [[var now, ..], ..] ? $"waits on {now.What} · {Shown(now, items)}"
                    : item.WaitsUpstream ? $"waits on {item.Upstream}" : "";
                // Selection finds a card by reference, so an item whose tags haven't changed stays the same one.
                return waitsOn == item.WaitsOn && Same(holding, item.Holding) ? item : item with { Holding = holding, WaitsOn = waitsOn };
            }),
        ];
    }

    /// <summary>Every card a task waits on now, on any team's board, whether or not Work shows it.</summary>
    public static IReadOnlySet<(string Team, int Number)> Holders(IReadOnlyList<WaitingItem> items) =>
        items.SelectMany(item => (item.Chains ?? []).SelectMany(chain => chain[0].Team.Length > 0 ? chain[0].Refs.Select(number => (chain[0].Team, number)) : []))
            .Concat(items.Where(item => item.Holding.Count > 0 || item.Frees is { Count: > 0 }).Select(item => (item.Team, item.Number)))
            .ToHashSet();

    private static bool Same(IReadOnlyList<Hold> one, IReadOnlyList<Hold> other) =>
        one.Count == other.Count && one.Zip(other).All(pair =>
            pair.First.Team == pair.Second.Team && pair.First.Number == pair.Second.Number && pair.First.Then.SequenceEqual(pair.Second.Then));

    /// <summary>What a card holding up tasks says after its number: the first of them, and how many more.</summary>
    public static string Frees(WaitingItem item) =>
        item.Holding.Count == 0 ? ""
        : $"frees {Name(item, item.Holding[0])}{(item.Holding.Count > 1 ? $" +{item.Holding.Count - 1}" : "")}";

    /// <summary>The chain for the status bar: who waits on this card, then each link still to go after it.</summary>
    public static string Chain(WaitingItem item)
    {
        var tasks = item.Holding.Select(hold => Name(item, hold)).Distinct().ToList();
        var waiting = $"{string.Join(" and ", tasks)} {(tasks.Count == 1 ? "waits" : "wait")} on this";
        return string.Join(" · ", [waiting, .. item.Holding[0].Then.Select(link => $"then {link.What}")]);
    }

    /// <summary>A held task for the status bar: the link holding it now, then each link still to go after it.</summary>
    public static string Waiting(WaitingItem item) =>
        string.Join(" · ", [$"#{item.Number}", item.WaitsOn, .. (item.Chains is [var chain, ..] ? chain.Skip(1) : []).Select(link => $"then {link.What}")]);

    private static string Name(WaitingItem item, Hold hold) =>
        hold.Team == item.Team ? $"#{hold.Number}" : $"{hold.Team} #{hold.Number}";

    private static bool Stands(ChainLink link, WaitingItem item) =>
        link.Team.Length > 0 && link.Team == item.Team
        && (link.Refs.Contains(item.Number) || item.Pr > 0 && link.Refs.Contains(item.Pr));

    /// <summary>How far a link has got: the gate its card waits at and whose move it is, or what the board said
    /// where no card stands for it.</summary>
    private static string Shown(ChainLink link, IReadOnlyList<WaitingItem> items)
    {
        if (items.FirstOrDefault(item => Stands(link, item)) is not { } card)
            return link.Shown;
        var gate = WorkView.GateOf(card) switch
        {
            "Triage" => "to rank",
            "Pitches" => "pitched",
            "Blocked" => "blocked",
            "Review" => "in review",
            _ => "",
        };
        var whose = card.Mine ? "yours" : card.Turn switch
        {
            "dev" => "the Dev's",
            "lead" => "the Lead's",
            "reviewer" => "the Reviewer's",
            "customer" => "the Customer lead's",
            _ => card.Turn,
        };
        return string.Join(", ", new[] { gate, whose }.Where(part => part.Length > 0));
    }
}
