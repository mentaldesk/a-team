using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.Text;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace ATeam.Dashboard;

/// <summary>Which vocabulary the dashboard draws its icons from.</summary>
public enum IconStyle
{
    Auto,
    NerdFont,
    Unicode,
}

/// <summary>Everything the dashboard classifies, named by what it means rather than by the shape it wears.</summary>
public enum Icon
{
    Running,
    NeverRun,
    Paused,
    Held,
    StoppedByYou,
    Ok,
    Failed,
    CutShort,
    Selected,
    ToolCall,
    ToolError,
    Finished,
    YourMove,
    TheirMove,
    Idea,
    Pitch,
    Task,
    PullRequest,
}

/// <summary>A card's icon for whose move it is, and the scheme its colour comes from.</summary>
public readonly record struct TurnIcon(string Glyph, string Scheme);

/// <summary>The dashboard's one vocabulary: every meaning in an <c>nf-md-*</c> style and in the plain
/// one a terminal without that font can draw, each in a field of the same width.</summary>
public static class Icons
{
    /// <summary>The cells an icon spends, the same either way so the text beside it doesn't shift with the style.</summary>
    public const int Width = 2;

    private static readonly Dictionary<Icon, string> NerdGlyphs = new()
    {
        [Icon.Running] = "\U000F040C",   // nf-md-play_circle
        [Icon.NeverRun] = "\U000F0766",  // nf-md-circle_outline
        [Icon.Paused] = "\U000F03E5",    // nf-md-pause_circle
        [Icon.Held] = "\U000F03E5",      // nf-md-pause_circle
        [Icon.StoppedByYou] = "\U000F03E5", // nf-md-pause_circle
        [Icon.Ok] = "\U000F05E0",        // nf-md-check_circle
        [Icon.Failed] = "\U000F0159",    // nf-md-close_circle
        [Icon.CutShort] = "\U000F0159",  // nf-md-close_circle
        [Icon.Selected] = "\U000F0142",  // nf-md-chevron_right
        [Icon.ToolCall] = "\U000F0169",  // nf-md-code_braces
        [Icon.ToolError] = "\U000F0159", // nf-md-close_circle
        [Icon.Finished] = "\U000F023C",  // nf-md-flag_checkered
        [Icon.YourMove] = "\U000F05E0",  // nf-md-check_circle
        [Icon.TheirMove] = "\U000F0D70", // nf-md-face_agent
        [Icon.Idea] = "\uF400",        // nf-oct-light_bulb
        [Icon.Pitch] = "\U000F0428",    // nf-md-presentation
        [Icon.Task] = "\uEC37",         // nf-cod-code_review
        [Icon.PullRequest] = "\uE726",  // nf-dev-git_pull_request
    };

    private static readonly Dictionary<Icon, string> UnicodeGlyphs = new()
    {
        [Icon.Running] = "●",
        [Icon.NeverRun] = "○",
        [Icon.Paused] = "⏸",
        [Icon.Held] = "⏸",
        [Icon.StoppedByYou] = "⏸",
        [Icon.Ok] = "✓",
        [Icon.Failed] = "✗",
        [Icon.CutShort] = "✗",
        [Icon.Selected] = "▶",
        [Icon.ToolCall] = "▸",
        [Icon.ToolError] = "✗",
        [Icon.Finished] = "■",
        [Icon.YourMove] = "✓",
        [Icon.TheirMove] = "·",
        [Icon.Idea] = "\U0001F4A1",
        [Icon.Pitch] = "◇",
        [Icon.Task] = "‹›",
        [Icon.PullRequest] = "PR",
    };

    /// <summary>The meanings a style's sample shows, in the order Settings names them underneath.</summary>
    private static readonly Icon[] Sampled =
        [Icon.Running, Icon.NeverRun, Icon.Paused, Icon.Ok, Icon.Failed, Icon.Selected, Icon.ToolCall];

    /// <summary>The style a view actually draws in: a style the reader picked is their own, and Auto is what
    /// <paramref name="auto"/> — the terminal this launch is drawing in — answered.</summary>
    public static IconStyle Resolve(IconStyle style, IconStyle auto) => style == IconStyle.Auto ? auto : style;

    public static string Glyph(Icon icon, IconStyle style) =>
        (style == IconStyle.NerdFont ? NerdGlyphs : UnicodeGlyphs)[icon];

    public static string Field(Icon icon, IconStyle style) => Field(Glyph(icon, style));

    /// <summary>The glyph in the cells it's drawn in: padded where it costs one, and left to fill the
    /// field where the runtime already gives it two.</summary>
    public static string Field(string glyph) =>
        glyph + new string(' ', Math.Max(0, Width - glyph.GetColumns()));

    /// <summary>A style drawn in its own vocabulary, so a row can be picked by looking at it.</summary>
    public static string Sample(IconStyle style) => string.Join(" ", Sampled.Select(icon => Field(icon, style)));

