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

/// <summary>A team's vision interview, from the area it was started in: back on the card <paramref name="Left"/> names in
/// Work, or the agent <paramref name="Role"/> names on the grid.</summary>
public sealed record VisionHandover(string Team, Area Shown, Place? Left = null, string? Role = null) : Handover
{
    public override string[] Arguments => ["vision", Team];

    public override Area Area => Shown;
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

/// <summary>A document pitch's one file, written to <paramref name="File"/> and handed to <paramref name="Editor"/>. What you
/// save is committed to the pitch as you, and Work opens its reader again, where it was.</summary>
public sealed record DocumentHandover(
    WaitingItem Item, DocumentPitch Document, string File, string[] Editor, bool OnPr, IReadOnlyList<WaitingItem> Items,
    DateTimeOffset? ReadAt, ReaderPlace Reader) : Handover
{
    /// <summary>What became of the edit, where it went through.</summary>
    public string? Said { get; init; }

    /// <summary>Whether the pitch has a new commit, so the reader reads it again.</summary>
    public bool Committed { get; init; }

    public override string[] Arguments => [.. Editor[1..], File];

    public override Area Area => Area.Work;

    /// <summary>The file to hand your editor: the document's own name, so the editor knows what it is.</summary>
    public static string FileFor(WaitingItem item, DocumentPitch document) =>
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"a-team-{item.Team}-{item.Number}-{System.IO.Path.GetFileName(document.Path)}");

    /// <summary>Waits for <paramref name="hand"/> to give the terminal back, then commits the file if it changed. A
    /// refused commit leaves your edit in <paramref name="keep"/>, and says where.</summary>
    public DocumentHandover Open(Func<string[], string?> hand, Func<string[], string?> commit, string keep, DateTimeOffset now)
    {
        if (hand(Arguments) is { } failure)
            return Done(this with { Failure = failure });
        string edited;
        try
        {
            edited = System.IO.File.ReadAllText(File);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Done(this with { Failure = $"couldn't read your edit: {e.Message}" });
        }
        if (edited == Document.Text)
            return Done(this with { Said = $"no change to {Document.Path}" });
        var refused = commit(["board", Item.Team, "edit", "you", Item.Number.ToString(), Document.Path, Document.Sha, File]);
        if (refused is null or { Length: 0 })
            return Done(this with { Said = $"committed your edit to {Document.Path}", Committed = true });
        var kept = System.IO.Path.Combine(keep, $"{Item.Team}-{Item.Number}-{now:yyyyMMddHHmmss}-{System.IO.Path.GetFileName(Document.Path)}");
        try
        {
            Directory.CreateDirectory(keep);
            System.IO.File.Move(File, kept);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            kept = File;
        }
        return this with { Failure = $"{refused}: your edit is kept in {kept}" };
    }

    private DocumentHandover Done(DocumentHandover handover)
    {
        System.IO.File.Delete(File);
        return handover;
    }
}
