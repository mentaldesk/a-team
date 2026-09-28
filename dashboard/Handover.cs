namespace ATeam.Dashboard;

/// <summary>Something the dashboard hands the terminal to, and what to show again once it takes it back.</summary>
public abstract record Handover
{
    public string? Failure { get; init; }

    public abstract string[] Arguments { get; }

    public abstract Area Area { get; }
}

/// <summary>What the Work area was showing when it handed the terminal to <c>a-team try</c>, so the window that
/// takes it back opens on the same row, and says so if the try failed.</summary>
public sealed record TryHandover(WaitingItem Item, bool OnPr, IReadOnlyList<WaitingItem> Items, DateTimeOffset? ReadAt)
    : Handover
{
    public override string[] Arguments => ["try", Item.Team, Item.Pr.ToString()];

    public override Area Area => Area.Work;
}

/// <summary>The agent handed to <c>a-team attach</c>, selected again on the grid once you quit.</summary>
public sealed record AttachHandover(string Team, string Role) : Handover
{
    public override string[] Arguments => ["attach", Team, Role];

    public override Area Area => Area.Dashboard;
}