    public static Icon For(PaneStatus status) => status switch
    {
        PaneStatus.Running => Icon.Running,
        PaneStatus.NeverRun => Icon.NeverRun,
        PaneStatus.Paused => Icon.Paused,
        PaneStatus.Held => Icon.Held,
        PaneStatus.StoppedByYou => Icon.StoppedByYou,
        PaneStatus.Ok => Icon.Ok,
        PaneStatus.Failed => Icon.Failed,
        PaneStatus.CutShort => Icon.CutShort,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "No icon for this status."),
    };

    /// <summary>The meaning a log line carries, or null for a line that classifies nothing.</summary>
    public static Icon? For(LogLineKind kind) => kind switch
    {
        LogLineKind.ToolCall => Icon.ToolCall,
        LogLineKind.ToolError => Icon.ToolError,
        LogLineKind.ResultOk or LogLineKind.ResultError => Icon.Finished,
        _ => null,
    };

    public static TurnIcon For(WaitingItem item, IconStyle style) => item.Mine
        ? new TurnIcon(Glyph(Icon.YourMove, style), LogSchemes.Success)
        : new TurnIcon(Glyph(Icon.TheirMove, style), LogSchemes.Dimmed);

    /// <summary>What kind of thing the item is, in the colour of whose move it is.</summary>
    public static TurnIcon Kind(WaitingItem item, IconStyle style) =>
        For(item, style) with { Glyph = Glyph(KindOf(item), style) };

    private static Icon KindOf(WaitingItem item) =>
        item.Status == "Idea" ? Icon.Idea : item.Pitch ? Icon.Pitch : Icon.Task;
}

/// <summary>A row wears its icon and its Priority in the cells the tree laid out in front of its text: drawn
/// rather than put in the text, in their own colours over the row's own background so the selection still reads.</summary>
internal static class CardCells
{
    /// <summary>Paints a field at <paramref name="at"/> for each of <paramref name="leads"/>, the number after them
    /// in its Priority's colour, and <paramref name="text"/>'s astral runes back into the cells
    /// <see cref="LaidOut"/> kept for them. A row scrolled sideways has none of it on screen.</summary>
    internal static void Paint(IList<Cell> cells, int at, string text, IReadOnlyList<TurnIcon> leads, PriorityMark mark)
    {
        if (at < 0)
            return;
        foreach (var lead in leads)
        {
            var field = Field(lead.Glyph);
            for (var cell = 0; cell < Icons.Width; cell++)
                Paint(cells, at + cell, field[cell], lead.Scheme);
            at += Icons.Width;
        }
        for (var cell = 0; cell < mark.Width; cell++)
            Paint(cells, at + cell, null, mark.Scheme);
        Astral(cells, at, text);
    }

    /// <summary>The text as the tree can lay it out. It makes a cell of every <c>char</c>, and half a surrogate pair
    /// isn't a grapheme a cell can hold, so an astral rune — an emoji in a title — throws as it draws. A blank
    /// stands in for each of its two chars, leaving the row the length the tree measured, and <see cref="Paint"/>
    /// writes the rune back into the first of them.</summary>
    internal static string LaidOut(string text) =>
        text.Any(char.IsSurrogate)
            ? string.Concat(text.EnumerateRunes().Select(rune => rune.IsBmp ? rune.ToString() : "  "))
            : text;

    /// <summary>Each astral rune in the first of its two cells, leaving the second empty as a glyph that fills two
    /// is drawn.</summary>
    private static void Astral(IList<Cell> cells, int at, string text)
    {
        var index = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (!rune.IsBmp)
            {
                Paint(cells, at + index, rune.ToString(), null);
                Paint(cells, at + index + 1, "", null);
            }
            index += rune.Utf16SequenceLength;
        }
    }

    /// <summary>The scheme's own foreground over the row's background, so a selected card keeps its highlight.</summary>
    internal static Attribute Colour(string name, Attribute row) =>
        SchemeManager.TryGetScheme(name, out var scheme)
            ? new Attribute(scheme.GetAttributeForRole(VisualRole.Normal).Foreground, row.Background, row.Style)
            : row;

    /// <summary>The field a cell at a time: a glyph the runtime draws in two cells fills it by itself, leaving the
    /// second one empty.</summary>
    internal static string[] Field(string glyph)
    {
        var field = new string[Icons.Width];
        var cell = 0;
        foreach (var rune in Icons.Field(glyph).EnumerateRunes())
            if (cell < field.Length)
                field[cell++] = rune.ToString();
        while (cell < field.Length)
            field[cell++] = "";
        return field;
    }

    private static void Paint(IList<Cell> cells, int index, string? grapheme, string? scheme)
    {
        if (index < 0 || index >= cells.Count)
            return;
        var cell = cells[index];
        if (scheme is not null)
            cell.Attribute = Colour(scheme, cell.Attribute ?? default);
        if (grapheme is not null)
            cell.Grapheme = grapheme;
        cells[index] = cell;
    }
}
