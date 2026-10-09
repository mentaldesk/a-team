namespace ATeam.Dashboard;

/// <summary>Something the dashboard hands the terminal to, and what to show again once it takes it back.</summary>
public abstract record Handover
{
    public string? Failure { get; init; }

    public abstract string[] Arguments { get; }

    public abstract Area Area { get; }
}

/// <summary>What the Work area was showing when it handed the terminal to <c>a-team try</c>, so the window that
/// takes it back opens on the same row, or the same reader, and says so if the try failed.</summary>
public sealed record TryHandover(
    WaitingItem Item, bool OnPr, IReadOnlyList<WaitingItem> Items, DateTimeOffset? ReadAt, ReaderPlace? Reader = null)
    : Handover
{
    public override string[] Arguments => Item.Pr > 0 ? ["try", Item.Team, Item.Pr.ToString()] : ["try", Item.Team];

    public override Area Area => Area.Work;
}

/// <summary>The agent handed to <c>a-team attach</c>, or just its run on <paramref name="Task"/>, selected again on
/// the grid once you quit.</summary>
public sealed record AttachHandover(string Team, string Role, int? Task = null) : Handover
{
    public override string[] Arguments => Task is { } task ? ["attach", Team, Role, task.ToString()] : ["attach", Team, Role];

    public override Area Area => Area.Dashboard;
}

/// <summary>The teams changed in Settings, so the window is built again over the new list, back on the card
/// <paramref name="Left"/> names where it's still there.</summary>
public sealed record TeamsChanged(Area Shown, Place? Left = null) : Handover
{
    public override string[] Arguments => [];

    public override Area Area => Shown;
}

/// <summary>An agent's whole session, written to <paramref name="File"/> and handed to <paramref name="Editor"/>; the
/// pane opens expanded at <paramref name="Place"/> once you quit.</summary>
public sealed record EditorHandover(string Team, string Role, PanePlace Place, string File, string[] Editor) : Handover
{
    public override string[] Arguments => [.. Editor[1..], File];

    public override Area Area => Area.Dashboard;

    /// <summary>Waits for the editor to quit, then deletes the file. Null once it has; otherwise how it failed.</summary>
    public string? Open()
    {
        try
        {
            return new TeamCommand(Editor[0]).Hand(Arguments, Path.GetFileName(Editor[0]));
        }
        finally
        {
            System.IO.File.Delete(File);
        }
    }

    /// <summary><c>$VISUAL</c>, else <c>$EDITOR</c>, else <c>less</c>, split into the program and its arguments.</summary>
    public static string[] Command(Func<string, string?> environment) =>
        (environment("VISUAL") is { Length: > 0 } visual ? visual
            : environment("EDITOR") is { Length: > 0 } editor ? editor
            : "less").Split(' ', StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>The reader a try was started from: what it showed, its link, and the row it was scrolled to.</summary>
public sealed record ReaderPlace(IssueBody Body, string? Url, int Top);
