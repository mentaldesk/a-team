namespace ATeam.Dashboard;

/// <summary>Where a decision puts its card from the moment you confirm it, while the board write behind it runs or
/// waits: a refused one is dropped from the queue, and the card goes back to where the board has it.</summary>
public static class Deciding
{
    public static bool Moves(Write write) => write.Item is not null
        && write.Kind is WriteKind.Approve or WriteKind.Accept or WriteKind.Decline or WriteKind.Shelve or WriteKind.Rank;

    /// <summary>The decisions running or queued, oldest first.</summary>
    public static IReadOnlyList<Write> In(WriteQueue writes) =>
        [.. (writes.Running is { } running ? [running] : Array.Empty<Write>()).Concat(writes.Queued).Where(Moves)];

    public static bool Decides(Write write, string team, int number) =>
        write.Item is { } item && item.Team == team && item.Number == number;

    /// <summary>A rank moves a card to where its new Priority puts it; every other decision takes it off Work.</summary>
    public static WaitingItem? OnWork(Write write, WaitingItem item) =>
        write.Kind == WriteKind.Rank ? item.RankedAs(write.Rank) : null;

    /// <summary>Overseer has no Done column, so an accepted or declined card leaves it.</summary>
    public static BoardCard? OnOverseer(Write write, BoardCard card) => write.Kind switch
    {
        WriteKind.Rank => card.RankedAs(write.Rank),
        WriteKind.Approve => card with { Status = "Approved" },
        WriteKind.Shelve => card with { Status = "Idea" },
        _ => null,
    };

    public static IEnumerable<WaitingItem> Apply(IReadOnlyList<Write> deciding, IEnumerable<WaitingItem> items) =>
        items.Select(item => deciding.Where(write => Decides(write, item.Team, item.Number))
            .Aggregate((WaitingItem?)item, (now, write) => now is null ? null : OnWork(write, now))).OfType<WaitingItem>();

    public static IEnumerable<BoardCard> Apply(IReadOnlyList<Write> deciding, IEnumerable<BoardCard> cards) =>
        cards.Select(card => deciding.Where(write => Decides(write, card.Team, card.Number))
            .Aggregate((BoardCard?)card, (now, write) => now is null ? null : OnOverseer(write, now))).OfType<BoardCard>();
}
