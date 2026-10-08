using System.Drawing;
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
        Assert.Equal(["\U0001F4A1Triage · 1", "◇ Pitches · 2", "Questions · 0", "Review · 1", "\U0001F4A1Triage · 0", "◇ Pitches · 1", "Questions · 0", "Review · 0"],
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
    public void A_record_that_can_t_be_read_leaves_the_title_with_what_s_waiting_now()
    {
        using var window = Open(readTrend: _ => Task.FromResult(new Reading("", "no record")));

        window.Refresh();
        window.Refresh();

        Assert.Equal($"Work · {window.Work.Items.Count} waiting on you", window.WorkTitle);
    }

    [Fact]
    public void Work_says_Loading_until_the_first_read_lands()
    {
        var finish = new TaskCompletionSource<Reading>();
        using var window = Open(read: _ => finish.Task);

        Assert.True(window.Loading.Visible);

        finish.SetResult(new Reading(Waiting("team0"), null));
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.False(window.Loading.Visible);
    }

    [Fact]
    public void Loading_is_a_still_untitled_frame_in_the_middle_of_the_Work_area_and_no_van()
    {
        using var window = Open(read: _ => new TaskCompletionSource<Reading>().Task);

        LayOut(window, 120, 40);

        var frame = window.Loading.Frame;
        Assert.Equal("", window.Loading.Title);
        Assert.Equal("Loading…", Assert.IsType<Label>(Assert.Single(window.Loading.SubViews)).Text);
        Assert.InRange(frame.X - (120 - frame.Right), -1, 1);
        Assert.InRange(frame.Y - 2 - (40 - 1 - frame.Bottom), -1, 1);
        Assert.Empty(Descendants(window).OfType<LoadingView>());
    }

    [Fact]
    public void A_later_read_leaves_the_cards_already_on_screen_rather_than_covering_them()
    {
        using var window = Open();
        window.Refresh();
        LayOut(window, 120, 30);

        window.Commands.Execute("work.refresh");

        Assert.False(window.Loading.Visible);
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
        Assert.Equal("gh: API rate limit exceeded", window.Message.Says);

        clock.Now += DashboardWindow.ReadEvery;
        window.Refresh();
        Assert.Equal(4, reads);
    }

    [Fact]
    public void The_Dashboard_reads_nothing_by_itself()
    {
        var clock = new Clock();
        var reads = 0;
        using var window = Open(read: team =>
        {
            reads++;
            return Task.FromResult(new Reading(Waiting(team), null));
        }, area: Area.Dashboard, clock: clock);

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
    public void Leaving_Work_while_it_is_still_reading_takes_Loading_with_it()
    {
        var finish = new TaskCompletionSource<Reading>();
        using var window = Open(read: _ => finish.Task);
        Assert.True(window.Loading.Visible);

        window.Commands.Execute("view.dashboard");

        Assert.False(window.Loading.Visible);
    }

    [Fact]
    public void Opening_on_the_Dashboard_reads_nothing_and_shows_no_Loading()
    {
        using var window = Open(area: Area.Dashboard, read: _ => new TaskCompletionSource<Reading>().Task);

        Assert.False(window.Loading.Visible);
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
        window.NewKeyDownEvent(Key.CursorRight);
        Assert.Equal("#49 · dev · answering your feedback since 10:15 · PR #122", window.Message.Says);
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
        Assert.Equal("Triage · team0", window.Message.Says);
    }

    [Fact]
    public void m_hides_every_card_that_isn_t_yours_and_m_again_brings_them_back()
    {
        using var window = Open();
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.True(window.NewKeyDownEvent(new Key('m')));
        LayOut(window, 120, 30);

        Assert.Equal(["\U0001F4A1Triage · 1", "◇ Pitches · 1", "Questions · 0", "Review · 0", "\U0001F4A1Triage · 0", "◇ Pitches · 1", "Questions · 0", "Review · 0"],
            Titles(window));
        Assert.Equal(6, window.Work.Selected?.Number);

        window.NewKeyDownEvent(new Key('m'));
        LayOut(window, 120, 30);

        Assert.Equal(["\U0001F4A1Triage · 1", "◇ Pitches · 2", "Questions · 0", "Review · 1", "\U0001F4A1Triage · 0", "◇ Pitches · 1", "Questions · 0", "Review · 0"],
            Titles(window));
    }

    [Fact]
    public void The_status_bar_says_when_it_last_read_and_whether_the_filter_is_on()
    {
        using var window = Open();
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal("read <1m ago · All items", window.Status.State.Text);
        Assert.Equal(window.Status.Viewport.Width, window.Status.State.Frame.Right);

        window.NewKeyDownEvent(new Key('m'));
        LayOut(window, 120, 30);

        Assert.Equal("read <1m ago · My items", window.Status.State.Text);
    }

    [Fact]
    public void The_status_bar_shows_the_selected_card_at_its_left_and_the_stamp_and_filter_at_its_right()
    {
        using var window = Open();
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal("#6 · waiting to be ranked", window.Message.Says);
        Assert.Equal(window.Message.Says, window.Message.Text);
        Assert.Equal(0, window.Message.Frame.X);
        Assert.Equal("read <1m ago · All items", window.Status.State.Text);
        Assert.Equal(window.Status.Viewport.Width, window.Status.State.Frame.Right);
        Assert.True(window.Message.Frame.Right < window.Status.State.Frame.X);
    }

    [Fact]
    public void A_message_takes_no_row_of_its_own_so_the_status_bar_stays_the_last_row()
    {
        using var window = Open();
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal(1, window.Message.Lines);
        Assert.Equal(window.Viewport.Height - 1, window.Status.Frame.Y);

        window.NewKeyDownEvent(new Key('d'));
        LayOut(window, 120, 30);

        Assert.Equal(0, window.Message.Lines);
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

        Assert.StartsWith("board.sh: xxx", window.Message.Says);
        Assert.Equal(80 - window.Status.State.Text.Length - 1, window.Message.Frame.Width);
        Assert.Equal(80, window.Status.State.Frame.Right);
    }

    [Fact]
    public void The_Dashboard_has_no_filter_to_report()
    {
        using var window = Open();
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(new Key('d'));

        Assert.Equal("", window.Status.State.Text);
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
        Assert.All(window.Panes, pane => Assert.InRange(char.ConvertToUtf32(pane.Title, 0), 0xF0001, 0xF1AF0));
        Assert.False(File.Exists(Path.Combine(Config, "dashboard.json")));
    }

    [Theory]
    [InlineData(IconStyle.NerdFont, IconStyle.Unicode)]
    [InlineData(IconStyle.Unicode, IconStyle.NerdFont)]
    public void A_style_of_my_own_is_what_the_app_draws_whatever_the_terminal_is(IconStyle stored, IconStyle auto)
    {
        new DashboardSettings(Config).WriteIcons(stored);

        using var window = Open(auto: auto);
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal(stored, window.Work.Icons);
    }

    [Fact]
    public void The_icon_style_stored_is_what_the_app_opens_with_next_time()
    {
        new DashboardSettings(Config).WriteIcons(IconStyle.NerdFont);

        using var window = Open();
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal(IconStyle.NerdFont, window.Work.Icons);
        Assert.Equal(
            [Icons.Glyph(Icon.YourMove, IconStyle.NerdFont), Icons.Glyph(Icon.TheirMove, IconStyle.NerdFont)],
            window.Work.Lanes[0].Columns[1].CardIcons.Select(icon => icon.Glyph));
        Assert.All(window.Panes, pane => Assert.InRange(char.ConvertToUtf32(pane.Title, 0), 0xF0001, 0xF1AF0));
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
        Assert.Equal(["\U0001F4A1Triage · 1", "◇ Pitches · 1", "Questions · 0", "Review · 0", "\U0001F4A1Triage · 0", "◇ Pitches · 1", "Questions · 0", "Review · 0"],
            Titles(reopened));
    }

    [Fact]
    public void g_opens_the_selected_cards_issue()
    {
        var opened = new List<string>();
        using var window = Open(openUrl: opened.Add);
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.CursorRight);
        Assert.True(window.NewKeyDownEvent(new Key('g')));

        Assert.Equal(["https://github.com/mentaldesk/team0/issues/107"], opened);
    }

    [Fact]
    public void g_on_the_row_under_a_card_opens_its_PR()
    {
        var opened = new List<string>();
        using var window = Open(openUrl: opened.Add);
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorDown);
        Assert.True(window.NewKeyDownEvent(new Key('g')));

        Assert.Equal(["https://github.com/mentaldesk/team0/pull/122"], opened);
        Assert.Equal(49, window.Work.Selected?.Number);
    }

    [Fact]
    public void An_Idea_has_no_row_under_it_and_hands_over_to_GitHub_on_g()
    {
        var opened = new List<string>();
        using var window = Open(openUrl: opened.Add);
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal("#6 · waiting to be ranked", window.Message.Says);

        Assert.True(window.NewKeyDownEvent(new Key('g')));
        Assert.Equal(["https://github.com/mentaldesk/team0/issues/6"], opened);

        window.NewKeyDownEvent(Key.CursorDown);
        Assert.Equal("Pitches · team1", window.Work.Region);
    }

    [Fact]
    public void p_is_no_longer_a_key_the_Work_area_answers()
    {
        var opened = new List<string>();
        using var window = Open(openUrl: opened.Add);
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(new Key('p'));
        window.Refresh();

        Assert.Empty(opened);
        Assert.DoesNotContain("work.pr", window.Commands.Registered.Select(command => command.Id));
        Assert.Equal("#107 · awaiting your approval since 08:14", window.Message.Says);
    }

    [Fact]
    public void p_sets_a_priority_on_a_card_in_any_column_and_on_no_PR_row()
    {
        using var window = Open();
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal(new Key('p'), window.Commands.KeyFor("work.priority"));
        Assert.True(window.Commands.IsEnabled("work.priority"));

        window.NewKeyDownEvent(Key.CursorRight);
        Assert.True(window.Commands.IsEnabled("work.priority"));

        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);
        Assert.True(window.Commands.IsEnabled("work.priority"));

        window.NewKeyDownEvent(Key.CursorDown);
        Assert.Equal(49, window.Work.Selected?.Number);
        Assert.False(window.Commands.IsEnabled("work.priority"));
    }

    [Fact]
    public void Enter_on_a_Triage_card_opens_the_reader_with_the_ranks_on_None_and_elsewhere_without_them()
    {
        var offered = new List<(int Number, Rank? Rank)>();
        using var window = Open(
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            chooseRank: (item, _, rank) =>
            {
                offered.Add((item.Number, rank));
                return null;
            });
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();

        Assert.Equal([(6, Rank.None), (107, null)], offered);
    }

    [Fact]
    public void Enter_on_a_question_opens_the_reader_without_the_ranks()
    {
        Rank? offered = Rank.Low;
        using var window = Open(
            read: team => Task.FromResult(new Reading(team == "team0" ? Question : "[]", null)),
            chooseRank: (_, _, rank) => offered = rank);
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();

        Assert.Null(offered);
    }

    [Fact]
    public void p_opens_the_same_reader_on_the_card_s_own_rank_with_its_conversation()
    {
        (Rank? Rank, IssueBody Body)? offered = null;
        var at = new DateTimeOffset(2026, 9, 29, 4, 31, 0, TimeSpan.Zero);
        using var window = Open(
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            readConversation: _ => Task.FromResult(new Reading(
                $$"""[{"who": "dev", "at": "{{at:O}}", "body": "Done."}]""", null)),
            chooseRank: (_, body, rank) =>
            {
                offered = (rank, body);
                return null;
            });
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);

        window.Commands.Execute("work.priority");
        window.Refresh();

        Assert.Equal(Rank.High, offered?.Rank);
        Assert.Equal(new IssueBody("## Opportunity").With(new Conversation([new Remark("dev", at, "Done.")])),
            offered?.Body);
    }

    [Fact]
    public void Setting_the_rank_a_card_already_has_writes_nothing()
    {
        var calls = new List<string[]>();
        using var window = Open(
            run: arguments =>
            {
                calls.Add(arguments);
                return Task.FromResult<string?>(null);
            },
            chooseRank: (_, _, rank) => rank);
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);

        window.Commands.Execute("work.priority");
        window.Refresh();
        window.Refresh();

        Assert.Empty(calls);
        Assert.Equal(107, window.Work.Selected?.Number);
    }

    [Fact]
    public void Enter_on_a_Triage_card_and_a_rank_set_moves_the_card_where_its_rank_puts_it()
    {
        var calls = new List<string[]>();
        using var window = Open(
            run: arguments =>
            {
                calls.Add(arguments);
                return Task.FromResult<string?>(null);
            },
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            chooseRank: (_, _, _) => Rank.Low);
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal([["board", "team0", "priority", "you", "6", "Low"]], calls);
        Assert.Equal("\U0001F4A1Triage · 0", window.Work.Lanes[0].Columns[0].Title);
        Assert.Equal("#6 · set to Low", window.Message.Says);
    }

    [Fact]
    public void Ranking_an_Idea_asks_the_board_to_set_it_and_says_so_while_it_runs()
    {
        var calls = new List<string[]>();
        var finish = new TaskCompletionSource<string?>();
        using var window = Open(run: arguments =>
        {
            calls.Add(arguments);
            return finish.Task;
        }, chooseRank: (_, _, _) => Rank.High);
        window.Refresh();
        LayOut(window, 120, 30);

        window.Commands.Execute("work.priority");
        Assert.Equal("Reading #6…", window.Message.Says);

        window.Refresh();

        Assert.Equal([["board", "team0", "priority", "you", "6", "High"]], calls);
        Assert.Equal("Setting…", window.Message.Says);
    }

    [Fact]
    public void A_ranked_Idea_leaves_its_column_at_once_and_the_selection_carries_on()
    {
        var reads = 0;
        using var window = Open(
            read: team =>
            {
                reads++;
                return Task.FromResult(new Reading(Waiting(team), null));
            },
            chooseRank: (_, _, _) => Rank.High);
        window.Refresh();
        LayOut(window, 120, 30);

        window.Commands.Execute("work.priority");
        window.Refresh();
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal(["\U0001F4A1Triage · 0", "◇ Pitches · 2", "Questions · 0", "Review · 1", "\U0001F4A1Triage · 0", "◇ Pitches · 1", "Questions · 0", "Review · 0"],
            Titles(window));
        Assert.Equal("#6 · set to High", window.Message.Says);
        Assert.Equal(2, reads);
    }

    [Fact]
    public void The_selection_carries_on_to_the_next_card_in_the_queue()
    {
        using var window = Open(
            read: team => Task.FromResult(new Reading(team == "team0" ? Queue : "[]", null)),
            chooseRank: (_, _, _) => Rank.High);
        window.Refresh();
        LayOut(window, 120, 30);
        Assert.Equal(6, window.Work.Selected?.Number);

        window.Commands.Execute("work.priority");
        window.Refresh();
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal(26, window.Work.Selected?.Number);
        Assert.Equal("\U0001F4A1Triage · 1", window.Work.Lanes[0].Columns[0].Title);
    }

    [Fact]
    public void The_message_goes_as_soon_as_the_selection_does()
    {
        using var window = Open(chooseRank: (_, _, _) => Rank.High);
        window.Refresh();
        LayOut(window, 120, 30);

        window.Commands.Execute("work.priority");
        window.Refresh();
        window.Refresh();
        Assert.Equal("#6 · set to High", window.Message.Says);

        window.NewKeyDownEvent(Key.CursorRight);
        Assert.Equal("#49 · dev · answering your feedback since 10:15 · PR #122", window.Message.Says);
    }

    [Fact]
    public void Clearing_a_rank_with_None_drops_the_card_into_Triage_and_the_selection_carries_on()
    {
        var calls = new List<string[]>();
        using var window = Open(
            run: arguments =>
            {
                calls.Add(arguments);
                return Task.FromResult<string?>(null);
            },
            chooseRank: (_, _, _) => Rank.None);
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);
        Assert.Equal(107, window.Work.Selected?.Number);

        window.Commands.Execute("work.priority");
        window.Refresh();
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal([["board", "team0", "priority", "you", "107", "none"]], calls);
        Assert.Equal(["\U0001F4A1Triage · 2", "◇ Pitches · 1", "Questions · 0", "Review · 1", "\U0001F4A1Triage · 0", "◇ Pitches · 1", "Questions · 0", "Review · 0"],
            Titles(window));
        Assert.Equal(108, window.Work.Selected?.Number);
        Assert.Equal("#107 · set to None", window.Message.Says);
    }

    [Fact]
    public void Ranking_a_pitch_that_carried_none_takes_it_out_of_Triage_into_Pitches()
    {
        var reads = 0;
        using var window = Open(
            read: team =>
            {
                reads++;
                return Task.FromResult(new Reading(team == "team0" ? Unranked : "[]", null));
            },
            chooseRank: (_, _, _) => Rank.High);
        window.Refresh();
        LayOut(window, 120, 30);
        Assert.Equal("Triage · team0", window.Work.Region);

        window.Commands.Execute("work.priority");
        window.Refresh();
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal(["\U0001F4A1Triage · 0", "◇ Pitches · 1", "Questions · 0", "Review · 0", "\U0001F4A1Triage · 0", "◇ Pitches · 0", "Questions · 0", "Review · 0"],
            Titles(window));
        Assert.Equal("#107 · set to High", window.Message.Says);
        Assert.Equal(2, reads);
    }

    [Fact]
    public void A_write_that_failed_says_so_and_leaves_the_cards_the_counts_and_the_stamp_as_they_were()
    {
        using var window = Open(
            run: _ => Task.FromResult<string?>("board.sh: API rate limit exceeded\nand a second line"),
            chooseRank: (_, _, _) => Rank.High);
        window.Refresh();
        LayOut(window, 120, 30);
        var stamp = window.Status.State.Text;

        window.Commands.Execute("work.priority");
        window.Refresh();
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal("board.sh: API rate limit exceeded", window.Message.Says);
        Assert.Equal(SchemeManager.SchemesToSchemeName(Schemes.Error), window.Message.SchemeName);
        Assert.True(window.Message.Frame.Right < window.Status.State.Frame.X);
        Assert.Equal(["\U0001F4A1Triage · 1", "◇ Pitches · 2", "Questions · 0", "Review · 1", "\U0001F4A1Triage · 0", "◇ Pitches · 1", "Questions · 0", "Review · 0"],
            Titles(window));
        Assert.Equal(stamp, window.Status.State.Text);
        Assert.Equal(6, window.Work.Selected?.Number);
    }

    [Fact]
    public void p_reads_the_item_before_the_dialog_opens_and_hands_it_the_body()
    {
        var read = new List<WaitingItem>();
        IssueBody? asked = null;
        using var window = Open(
            readBody: item =>
            {
                read.Add(item);
                return Task.FromResult(new Reading(Body, null));
            },
            chooseRank: (_, body, _) =>
            {
                asked = body;
                return null;
            });
        window.Refresh();
        LayOut(window, 120, 30);

        window.Commands.Execute("work.priority");
        Assert.Null(asked);

        window.Refresh();

        Assert.Equal([6], read.Select(item => item.Number));
        Assert.Equal(new IssueBody("## Opportunity"), asked);
    }

    [Fact]
    public void A_read_that_failed_still_opens_the_dialog_saying_what_went_wrong()
    {
        IssueBody? asked = null;
        using var window = Open(
            readBody: _ => Task.FromResult(new Reading("", "board.sh: can't read #6 (gh: Not Found (HTTP 404))")),
            chooseRank: (_, body, _) =>
            {
                asked = body;
                return null;
            });
        window.Refresh();
        LayOut(window, 120, 30);

        window.Commands.Execute("work.priority");
        window.Refresh();

        Assert.Equal(new IssueBody(Failure: "board.sh: can't read #6 (gh: Not Found (HTTP 404))"), asked);
    }

    [Fact]
    public void Enter_reads_the_selected_card_and_opens_the_reader_on_its_body()
    {
        var read = new List<WaitingItem>();
        (WaitingItem Item, IssueBody Body)? shown = null;
        using var window = Open(
            readBody: item =>
            {
                read.Add(item);
                return Task.FromResult(new Reading(Body, null));
            },
            showBody: (item, body, _, _, _, _, _) => shown = (item, body));
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.True(window.NewKeyDownEvent(Key.Enter));
        Assert.Null(shown);

        window.Refresh();

        Assert.Equal([6], read.Select(item => item.Number));
        Assert.Equal(6, shown?.Item.Number);
        Assert.Equal(new IssueBody("## Opportunity"), shown?.Body);
    }

    [Fact]
    public void Enter_shows_the_conversation_under_the_body()
    {
        IssueBody? shown = null;
        var at = new DateTimeOffset(2026, 9, 29, 4, 31, 0, TimeSpan.Zero);
        using var window = Open(
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            readConversation: _ => Task.FromResult(new Reading(
                $$"""[{"who": "dev", "at": "{{at:O}}", "body": "Done.", "pr": 239, "description": true}]""", null)),
            showBody: (_, body, _, _, _, _, _) => shown = body);
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.True(window.NewKeyDownEvent(Key.Enter));
        window.Refresh();

        Assert.Equal(new IssueBody("## Opportunity").With(new Conversation([new Remark("dev", at, "Done.", 239, true)])),
            shown);
    }

    [Fact]
    public void Enter_reads_the_card_s_history_for_the_reader_to_show_beside_the_body()
    {
        IssueBody? shown = null;
        var at = new DateTimeOffset(2026, 10, 3, 10, 41, 0, TimeSpan.Zero);
        using var window = Open(
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            readHistory: item => Task.FromResult(new Reading(
                $$"""{"since": "{{at:O}}", "events": [{"at": "{{at:O}}", "who": "lead", "what": "added as Ready #{{item.Number}}"}]}""",
                null)),
            showBody: (_, body, _, _, _, _, _) => shown = body);
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.True(window.NewKeyDownEvent(Key.Enter));
        window.Refresh();

        Assert.Equal("## Opportunity", shown?.Text);
        Assert.Equal([new HistoryEvent(at, "lead", "added as Ready #6")], shown?.History?.Events);
    }

    [Fact]
    public void A_history_that_wont_read_still_shows_the_body()
    {
        IssueBody? shown = null;
        using var window = Open(
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            readHistory: _ => Task.FromResult(new Reading("", "board.sh: can't read the record")),
            showBody: (_, body, _, _, _, _, _) => shown = body);
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.True(window.NewKeyDownEvent(Key.Enter));
        window.Refresh();

        Assert.Equal("## Opportunity", shown?.Text);
        Assert.Equal("board.sh: can't read the record", shown?.History?.Failure);
    }

    [Fact]
    public void A_question_reads_only_its_history()
    {
        var bodies = 0;
        IssueBody? shown = null;
        using var window = Open(
            read: _ => Task.FromResult(new Reading(Question, null)),
            readBody: _ =>
            {
                bodies++;
                return Task.FromResult(new Reading(Body, null));
            },
            readHistory: _ => Task.FromResult(new Reading("""{"since": null, "events": []}""", null)),
            showBody: (_, body, _, _, _, _, _) => shown = body);
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.True(window.NewKeyDownEvent(Key.Enter));
        window.Refresh();

        Assert.Equal(0, bodies);
        Assert.Equal("Which marker should it post?", shown?.Text);
        Assert.NotNull(shown?.History);
    }

    [Fact]
    public void A_conversation_that_wont_read_still_shows_the_body_with_the_reason_under_it()
    {
        IssueBody? shown = null;
        using var window = Open(
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            readConversation: _ => Task.FromResult(new Reading("", "board.sh: can't read the conversation on #6 (gh: HTTP 502)")),
            showBody: (_, body, _, _, _, _, _) => shown = body);
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.True(window.NewKeyDownEvent(Key.Enter));
        window.Refresh();

        Assert.Equal("## Opportunity\n\nboard.sh: can't read the conversation on #6 (gh: HTTP 502)", shown?.Text);
    }

    [Fact]
    public void Enter_on_a_question_opens_the_reader_on_the_Dev_s_question_with_nothing_read()
    {
        var read = new List<WaitingItem>();
        (WaitingItem Item, IssueBody Body)? shown = null;
        using var window = Open(
            read: _ => Task.FromResult(new Reading(Question, null)),
            readBody: item =>
            {
                read.Add(item);
                return Task.FromResult(new Reading(Body, null));
            },
            showBody: (item, body, _, _, _, _, _) => shown = (item, body));
        window.Refresh();
        LayOut(window, 120, 30);
        Assert.Equal("Questions · team0", window.Work.Region);
        Assert.Equal("#192 · asked you since 08:23", window.Work.Selected?.Line);

        Assert.True(window.NewKeyDownEvent(Key.Enter));
        window.Refresh();

        Assert.Empty(read);
        Assert.Equal(192, shown?.Item.Number);
        Assert.Equal(new IssueBody("Which marker should it post?"), shown?.Body);
    }

    [Fact]
    public void Enter_on_a_pitch_s_question_opens_the_reader_on_it_and_a_approves_the_pitch()
    {
        var calls = new List<string[]>();
        IssueBody? shown = null;
        using var window = Open(
            read: _ => Task.FromResult(new Reading(PitchQuestion, null)),
            run: arguments =>
            {
                calls.Add(arguments);
                return Task.FromResult<string?>(null);
            },
            showBody: (_, body, _, approve, _, _, _) =>
            {
                shown = body;
                approve!();
            });
        window.Refresh();
        LayOut(window, 120, 30);
        Assert.Equal("Questions · team0", window.Work.Region);

        Assert.True(window.NewKeyDownEvent(Key.Enter));
        window.Refresh();

        Assert.Equal(new IssueBody("## Needs your answer\n\n1. Which?"), shown);
        Assert.Equal(["board", "team0", "approve", "you", "257"], Assert.Single(calls));
    }

    [Fact]
    public void a_in_the_reader_approves_the_pitch_it_shows_and_the_card_leaves_with_no_re_read()
    {
        var calls = new List<string[]>();
        var reads = 0;
        using var window = Open(
            read: team =>
            {
                reads++;
                return Task.FromResult(new Reading(Waiting(team), null));
            },
            run: arguments =>
            {
                calls.Add(arguments);
                return Task.FromResult<string?>(null);
            },
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (_, _, _, onApprove, _, _, _) => onApprove!());
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);
        Assert.Equal(107, window.Work.Selected?.Number);
        var before = reads;

        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();
        Assert.Equal("Approving #107…", window.Message.Says);
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal([["board", "team0", "approve", "you", "107"]], calls);
        Assert.Equal("◇ Pitches · 1", window.Work.Lanes[0].Columns[1].Title);
        Assert.Equal(108, window.Work.Selected?.Number);
        Assert.Equal(before, reads);
        Assert.Equal("#107 approved", window.Message.Says);
    }

    [Fact]
    public void A_failed_approve_leaves_the_card_where_it_was_and_says_why_in_the_error_colour()
    {
        using var window = Open(
            run: _ => Task.FromResult<string?>("board.sh: only a Pitched pitch can be approved (#107 is in 'Approved')"),
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (_, _, _, onApprove, _, _, _) => onApprove!());
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);

        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();
        window.Refresh();

        Assert.Equal("◇ Pitches · 2", window.Work.Lanes[0].Columns[1].Title);
        Assert.Equal(107, window.Work.Selected?.Number);
        Assert.Equal("board.sh: only a Pitched pitch can be approved (#107 is in 'Approved')", window.Message.Says);
        Assert.Equal(SchemeManager.SchemesToSchemeName(Schemes.Error), window.Message.SchemeName);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 0)]
    public void The_reader_offers_no_approve_on_anything_but_a_Pitched_pitch(int right, int down)
    {
        var offered = new List<bool>();
        using var window = Open(
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (_, _, _, onApprove, _, _, _) => offered.Add(onApprove is not null));
        window.Refresh();
        LayOut(window, 120, 30);
        for (var i = 0; i < right; i++)
            window.NewKeyDownEvent(Key.CursorRight);
        for (var i = 0; i < down; i++)
            window.NewKeyDownEvent(Key.CursorDown);

        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();

        Assert.Equal([false], offered);
    }

    [Fact]
    public void A_Pitched_item_that_isn_t_a_pitch_offers_no_approve()
    {
        var offered = new List<bool>();
        using var window = Open(
            read: team => Task.FromResult(new Reading(team == "team0" ? Unranked : "[]", null)),
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (_, _, _, onApprove, _, _, _) => offered.Add(onApprove is not null));
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();

        Assert.Equal([false], offered);
    }

    [Fact]
    public void work_approve_is_registered_on_a_but_neither_bound_nor_hinted_on_the_board()
    {
        var calls = new List<string[]>();
        using var window = Open(run: arguments =>
        {
            calls.Add(arguments);
            return Task.FromResult<string?>(null);
        });
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);

        var approve = window.Commands.Registered.Single(command => command.Id == "work.approve");
        Assert.Equal("Approve the pitch you're reading", approve.Label);
        Assert.Equal(new Key('a'), approve.Key);
        Assert.False(window.Commands.IsEnabled("work.approve"));

        Assert.False(window.NewKeyDownEvent(new Key('a')));
        window.Refresh();

        Assert.Empty(calls);
        Assert.Equal("◇ Pitches · 2", window.Work.Lanes[0].Columns[1].Title);
    }

    [Fact]
    public void A_on_a_Review_task_asks_then_merges_its_PR_and_the_card_leaves_with_no_re_read()
    {
        var asked = new List<WaitingItem>();
        var calls = new List<string[]>();
        var reads = 0;
        using var window = Open(
            read: team =>
            {
                reads++;
                return Task.FromResult(new Reading(team == "team0" ? Review() : "[]", null));
            },
            run: arguments =>
            {
                calls.Add(arguments);
                return Task.FromResult<string?>(null);
            },
            confirmAccept: item =>
            {
                asked.Add(item);
                return true;
            });
        window.Refresh();
        LayOut(window, 120, 30);
        Assert.Equal(49, window.Work.Selected?.Number);
        var before = reads;

        Assert.True(window.NewKeyDownEvent(new Key('a')));
        Assert.Equal("Merging PR #122…", window.Message.Says);
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal([49], asked.Select(item => item.Number));
        Assert.Equal([["board", "team0", "accept", "you", "49"]], calls);
        Assert.Equal(["Review · 0"], Titles(window).Take(4).Where(title => title.StartsWith("Review")));
        Assert.Equal(before, reads);
        Assert.Equal("merged PR #122", window.Message.Says);
    }

    [Fact]
    public void The_Customer_lead_s_docs_PR_waits_in_Review_and_A_merges_it()
    {
        var calls = new List<string[]>();
        const string docs = """
            [{"number": 352, "title": "Docs: what's changed since 28 Sep", "status": "In review",
              "url": "https://github.com/mentaldesk/team0/pull/352", "team": "team0", "turn": "you",
              "reason": "awaiting your acceptance since 10:15", "role": "customer",
              "pr": 352, "prUrl": "https://github.com/mentaldesk/team0/pull/352", "base": "main", "unready": ""}]
            """;
        using var window = Open(
            read: team => Task.FromResult(new Reading(team == "team0" ? docs : "[]", null)),
            run: arguments =>
            {
                calls.Add(arguments);
                return Task.FromResult<string?>(null);
            },
            confirmAccept: _ => true);
        window.Refresh();
        LayOut(window, 120, 30);
        Assert.Equal("Review · 1", window.Work.Lanes[0].Columns[3].Title);
        Assert.Equal(352, window.Work.Selected?.Number);

        window.NewKeyDownEvent(new Key('a'));
        window.Refresh();

        Assert.Equal([["board", "team0", "accept", "you", "352"]], calls);
        Assert.Equal("merged PR #352", window.Message.Says);
    }

    [Fact]
    public void The_Customer_lead_s_docs_proposal_waits_in_Pitches_though_it_has_no_rank()
    {
        const string proposal = """
            [{"number": 360, "title": "Docs proposal: where TuiCode's user docs live", "status": "Pitched",
              "url": "https://github.com/mentaldesk/team0/pull/360", "team": "team0", "turn": "you", "role": "customer"}]
            """;
        using var window = Open(read: team => Task.FromResult(new Reading(team == "team0" ? proposal : "[]", null)));
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal(["\U0001F4A1Triage · 0", "◇ Pitches · 1"], Titles(window).Take(2));
        Assert.Equal(360, window.Work.Selected?.Number);
    }

    [Fact]
    public void Cancelling_the_confirmation_merges_nothing()
    {
        var calls = new List<string[]>();
        using var window = Open(
            read: team => Task.FromResult(new Reading(team == "team0" ? Review() : "[]", null)),
            run: arguments =>
            {
                calls.Add(arguments);
                return Task.FromResult<string?>(null);
            },
            confirmAccept: _ => false);
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(new Key('a'));
        window.Refresh();

        Assert.Empty(calls);
        Assert.Equal(49, window.Work.Selected?.Number);
    }

    [Fact]
    public void A_failed_merge_leaves_the_card_in_Review_and_says_why_in_the_error_colour()
    {
        using var window = Open(
            read: team => Task.FromResult(new Reading(team == "team0" ? Review() : "[]", null)),
            run: _ => Task.FromResult<string?>("board.sh: can't merge PR #122 (gh: Pull Request is not mergeable (HTTP 405))"),
            confirmAccept: _ => true);
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(new Key('a'));
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Contains("Review · 1", Titles(window));
        Assert.Equal(49, window.Work.Selected?.Number);
        Assert.Equal("board.sh: can't merge PR #122 (gh: Pull Request is not mergeable (HTTP 405))", window.Message.Says);
        Assert.Equal(SchemeManager.SchemesToSchemeName(Schemes.Error), window.Message.SchemeName);
    }

    [Theory]
    [InlineData("CI failing", 122, "#49 CI failing")]
    [InlineData("conflicts with main", 122, "#49 conflicts with main")]
    [InlineData("CI running", 122, "#49 CI running")]
    [InlineData("still a draft", 122, "#49 still a draft")]
    [InlineData("", 0, "#49 no PR to merge")]
    public void A_task_the_Dev_is_still_fixing_is_no_card_so_A_merges_nothing(string trouble, int pr, string says)
    {
        var asked = 0;
        var calls = new List<string[]>();
        using var window = Open(
            read: team => Task.FromResult(new Reading(team == "team0" ? Review(trouble, pr) : "[]", null)),
            run: arguments =>
            {
                calls.Add(arguments);
                return Task.FromResult<string?>(null);
            },
            confirmAccept: _ => ++asked > 0);
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(new Key('a'));
        window.Refresh();

        Assert.Null(window.Work.Selected);
        Assert.Equal(0, asked);
        Assert.Empty(calls);
        Assert.Contains("Review · 0", Titles(window));
        Assert.Equal($"1 with the Dev: {says}", window.Work.Lanes[0].Columns[3].Summary);
    }

    [Fact]
    public void A_on_a_validated_pitch_asks_then_closes_it_and_the_card_leaves()
    {
        var asked = new List<WaitingItem>();
        var calls = new List<string[]>();
        using var window = Open(
            read: team => Task.FromResult(new Reading(team == "team0" ? ReviewPitch() : "[]", null)),
            run: arguments =>
            {
                calls.Add(arguments);
                return Task.FromResult<string?>(null);
            },
            confirmAccept: item =>
            {
                asked.Add(item);
                return true;
            });
        window.Refresh();
        LayOut(window, 120, 30);
        Assert.Equal(174, window.Work.Selected?.Number);

        Assert.True(window.NewKeyDownEvent(new Key('a')));
        Assert.Equal("Closing #174…", window.Message.Says);
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal([(174, 3)], asked.Select(item => (item.Number, item.Tasks)));
        Assert.Equal([["board", "team0", "accept", "you", "174"]], calls);
        Assert.DoesNotContain("Review · 1", Titles(window));
        Assert.Equal("accepted #174", window.Message.Says);
    }

    [Theory]
    [InlineData(2, "#174 has 2 open tasks")]
    [InlineData(1, "#174 has 1 open task")]
    public void A_pitch_with_open_tasks_is_no_card_so_A_closes_nothing(int open, string says)
    {
        var asked = 0;
        var calls = new List<string[]>();
        using var window = Open(
            read: team => Task.FromResult(new Reading(team == "team0" ? ReviewPitch(open) : "[]", null)),
            run: arguments =>
            {
                calls.Add(arguments);
                return Task.FromResult<string?>(null);
            },
            confirmAccept: _ => ++asked > 0);
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(new Key('a'));
        window.Refresh();

        Assert.Equal(0, asked);
        Assert.Empty(calls);
        Assert.Equal($"1 with the Dev: {says}", window.Work.Lanes[0].Columns[3].Summary);
    }

    [Fact]
    public void A_pitch_that_wont_close_stays_in_Review_and_says_why()
    {
        using var window = Open(
            read: team => Task.FromResult(new Reading(team == "team0" ? ReviewPitch() : "[]", null)),
            run: _ => Task.FromResult<string?>("board.sh: can't close #174 (gh: Resource not accessible (HTTP 403))"),
            confirmAccept: _ => true);
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(new Key('a'));
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Contains("Review · 1", Titles(window));
        Assert.Equal(174, window.Work.Selected?.Number);
        Assert.Equal("board.sh: can't close #174 (gh: Resource not accessible (HTTP 403))", window.Message.Says);
        Assert.Equal(SchemeManager.SchemesToSchemeName(Schemes.Error), window.Message.SchemeName);
    }

    [Fact]
    public void work_accept_is_on_A_in_the_Cards_menu_and_enabled_only_on_a_Review_task()
    {
        using var window = Open();
        window.Refresh();
        LayOut(window, 120, 30);

        var accept = window.Commands.Registered.Single(command => command.Id == "work.accept");
        Assert.Equal("Accept", accept.Label);
        Assert.Equal(new Key('a'), accept.Key);
        Assert.True(accept.OnCard);
        Assert.Equal(6, window.Work.Selected?.Number);
        Assert.False(window.Commands.IsEnabled("work.accept"));
        window.NewKeyDownEvent(Key.CursorRight);
        Assert.False(window.Commands.IsEnabled("work.accept"));
        window.NewKeyDownEvent(Key.CursorRight);
        Assert.Equal(49, window.Work.Selected?.Number);
        Assert.True(window.Commands.IsEnabled("work.accept"));
    }

    [Fact]
    public void Accept_can_be_rebound_and_the_reader_offers_it_on_the_new_key()
    {
        Directory.CreateDirectory(_root);
        new DashboardSettings(Config).WriteKeys([("work.accept", new Key('z'))]);
        var calls = new List<string[]>();
        ReaderCommand? offered = null;
        using var window = Open(
            read: team => Task.FromResult(new Reading(team == "team0" ? Review() : "[]", null)),
            run: arguments =>
            {
                calls.Add(arguments);
                return Task.FromResult<string?>(null);
            },
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (_, _, _, _, accept, _, _) => offered = accept,
            confirmAccept: _ => true);
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.False(window.NewKeyDownEvent(new Key('a')));
        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();

        Assert.Equal(new Key('z'), offered?.Key);
        Assert.Equal("accept", offered?.Hint);
        Assert.True(offered?.Enabled);
        Assert.Empty(calls);

        window.NewKeyDownEvent(new Key('z'));

        Assert.Equal([["board", "team0", "accept", "you", "49"]], calls);
    }

    [Fact]
    public void Accepting_from_the_reader_merges_the_task_it_shows_and_the_reader_closes()
    {
        var calls = new List<string[]>();
        var closes = new List<bool>();
        using var window = Open(
            read: team => Task.FromResult(new Reading(team == "team0" ? Review() : "[]", null)),
            run: arguments =>
            {
                calls.Add(arguments);
                return Task.FromResult<string?>(null);
            },
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (_, _, _, _, accept, _, _) => closes.Add(accept!.Run()),
            confirmAccept: _ => true);
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();
        window.Refresh();

        Assert.Equal([true], closes);
        Assert.Equal([["board", "team0", "accept", "you", "49"]], calls);
        Assert.Equal("merged PR #122", window.Message.Says);
    }

    [Fact]
    public void The_reader_offers_no_accept_on_a_pitch()
    {
        var offered = new List<bool>();
        using var window = Open(
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (_, _, _, _, accept, _, _) => offered.Add(accept is not null));
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);

        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();

        Assert.Equal([false], offered);
    }

    [Fact]
    public void work_comment_is_registered_on_c()
    {
        using var window = Open();

        var comment = window.Commands.Registered.Single(command => command.Id == "work.comment");
        Assert.Equal("Comment on the item you're reading", comment.Label);
        Assert.Equal(new Key('c'), comment.Key);
        Assert.False(window.Commands.IsEnabled("work.comment"));
    }

    [Fact]
    public void Commenting_from_the_reader_posts_your_words_as_you_on_the_item_it_shows()
    {
        var calls = new List<string[]>();
        var bodies = new List<string>();
        var asked = new List<int>();
        var posted = new List<Remark?>();
        ReaderComment? offered = null;
        using var window = Open(
            run: arguments =>
            {
                calls.Add(arguments);
                bodies.Add(File.ReadAllText(arguments[^1]));
                return Task.FromResult<string?>(null);
            },
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (_, _, _, _, _, comment, _) =>
            {
                offered = comment;
                posted.Add(comment!.Run());
            },
            askComment: (item, post) =>
            {
                asked.Add(item.Number);
                return post("Not yet: shelve it.").Result is null ? "Not yet: shelve it." : null;
            });
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();

        Assert.Equal(new Key('c'), offered?.Key);
        Assert.Equal("comment", offered?.Hint);
        var number = asked.Single();
        Assert.Equal(("you", "Not yet: shelve it."), (posted.Single()?.Who, posted.Single()?.Body));
        Assert.Equal(["board", "team0", "comment", "you", number.ToString()], calls.Single()[..5]);
        Assert.Equal(["Not yet: shelve it."], bodies);
        Assert.False(File.Exists(calls.Single()[^1]));
    }

    [Fact]
    public void A_comment_that_fails_to_post_says_why_to_the_dialog()
    {
        string? said = "";
        using var window = Open(
            run: _ => Task.FromResult<string?>("gh: HTTP 502"),
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (_, _, _, _, _, comment, _) => comment!.Run(),
            askComment: (_, post) =>
            {
                said = post("Shelve it.").Result;
                return null;
            });
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();

        Assert.Equal("gh: HTTP 502", said);
    }

    [Fact]
    public void o_in_the_reader_opens_what_o_on_the_board_would_have()
    {
        var opened = new List<string>();
        using var window = Open(
            openUrl: opened.Add,
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (_, _, onGitHub, _, _, _, _) => onGitHub());
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorDown);
        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();

        Assert.Equal(["https://github.com/mentaldesk/team0/pull/122"], opened);
    }

    [Fact]
    public void Closing_the_reader_leaves_the_same_card_selected_and_the_board_unread()
    {
        var reads = 0;
        using var window = Open(
            read: team =>
            {
                reads++;
                return Task.FromResult(new Reading(Waiting(team), null));
            },
            readBody: _ => Task.FromResult(new Reading(Body, null)));
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);
        var selected = window.Work.Selected;
        var region = window.Work.Region;
        var before = reads;

        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();

        Assert.Same(selected, window.Work.Selected);
        Assert.Equal(region, window.Work.Region);
        Assert.Equal(before, reads);
    }

    [Fact]
    public void A_read_that_failed_opens_no_reader_and_says_why_in_the_error_colour()
    {
        var shown = false;
        using var window = Open(
            readBody: _ => Task.FromResult(new Reading("", "board.sh: can't read #6 (gh: Not Found (HTTP 404))")),
            showBody: (_, _, _, _, _, _, _) => shown = true);
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);

        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();

        Assert.False(shown);
        Assert.Equal("board.sh: can't read #6 (gh: Not Found (HTTP 404))", window.Message.Says);
        Assert.Equal(SchemeManager.SchemesToSchemeName(Schemes.Error), window.Message.SchemeName);
    }

    [Fact]
    public void A_card_with_no_body_opens_no_reader_and_says_so_in_the_error_colour()
    {
        var shown = false;
        using var window = Open(showBody: (_, _, _, _, _, _, _) => shown = true);
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);

        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();

        Assert.False(shown);
        Assert.Equal("#107 has no description", window.Message.Says);
        Assert.Equal(SchemeManager.SchemesToSchemeName(Schemes.Error), window.Message.SchemeName);
    }

    [Fact]
    public void Reading_and_opening_on_GitHub_are_commands_of_their_own()
    {
        using var window = Open();

        Assert.Equal(
            [("work.read", "Open", Key.Enter), ("work.github", "Open on GitHub", new Key('g'))],
            window.Commands.Registered
                .Where(command => command.Id is "work.read" or "work.github")
                .Select(command => (command.Id, command.Label, command.Key)));
    }

    [Fact]
    public void A_second_p_while_the_first_is_still_reading_is_refused_not_queued()
    {
        var reads = 0;
        var finish = new TaskCompletionSource<Reading>();
        using var window = Open(readBody: _ =>
        {
            reads++;
            return finish.Task;
        });
        window.Refresh();
        LayOut(window, 120, 30);

        window.Commands.Execute("work.priority");
        window.Refresh();
        window.Commands.Execute("work.priority");

        Assert.Equal(1, reads);
    }

    [Fact]
    public void Cancelling_the_dialog_asks_the_board_for_nothing()
    {
        var calls = 0;
        using var window = Open(run: _ =>
        {
            calls++;
            return Task.FromResult<string?>(null);
        });
        window.Refresh();
        LayOut(window, 120, 30);

        window.Commands.Execute("work.priority");
        window.Refresh();

        Assert.Equal(0, calls);
        Assert.Equal("#6 · waiting to be ranked", window.Message.Says);
    }

    [Fact]
    public void The_d_key_goes_back_to_the_Dashboard_and_the_agents_are_there_again()
    {
        using var window = Open();
        window.Refresh();

        Assert.True(window.NewKeyDownEvent(new Key('d')));

        Assert.Equal(Area.Dashboard, window.CurrentArea);
        Assert.True(window.Agents.Visible);
        Assert.True(window.Dispatcher.Visible);
        Assert.False(window.Work.Visible);
    }

    [Fact]
    public void The_area_you_were_in_is_what_the_app_opens_in_next_time()
    {
        using var window = Open();

        window.NewKeyDownEvent(new Key('d'));

        Assert.Equal(Area.Dashboard, new DashboardSettings(Config).ReadArea());
        Assert.True(window.NewKeyDownEvent(new Key('w')));
        Assert.Equal(Area.Work, new DashboardSettings(Config).ReadArea());
    }

    [Fact]
    public void The_status_bar_says_when_it_last_read()
    {
        var now = DateTimeOffset.UtcNow;
        using var window = Open();
        Assert.Equal("All items", window.Status.State.Text);

        window.Refresh();

        Assert.Equal("read <1m ago · All items", window.Status.State.Text);
        Assert.Equal("read 2m ago", DashboardWindow.Stamped(now, now.AddMinutes(2)));
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
        var stamp = window.Status.State.Text;

        failing = true;
        window.NewKeyDownEvent(Key.F5);
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal("a-team board: API rate limit exceeded", window.Message.Says);
        Assert.Equal(["\U0001F4A1Triage · 1", "◇ Pitches · 2", "Questions · 0", "Review · 1", "\U0001F4A1Triage · 0", "◇ Pitches · 1", "Questions · 0", "Review · 0"],
            Titles(window));
        Assert.Equal(stamp, window.Status.State.Text);
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

        window.NewKeyDownEvent(Key.F5);
        Assert.Equal("Reading…", window.Message.Says);
        window.NewKeyDownEvent(Key.F5);

        Assert.Equal(2, reads);
        finish.SetResult(new Reading("[]", null));
        window.Refresh();
        window.NewKeyDownEvent(Key.F5);
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
    public void Coming_back_with_nothing_changed_reads_nothing_and_keeps_the_card()
    {
        var clock = new Clock();
        var reads = 0;
        using var window = Open(read: team =>
        {
            reads++;
            return Task.FromResult(new Reading(Waiting(team), null));
        }, clock: clock);
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);
        Assert.Equal(107, window.Work.Selected?.Number);

        window.NewKeyDownEvent(new Key('d'));
        clock.Now += DashboardWindow.ReadEvery - TimeSpan.FromSeconds(1);
        window.Refresh();
        window.NewKeyDownEvent(new Key('w'));

        Assert.Equal(2, reads);
        Assert.Equal(107, window.Work.Selected?.Number);
        Assert.NotNull(window.Work.SelectedCard);
        Assert.StartsWith("read 4m ago", window.Status.State.Text);
    }

    [Fact]
    public void Coming_back_keeps_the_PR_row()
    {
        using var window = Open();
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorDown);
        Assert.Equal("https://github.com/mentaldesk/team0/pull/122", window.Work.SelectedUrl);

        window.NewKeyDownEvent(new Key('d'));
        window.NewKeyDownEvent(new Key('w'));

        Assert.Equal("https://github.com/mentaldesk/team0/pull/122", window.Work.SelectedUrl);
    }

    [Fact]
    public void Coming_back_after_a_run_started_reads_again_and_keeps_the_card()
    {
        var reads = 0;
        using var window = Open(read: team =>
        {
            reads++;
            return Task.FromResult(new Reading(Waiting(team), null));
        });
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);

        window.NewKeyDownEvent(new Key('d'));
        WriteRun("team1", "lead", Environment.ProcessId);
        window.NewKeyDownEvent(new Key('w'));
        window.Refresh();

        Assert.Equal(4, reads);
        Assert.Equal(107, window.Work.Selected?.Number);
    }

    [Fact]
    public void Coming_back_after_a_run_finished_reads_again_and_keeps_the_card()
    {
        WriteRun("team0", "dev", Environment.ProcessId);
        var reads = 0;
        using var window = Open(read: team =>
        {
            reads++;
            return Task.FromResult(new Reading(Waiting(team), null));
        });
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);

        window.NewKeyDownEvent(new Key('d'));
        WriteRun("team0", "dev", 999999);
        window.NewKeyDownEvent(new Key('w'));
        window.Refresh();

        Assert.Equal(4, reads);
        Assert.Equal(107, window.Work.Selected?.Number);
    }

    [Fact]
    public void Coming_back_five_minutes_after_the_last_read_reads_again_and_keeps_the_card()
    {
        var clock = new Clock();
        var reads = 0;
        using var window = Open(read: team =>
        {
            reads++;
            return Task.FromResult(new Reading(Waiting(team), null));
        }, clock: clock);
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);

        window.NewKeyDownEvent(new Key('d'));
        clock.Now += DashboardWindow.ReadEvery;
        window.NewKeyDownEvent(new Key('w'));
        window.Refresh();

        Assert.Equal(4, reads);
        Assert.Equal(107, window.Work.Selected?.Number);
        Assert.StartsWith("read <1m ago", window.Status.State.Text);
    }

    [Fact]
    public void Coming_back_after_a_read_that_failed_reads_again()
    {
        var reads = 0;
        using var window = Open(read: _ =>
        {
            reads++;
            return Task.FromResult(new Reading("", "gh: API rate limit exceeded"));
        });
        window.Refresh();

        window.NewKeyDownEvent(new Key('d'));
        window.NewKeyDownEvent(new Key('w'));

        Assert.Equal(4, reads);
    }

    [Fact]
    public void A_read_that_moves_the_card_to_another_column_takes_the_selection_with_it()
    {
        var waiting = Waiting("team0");
        using var window = Open(read: team => Task.FromResult(new Reading(team == "team0" ? waiting : Waiting(team), null)));
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);
        Assert.Equal(107, window.Work.Selected?.Number);

        waiting = waiting.Replace("\"priority\": \"High\", ", "");
        window.Commands.Execute("work.refresh");
        window.Refresh();

        Assert.Equal(107, window.Work.Selected?.Number);
        Assert.Equal("Triage · team0", window.Work.Region);
    }

    [Fact]
    public void F5_reads_now()
    {
        var reads = 0;
        using var window = Open(read: team =>
        {
            reads++;
            return Task.FromResult(new Reading(Waiting(team), null));
        });
        window.Refresh();

        window.NewKeyDownEvent(Key.F5);
        window.Refresh();

        Assert.Equal(4, reads);
    }

    [Fact]
    public void Starting_in_Work_reads_and_lands_on_the_first_card()
    {
        var reads = 0;
        using var window = Open(read: team =>
        {
            reads++;
            return Task.FromResult(new Reading(Waiting(team), null));
        });
        window.Refresh();

        Assert.Equal(2, reads);
        Assert.Equal(6, window.Work.Selected?.Number);
    }

    [Fact]
    public void Esc_in_Work_does_nothing_at_all()
    {
        var reads = 0;
        using var window = Open(read: team =>
        {
            reads++;
            return Task.FromResult(new Reading(Waiting(team), null));
        });
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);

        Assert.False(window.NewKeyDownEvent(Key.Esc));

        Assert.Equal(Area.Work, window.CurrentArea);
        Assert.Equal(107, window.Work.Selected?.Number);
        Assert.Equal(2, reads);
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
    public void The_arrows_reach_every_column_and_every_lane_that_has_a_card()
    {
        using var window = Open();
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);
        Assert.Equal(49, window.Work.Selected?.Number);
        Assert.Equal("Review · team0", window.Work.Region);

        window.NewKeyDownEvent(Key.CursorDown);
        Assert.Equal("Review · team0", window.Work.Region);

        window.NewKeyDownEvent(Key.CursorDown);
        Assert.Equal(133, window.Work.Selected?.Number);
        Assert.Equal("Pitches · team1", window.Work.Region);

        window.NewKeyDownEvent(Key.CursorUp);
        Assert.Equal(108, window.Work.Selected?.Number);
        Assert.Equal("Pitches · team0", window.Work.Region);
    }

    [Fact]
    public void o_opens_a_card_the_arrows_moved_to()
    {
        var opened = new List<string>();
        using var window = Open(openUrl: opened.Add);
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(new Key('g'));

        Assert.Equal(["https://github.com/mentaldesk/team0/issues/49"], opened);
    }

    [Fact]
    public void The_work_area_fills_the_window_under_the_menu_and_its_title()
    {
        using var window = Open();
        window.Refresh();

        LayOut(window, 120, 30);

        Assert.Equal(1, window.Message.Lines);
        Assert.Equal(new Rectangle(0, 0, window.Viewport.Width, 1), window.Menu.Frame);
        Assert.Equal(new Rectangle(0, 2, window.Viewport.Width, window.Viewport.Height - 3), window.Work.Frame);
    }

    [Fact]
    public void Every_menu_item_runs_the_command_its_id_names_on_the_key_that_command_has()
    {
        using var window = Open(area: Area.Dashboard);

        Assert.Equal(
            [
                "view.dashboard", "view.work", "settings", "quit", "work.read", "work.priority", "work.try",
                "work.github", "work.accept", "work.refresh", "work.mine", "agent.hold", "agent.interrupt",
                "dispatch.pass", "agent.expand", "log.toolCalls", "agent.collapse", "log.copyLines", "log.copyAll", "log.editor", "help", "guide", "commands", "about",
            ],
            window.MenuItems.Select(item => item.Id));
        Assert.All(window.MenuItems, item =>
        {
            var command = window.Commands.Registered.Single(registered => registered.Id == item.Id);
            Assert.Equal(command.MenuLabel, item.Item.Title.Replace("_", ""));
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
    public void work_try_is_on_t_and_acts_on_the_card()
    {
        using var window = Open();

        var command = window.Commands.Registered.Single(registered => registered.Id == "work.try");

        Assert.Equal(new Key('t'), command.Key);
        Assert.True(command.OnCard);
    }

    [Fact]
    public void work_try_is_disabled_until_a_card_with_a_PR_is_selected()
    {
        using var window = Open();

        Assert.False(window.Commands.IsEnabled("work.try"));

        window.Refresh();
        LayOut(window, 120, 30);
        Assert.Equal(6, window.Work.Selected?.Number);
        Assert.False(window.Commands.IsEnabled("work.try"));

        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);
        Assert.Equal(49, window.Work.Selected?.Number);
        Assert.True(window.Commands.IsEnabled("work.try"));

        window.NewKeyDownEvent(Key.CursorDown);
        Assert.Null(window.Work.SelectedCard);
        Assert.True(window.Commands.IsEnabled("work.try"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void t_hands_the_terminal_to_try_for_the_cards_team_and_PR_from_either_of_its_rows(bool onPr)
    {
        var handed = new List<Handover>();
        using var window = Open(handOver: handed.Add);
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);
        if (onPr)
            window.NewKeyDownEvent(Key.CursorDown);

        Assert.True(window.NewKeyDownEvent(new Key('t')));

        var handover = Assert.IsType<TryHandover>(Assert.Single(handed));
        Assert.Equal(["try", "team0", "122"], handover.Arguments);
        Assert.Equal(49, handover.Item.Number);
        Assert.Equal(onPr, handover.OnPr);
        Assert.Equal(window.Work.Items, handover.Items);
    }

    [Fact]
    public void t_on_a_card_with_no_PR_does_nothing_and_its_Cards_item_is_greyed_out()
    {
        var handed = new List<Handover>();
        using var window = Open(handOver: handed.Add);
        window.Refresh();
        LayOut(window, 120, 30);
        window.Menus.Single(menu => menu.Title == AppMenu.Cards).PopoverMenu!.Enabled = true;
        var item = window.MenuItems.Single(entry => entry.Id == "work.try").Item;

        Assert.False(window.NewKeyDownEvent(new Key('t')));
        window.Refresh();

        Assert.Empty(handed);
        Assert.False(item.Enabled);

        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);
        window.Refresh();
        Assert.True(item.Enabled);
    }

    [Fact]
    public void Picking_Try_from_Cards_hands_over_as_t_would()
    {
        var handed = new List<Handover>();
        using var window = Open(handOver: handed.Add);
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);

        window.MenuItems.Single(entry => entry.Id == "work.try").Item.Action!();

        Assert.Equal(["try", "team0", "122"], Assert.Single(handed).Arguments);
    }

    [Fact]
    public void t_on_the_Dashboard_shows_tool_calls_and_hands_nothing_over()
    {
        var handed = new List<Handover>();
        using var window = Open(handOver: handed.Add, area: Area.Dashboard);
        window.NewKeyDownEvent(Key.Tab);

        Assert.True(window.NewKeyDownEvent(new Key('t')));

        Assert.Empty(handed);
        Assert.True(window.Panes[0].Expanded);
    }

    [Fact]
    public void The_card_commands_stay_on_the_selected_card_while_the_menu_has_focus()
    {
        using var window = Open();
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);

        // An open menu is a popover outside the window, so the window loses focus to it.
        window.HasFocus = false;

        Assert.Equal(49, window.Work.Selected?.Number);
        Assert.All(["work.read", "work.priority", "work.try", "work.github"], id => Assert.True(window.Commands.IsEnabled(id), id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Back_from_a_try_the_same_row_is_selected_with_the_same_cards_and_no_re_read(bool onPr)
    {
        var handed = new List<Handover>();
        using var first = Open(handOver: handed.Add);
        first.Refresh();
        LayOut(first, 120, 30);
        first.NewKeyDownEvent(Key.CursorRight);
        first.NewKeyDownEvent(Key.CursorRight);
        first.NewKeyDownEvent(Key.CursorRight);
        if (onPr)
            first.NewKeyDownEvent(Key.CursorDown);
        first.NewKeyDownEvent(new Key('t'));
        var titles = Titles(first).ToList();
        var reads = 0;

        using var back = Open(read: _ =>
        {
            reads++;
            return Task.FromResult(new Reading("[]", null));
        }, resume: handed.Single());
        back.Refresh();
        LayOut(back, 120, 30);
        back.FocusResumed();

        Assert.Equal(0, reads);
        Assert.Equal(titles, Titles(back));
        Assert.Equal(49, back.Work.Selected?.Number);
        Assert.Equal(onPr, back.Work.SelectedCard is null);
        Assert.False(back.Loading.Visible);
    }

    [Fact]
    public void A_try_that_failed_says_so_in_the_error_colour_once_you_re_back()
    {
        var handed = new List<Handover>();
        using var first = Open(handOver: handed.Add);
        first.Refresh();
        LayOut(first, 120, 30);
        first.NewKeyDownEvent(Key.CursorRight);
        first.NewKeyDownEvent(Key.CursorRight);
        first.NewKeyDownEvent(Key.CursorRight);
        first.NewKeyDownEvent(new Key('t'));

        using var back = Open(resume: handed.Single() with { Failure = "try team0 122 exited 1" });
        back.Refresh();
        LayOut(back, 120, 30);
        back.FocusResumed();

        Assert.Equal("try team0 122 exited 1", back.Message.Says);
        Assert.Equal(SchemeManager.SchemesToSchemeName(Schemes.Error), back.Message.SchemeName);
    }

    [Fact]
    public void t_in_the_reader_hands_over_to_try_with_the_team_PR_and_where_the_reader_was()
    {
        var handed = new List<Handover>();
        var shown = new IssueBody("## Opportunity");
        using var window = Open(
            handOver: handed.Add,
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (_, _, _, _, _, _, tryIt) => tryIt!.Run(shown, 7));
        OpenTheTaskInReview(window);

        var handover = Assert.IsType<TryHandover>(Assert.Single(handed));
        Assert.Equal(["try", "team0", "122"], handover.Arguments);
        Assert.Equal(49, handover.Item.Number);
        Assert.Equal(new ReaderPlace(shown, "https://github.com/mentaldesk/team0/issues/49", 7), handover.Reader);
    }

    [Fact]
    public void Back_from_a_try_in_the_reader_it_reopens_on_that_item_where_it_was_with_no_read()
    {
        var handed = new List<Handover>();
        var shown = new IssueBody("## Opportunity");
        using var first = Open(
            handOver: handed.Add,
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (_, _, _, _, _, _, tryIt) => tryIt!.Run(shown, 7));
        OpenTheTaskInReview(first);
        var reads = 0;
        var reopened = new List<(WaitingItem Item, IssueBody Body, ReaderTry? Try)>();

        using var back = Open(
            read: _ =>
            {
                reads++;
                return Task.FromResult(new Reading("[]", null));
            },
            readBody: _ =>
            {
                reads++;
                return Task.FromResult(new Reading(Body, null));
            },
            showBody: (item, body, _, _, _, _, tryIt) => reopened.Add((item, body, tryIt)),
            resume: handed.Single());
        back.Refresh();
        LayOut(back, 120, 30);
        back.FocusResumed();
        back.ReopenReader();

        Assert.Equal(0, reads);
        var (item, body, again) = Assert.Single(reopened);
        Assert.Equal(49, item.Number);
        Assert.Same(shown, body);
        Assert.Equal(7, again?.Top);
        Assert.Null(again?.Failure);
        Assert.Equal(49, back.Work.Selected?.Number);
        Assert.NotNull(back.Work.SelectedCard);
    }

    [Fact]
    public void A_try_from_the_reader_that_failed_says_so_in_the_reopened_reader()
    {
        var handed = new List<Handover>();
        using var first = Open(
            handOver: handed.Add,
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (_, body, _, _, _, _, tryIt) => tryIt!.Run(body, 0));
        OpenTheTaskInReview(first);
        ReaderTry? reopened = null;

        using var back = Open(
            showBody: (_, _, _, _, _, _, tryIt) => reopened = tryIt,
            resume: handed.Single() with { Failure = "try team0 122 exited 1" });
        back.Refresh();
        LayOut(back, 120, 30);
        back.FocusResumed();
        back.ReopenReader();

        Assert.Equal("try team0 122 exited 1", reopened?.Failure);
        Assert.NotEqual("try team0 122 exited 1", back.Message.Says);
    }

    [Fact]
    public void The_reader_offers_no_try_on_an_item_with_no_PR()
    {
        var offered = new List<bool>();
        using var window = Open(
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (_, _, _, _, _, _, tryIt) => offered.Add(tryIt is not null));
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();

        Assert.Equal([false], offered);
    }

    [Fact]
    public void Try_can_be_rebound_and_the_card_and_the_reader_share_the_new_key()
    {
        Directory.CreateDirectory(_root);
        new DashboardSettings(Config).WriteKeys([("work.try", new Key('y'))]);
        var handed = new List<Handover>();
        ReaderTry? offered = null;
        using var window = Open(
            handOver: handed.Add,
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (_, _, _, _, _, _, tryIt) => offered = tryIt);
        OpenTheTaskInReview(window);

        Assert.Equal(new Key('y'), offered?.Key);
        Assert.Empty(handed);

        Assert.True(window.NewKeyDownEvent(new Key('y')));

        Assert.Equal(["try", "team0", "122"], Assert.Single(handed).Arguments);
    }

    [Fact]
    public void t_on_a_validated_pitch_hands_over_to_try_the_team_s_default_branch()
    {
        var handed = new List<Handover>();
        using var window = Open(
            read: team => Task.FromResult(new Reading(team == "team0" ? ReviewPitch() : "[]", null)),
            handOver: handed.Add);
        window.Refresh();
        LayOut(window, 120, 30);
        Assert.Equal(174, window.Work.Selected?.Number);

        Assert.True(window.NewKeyDownEvent(new Key('t')));

        var handover = Assert.IsType<TryHandover>(Assert.Single(handed));
        Assert.Equal(["try", "team0"], handover.Arguments);
        Assert.Equal(174, handover.Item.Number);
        Assert.Null(handover.Reader);
    }

    [Fact]
    public void t_in_a_validated_pitch_s_reader_hands_over_to_try_the_default_branch_and_reopens_it_after()
    {
        var handed = new List<Handover>();
        var shown = new IssueBody("## Opportunity");
        using var first = Open(
            read: team => Task.FromResult(new Reading(team == "team0" ? ReviewPitch() : "[]", null)),
            handOver: handed.Add,
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (_, _, _, _, _, _, tryIt) => tryIt!.Run(shown, 5));
        first.Refresh();
        LayOut(first, 120, 30);
        first.NewKeyDownEvent(Key.Enter);
        first.Refresh();
        var handover = Assert.IsType<TryHandover>(Assert.Single(handed));
        Assert.Equal(["try", "team0"], handover.Arguments);
        var reopened = new List<(WaitingItem Item, IssueBody Body, ReaderTry? Try)>();

        using var back = Open(
            showBody: (item, body, _, _, _, _, tryIt) => reopened.Add((item, body, tryIt)),
            resume: handover with { Failure = "try team0 exited 1" });
        back.Refresh();
        LayOut(back, 120, 30);
        back.FocusResumed();
        back.ReopenReader();

        var (item, body, again) = Assert.Single(reopened);
        Assert.Equal(174, item.Number);
        Assert.Same(shown, body);
        Assert.Equal(5, again?.Top);
        Assert.Equal("try team0 exited 1", again?.Failure);
    }

    [Fact]
    public void A_try_from_a_validated_pitch_s_card_that_failed_says_so_on_the_message_line()
    {
        var handed = new List<Handover>();
        using var first = Open(
            read: team => Task.FromResult(new Reading(team == "team0" ? ReviewPitch() : "[]", null)),
            handOver: handed.Add);
        first.Refresh();
        LayOut(first, 120, 30);
        first.NewKeyDownEvent(new Key('t'));

        using var back = Open(resume: handed.Single() with { Failure = "try team0 exited 1" });
        back.Refresh();
        LayOut(back, 120, 30);
        back.FocusResumed();

        Assert.Equal("try team0 exited 1", back.Message.Says);
        Assert.Equal(SchemeManager.SchemesToSchemeName(Schemes.Error), back.Message.SchemeName);
    }

    [Theory]
    [InlineData("Idea", true, "")]
    [InlineData("Pitched", true, "")]
    [InlineData("Pitched", true, "High")]
    [InlineData("Idea", false, "")]
    public void A_pitch_short_of_Review_or_an_Idea_has_no_try_on_its_card_or_in_its_reader(
        string status, bool pitch, string priority)
    {
        var handed = new List<Handover>();
        var offered = new List<bool>();
        using var window = Open(
            read: team => Task.FromResult(new Reading(team == "team0" ? Unvalidated(status, pitch, priority) : "[]", null)),
            handOver: handed.Add,
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (_, _, _, _, _, _, tryIt) => offered.Add(tryIt is not null));
        window.Refresh();
        LayOut(window, 120, 30);
        Assert.Equal(174, window.Work.Selected?.Number);

        Assert.False(window.Commands.IsEnabled("work.try"));
        window.NewKeyDownEvent(new Key('t'));
        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();

        Assert.Empty(handed);
        Assert.Equal([false], offered);
    }

    private static void OpenTheTaskInReview(DashboardWindow window)
    {
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();
    }

    /// <summary>Two gated items and an unranked Idea for the first team, one gated item for the second, so
    /// columns come back empty. One of the first team's is the Lead's move, so the filter has something to hide.</summary>
    private static string Waiting(string team) => team == "team0"
        ? """
          [{"number": 107, "title": "When the dashboard goes quiet", "status": "Pitched",
            "url": "https://github.com/mentaldesk/team0/issues/107", "team": "team0",
            "turn": "you", "reason": "awaiting your approval since 08:14", "priority": "High", "pitch": true},
           {"number": 108, "title": "A misconfigured team looks like a working one", "status": "Pitched",
            "url": "https://github.com/mentaldesk/team0/issues/108", "team": "team0",
            "turn": "lead", "reason": "answering your feedback since 09:30", "priority": "Medium", "pitch": true},
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
            "turn": "you", "reason": "awaiting your approval since 21:37", "priority": "Low", "pitch": true}]
          """;

    /// <summary>One task In review and nothing else, its PR in <paramref name="unready"/> trouble where there's any.</summary>
    private static string Review(string unready = "", int pr = 122) =>
        $$"""
          [{"number": 49, "title": "I can't change any of the keys", "status": "In review",
            "url": "https://github.com/mentaldesk/team0/issues/49", "team": "team0",
            "turn": "you", "reason": "awaiting your acceptance since 10:15",
            "pr": {{pr}}, "prUrl": "https://github.com/mentaldesk/team0/pull/{{pr}}", "base": "main",
            "unready": "{{unready}}"}]
          """;

    private static string ReviewPitch(int open = 0) =>
        $$"""
          [{"number": 174, "title": "Accepting finished work", "status": "In review",
            "url": "https://github.com/mentaldesk/team0/issues/174", "team": "team0", "pitch": true,
            "turn": "you", "reason": "awaiting your acceptance since 10:15", "tasks": 3, "openTasks": {{open}}}]
          """;

    private static string Unvalidated(string status, bool pitch, string priority) =>
        $$"""
          [{"number": 174, "title": "Accepting finished work", "status": "{{status}}",
            "url": "https://github.com/mentaldesk/team0/issues/174", "team": "team0", "pitch": {{(pitch ? "true" : "false")}},
            "turn": "you", "reason": "waiting", "priority": "{{priority}}"}]
          """;

    /// <summary><see cref="Waiting"/> read again later, every reason moved on and team0's cards in
    /// <paramref name="order"/>, any it leaves out gone.</summary>
    private static string ReadAgain(string team, params int[] order)
    {
        var cards = JsonNode.Parse(Waiting(team))!.AsArray();
        if (team == "team0")
            cards = [.. order.Select(n => cards.Single(card => card!["number"]!.GetValue<int>() == n)!.DeepClone())];
        foreach (var card in cards)
            card!["reason"] = $"{card["reason"]}, and later";
        return cards.ToJsonString();
    }

    /// <summary>A pitch carrying no Priority, which waits in Triage until it's ranked.</summary>
    private const string Unranked =
        """
        [{"number": 107, "title": "When the dashboard goes quiet", "status": "Pitched",
          "url": "https://github.com/mentaldesk/team0/issues/107", "team": "team0",
          "turn": "you", "reason": "awaiting your approval since 08:14"}]
        """;

    /// <summary>Two Ideas of one team's own, so ranking the first leaves the selection somewhere to go.</summary>
    private const string Queue =
        """
        [{"number": 6, "title": "The agents can't say what they'd change", "status": "Idea",
          "url": "https://github.com/mentaldesk/team0/issues/6", "team": "team0",
          "turn": "you", "reason": "waiting to be ranked"},
         {"number": 26, "title": "A pitch I've shelved", "status": "Idea",
          "url": "https://github.com/mentaldesk/team0/issues/26", "team": "team0",
          "turn": "you", "reason": "waiting to be ranked"}]
        """;

    /// <summary>A task the Dev handed back with a question, and nothing else.</summary>
    private const string Question =
        """
        [{"number": 192, "title": "I can reply to a pitch", "status": "Ready",
          "url": "https://github.com/mentaldesk/team0/issues/192", "team": "team0",
          "turn": "you", "reason": "asked you since 08:23", "question": "Which marker should it post?"}]
        """;

    /// <summary>A pitch the Lead needs an answer on, and nothing else.</summary>
    private const string PitchQuestion =
        """
        [{"number": 257, "title": "I wait on the team", "status": "Pitched", "pitch": true,
          "url": "https://github.com/mentaldesk/team0/issues/257", "team": "team0", "priority": "High",
          "turn": "you", "reason": "asked you since 08:00", "question": "## Needs your answer\n\n1. Which?"}]
        """;

    private const string Body = """{"number": 6, "title": "t", "body": "## Opportunity"}""";

    private static IEnumerable<View> Descendants(View view) =>
        view.SubViews.SelectMany(child => Descendants(child).Prepend(child));

    private static IEnumerable<string> Titles(DashboardWindow window) =>
        window.Work.Lanes.SelectMany(lane => lane.Columns).Select(column => column.Title);

    private static void LayOut(DashboardWindow window, int width, int height)
    {
        window.Frame = new Rectangle(0, 0, width, height);
        window.Layout(new Size(width, height));
    }

    [Fact]
    public void F5_reads_what_s_waiting_again_and_r_no_longer_does()
    {
        var reads = 0;
        using var window = Open(read: team =>
        {
            reads++;
            return Task.FromResult(new Reading(Waiting(team), null));
        });
        window.Refresh();

        Assert.False(window.NewKeyDownEvent(new Key('r')));
        Assert.Equal(2, reads);

        Assert.True(window.NewKeyDownEvent(Key.F5));
        Assert.Equal(4, reads);
    }

    [Fact]
    public void A_rebound_refresh_keeps_its_key_and_the_menu_shows_it()
    {
        var reads = 0;
        Directory.CreateDirectory(Config);
        new DashboardSettings(Config).WriteKeys([("work.refresh", new Key('r'))]);
        using var window = Open(read: team =>
        {
            reads++;
            return Task.FromResult(new Reading(Waiting(team), null));
        });
        window.Refresh();

        Assert.True(window.NewKeyDownEvent(new Key('r')));

        Assert.Equal(4, reads);
        Assert.Equal(new Key('r'), MenuItem(window, "work.refresh").Key);
    }

    [Fact]
    public void Refresh_in_the_menu_reads_what_s_waiting_again()
    {
        var reads = 0;
        using var window = Open(read: team =>
        {
            reads++;
            return Task.FromResult(new Reading(Waiting(team), null));
        });
        window.Refresh();

        MenuItem(window, "work.refresh").Action!();

        Assert.Equal(4, reads);
    }

    [Fact]
    public void The_menu_offers_whichever_filter_is_not_in_effect_whether_switched_from_the_menu_or_with_m()
    {
        using var window = Open();
        window.Refresh();
        Assert.Equal("Show only _mine", FilterTitle(window));

        MenuItem(window, "work.mine").Action!();
        Assert.True(window.Work.OnlyMine);
        Assert.Equal("_Show all", FilterTitle(window));

        window.NewKeyDownEvent(new Key('m'));
        Assert.False(window.Work.OnlyMine);
        Assert.Equal("Show only _mine", FilterTitle(window));
    }

    [Fact]
    public void The_Commands_palette_shows_F5_for_refresh_and_Help_leaves_it_to_the_menu()
    {
        using var window = Open();

        using var help = new HelpDialog(window.Commands.Registered);
        using var palette = new CommandsDialog(window.Commands.Registered);

        Assert.DoesNotContain("F5", help.Keys.Text);
        Assert.Contains(
            palette.List.Source!.ToList().Cast<string>(),
            row => row.StartsWith("Read what's waiting again") && row.TrimEnd().EndsWith(" F5"));
    }

    private static MenuItem MenuItem(DashboardWindow window, string id) =>
        window.MenuItems.Single(item => item.Id == id).Item;

    private static string FilterTitle(DashboardWindow window) => MenuItem(window, "work.mine").Title;

    private DashboardWindow Open(
        Func<string, Task<Reading>>? read = null,
        Action<string>? openUrl = null,
        Func<string[], Task<string?>>? run = null,
        Func<WaitingItem, IssueBody, Rank?, Rank?>? chooseRank = null,
        Func<WaitingItem, Task<Reading>>? readBody = null,
        Action<WaitingItem, IssueBody, Action, Action?, ReaderCommand?, ReaderComment?, ReaderTry?>? showBody = null,
        Area area = Area.Work,
        IconStyle auto = IconStyle.Unicode,
        Action<Handover>? handOver = null,
        Handover? resume = null,
        Func<WaitingItem, Task<Reading>>? readConversation = null,
        Func<WaitingItem, bool>? confirmAccept = null,
        TimeProvider? clock = null,
        Func<WaitingItem, Func<string, Task<string?>>, string?>? askComment = null,
        Func<WaitingItem, Task<Reading>>? readHistory = null,
        Func<string, Task<Reading>>? readTrend = null,
        Action<IReadOnlyList<(string Team, int? Waiting)>>? showTrends = null)
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
            openUrl ?? (_ => { }),
            (item, body, onGitHub, onApprove, accept, comment, tryIt, rank) =>
            {
                showBody?.Invoke(item, body, onGitHub, onApprove, accept, comment, tryIt);
                return chooseRank?.Invoke(item, body, rank);
            },
            area,
            auto,
            handOver,
            resume,
            readConversation: readConversation,
            confirmAccept: confirmAccept,
            clock: clock,
            askComment: askComment,
            readHistory: readHistory,
            readTrend: readTrend,
            showTrends: showTrends);
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
        internal DateTimeOffset Now { get; set; } = new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private string Config => Path.Combine(_root, "config");
}
