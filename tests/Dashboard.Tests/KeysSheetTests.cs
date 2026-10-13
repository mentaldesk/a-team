using System.Drawing;
using MentalDesk.Tui.Keys;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace ATeam.Dashboard.Tests;

[Collection("StaticConfiguration")]
public class KeysSheetTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"a-team-{Guid.NewGuid():n}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Here_in_Work_lists_moving_between_cards_and_teams_the_filter_and_refresh()
    {
        using var window = Open(Area.Work);

        var here = window.Keys.Here!;

        Assert.Equal("Work", here.Heading);
        Assert.Equal(
            [
                ("CursorRight", "Select the column to the right"), ("CursorLeft", "Select the column to the left"),
                ("CursorDown", "Select the card below"), ("CursorUp", "Select the card above"),
                ("Ctrl+PageDown", "Next team"), ("Ctrl+PageUp", "Previous team"),
                ("m", "Show only what's your move"), ("F5", "Read what's waiting again"), ("1", "Go to a-team"),
            ],
            Rows(here));
    }

    [Fact]
    public void Here_in_Overseer_lists_moving_between_chips_and_lanes_and_refresh()
    {
        using var window = Open(Area.Overseer);

        var here = window.Keys.Here!;

        Assert.Equal("Overseer", here.Heading);
        Assert.Equal(
            [
                ("CursorRight", "Select the chip to the right"), ("CursorLeft", "Select the chip to the left"),
                ("CursorDown", "Select the chip below"), ("CursorUp", "Select the chip above"),
                ("Tab", "Select the next team's lane"), ("Shift+Tab", "Select the previous team's lane"),
                ("PgDn", "Scroll the lanes down"), ("PgUp", "Scroll the lanes up"), ("F5", "Read every board again"),
            ],
            Rows(here));
    }

    [Fact]
    public void Here_on_the_agent_grid_lists_moving_between_agents_and_what_the_selected_one_can_do()
    {
        using var window = Open(Area.Dashboard);
        window.NewKeyDownEvent(Key.Tab);

        var here = window.Keys.Here!;

        Assert.Equal("Agents", here.Heading);
        Assert.Equal(
            [
                "Select the next agent", "Select the previous agent", "Select the agent to the right", "Select the agent to the left",
                "Select the agent below", "Select the agent above", "Expand the selected agent", "Scroll the log up", "Scroll the log down",
                "Jump to the top of the log", "Jump to the bottom of the log", "Show tool calls in full", "Pause selected agent's role",
            ],
            Rows(here).Select(row => row.Label));
    }

    [Fact]
    public void Here_in_an_expanded_log_lists_selecting_lines_copying_and_going_back()
    {
        using var window = Open(Area.Dashboard);
        window.NewKeyDownEvent(Key.Tab);
        window.NewKeyDownEvent(Key.Enter);

        var here = window.Keys.Here!;

        Assert.Equal("Log", here.Heading);
        Assert.Contains(("Shift+CursorDown", "Extend the selection down"), Rows(here));
        Assert.Contains(("l", "Copy the selected lines"), Rows(here));
        Assert.Contains(("L", "Copy the whole log"), Rows(here));
        Assert.Contains(("e", "Open the whole log in your editor"), Rows(here));
        Assert.Contains(("Esc", "Back to the agent grid"), Rows(here));
        Assert.DoesNotContain(Rows(here), row => row.Label == "Expand the selected agent");
    }

    [Fact]
    public void Each_region_lists_keys_of_its_own()
    {
        using var work = Open(Area.Work);
        using var overseer = Open(Area.Overseer);
        using var grid = Open(Area.Dashboard);
        grid.NewKeyDownEvent(Key.Tab);
        using var log = Open(Area.Dashboard);
        log.NewKeyDownEvent(Key.Tab);
        log.NewKeyDownEvent(Key.Enter);

        List<string> sheets = [.. new[] { work, overseer, grid, log }.Select(window => string.Join('\n', Rows(window.Keys.Here!)))];

        Assert.Equal(4, sheets.Distinct().Count());
    }

    [Fact]
    public void Everywhere_lists_the_global_keys_under_the_menu_that_holds_them_and_nothing_from_a_region()
    {
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        using var window = Open(Area.Work);
        app.Begin(window);

        var everywhere = window.Keys.Everywhere;

        Assert.Equal("Everywhere", everywhere.Heading);
        Assert.Equal(["View", "Help"], everywhere.Groups.Select(group => group.Heading));
        Assert.Equal(
            [("d", "Dashboard"), ("w", "Work"), ("o", "Overseer"), ("s", "Settings"), ("q", "Quit")],
            Rows(everywhere.Groups[0]));
        Assert.Equal([("F1", "Show keys"), ("Ctrl+E", "Commands")], Rows(everywhere.Groups[1]));
        Assert.DoesNotContain(everywhere.Groups.SelectMany(group => group.Rows), row => row.Label == "Next team");
    }

    [Fact]
    public void A_rebound_key_shows_as_it_is_bound()
    {
        Directory.CreateDirectory(Config);
        new DashboardSettings(Config).WriteKeys([("work.mine", new Key('z'))]);
        using var window = Open(Area.Work);

        Assert.Contains(("z", "Show only what's your move"), Rows(window.Keys.Here!));
    }

    [Fact]
    public void Side_by_side_on_a_wide_terminal_stacked_on_a_narrow_one()
    {
        using var window = Open(Area.Work);

        using var wide = KeysSheet.Create(window.Keys, new Size(160, 40));
        using var narrow = KeysSheet.Create(window.Keys, new Size(60, 40));

        Assert.False(wide.Stacked);
        Assert.True(narrow.Stacked);
    }

    [Fact]
    public void Everywhere_is_on_the_left_side_by_side_and_the_place_on_top_stacked_as_in_TuiCode()
    {
        using var window = Open(Area.Work);

        using var wide = KeysSheet.Create(window.Keys, new Size(160, 40));
        using var narrow = KeysSheet.Create(window.Keys, new Size(60, 40));

        Assert.Equal(["Everywhere", "Work"], Headings(wide.Sheet));
        Assert.Equal(["Work", "Everywhere"], Headings(narrow.Sheet));
    }

    private static List<string> Headings(KeySheet sheet) =>
        [.. new[] { sheet.Here, sheet.Everywhere }.OfType<KeyColumn>().Select(column => column.Heading)];

    [Fact]
    public void On_a_short_terminal_the_sheet_scrolls()
    {
        using var window = Open(Area.Work);
        using var dialog = KeysSheet.Create(window.Keys, new Size(60, 12));
        dialog.Layout(new Size(60, 12));

        Assert.True(dialog.Body.GetContentSize().Height > dialog.Body.Viewport.Height);

        Assert.True(dialog.NewKeyDownEvent(Key.CursorDown));
        Assert.Equal(1, dialog.Body.Viewport.Y);
        Assert.True(dialog.NewKeyDownEvent(Key.PageDown));
        Assert.True(dialog.Body.Viewport.Y > 1);
    }

    [Fact]
    public void Esc_closes_it_and_no_other_key_reaches_the_window()
    {
        using var window = Open(Area.Work);
        using var dialog = KeysSheet.Create(window.Keys, new Size(120, 40));

        Assert.True(dialog.NewKeyDownEvent(new Key('q')));
        Assert.True(dialog.NewKeyDownEvent(Key.Esc));
    }

    [Fact]
    public void It_closes_with_a_button_centred_below_the_sheet()
    {
        using var window = Open(Area.Work);
        using var dialog = KeysSheet.Create(window.Keys, new Size(120, 40));
        dialog.Layout(new Size(120, 40));

        var close = Assert.Single(dialog.SubViews.OfType<Button>(), button => button.Visible);
        Assert.Equal(" Esc Close ", close.Text);
        Assert.False(dialog.Hints.Visible);
        var viewport = dialog.Viewport.Width;
        Assert.InRange(close.Frame.X - (viewport - close.Frame.Right), -1, 1);
        Assert.True(close.Frame.Y >= dialog.Body.Frame.Bottom, $"{close.Frame} overlaps {dialog.Body.Frame}");
    }

    private static List<(string Keys, string Label)> Rows(KeyColumn column) =>
        [.. column.Groups.SelectMany(Rows)];

    private static List<(string Keys, string Label)> Rows(KeyGroup group) =>
        [.. group.Rows.Select(row => (row.Keys, row.Label))];

    private DashboardWindow Open(Area area)
    {
        Directory.CreateDirectory(_root);
        return new DashboardWindow(
            [("a-team", "lead"), ("a-team", "dev")],
            _root,
            new DashboardSettings(Config),
            new TeamConfigs(Config),
            _ => Task.FromResult<string?>(null),
            _ => Task.FromResult(new Reading("[]", null)),
            _ => Task.FromResult(new Reading("{}", null)),
            _ => { },
            (_, _, _, _, _, _, _, _, _, _) => null,
            area,
            IconStyle.Unicode);
    }

    private string Config => Path.Combine(_root, "config");
}
