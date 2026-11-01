using System.Text.Json.Nodes;
using MentalDesk.Tui.Commands;
using MentalDesk.Tui.Focus;
using MentalDesk.Tui.Layout;
using Terminal.Gui.Input;

namespace ATeam.Dashboard;

/// <summary>The widths the reader's and the Guide's side panes were left at, kept in the dashboard's settings, and the
/// side panes of whichever of them is open, for the window's Commands to resize.</summary>
public sealed class PaneWidths
{
    public static readonly FocusRegion History = new("History", new CommandScope("History"));
    public static readonly FocusRegion Comment = new("Comment", new CommandScope("Comment"));
    public static readonly FocusRegion Contents = new("Contents", new CommandScope("Contents"));

    public static readonly IReadOnlyList<(string Id, string Label, Action<SidePanes> Run)> Commands =
    [
        (SidePanes.WidenId, "Widen pane", panes => panes.Widen()),
        (SidePanes.NarrowId, "Narrow pane", panes => panes.Narrow()),
        (SidePanes.ResetId, "Reset pane width", panes => panes.Reset()),
    ];

    private readonly Func<JsonObject?> _read;
    private readonly Action<JsonObject> _write;
    private JsonObject? _kept;

    public PaneWidths(Func<JsonObject?> read, Action<JsonObject> write)
    {
        _read = read;
        _write = write;
    }

    /// <summary>Widths kept only until the dashboard closes.</summary>
    public PaneWidths()
    {
        _read = () => _kept;
        _write = widths => _kept = widths;
    }

    public SidePanes? Open { get; private set; }

    /// <summary>The keys each of <see cref="Commands"/> was given on Settings › Keys.</summary>
    public Func<string, IEnumerable<IReadOnlyList<Key>>> Keys { get; set; } = _ => [];

    /// <summary>The window's keys that work over the dialog too.</summary>
    public Func<Key, bool> Window { get; set; } = _ => false;

    public SidePanes For(View row, Func<FocusRegion?> focused, Action<string> say) => new(row, focused, say, _read, _write);

    /// <summary>The width <paramref name="region"/> was left at, or <paramref name="fallback"/>.</summary>
    public int Width(FocusRegion region, int fallback) =>
        _read()?[region.Name] is JsonValue value && value.TryGetValue<int>(out var width) && width >= SidePanes.MinimumWidth
            ? width
            : fallback;

    public void While(SidePanes panes, Action run)
    {
        var before = Open;
        Open = panes;
        try
        {
            run();
        }
        finally
        {
            Open = before;
        }
    }
}
