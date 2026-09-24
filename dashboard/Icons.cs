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
    Ok,
    Failed,
    CutShort,
    Selected,
    ToolCall,
    ToolError,
    Finished,
    YourMove,
    TheirMove,
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
        [Icon.Ok] = "\U000F05E0",        // nf-md-check_circle
        [Icon.Failed] = "\U000F0159",    // nf-md-close_circle
        [Icon.CutShort] = "\U000F0159",  // nf-md-close_circle
        [Icon.Selected] = "\U000F0142",  // nf-md-chevron_right
        [Icon.ToolCall] = "\U000F0169",  // nf-md-code_braces
        [Icon.ToolError] = "\U000F0159", // nf-md-close_circle
        [Icon.Finished] = "\U000F023C",  // nf-md-flag_checkered
        [Icon.YourMove] = "\U000F05E0",  // nf-md-check_circle
        [Icon.TheirMove] = "\U000F0D70", // nf-md-face_agent
    };

    private static readonly Dictionary<Icon, string> UnicodeGlyphs = new()
    {
        [Icon.Running] = "●",
        [Icon.NeverRun] = "○",
        [Icon.Paused] = "⏸",
        [Icon.Ok] = "✓",
        [Icon.Failed] = "✗",
        [Icon.CutShort] = "✗",
        [Icon.Selected] = "▶",
        [Icon.ToolCall] = "▸",
        [Icon.ToolError] = "✗",
        [Icon.Finished] = "■",
        [Icon.YourMove] = "✓",
        [Icon.TheirMove] = "·",
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
        PaneStatus.Ok => Icon.Ok,
        PaneStatus.Failed => Icon.Failed,
        PaneStatus.CutShort => Icon.CutShort,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "No icon for this status."),
    };

    public static TurnIcon For(WaitingItem item, IconStyle style) => item.Mine
        ? new TurnIcon(Glyph(Icon.YourMove, style), LogSchemes.Success)
        : new TurnIcon(Glyph(Icon.TheirMove, style), LogSchemes.Dimmed);
}

/// <summary>A row wears its icon and its Priority in the cells the tree laid out in front of its text: drawn
/// rather than put in the text, in their own colours over the row's own background so the selection still reads.</summary>
internal static class CardCells
{
    /// <summary>Paints the field at <paramref name="at"/> with <paramref name="lead"/>, and the number after it in
    /// its Priority's colour. A row scrolled sideways has neither on screen.</summary>
    internal static void Paint(IList<Cell> cells, int at, TurnIcon lead, PriorityMark mark)
    {
        if (at < 0)
            return;
        var field = Field(lead.Glyph);
        for (var cell = 0; cell < Icons.Width; cell++)
            Paint(cells, at + cell, field[cell], lead.Scheme);
        for (var cell = 0; cell < mark.Width; cell++)
            Paint(cells, at + Icons.Width + cell, null, mark.Scheme);
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

    private static void Paint(IList<Cell> cells, int index, string? grapheme, string scheme)
    {
        if (index < 0 || index >= cells.Count)
            return;
        var cell = cells[index];
        cell.Attribute = Colour(scheme, cell.Attribute ?? default);
        if (grapheme is not null)
            cell.Grapheme = grapheme;
        cells[index] = cell;
    }
}
