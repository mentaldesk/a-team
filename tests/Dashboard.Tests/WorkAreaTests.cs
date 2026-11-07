using MentalDesk.Tui.Chrome;
using MentalDesk.Tui;
using MentalDesk.Tui.Keys;
using MentalDesk.Tui.Links;
using MentalDesk.Tui.Loading;
using System.Drawing;
using System.Globalization;
using System.Text.Json.Nodes;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace ATeam.Dashboard.Tests;

/// <summary>The window with the Work area in front: what it reads, when, and what a key does there.</summary>
[Collection("StaticConfiguration")]
public partial class WorkAreaTests : IDisposable
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
        Assert.Equal(["\U0001F4A1Triage · 1", "◇ Pitches · 2", "Blocked · 0", "Review · 1", "\U0001F4A1Triage · 0", "◇ Pitches · 1", "Blocked · 0", "Review · 0"],
            Titles(window));
    }

    [Fact]
    public void Work_s_title_counts_every_card_and_reads_the_record_after_the_cards()
    {
        var order = new List<string>();
        using var window = Open(
            read: team =>
            {
                order.Add($"waiting {team}");
                return Task.FromResult(new Reading(Waiting(team), null));
            },
            readTrend: team =>
            {
                order.Add($"trend {team}");
                return Task.FromResult(new Reading(team == "team0"
                    ? """{"since": "2026-09-01T00:00:00Z", "weekAgo": 9, "accepted": 17}"""
                    : """{"since": "2026-09-20T00:00:00Z", "weekAgo": 3, "accepted": 6}""", null));
            },
            clock: new Clock());

        Assert.Equal("Work", window.WorkTitle);
        window.Refresh();
        window.Refresh();

        Assert.Equal(["waiting team0", "waiting team1", "trend team0", "trend team1"], order);
        Assert.Equal($"Work · {window.Work.Items.Count} waiting on you (12 a week ago) · 23 accepted in 7 days", window.WorkTitle);
    }

    [Fact]
    public void New_idea_is_on_Ctrl_N_in_the_palette_and_in_Cards()
    {
        using var window = Open(newIdea: (_, _, _, _) => null);

        Assert.Contains(window.Commands.Enabled, command => command is { Id: "work.new", Label: "New idea" });
        Assert.Equal(Key.N.WithCtrl, window.Commands.KeyFor("work.new"));
        Assert.Contains(window.MenuItems, item => item.Id == "work.new");
    }

    [Fact]
    public void New_idea_starts_on_the_lane_you_re_in()
    {
        var asked = new List<(IReadOnlyList<string> Teams, string Team)>();
        using var window = Open(newIdea: (teams, team, _, _) =>
        {
            asked.Add((teams, team));
            return null;
        });
        window.Refresh();
        LayOut(window, 120, 30);
        window.Work.Focus(window.Work.Items.Single(item => item.Number == 133), false);

        window.Press("Ctrl+N");

        Assert.Equal(["team0", "team1"], asked.Single().Teams);
        Assert.Equal("team1", asked.Single().Team);
    }

    [Fact]
    public void New_idea_off_Work_starts_on_the_first_team()
    {
        var asked = new List<string>();
        using var window = Open(area: Area.Overseer, newIdea: (_, team, _, _) =>
        {
            asked.Add(team);
            return null;
        });

        window.Commands.Execute("work.new");

        Assert.Equal(["team0"], asked);
    }

    [Fact]
    public void An_added_idea_is_read_straight_away_and_the_bar_says_where_it_went()
    {
        var reads = 0;
        using var window = Open(
            read: team =>
            {
                reads++;
                return Task.FromResult(new Reading(Waiting(team), null));
            },
            newIdea: (_, _, _, _) => ("team1", 412));
        window.Refresh();
        LayOut(window, 120, 30);
        var before = reads;

        window.Commands.Execute("work.new");
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal(before + 2, reads);
        Assert.Equal("#412 added to team1's ideas", window.Says());
    }

    [Fact]
    public void A_cancelled_idea_reads_nothing()
    {
        var reads = 0;
        using var window = Open(
            read: team =>
            {
                reads++;
                return Task.FromResult(new Reading(Waiting(team), null));
            },
            newIdea: (_, _, _, _) => null);
        window.Refresh();
        var before = reads;

        window.Commands.Execute("work.new");

        Assert.Equal(before, reads);
    }

    [Fact]
    public void Trends_is_in_the_palette_and_opens_with_each_team_s_cards_once_Work_has_read_them()
    {
        var opened = new List<IReadOnlyList<(string Team, int? Waiting)>>();
        using var window = Open(showTrends: opened.Add);

        Assert.Contains(window.Commands.Enabled, command => command is { Id: "trends", Label: "Trends" });
        window.Commands.Execute("trends");
        window.Refresh();
        window.Commands.Execute("trends");

        Assert.Equal([("team0", null), ("team1", null)], opened[0]);
        Assert.Equal(
            [("team0", window.Work.Items.Count(item => item.Team == "team0")), ("team1", window.Work.Items.Count(item => item.Team == "team1"))],
            opened[1]);
    }

    [Fact]
    public void Spending_is_in_the_palette_for_every_team_and_Enter_on_a_pitch_opens_its_reader()
    {
        IReadOnlyList<string>? teams = null;
        Action<string, SpendingItem>? open = null;
        WaitingItem? read = null;
        WaitingItem? shown = null;
        using var window = Open(
            showSpending: (names, opening) => (teams, open) = (names, opening),
            readBody: item =>
            {
                read = item;
                return Task.FromResult(new Reading("{\"body\": \"## Opportunity\"}", null));
            },
            showBody: (item, _, _, _, _, _, _) => shown = item);

        Assert.Contains(window.Commands.Enabled, command => command is { Id: "spending", Label: "Spending" });
        window.Commands.Execute("spending");
        open!("team1", new SpendingItem(81, "Which bets the money went on", "https://github.com/o/r/issues/81", "Done", true, null, null));
        window.Refresh();

        Assert.Equal(["team0", "team1"], teams);
        Assert.Equal(new WaitingItem(81, "Which bets the money went on", "Done", "https://github.com/o/r/issues/81", "team1", Pitch: true), read);
        Assert.Equal(81, shown?.Number);
    }

    [Fact]
    public void Decisions_is_in_the_palette_and_the_View_menu_and_opens_for_the_selected_team()
    {
        var opened = new List<string>();
        using var window = Open(showDecisions: opened.Add);
        window.Refresh();

        Assert.Contains(window.Commands.Enabled, command => command is { Id: "decisions", Label: "Decisions… · team0" });
        Assert.Contains("decisions", AppMenu.Layout.Single(menu => menu.Title == "_View").Ids);
        window.Commands.Execute("decisions");

        Assert.Equal(["team0"], opened);
    }

    [Fact]
    public void Decisions_isn_t_offered_without_a_team_selected()
    {
        using var window = Open(read: _ => Task.FromResult(new Reading("", "offline")), showDecisions: _ => { });
        window.Refresh();

        Assert.DoesNotContain(window.Commands.Enabled, command => command.Id == "decisions");
    }

    [Fact]
    public void A_record_that_can_t_be_read_leaves_the_title_with_what_s_waiting_now()
    {
        using var window = Open(readTrend: _ => Task.FromResult(new Reading("", "no record")));

        window.Refresh();
        window.Refresh();

        Assert.Equal($"Work · {window.Work.Items.Count} waiting on you", window.WorkTitle);
    }

    [Fact]
    public void Each_tab_is_loading_until_its_own_read_lands()
    {
        var finish = new Dictionary<string, TaskCompletionSource<Reading>> { ["team0"] = new(), ["team1"] = new() };
        using var window = Open(read: team => finish[team].Task);

        Assert.All(window.Work.Lanes, lane => Assert.Equal(LoadState.Loading, lane.Load.State));

        finish["team1"].SetResult(new Reading(Waiting("team1"), null));
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal([LoadState.Loading, LoadState.Loaded], window.Work.Lanes.Select(lane => lane.Load.State));
        Assert.All(window.Work.Items, item => Assert.Equal("team1", item.Team));
    }

    [Fact]
    public void One_team_failing_leaves_the_others_cards_and_says_so_only_in_its_own_tab()
    {
        using var window = Open(read: team => Task.FromResult(team == "team1"
            ? new Reading("", "gh: API rate limit exceeded for mentaldesk/TuiCode\nmore")
            : new Reading(SyncedAt(team, DateTimeOffset.UtcNow), null)));
        window.Refresh();
        LayOut(window, 120, 30);

        var (good, bad) = (window.Work.Lanes[0], window.Work.Lanes[1]);
        Assert.Equal(LoadState.Loaded, good.Load.State);
        Assert.True(good.Count > 0);
        Assert.Equal(LoadState.Failed, bad.Load.State);
        Assert.Equal("gh: API rate limit exceeded for mentaldesk/TuiCode", bad.Load.Says);
        Assert.Equal(0, window.Status.UnreadErrors);
        Assert.Equal("All items · synced just now", window.Status.FocusWord);

        window.Work.Pick("team1");
        window.Refresh();

        Assert.Equal("All items", window.Status.FocusWord);
    }

    [Fact]
    public void R_in_a_failed_tab_reads_only_that_team_again_and_its_cards_appear()
    {
        var reads = new List<string>();
        var failing = true;
        using var window = Open(read: team =>
        {
            reads.Add(team);
            return Task.FromResult(team == "team1" && failing ? new Reading("", "offline") : new Reading(Waiting(team), null));
        });
        window.Refresh();
        LayOut(window, 120, 30);
        window.Work.Pick("team1");
        Assert.Contains(window.Keys.Here!.Groups.SelectMany(group => group.Rows), row => row.Label == "Read this team again");
        reads.Clear();
        failing = false;

        window.NewKeyDownEvent(new Key('r'));
        window.Refresh();

        Assert.Equal(["team1"], reads);
        Assert.Equal(LoadState.Loaded, window.Work.Lanes[1].Load.State);
        Assert.Contains(window.Work.Items, item => item.Number == 133);
    }

    [Fact]
    public void R_reads_nothing_in_a_tab_that_isn_t_failed()
    {
        var reads = 0;
        using var window = Open(read: team =>
        {
            reads++;
            return Task.FromResult(new Reading(Waiting(team), null));
        });
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(new Key('r'));
        window.Refresh();

        Assert.Equal(2, reads);
        Assert.False(window.Commands.IsEnabled("work.retry"));
        Assert.DoesNotContain(window.Keys.Here!.Groups.SelectMany(group => group.Rows), row => row.Label == "Read this team again");
    }

    [Fact]
    public void F5_still_reads_every_team_from_a_failed_tab()
    {
        var reads = new List<string>();
        using var window = Open(read: team =>
        {
            reads.Add(team);
            return Task.FromResult(team == "team1" ? new Reading("", "offline") : new Reading(Waiting(team), null));
        });
        window.Refresh();
        window.Work.Pick("team1");
        reads.Clear();

        window.NewKeyDownEvent(Key.F5);

        Assert.Equal(["team0", "team1"], reads);
    }

    [Fact]
    public void A_team_with_nothing_waiting_says_so()
    {
        using var window = Open(read: team => Task.FromResult(new Reading(team == "team1" ? "[]" : Waiting(team), null)));
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal(LoadState.Empty, window.Work.Lanes[1].Load.State);
        Assert.Equal("Nothing waiting on you", window.Work.Lanes[1].Load.Says);
    }

    [Fact]
    public void The_old_Loading_box_and_the_van_are_gone_from_Work()
    {
        using var window = Open(read: _ => new TaskCompletionSource<Reading>().Task);

        LayOut(window, 120, 40);

        Assert.DoesNotContain(Descendants(window).OfType<Label>(), label => label.Text == "Loading…");
        Assert.Empty(Descendants(window).OfType<LoadingView>());
    }

    [Fact]
    public void A_later_read_keeps_the_cards_on_screen_with_a_spinner_on_the_tab()
    {
        var reads = 0;
        using var window = Open(read: team => team == "team0" && reads++ > 0
            ? new TaskCompletionSource<Reading>().Task
            : Task.FromResult(new Reading(Waiting(team), null)));
        window.Refresh();
        LayOut(window, 120, 30);

        window.Commands.Execute("work.refresh");

        var lane = window.Work.Lanes[0];
        Assert.Equal(LoadState.Loaded, lane.Load.State);
        Assert.True(lane.Load.Reloading);
        Assert.True(lane.Count > 0);
    }

    [Fact]
    public void A_later_read_leaves_the_selection_on_the_card_it_was_on_wherever_that_card_now_sits()
    {
        Func<string, string> page = Waiting;
        using var window = Open(read: team => Task.FromResult(new Reading(page(team), null)));
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);
        Assert.Equal(107, window.Work.Selected?.Number);

        page = team => ReadAgain(team, 6, 108, 107, 49);
        window.Commands.Execute("work.refresh");
        window.Refresh();

        Assert.Equal(107, window.Work.Selected?.Number);
    }

    [Fact]
    public void A_later_read_leaves_the_selection_on_the_PR_row_it_was_on()
    {
        Func<string, string> page = Waiting;
        using var window = Open(read: team => Task.FromResult(new Reading(page(team), null)));
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorDown);
        Assert.Equal("https://github.com/mentaldesk/team0/pull/122", window.Work.SelectedUrl);

        page = team => ReadAgain(team, 6, 107, 108, 49);
        window.Commands.Execute("work.refresh");
        window.Refresh();

        Assert.Equal("https://github.com/mentaldesk/team0/pull/122", window.Work.SelectedUrl);
    }

    [Fact]
    public void A_later_read_without_the_selected_card_hands_the_selection_to_the_next_card_down()
    {
        Func<string, string> page = Waiting;
        using var window = Open(read: team => Task.FromResult(new Reading(page(team), null)));
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);
        Assert.Equal(107, window.Work.Selected?.Number);

        page = team => ReadAgain(team, 6, 108, 49);
        window.Commands.Execute("work.refresh");
        window.Refresh();

        Assert.Equal(108, window.Work.Selected?.Number);
    }

    [Fact]
    public void Work_reads_again_by_itself_once_its_last_read_began_five_minutes_ago()
    {
        var clock = new Clock();
        var reads = 0;
        using var window = Open(read: team =>
        {
            reads++;
            return Task.FromResult(new Reading(Waiting(team), null));
        }, clock: clock);
        window.Refresh();

        clock.Now += DashboardWindow.ReadEvery - TimeSpan.FromSeconds(1);
        window.Refresh();
        Assert.Equal(2, reads);

        clock.Now += TimeSpan.FromSeconds(1);
        window.Refresh();
        Assert.Equal(4, reads);
    }

    [Fact]
    public void Refreshing_by_hand_puts_off_the_next_read_by_itself()
    {
        var clock = new Clock();
        var reads = 0;
        using var window = Open(read: team =>
        {
            reads++;
            return Task.FromResult(new Reading(Waiting(team), null));
        }, clock: clock);
        window.Refresh();
        clock.Now += TimeSpan.FromMinutes(4);
        window.Commands.Execute("work.refresh");
        window.Refresh();

        clock.Now += TimeSpan.FromMinutes(4);
        window.Refresh();

        Assert.Equal(4, reads);
    }

    [Fact]
    public void A_read_that_failed_is_tried_again_by_itself_five_minutes_later_not_every_second()
    {
        var clock = new Clock();
        var reads = 0;
        using var window = Open(read: _ =>
        {
            reads++;
            return Task.FromResult(new Reading("", "gh: API rate limit exceeded"));
        }, clock: clock);
        window.Refresh();

        clock.Now += TimeSpan.FromSeconds(1);
        window.Refresh();
        Assert.Equal(2, reads);
        Assert.All(window.Work.Lanes, lane => Assert.Equal(LoadState.Failed, lane.Load.State));

        clock.Now += DashboardWindow.ReadEvery;
        window.Refresh();
        Assert.Equal(4, reads);
    }

    [Fact]
    public void Work_reads_nothing_by_itself_while_Overseer_is_in_front()
    {
        var clock = new Clock();
        var reads = 0;
        using var window = Open(read: team =>
        {
            reads++;
            return Task.FromResult(new Reading(Waiting(team), null));
        }, area: Area.Overseer, clock: clock);

        clock.Now += DashboardWindow.ReadEvery * 2;
        window.Refresh();

        Assert.Equal(0, reads);
    }

    [Fact]
    public void Work_waits_to_read_by_itself_until_the_dialog_over_it_closes()
    {
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        var clock = new Clock();
        var reads = 0;
        using var window = Open(read: team =>
        {
            reads++;
            return Task.FromResult(new Reading(Waiting(team), null));
        }, clock: clock);
        app.Begin(window);
        window.Refresh();
        using var dialog = new Dialog();
        var over = app.Begin(dialog);

        clock.Now += DashboardWindow.ReadEvery;
        window.Refresh();
        Assert.Equal(2, reads);

        app.End(over!);
        window.Refresh();
        Assert.Equal(4, reads);
    }

    [Fact]
    public void Work_waits_to_read_by_itself_until_the_menu_over_it_closes()
    {
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        var clock = new Clock();
        var reads = 0;
        using var window = Open(read: team =>
        {
            reads++;
            return Task.FromResult(new Reading(Waiting(team), null));
        }, clock: clock);
        app.Begin(window);
        window.Refresh();
        window.NewKeyDownEvent(new Key('c').WithAlt);
        Assert.True(window.Menu.IsOpen());

        clock.Now += DashboardWindow.ReadEvery;
        window.Refresh();
        Assert.Equal(2, reads);

        window.Menus.Single(menu => menu.PopoverMenuOpen).PopoverMenu!.NewKeyDownEvent(Key.Esc);
        Assert.False(window.Menu.IsOpen());
        window.Refresh();
        Assert.Equal(4, reads);
    }

    [Fact]
    public void Back_from_a_try_Work_reads_by_itself_once_the_cards_it_brought_back_are_five_minutes_old()
    {
        var clock = new Clock();
        var reads = 0;
        var items = WaitingItem.Parse(Waiting("team0"));
        var tried = new TryHandover(items[2], true, items, clock.Now - TimeSpan.FromMinutes(4));
        using var window = Open(read: team =>
        {
            reads++;
            return Task.FromResult(new Reading(Waiting(team), null));
        }, resume: tried, clock: clock);
        window.Refresh();
        Assert.Equal(0, reads);

        clock.Now += TimeSpan.FromMinutes(1);
        window.Refresh();
        Assert.Equal(2, reads);
    }

    [Fact]
    public void Opening_on_Overseer_reads_nothing_of_Work_without_a_board_to_read()
    {
        var reads = 0;
        using var window = Open(area: Area.Overseer, read: _ =>
        {
            reads++;
            return new TaskCompletionSource<Reading>().Task;
        });

        Assert.Equal(0, reads);
        Assert.False(window.Work.Visible);
    }

    [Fact]
    public void Focus_starts_on_the_first_card_and_the_message_bar_says_whose_move_it_is()
    {
        using var window = Open();

        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal(6, window.Work.Selected?.Number);
        Assert.Equal("#6 · waiting to be ranked", window.Says());
    }

    [Fact]
    public void The_message_bar_follows_the_selection()
    {
        using var window = Open();
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.CursorRight);
        Assert.Equal("#107 · awaiting your approval since 08:14", window.Says());

        window.NewKeyDownEvent(Key.CursorDown);
        Assert.Equal("#108 · lead · answering your feedback since 09:30", window.Says());

        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);
        Assert.Equal("#49 · dev · answering your feedback since 10:15 · PR #122", window.Says());
    }

    [Fact]
    public void A_card_with_nothing_to_describe_names_the_region_instead()
    {
        using var window = Open(read: team => Task.FromResult(new Reading(team == "team0"
            ? """[{"number": 12, "title": "Whatever this is", "status": "Idea", "url": "https://github.com/x/12", "team": "team0"}]"""
            : "[]", null)));
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal(12, window.Work.Selected?.Number);
        Assert.Equal("Triage · team0", window.Says());
    }

    [Fact]
    public void m_hides_every_card_that_isn_t_yours_and_m_again_brings_them_back()
    {
        using var window = Open();
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.True(window.Press("Ctrl+T M"));
        LayOut(window, 120, 30);

        Assert.Equal(["\U0001F4A1Triage · 1", "◇ Pitches · 1", "Blocked · 0", "Review · 0", "\U0001F4A1Triage · 0", "◇ Pitches · 1", "Blocked · 0", "Review · 0"],
            Titles(window));
        Assert.Equal(6, window.Work.Selected?.Number);

        window.Press("Ctrl+T M");
        LayOut(window, 120, 30);

        Assert.Equal(["\U0001F4A1Triage · 1", "◇ Pitches · 2", "Blocked · 0", "Review · 1", "\U0001F4A1Triage · 0", "◇ Pitches · 1", "Blocked · 0", "Review · 0"],
            Titles(window));
    }

    [Fact]
    public void The_status_bar_starts_with_whether_the_filter_is_on_then_when_it_last_synced()
    {
        using var window = Open(read: team => Task.FromResult(new Reading(SyncedAt(team, DateTimeOffset.UtcNow), null)));
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal("All items · synced just now", window.Status.FocusWord);

        window.Press("Ctrl+T M");
        LayOut(window, 120, 30);

        Assert.Equal("My items · synced just now", window.Status.FocusWord);
    }

    [Fact]
    public void The_status_bar_shows_the_filter_and_stamp_then_the_selected_card_and_help_at_its_right()
    {
        using var window = Open(read: team => Task.FromResult(new Reading(SyncedAt(team, DateTimeOffset.UtcNow), null)));
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal("#6 · waiting to be ranked", window.Status.RestingText);
        Assert.Equal("All items · synced just now", window.Status.FocusWord);
        Assert.Equal("Press F1 for help", window.Status.Hints.Says);
        Assert.Equal(window.Status.FrameToScreen().Right - 1, window.Status.Hints.FrameToScreen().Right);
        Assert.DoesNotContain(window.Status.SubViews, view => view.Visible && view.Text.Contains("a-team "));
    }

    [Fact]
    public void A_message_takes_no_row_of_its_own_so_the_status_bar_stays_the_last_row()
    {
        using var window = Open();
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.NotEqual("", window.Says());
        Assert.Equal(window.Viewport.Height - 1, window.Status.Frame.Y);

        window.Press("Ctrl+G O");
        LayOut(window, 120, 30);

        Assert.Equal("", window.Says());
        Assert.Equal(window.Viewport.Height - 1, window.Status.Frame.Y);
    }

    [Fact]
    public void A_message_longer_than_the_room_left_is_cut_short_before_the_stamp()
    {
        using var window = Open(run: _ => Task.FromResult<string?>("board.sh: " + new string('x', 200)), chooseRank: (_, _, _) => Rank.High);
        window.Refresh();
        LayOut(window, 80, 30);

        window.Commands.Execute("work.priority");
        window.Refresh();
        window.Refresh();
        LayOut(window, 80, 30);

        Assert.StartsWith("setting #6 to High: board.sh: xxx", window.Says());
        Assert.Equal(79, window.Status.Hints.FrameToScreen().Right);
    }

    [Fact]
    public void Overseer_has_no_filter_to_report()
    {
        using var window = Open();
        window.Refresh();
        LayOut(window, 120, 30);

        window.Press("Ctrl+G O");

        Assert.Equal("Overseer", window.Status.FocusWord);
    }

    [Fact]
    public void Every_card_wears_the_icon_for_whose_move_it_is()
    {
        using var window = Open();
        window.Refresh();
        LayOut(window, 120, 30);

        var icons = window.Work.Lanes[0].Columns[1].CardIcons;

        Assert.Equal(IconStyle.Unicode, window.Work.Icons);
        Assert.Equal([LogSchemes.Success, LogSchemes.Dimmed], icons.Select(icon => icon.Scheme));
    }

    [Fact]
    public void A_terminal_that_bundles_a_Nerd_Font_draws_those_icons_on_the_first_run()
    {
        using var window = Open(auto: IconStyle.NerdFont);
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal(IconStyle.NerdFont, window.Work.Icons);
        Assert.False(File.Exists(Path.Combine(Config, "dashboard.json")));
    }

    private static MenuItem MenuItem(DashboardWindow window, string id) =>
        window.MenuItems.Single(item => item.Id == id).Item;

    private static string FilterTitle(DashboardWindow window) => MenuItem(window, "work.mine").Title;

    private DashboardWindow Open(
        Func<string, Task<Reading>>? read = null,
        Func<string, LinkOutcome>? openUrl = null,
        Func<string[], Task<string?>>? run = null,
        Func<WaitingItem, IssueBody, Rank?, Rank?>? chooseRank = null,
        Func<WaitingItem, Task<Reading>>? readBody = null,
        Action<WaitingItem, IssueBody, Func<LinkOutcome?>, Func<bool>?, ReaderCommand?, ReaderComment?, ReaderTry?>? showBody = null,
        Area area = Area.Work,
        IconStyle auto = IconStyle.Unicode,
        Action<Handover>? handOver = null,
        Handover? resume = null,
        Func<WaitingItem, Task<Reading>>? readConversation = null,
        Func<WaitingItem, bool>? confirmAccept = null,
        TimeProvider? clock = null,
        Func<WaitingItem, Task<Reading>>? readHistory = null,
        Func<string, Task<Reading>>? readTrend = null,
        Action<IReadOnlyList<(string Team, int? Waiting)>>? showTrends = null,
        Func<IReadOnlyList<string>, string, string, Attaching?, (string Team, int Number)?>? newIdea = null,
        Attention? attention = null,
        Action<ReaderEdit?>? showEdit = null,
        Func<WaitingItem, Task<Reading>>? readDocument = null,
        Func<Write?, IReadOnlyList<Write>, bool>? confirmQuit = null,
        IClipboard? clipboard = null,
        Action<ReaderIdea?, Func<Key, bool>>? showIdea = null,
        Action<IReadOnlyList<string>, Action<string, SpendingItem>>? showSpending = null,
        Action<string>? showDecisions = null,
        Func<WaitingItem, bool>? confirmApprove = null,
        Func<WaitingItem, string?>? askHold = null,
        Func<string, Task<Reading>>? syncBoard = null,
        Func<WaitingItem, IReadOnlyList<string>, string?>? askFix = null,
        Func<WaitingItem, Task<bool>, Func<string, Task<string?>>, bool>? giveSecret = null,
        GitHubSecrets? secrets = null)
    {
        Directory.CreateDirectory(_root);
        return new DashboardWindow(
            [("team0", "lead"), ("team0", "dev"), ("team1", "lead"), ("team1", "dev")],
            _root,
            new DashboardSettings(Config),
            new TeamConfigs(Config),
            run ?? (_ => Task.FromResult<string?>(null)),
            read ?? (team => Task.FromResult(new Reading(Waiting(team), null))),
            readBody ?? (_ => Task.FromResult(new Reading("{\"body\": \"\"}", null))),
            openUrl ?? Opening.Opened,
            (item, body, onGitHub, onApprove, accept, comment, tryIt, edit, _, idea, keys, rank) =>
            {
                showBody?.Invoke(item, body, onGitHub, onApprove, accept, comment, tryIt);
                showIdea?.Invoke(idea, keys);
                showEdit?.Invoke(edit);
                return chooseRank?.Invoke(item, body, rank);
            },
            area,
            auto,
            new DashboardOptions
            {
                HandOver = handOver,
                Resume = resume,
                ReadConversation = readConversation,
                ConfirmAccept = confirmAccept,
                Clock = clock,
                ReadHistory = readHistory,
                ReadTrend = readTrend,
                ShowTrends = showTrends,
                NewIdea = newIdea,
                Attention = attention,
                ReadDocument = readDocument,
                ConfirmQuit = confirmQuit,
                Clipboard = clipboard,
                ShowSpending = showSpending,
                ShowDecisions = showDecisions,
                ConfirmApprove = confirmApprove ?? (_ => true),
                AskHold = askHold,
                AskFix = askFix,
                GiveSecret = giveSecret,
                Secrets = secrets,
                SyncBoard = syncBoard,
            });
    }

    private void WriteRun(string team, string role, int pid)
    {
        var dir = Path.Combine(_root, team, role);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "pid"), pid.ToString());
        File.WriteAllText(Path.Combine(dir, "last-start"), "100");
    }

    /// <summary>A clock the test moves by hand.</summary>
    private sealed class Clock : TimeProvider
    {
        private readonly List<Timer> _timers = [];
        private DateTimeOffset _now = new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);

        /// <summary>Moving it fires every timer that has come due.</summary>
        internal DateTimeOffset Now
        {
            get => _now;
            set
            {
                _now = value;
                foreach (var timer in _timers.Where(timer => timer.Due <= _now).ToList())
                {
                    _timers.Remove(timer);
                    timer.Fire();
                }
            }
        }

        public override DateTimeOffset GetUtcNow() => Now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Timer(this, () => callback(state), _now + dueTime);
            _timers.Add(timer);
            return timer;
        }

        private sealed class Timer(Clock clock, Action fire, DateTimeOffset due) : ITimer
        {
            internal DateTimeOffset Due { get; private set; } = due;

            internal void Fire() => fire();

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                Due = clock._now + dueTime;
                return true;
            }

            public void Dispose() => clock._timers.Remove(this);

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    private string Config => Path.Combine(_root, "config");
}
