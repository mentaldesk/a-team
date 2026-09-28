using System.Drawing;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
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
        Assert.Equal(["Triage · 1", "Pitches · 2", "Review · 1", "Triage · 0", "Pitches · 1", "Review · 0"],
            Titles(window));
    }

    [Fact]
    public void The_van_drives_in_the_Work_area_until_the_first_read_lands()
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
    public void The_van_is_a_frame_in_the_middle_of_the_Work_area_rather_than_filling_it()
    {
        using var window = Open(read: _ => new TaskCompletionSource<Reading>().Task);

        LayOut(window, 120, 40);

        var van = window.Loading.Frame;
        Assert.Equal(new Size(LoadingView.Cells + 2, LoadingView.Rows + 2), van.Size);
        Assert.InRange(van.X - (120 - van.Right), -1, 1);
        Assert.InRange(van.Y - 1 - (40 - 2 - van.Bottom), -1, 1);
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
    public void Leaving_Work_while_it_is_still_reading_takes_the_van_with_it()
    {
        var finish = new TaskCompletionSource<Reading>();
        using var window = Open(read: _ => finish.Task);
        Assert.True(window.Loading.Visible);

        window.Commands.Execute("view.dashboard");

        Assert.False(window.Loading.Visible);
    }

    [Fact]
    public void Opening_on_the_Dashboard_reads_nothing_and_shows_no_van()
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

        Assert.Equal(["Triage · 1", "Pitches · 1", "Review · 0", "Triage · 0", "Pitches · 1", "Review · 0"],
            Titles(window));
        Assert.Equal(6, window.Work.Selected?.Number);

        window.NewKeyDownEvent(new Key('m'));
        LayOut(window, 120, 30);

        Assert.Equal(["Triage · 1", "Pitches · 2", "Review · 1", "Triage · 0", "Pitches · 1", "Review · 0"],
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
    public void The_status_bar_carries_the_Work_areas_keys_and_clicking_one_runs_it()
    {
        using var window = Open();
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal(window.HintLine, window.Status.Says);
        Assert.Contains("Enter: read", window.Status.Says);

        Hint(window, "m: only mine").InvokeCommand(Command.Accept);

        Assert.True(window.Work.OnlyMine);
    }

    /// <summary>The filter has moved to the status bar, so the message row is the message and nothing else: on the
    /// Dashboard, where there's nothing to say, it takes no rows at all.</summary>
    [Fact]
    public void The_message_row_says_what_there_is_to_say_and_no_longer_the_filter()
    {
        using var window = Open();
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal("#6 · waiting to be ranked", window.Message.Says);
        Assert.Equal(window.Message.Says, window.Message.Text);
        Assert.Equal(window.Viewport.Height - 2, window.Status.Frame.Y);

        window.NewKeyDownEvent(new Key('d'));
        LayOut(window, 120, 30);

        Assert.Equal(0, window.Message.Lines);
        Assert.Equal(window.Viewport.Height - 1, window.Status.Frame.Y);
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
        Assert.Equal(["Triage · 1", "Pitches · 1", "Review · 0", "Triage · 0", "Pitches · 1", "Review · 0"],
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
        Assert.Equal("Triage · team1", window.Work.Region);
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
        Assert.True(window.Commands.IsEnabled("work.priority"));

        window.NewKeyDownEvent(Key.CursorDown);
        Assert.Equal(49, window.Work.Selected?.Number);
        Assert.False(window.Commands.IsEnabled("work.priority"));
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
        }, askPriority: (_, _) => Rank.High);
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
            askPriority: (_, _) => Rank.High);
        window.Refresh();
        LayOut(window, 120, 30);

        window.Commands.Execute("work.priority");
        window.Refresh();
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal(["Triage · 0", "Pitches · 2", "Review · 1", "Triage · 0", "Pitches · 1", "Review · 0"],
            Titles(window));
        Assert.Equal("#6 · set to High", window.Message.Says);
        Assert.Equal(2, reads);
    }

    [Fact]
    public void The_selection_carries_on_to_the_next_card_in_the_queue()
    {
        using var window = Open(
            read: team => Task.FromResult(new Reading(team == "team0" ? Queue : "[]", null)),
            askPriority: (_, _) => Rank.High);
        window.Refresh();
        LayOut(window, 120, 30);
        Assert.Equal(6, window.Work.Selected?.Number);

        window.Commands.Execute("work.priority");
        window.Refresh();
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal(26, window.Work.Selected?.Number);
        Assert.Equal("Triage · 1", window.Work.Lanes[0].Columns[0].Title);
    }

    [Fact]
    public void The_message_goes_as_soon_as_the_selection_does()
    {
        using var window = Open(askPriority: (_, _) => Rank.High);
        window.Refresh();
        LayOut(window, 120, 30);

        window.Commands.Execute("work.priority");
        window.Refresh();
        window.Refresh();
        Assert.Equal("#6 · set to High", window.Message.Says);

        window.NewKeyDownEvent(Key.CursorRight);
        Assert.Equal("#107 · awaiting your approval since 08:14", window.Message.Says);
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
            askPriority: (_, _) => Rank.None);
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);
        Assert.Equal(107, window.Work.Selected?.Number);

        window.Commands.Execute("work.priority");
        window.Refresh();
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal([["board", "team0", "priority", "you", "107", "none"]], calls);
        Assert.Equal(["Triage · 2", "Pitches · 1", "Review · 1", "Triage · 0", "Pitches · 1", "Review · 0"],
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
            askPriority: (_, _) => Rank.High);
        window.Refresh();
        LayOut(window, 120, 30);
        Assert.Equal("Triage · team0", window.Work.Region);

        window.Commands.Execute("work.priority");
        window.Refresh();
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal(["Triage · 0", "Pitches · 1", "Review · 0", "Triage · 0", "Pitches · 0", "Review · 0"],
            Titles(window));
        Assert.Equal("#107 · set to High", window.Message.Says);
        Assert.Equal(2, reads);
    }

    [Fact]
    public void A_write_that_failed_says_so_and_leaves_the_cards_the_counts_and_the_stamp_as_they_were()
    {
        using var window = Open(
            run: _ => Task.FromResult<string?>("board.sh: API rate limit exceeded\nand a second line"),
            askPriority: (_, _) => Rank.High);
        window.Refresh();
        LayOut(window, 120, 30);
        var stamp = window.Status.State.Text;

        window.Commands.Execute("work.priority");
        window.Refresh();
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal("board.sh: API rate limit exceeded", window.Message.Says);
        Assert.Equal(["Triage · 1", "Pitches · 2", "Review · 1", "Triage · 0", "Pitches · 1", "Review · 0"],
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
            askPriority: (_, body) =>
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
            askPriority: (_, body) =>
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
            showBody: (item, body, _, _) => shown = (item, body));
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
            showBody: (_, _, _, onApprove) => onApprove!());
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
        Assert.Equal("Pitches · 1", window.Work.Lanes[0].Columns[1].Title);
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
            showBody: (_, _, _, onApprove) => onApprove!());
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);

        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();
        window.Refresh();

        Assert.Equal("Pitches · 2", window.Work.Lanes[0].Columns[1].Title);
        Assert.Equal(107, window.Work.Selected?.Number);
        Assert.Equal("board.sh: only a Pitched pitch can be approved (#107 is in 'Approved')", window.Message.Says);
        Assert.Equal(SchemeManager.SchemesToSchemeName(Schemes.Error), window.Message.SchemeName);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 0)]
    public void The_reader_offers_no_approve_on_anything_but_a_Pitched_pitch(int right, int down)
    {
        var offered = new List<bool>();
        using var window = Open(
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (_, _, _, onApprove) => offered.Add(onApprove is not null));
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
            showBody: (_, _, _, onApprove) => offered.Add(onApprove is not null));
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
        Assert.Null(approve.Hint);
        Assert.False(window.Commands.IsEnabled("work.approve"));

        Assert.False(window.NewKeyDownEvent(new Key('a')));
        window.Refresh();

        Assert.Empty(calls);
        Assert.DoesNotContain("approve", window.Status.Says);
        Assert.Equal("Pitches · 2", window.Work.Lanes[0].Columns[1].Title);
    }

    [Fact]
    public void o_in_the_reader_opens_what_o_on_the_board_would_have()
    {
        var opened = new List<string>();
        using var window = Open(
            openUrl: opened.Add,
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (_, _, onGitHub, _) => onGitHub());
        window.Refresh();
        LayOut(window, 120, 30);

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
            showBody: (_, _, _, _) => shown = true);
        window.Refresh();
        LayOut(window, 120, 30);

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
        using var window = Open(showBody: (_, _, _, _) => shown = true);
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();

        Assert.False(shown);
        Assert.Equal("#6 has no description", window.Message.Says);
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
        window.NewKeyDownEvent(new Key('r'));
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal("a-team board: API rate limit exceeded", window.Message.Says);
        Assert.Equal(["Triage · 1", "Pitches · 2", "Review · 1", "Triage · 0", "Pitches · 1", "Review · 0"],
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

        window.NewKeyDownEvent(new Key('d'));
        window.NewKeyDownEvent(new Key('w'));

        Assert.Equal(4, reads);
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
        Assert.Equal("Review · team0", window.Work.Region);

        window.NewKeyDownEvent(Key.CursorDown);
        Assert.Equal("Review · team1", window.Work.Region);

        window.NewKeyDownEvent(Key.CursorLeft);
        Assert.Equal(133, window.Work.Selected?.Number);
        Assert.Equal("Pitches · team1", window.Work.Region);
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
        window.NewKeyDownEvent(new Key('g'));

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
            new Rectangle(0, 1, window.Viewport.Width, window.Viewport.Height - 2 - window.Message.Lines),
            window.Work.Frame);
    }

    [Fact]
    public void Every_menu_item_runs_the_command_its_id_names_on_the_key_that_command_has()
    {
        using var window = Open(area: Area.Dashboard);

        Assert.Equal(
            [
                "view.dashboard", "view.work", "settings", "quit", "work.read", "work.priority", "work.try",
                "work.github", "team.pause", "agent.interrupt", "help", "commands", "about",
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
    public void A_menu_item_says_what_its_command_would_do_now()
    {
        var teams = Path.Combine(Config, "teams");
        Directory.CreateDirectory(teams);
        File.WriteAllText(Path.Combine(teams, "team0.json"), "{\"dispatch\": {\"enabled\": false}}");
        using var window = Open(area: Area.Dashboard);

        window.Refresh();

        Assert.Equal("_Resume team0", window.MenuItems.Single(item => item.Id == "team.pause").Item.Title);
    }

    [Fact]
    public void work_try_is_on_t_with_no_hint_and_the_Work_bar_is_unchanged()
    {
        using var window = Open();

        var command = window.Commands.Registered.Single(registered => registered.Id == "work.try");

        Assert.Equal(new Key('t'), command.Key);
        Assert.Null(command.Hint);
        Assert.True(command.OnCard);
        Assert.Equal("Enter: read · p: set priority · m: only mine · r: refresh", window.Commands.Hints(Mode.Work));
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
        first.NewKeyDownEvent(new Key('t'));

        using var back = Open(resume: handed.Single() with { Failure = "try team0 122 exited 1" });
        back.Refresh();
        LayOut(back, 120, 30);
        back.FocusResumed();

        Assert.Equal("try team0 122 exited 1", back.Message.Says);
        Assert.Equal(SchemeManager.SchemesToSchemeName(Schemes.Error), back.Message.SchemeName);
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

    private const string Body = """{"number": 6, "title": "t", "body": "## Opportunity"}""";

    private static Button Hint(DashboardWindow window, string text) =>
        window.Status.Hints.Single(hint => hint.Text == text);

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
        Func<string[], Task<string?>>? run = null,
        Func<WaitingItem, IssueBody, Rank?>? askPriority = null,
        Func<WaitingItem, Task<Reading>>? readBody = null,
        Action<WaitingItem, IssueBody, Action, Action?>? showBody = null,
        Area area = Area.Work,
        IconStyle auto = IconStyle.Unicode,
        Action<Handover>? handOver = null,
        Handover? resume = null)
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
            askPriority ?? ((_, _) => null),
            showBody ?? ((_, _, _, _) => { }),
            area,
            auto,
            handOver,
            resume);
    }

    private string Config => Path.Combine(_root, "config");
}
