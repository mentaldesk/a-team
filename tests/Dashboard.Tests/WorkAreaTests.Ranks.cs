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

public partial class WorkAreaTests
{
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
        Assert.Equal("#6 · set to Low", window.Says());
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
        Assert.Equal("Reading #6…", window.Says());

        window.Refresh();

        Assert.Equal([["board", "team0", "priority", "you", "6", "High"]], calls);
        Assert.Equal("Setting #6 to High…", window.Says());
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

        Assert.Equal(["\U0001F4A1Triage · 0", "◇ Pitches · 2", "Blocked · 0", "Review · 1", "\U0001F4A1Triage · 0", "◇ Pitches · 1", "Blocked · 0", "Review · 0"],
            Titles(window));
        Assert.Equal("#6 · set to High", window.Says());
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
    public void What_the_board_took_stays_as_the_selection_moves_and_goes_after_five_seconds()
    {
        var clock = new Clock();
        using var window = Open(chooseRank: (_, _, _) => Rank.High, clock: clock);
        window.Refresh();
        LayOut(window, 120, 30);

        window.Commands.Execute("work.priority");
        window.Refresh();
        window.Refresh();
        Assert.Equal("#6 · set to High", window.Says());

        window.NewKeyDownEvent(Key.CursorRight);
        Assert.Equal("#6 · set to High", window.Says());

        clock.Now += AppStatusBar.Lifetime;
        Assert.Equal("#49 · dev · answering your feedback since 10:15 · PR #122", window.Says());
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
        Assert.Equal(["\U0001F4A1Triage · 2", "◇ Pitches · 1", "Blocked · 0", "Review · 1", "\U0001F4A1Triage · 0", "◇ Pitches · 1", "Blocked · 0", "Review · 0"],
            Titles(window));
        Assert.Equal(108, window.Work.Selected?.Number);
        Assert.Equal("#107 · set to None", window.Says());
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

        Assert.Equal(["\U0001F4A1Triage · 0", "◇ Pitches · 1", "Blocked · 0", "Review · 0", "\U0001F4A1Triage · 0", "◇ Pitches · 0", "Blocked · 0", "Review · 0"],
            Titles(window));
        Assert.Equal("#107 · set to High", window.Says());
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
        var stamp = window.Status.FocusWord;

        window.Commands.Execute("work.priority");
        window.Refresh();
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal("setting #6 to High: board.sh: API rate limit exceeded", window.Says());
        Assert.Equal(Severity.Error, window.Status.MessageSeverity);
        Assert.Equal(["\U0001F4A1Triage · 1", "◇ Pitches · 2", "Blocked · 0", "Review · 1", "\U0001F4A1Triage · 0", "◇ Pitches · 1", "Blocked · 0", "Review · 0"],
            Titles(window));
        Assert.Equal(stamp, window.Status.FocusWord);
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
    public void The_reader_files_an_idea_on_the_card_s_team_and_the_keys_offer_only_what_works_there()
    {
        var asked = new List<(string Team, string Description)>();
        string? said = null;
        WaitingItem? reading = null;
        IReadOnlyList<string>? offered = null;
        var keys = new List<bool>();
        DashboardWindow? opened = null;
        using var window = opened = Open(
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            newIdea: (_, team, description, _) =>
            {
                asked.Add((team, description));
                return (team, 412);
            },
            showBody: (item, _, _, _, _, _, _) => reading = item,
            showIdea: (idea, pass) =>
            {
                said = idea!.File("From https://github.com/x/6");
                offered = [.. opened!.Keys.Everywhere!.Groups.SelectMany(group => group.Rows).Select(row => row.Label)];
                keys.Add(pass(new Key('w')));
            });
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();

        Assert.Equal([(reading!.Team, "From https://github.com/x/6")], asked);
        Assert.Equal($"#412 added to {reading.Team}'s ideas", said);
        Assert.Equal(["New idea"], offered);
        Assert.Equal([false], keys);
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
}
