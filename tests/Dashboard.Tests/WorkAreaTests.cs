using System.Drawing;
using Terminal.Gui;
using Terminal.Gui.App;
using Terminal.Gui.Input;

namespace ATeam.Dashboard.Tests;

/// <summary>The window with the Work area in front: what it reads, when, and what a key does there.</summary>
[Collection("StaticConfiguration")]
public class WorkAreaTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"a-team-{Guid.NewGuid():n}");
    private readonly PlatformKeyBinding _quit = Application.DefaultKeyBindings![Command.Quit];

    public void Dispose()
    {
        Application.SetDefaultKeyBinding(Command.Quit, _quit);
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Opening_in_Work_reads_every_team_once_and_draws_what_came_back()
    {
        var teams = new List<string>();
        using var window = Open(read: team =>
        {
            teams.Add(team);
            return Task.FromResult(new Reading(Waiting(team), null));
        });

        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal(["team0", "team1"], teams);
        Assert.Equal(["Ideas · 1", "Pitches · 2", "Review · 1", "Ideas · 0", "Pitches · 1", "Review · 0"],
            Titles(window));
    }

    [Fact]
    public void Focus_starts_on_the_first_card_and_the_message_bar_says_whose_move_it_is()
    {
        using var window = Open();

        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal(6, window.Work.Selected?.Number);
        Assert.Equal("#6 · waiting to be ranked", window.Message.Says);
    }

    [Fact]
    public void The_message_bar_follows_the_selection()
    {
        using var window = Open();
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.CursorRight);
        Assert.Equal("#107 · awaiting your approval since 08:14", window.Message.Says);

        window.NewKeyDownEvent(Key.CursorDown);
        Assert.Equal("#108 · lead · answering your feedback since 09:30", window.Message.Says);

        window.NewKeyDownEvent(Key.CursorRight);
        Assert.Equal("#49 · dev · answering your feedback since 10:15 · PR #122", window.Message.Says);
    }

    [Fact]
    public void A_column_with_no_card_to_describe_names_the_region_instead()
    {
        using var window = Open();
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorDown);

        Assert.Null(window.Work.Selected);
        Assert.Equal("Review · team1", window.Message.Says);
    }

    [Fact]
    public void m_hides_every_card_that_isn_t_yours_and_m_again_brings_them_back()
    {
        using var window = Open();
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.True(window.NewKeyDownEvent(new Key('m')));
        LayOut(window, 120, 30);

        Assert.Equal(["Ideas · 1", "Pitches · 1", "Review · 0", "Ideas · 0", "Pitches · 1", "Review · 0"],
            Titles(window));
        Assert.Equal(6, window.Work.Selected?.Number);

        window.NewKeyDownEvent(new Key('m'));
        LayOut(window, 120, 30);

        Assert.Equal(["Ideas · 1", "Pitches · 2", "Review · 1", "Ideas · 0", "Pitches · 1", "Review · 0"],
            Titles(window));
    }

    [Fact]
    public void The_foot_of_the_Work_area_says_whether_the_filter_is_on()
    {
        using var window = Open();
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal("All items", window.Message.Status);
        Assert.EndsWith("All items", window.Message.Text, StringComparison.Ordinal);

        window.NewKeyDownEvent(new Key('m'));
        LayOut(window, 120, 30);

        Assert.Equal("My items", window.Message.Status);
        Assert.EndsWith("My items", window.Message.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_Dashboard_has_no_filter_to_report()
    {
        using var window = Open();
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.Esc);

        Assert.Equal("", window.Message.Status);
    }

    [Fact]
    public void Every_card_wears_the_icon_for_whose_move_it_is()
    {
        using var window = Open();
        window.Refresh();
        LayOut(window, 120, 30);

        var icons = window.Work.Lanes[0].Columns[1].Icons;

        Assert.True(window.Work.NerdFont);
        Assert.Equal([LogSchemes.Success, LogSchemes.Dimmed], icons.Select(icon => icon.Scheme));
    }

    [Fact]
    public void A_terminal_without_a_Nerd_Font_is_what_the_app_opens_with_next_time()
    {
        new DashboardSettings(Config).WriteNerdFont(false);

        using var window = Open();
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.False(window.Work.NerdFont);
        Assert.Equal(["✓", "·"], window.Work.Lanes[0].Columns[1].Icons.Select(icon => icon.Glyph));
    }

    [Fact]
    public void Whether_only_mine_is_on_is_what_the_app_opens_with_next_time()
    {
        using (var window = Open())
        {
            window.Refresh();
            window.NewKeyDownEvent(new Key('m'));
        }

        Assert.True(new DashboardSettings(Config).ReadOnlyMine());

        using var reopened = Open();
        reopened.Refresh();
        LayOut(reopened, 120, 30);

        Assert.True(reopened.Work.OnlyMine);
        Assert.Equal(["Ideas · 1", "Pitches · 1", "Review · 0", "Ideas · 0", "Pitches · 1", "Review · 0"],
            Titles(reopened));
    }

    [Fact]
    public void Enter_opens_the_selected_cards_issue()
    {
        var opened = new List<string>();
        using var window = Open(openUrl: opened.Add);
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.CursorRight);
        Assert.True(window.NewKeyDownEvent(Key.Enter));

        Assert.Equal(["https://github.com/mentaldesk/team0/issues/107"], opened);
    }

    [Fact]
    public void p_opens_the_selected_cards_PR()
    {
        var opened = new List<string>();
        using var window = Open(openUrl: opened.Add);
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);
        Assert.True(window.NewKeyDownEvent(new Key('p')));

        Assert.Equal(["https://github.com/mentaldesk/team0/pull/122"], opened);
    }

    [Fact]
    public void p_on_a_card_with_no_PR_says_so_and_leaves_the_selection_alone()
    {
        var opened = new List<string>();
        using var window = Open(openUrl: opened.Add);
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.CursorRight);
        Assert.True(window.NewKeyDownEvent(new Key('p')));

        Assert.Empty(opened);
        Assert.Equal("#107 has no open PR", window.Message.Says);
        Assert.Equal(107, window.Work.Selected?.Number);
    }

    [Fact]
    public void An_Idea_hands_over_to_GitHub_on_Enter_and_has_no_PR_to_open()
    {
        var opened = new List<string>();
        using var window = Open(openUrl: opened.Add);
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal("#6 · waiting to be ranked", window.Message.Says);

        Assert.True(window.NewKeyDownEvent(Key.Enter));
        Assert.Equal(["https://github.com/mentaldesk/team0/issues/6"], opened);

        Assert.True(window.NewKeyDownEvent(new Key('p')));
        Assert.Equal("#6 has no open PR", window.Message.Says);
    }

    [Fact]
    public void That_refusal_goes_when_you_move_to_another_card()
    {
        using var window = Open();
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(new Key('p'));

        window.NewKeyDownEvent(Key.CursorDown);

        Assert.Equal("#108 · lead · answering your feedback since 09:30", window.Message.Says);
    }

    [Fact]
    public void Esc_goes_back_to_the_Dashboard_and_the_agents_are_there_again()
    {
        using var window = Open();
        window.Refresh();

        Assert.True(window.NewKeyDownEvent(Key.Esc));

        Assert.Equal(Area.Dashboard, window.CurrentArea);
        Assert.True(window.Agents.Visible);
        Assert.True(window.Dispatcher.Visible);
        Assert.False(window.Work.Visible);
    }

    [Fact]
    public void The_area_you_were_in_is_what_the_app_opens_in_next_time()
    {
        using var window = Open();

        window.NewKeyDownEvent(Key.Esc);

        Assert.Equal(Area.Dashboard, new DashboardSettings(Config).ReadArea());
        Assert.True(window.NewKeyDownEvent(new Key('w')));
        Assert.Equal(Area.Work, new DashboardSettings(Config).ReadArea());
    }

    [Fact]
    public void The_header_says_when_it_last_read()
    {
        var now = DateTimeOffset.UtcNow;
        using var window = Open();
        Assert.Equal("", window.Stamp.Text);

        window.Refresh();

        Assert.Equal("read <1m ago ", window.Stamp.Text);
        Assert.Equal("read 2m ago ", DashboardWindow.Stamped(now, now.AddMinutes(2)));
    }

    [Fact]
    public void A_read_that_failed_says_so_and_leaves_the_cards_and_the_stamp_as_they_were()
    {
        var failing = false;
        using var window = Open(read: team => Task.FromResult(failing
            ? new Reading("", "a-team board: API rate limit exceeded\nand a second line")
            : new Reading(Waiting(team), null)));
        window.Refresh();
        LayOut(window, 120, 30);
        var stamp = window.Stamp.Text;

        failing = true;
        window.NewKeyDownEvent(new Key('r'));
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal("a-team board: API rate limit exceeded", window.Message.Says);
        Assert.Equal(["Ideas · 1", "Pitches · 2", "Review · 1", "Ideas · 0", "Pitches · 1", "Review · 0"],
            Titles(window));
        Assert.Equal(stamp, window.Stamp.Text);
        Assert.Equal(6, window.Work.Selected?.Number);
    }

    [Fact]
    public void A_second_refresh_while_one_is_running_is_refused()
    {
        var reads = 0;
        var finish = new TaskCompletionSource<Reading>();
        using var window = Open(read: _ =>
        {
            reads++;
            return finish.Task;
        });

        window.NewKeyDownEvent(new Key('r'));
        Assert.Equal("Reading…", window.Message.Says);
        window.NewKeyDownEvent(new Key('r'));

        Assert.Equal(2, reads);
        finish.SetResult(new Reading("[]", null));
        window.Refresh();
        window.NewKeyDownEvent(new Key('r'));
        Assert.Equal(4, reads);
    }

    [Fact]
    public void Reads_happen_when_you_ask_and_never_on_a_timer()
    {
        var reads = 0;
        using var window = Open(read: team =>
        {
            reads++;
            return Task.FromResult(new Reading(Waiting(team), null));
        });

        for (var tick = 0; tick < 5; tick++)
            window.Refresh();

        Assert.Equal(2, reads);
    }

    [Fact]
    public void Leaving_the_area_and_coming_back_reads_again()
    {
        var reads = 0;
        using var window = Open(read: team =>
        {
            reads++;
            return Task.FromResult(new Reading(Waiting(team), null));
        });
        window.Refresh();

        window.NewKeyDownEvent(Key.Esc);
        window.NewKeyDownEvent(new Key('w'));

        Assert.Equal(4, reads);
    }

    [Fact]
    public void The_arrows_move_between_cards_instead_of_between_agents()
    {
        using var window = Open();
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorDown);

        Assert.Equal(108, window.Work.Selected?.Number);
        Assert.All(window.Panes, pane => Assert.False(pane.HasFocus));
    }

    [Fact]
    public void The_arrows_reach_every_column_and_every_lane()
    {
        using var window = Open();
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);
        Assert.Equal(49, window.Work.Selected?.Number);
        Assert.Equal("Review · team0", window.Work.Region);

        window.NewKeyDownEvent(Key.CursorDown);
        Assert.Equal("Review · team1", window.Work.Region);

        window.NewKeyDownEvent(Key.CursorLeft);
        Assert.Equal(133, window.Work.Selected?.Number);
        Assert.Equal("Pitches · team1", window.Work.Region);
    }

    [Fact]
    public void Enter_opens_a_card_the_arrows_moved_to()
    {
        var opened = new List<string>();
        using var window = Open(openUrl: opened.Add);
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.Enter);

        Assert.Equal(["https://github.com/mentaldesk/team0/issues/49"], opened);
    }

    [Fact]
    public void The_work_area_fills_the_window_under_the_menu()
    {
        using var window = Open();
        window.Refresh();

        LayOut(window, 120, 30);

        Assert.Equal(new Rectangle(0, 0, window.Viewport.Width, 1), window.Menu.Frame);
        Assert.Equal(
            new Rectangle(0, 1, window.Viewport.Width, window.Viewport.Height - 1 - window.Message.Lines),
            window.Work.Frame);
    }

    [Fact]
    public void Every_menu_item_runs_the_command_its_id_names_on_the_key_that_command_has()
    {
        using var window = Open(area: Area.Dashboard);

        Assert.Equal(
            ["view.dashboard", "view.work", "settings", "quit", "team.pause", "help", "commands", "about"],
            window.MenuItems.Select(item => item.Id));
        Assert.All(window.MenuItems, item =>
        {
            var command = window.Commands.Registered.Single(registered => registered.Id == item.Id);
            Assert.Equal(command.Label, item.Item.Title);
            Assert.Equal(command.Key, item.Item.Key);
        });
    }

    [Fact]
    public void A_menu_item_and_its_key_do_the_same_thing()
    {
        using var window = Open(area: Area.Dashboard);

        window.MenuItems.Single(item => item.Id == "view.work").Item.Action!();
        Assert.Equal(Area.Work, window.CurrentArea);

        window.MenuItems.Single(item => item.Id == "view.dashboard").Item.Action!();
        Assert.Equal(Area.Dashboard, window.CurrentArea);
        Assert.True(window.NewKeyDownEvent(new Key('w')));
        Assert.Equal(Area.Work, window.CurrentArea);
    }

    [Fact]
    public void A_menu_item_says_what_its_command_would_do_now()
    {
        var teams = Path.Combine(Config, "teams");
        Directory.CreateDirectory(teams);
        File.WriteAllText(Path.Combine(teams, "team0.json"), "{\"dispatch\": {\"enabled\": false}}");
        using var window = Open(area: Area.Dashboard);

        window.Refresh();

        Assert.Equal("Resume team0", window.MenuItems.Single(item => item.Id == "team.pause").Item.Title);
    }

    /// <summary>Two gated items and an unranked Idea for the first team, one gated item for the second, so
    /// columns come back empty. One of the first team's is the Lead's move, so the filter has something to hide.</summary>
    private static string Waiting(string team) => team == "team0"
        ? """
          [{"number": 107, "title": "When the dashboard goes quiet", "status": "Pitched",
            "url": "https://github.com/mentaldesk/team0/issues/107", "team": "team0",
            "turn": "you", "reason": "awaiting your approval since 08:14"},
           {"number": 108, "title": "A misconfigured team looks like a working one", "status": "Pitched",
            "url": "https://github.com/mentaldesk/team0/issues/108", "team": "team0",
            "turn": "lead", "reason": "answering your feedback since 09:30"},
           {"number": 49, "title": "I can't change any of the keys", "status": "In review",
            "url": "https://github.com/mentaldesk/team0/issues/49", "team": "team0",
            "turn": "dev", "reason": "answering your feedback since 10:15",
            "pr": 122, "prUrl": "https://github.com/mentaldesk/team0/pull/122",
            "checks": "pass", "conflicting": false, "draft": false},
           {"number": 6, "title": "The agents can't say what they'd change", "status": "Idea",
            "url": "https://github.com/mentaldesk/team0/issues/6", "team": "team0",
            "turn": "you", "reason": "waiting to be ranked"}]
          """
        : """
          [{"number": 133, "title": "Notice when open files change on disk", "status": "Pitched",
            "url": "https://github.com/mentaldesk/team1/issues/133", "team": "team1",
            "turn": "you", "reason": "awaiting your approval since 21:37"}]
          """;

    private static IEnumerable<string> Titles(DashboardWindow window) =>
        window.Work.Lanes.SelectMany(lane => lane.Columns).Select(column => column.Title);

    private static void LayOut(DashboardWindow window, int width, int height)
    {
        window.Frame = new Rectangle(0, 0, width, height);
        window.Layout(new Size(width, height));
    }

    private DashboardWindow Open(
        Func<string, Task<Reading>>? read = null,
        Action<string>? openUrl = null,
        Area area = Area.Work)
    {
        Directory.CreateDirectory(_root);
        return new DashboardWindow(
            [("team0", "lead"), ("team0", "dev"), ("team1", "lead"), ("team1", "dev")],
            _root,
            new DashboardSettings(Config),
            new TeamConfigs(Config),
            (_, _) => Task.FromResult<string?>(null),
            read ?? (team => Task.FromResult(new Reading(Waiting(team), null))),
            openUrl ?? (_ => { }),
            area);
    }

    private string Config => Path.Combine(_root, "config");
}
