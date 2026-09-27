namespace ATeam.Dashboard;

/// <summary>What the Work area was showing when it handed the terminal to <c>a-team try</c>, so the window that
/// takes it back opens on the same row, and says so if the try failed.</summary>
public sealed record Handover(
    WaitingItem Item, bool OnPr, IReadOnlyList<WaitingItem> Items, DateTimeOffset? ReadAt, string? Failure = null)
{
    public string[] Arguments => ["try", Item.Team, Item.Pr.ToString()];
}
