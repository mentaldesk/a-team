using MentalDesk.Tui.Dialogs;
using Terminal.Gui.Input;

namespace ATeam.Dashboard;

/// <summary>MentalDesk.Tui's <see cref="PickerDialog{T}"/> with a line above its filter saying where it's looking, and
/// its keys routed to its commands, which the library does only under an AppShell the dashboard doesn't have.</summary>
public abstract class PathPicker : PickerDialog<string>
{
    private const int ScreenMargin = 4;
    private const int Widest = 90;

    private readonly Label _where;
    private readonly Label _noMatches;

    protected PathPicker(string title, int width, int contentRows, IReadOnlyList<string> rows, string where, string verb)
        : base(title, width, contentRows, rows, row => row, verb)
    {
        _noMatches = SubViews.OfType<Label>().Single(label => label.Text == "(no matches)");
        _where = new Label { Text = where, X = 1, Y = 0, Width = Dim.Fill(1) };
        FilterField.Y = 1;
        Add(_where);
        foreach (var view in new View[] { this, FilterField })
            view.KeyDown += (_, key) => key.Handled = Route(key);
        FilterField.SetFocus();
    }

    internal string Where => _where.Text;

    /// <summary>What <see cref="PickerDialog{T}.NoMatches"/> shows in place of the list.</summary>
    internal string NoMatchesText => _noMatches.Text;

    /// <summary>The path picked, or null where it was cancelled.</summary>
    internal string? Picked { get; private set; }

    internal string Typed => FilterField.Text.Trim();

    /// <summary>The row the list has selected, or null where it shows none.</summary>
    protected string? Selected => Shown.Count > 0 && SubViews.OfType<ListView>().Single().SelectedItem is { } at && at < Shown.Count
        ? Shown[at]
        : null;

    /// <summary>The widest a picker can be on a screen <paramref name="columns"/> wide, and how many rows it can show
    /// on one <paramref name="lines"/> high.</summary>
    internal static (int Width, int Rows) Fit(int columns, int lines) =>
        (Math.Clamp(columns - ScreenMargin, 20, Widest), Math.Clamp(lines - ScreenMargin - 2, 6, 24));

    protected void Say(string text) => _noMatches.Text = text;

    protected void Take(string path)
    {
        Picked = path;
        Accept();
    }

    /// <summary>Handles a key the way the AppShell would, plus any the picker adds; true where it took it.</summary>
    internal virtual bool Route(Key key) => Keys.Handle(key) != MentalDesk.Tui.Keys.KeyResult.Pass;
}
