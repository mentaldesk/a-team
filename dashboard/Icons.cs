using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
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

    /// <summary>The style a view actually draws in. Auto means Unicode until the dashboard learns to
    /// recognise the terminals that bundle a Nerd Font.</summary>
    public static IconStyle Resolve(IconStyle style) =>
        style == IconStyle.NerdFont ? IconStyle.NerdFont : IconStyle.Unicode;

    public static string Glyph(Icon icon, IconStyle style) =>
        (Resolve(style) == IconStyle.NerdFont ? NerdGlyphs : UnicodeGlyphs)[icon];

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
}

/// <summary>Cards that wear their icon and their Priority: both drawn rather than put in the text, so a card's
/// text stays the item's own, in their own colours over the row's own background so the selection still reads.</summary>
internal sealed class CardSource(
    IReadOnlyList<string> cards, IReadOnlyList<TurnIcon> icons, IReadOnlyList<PriorityMark> priorities)
    : IListDataSource
{
    private readonly ListWrapper<string> _text = new(new ObservableCollection<string>(cards));

    public event NotifyCollectionChangedEventHandler? CollectionChanged
    {
        add => _text.CollectionChanged += value;
        remove => _text.CollectionChanged -= value;
    }

    public int Count => _text.Count;

    public int MaxItemLength => _text.MaxItemLength + Icons.Width;

    public bool SuspendCollectionChangedEvent
    {
        get => _text.SuspendCollectionChangedEvent;
        set => _text.SuspendCollectionChangedEvent = value;
    }

    public void Render(ListView listView, bool selected, int item, int col, int row, int width, int viewportX = 0)
    {
        // Scrolled sideways, the icon would sit on top of the text; draw the text alone.
        if (viewportX > 0 || item < 0 || item >= icons.Count)
        {
            _text.Render(listView, selected, item, col, row, width, viewportX);
            return;
        }

        var attribute = listView.GetCurrentAttribute();
        listView.Move(col, row);
        listView.SetAttribute(Colour(icons[item].Scheme, attribute));
        listView.AddStr(Icons.Field(icons[item].Glyph));
        listView.SetAttribute(attribute);
        if (width <= Icons.Width)
            return;
        _text.Render(listView, selected, item, col + Icons.Width, row, width - Icons.Width);
        Number(listView, item, col + Icons.Width, row, attribute);
    }

    public bool IsMarked(int item) => _text.IsMarked(item);

    public void SetMark(int item, bool value) => _text.SetMark(item, value);

    public IList ToList() => _text.ToList();

    public void Dispose() => _text.Dispose();

    /// <summary>The scheme's own foreground over the row's background, so a selected card keeps its highlight.</summary>
    internal static Attribute Colour(string name, Attribute row) =>
        SchemeManager.TryGetScheme(name, out var scheme)
            ? new Attribute(scheme.GetAttributeForRole(VisualRole.Normal).Foreground, row.Background, row.Style)
            : row;

    /// <summary>The issue number over again in its Priority's colour, leaving the rest of the card as drawn.</summary>
    private void Number(ListView listView, int item, int col, int row, Attribute attribute)
    {
        if (item >= priorities.Count || priorities[item] is not { Width: > 0 } mark)
            return;
        listView.Move(col, row);
        listView.SetAttribute(Colour(mark.Scheme, attribute));
        listView.AddStr(cards[item][..mark.Width]);
        listView.SetAttribute(attribute);
    }
}
