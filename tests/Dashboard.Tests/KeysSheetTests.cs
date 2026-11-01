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
                ("→", "Select the column to the right"), ("←", "Select the column to the left"),
                ("↓", "Select the card below"), ("↑", "Select the card above"),
                ("Ctrl+PgDn", "Next team"), ("Ctrl+PgUp", "Previous team"), ("Ctrl+G T", "Go to team…"),
                ("Ctrl+T M", "Show only what's your move"), ("F5", "Read what's waiting again"), ("1", "Go to a-team"),
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
                ("→", "Select the chip to the right"), ("←", "Select the chip to the left"),
                ("↓", "Select the chip below"), ("↑", "Select the chip above"),
                ("Tab", "Select the next team's lane"), ("Shift+Tab", "Select the previous team's lane"),
                ("PgDn", "Scroll the lanes down"), ("PgUp", "Scroll the lanes up"), ("F5", "Read every board again"),
            ],
            Rows(here));
    }

    [Fact]
    public void Here_in_the_team_s_panel_lists_choosing_a_role_and_what_it_can_do()
    {
        using var window = Open(Area.Overseer);
        window.Overseer.SelectHeading("a-team");
        window.Refresh();
        window.Commands.Execute("view.details");

        var here = window.Keys.Here!;

        Assert.Equal("Team", here.Heading);
        Assert.Contains(("↓", "Choose the role below"), Rows(here));
        Assert.Contains(("↑", "Choose the role above"), Rows(here));
        Assert.Contains(("H", "Pause this role"), Rows(here));
        Assert.DoesNotContain(Rows(here), row => row.Label == "Select the chip above");
    }

    [Fact]
    public void Here_in_the_dispatcher_s_log_lists_scrolling_and_going_back()
    {
        using var window = Open(Area.Overseer);
        window.Commands.Execute("view.dispatcher");

        var here = window.Keys.Here!;

        Assert.Equal("Log", here.Heading);
        Assert.Contains(("PgUp", "Scroll the log up"), Rows(here));
        Assert.Contains(("Esc", "Back from the dispatcher's log"), Rows(here));
        Assert.DoesNotContain(Rows(here), row => row.Label == "Copy the selected lines");
    }

    [Fact]
    public void Each_region_lists_keys_of_its_own()
    {
        using var work = Open(Area.Work);
        using var overseer = Open(Area.Overseer);
        using var team = Open(Area.Overseer);
        team.Overseer.SelectHeading("a-team");
        team.Refresh();
        team.Commands.Execute("view.details");
        using var log = Open(Area.Overseer);
        log.Commands.Execute("view.dispatcher");

        List<string> sheets = [.. new[] { work, overseer, team, log }.Select(window => string.Join('\n', Rows(window.Keys.Here!)))];

        Assert.Equal(4, sheets.Distinct().Count());
    }

    [Fact]
    public void Everywhere_lists_the_global_keys_under_the_menu_that_holds_them_and_nothing_from_a_region()
    {
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        using var window = Open(Area.Work);
        app.Begin(window);

        var everywhere = window.Keys.Everywhere;
        Assert.NotNull(everywhere);

        Assert.Equal("Everywhere", everywhere.Heading);
        Assert.Equal(["View", "Go", "Help"], everywhere.Groups.Select(group => group.Heading));
        Assert.Equal([("Ctrl+,", "Settings"), ("Ctrl+Q", "Quit")], Rows(everywhere.Groups[0]));
        Assert.Equal([("Ctrl+G W", "Work"), ("Ctrl+G O", "Overseer")], Rows(everywhere.Groups[1]));
        Assert.Equal([("F1", "Show keys"), ("Ctrl+E", "Commands")], Rows(everywhere.Groups[2]));
        Assert.DoesNotContain(everywhere.Groups.SelectMany(group => group.Rows), row => row.Label == "Next team");
    }

    [Fact]
    public void A_rebound_key_shows_as_it_is_bound()
    {
        Directory.CreateDirectory(Config);
        OldKeys.Save(Config, ("work.mine", "z"));
        using var window = Open(Area.Work);

        Assert.Contains(("Z", "Show only what's your move"), Rows(window.Keys.Here!));
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
        wide.Layout(new Size(160, 40));
        narrow.Layout(new Size(60, 40));

        Assert.Equal("Help", wide.Title);
        Assert.True(Heading(wide, "Everywhere").X < Heading(wide, "Work").X);
        Assert.Equal(Heading(wide, "Everywhere").Y, Heading(wide, "Work").Y);
        Assert.True(Heading(narrow, "Work").Y < Heading(narrow, "Everywhere").Y);
        Assert.Equal(Heading(narrow, "Work").X, Heading(narrow, "Everywhere").X);
    }

    /// <summary>Where the column headed <paramref name="text"/> is drawn.</summary>
    private static Point Heading(KeysDialog dialog, string text) =>
        dialog.Body.SubViews.SelectMany(column => column.SubViews).OfType<Label>().Single(label => label.Text == text).FrameToScreen().Location;

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
            Opening.Opened,
            (_, _, _, _, _, _, _, _, _, _, _, _) => null,
            area,
            IconStyle.Unicode);
    }

    private string Config => Path.Combine(_root, "config");
}
