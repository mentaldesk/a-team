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
    public void A_refused_decline_leaves_the_card_where_it_was()
    {
        using var window = Open(
            run: _ => Task.FromResult<string?>("board.sh: only an Idea or a Pitched pitch can be declined (#6 is in 'Done')"),
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (_, _, _, _, _, comment, _) => comment!.Decline!.Run("No."));
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(new Key('x'));
        window.Refresh();

        Assert.Contains(window.Work.Items, item => item.Number == 6);
        Assert.Equal(6, window.Work.Selected?.Number);
    }

    [Fact]
    public void The_reader_offers_decline_on_Ideas_and_pitches_only_and_opens_declining_only_from_x()
    {
        var offered = new List<(int Number, ReaderDecline? Decline)>();
        using var window = Open(
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (item, _, _, _, _, comment, _) => offered.Add((item.Number, comment?.Decline)));
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();
        window.NewKeyDownEvent(new Key('x'));
        window.Refresh();
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();

        Assert.Equal([6, 107, 107, 49], offered.Select(each => each.Number));
        Assert.Equal([false, false, true], offered.Take(3).Select(each => each.Decline!.Open));
        Assert.Null(offered[3].Decline);
    }

    [Fact]
    public void work_decline_is_on_x_in_the_Cards_menu_and_enabled_only_on_an_Idea_or_a_pitch()
    {
        using var window = Open();
        window.Refresh();
        LayOut(window, 120, 30);

        var decline = window.Commands.Registered.Single(command => command.Id == "work.decline");
        Assert.Equal("Decline", decline.Label);
        Assert.Equal("X", decline.Keys);
        Assert.True(decline.OnCard);
        Assert.DoesNotContain(window.Commands.Registered, command => command.Id != "work.decline" && command.Keys == "X");
        Assert.True(window.Commands.IsEnabled("work.decline"));
        window.NewKeyDownEvent(Key.CursorRight);
        Assert.True(window.Commands.IsEnabled("work.decline"));
        window.NewKeyDownEvent(Key.CursorRight);
        Assert.Equal(49, window.Work.Selected?.Number);
        Assert.False(window.Commands.IsEnabled("work.decline"));
    }

    [Fact]
    public void l_on_a_pitch_opens_the_reader_saying_Not_now_and_the_card_leaves_saying_when_it_s_back()
    {
        var calls = new List<string[]>();
        var reasons = new List<string>();
        ReaderShelve? offered = null;
        using var window = Open(
            run: arguments =>
            {
                calls.Add(arguments);
                reasons.Add(File.ReadAllText(arguments[^1]));
                return Task.FromResult<string?>(null);
            },
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (_, _, _, _, _, comment, _) =>
            {
                offered = comment?.Shelve;
                Assert.Null(offered!.Run(3, "").Result);
            },
            clock: new Clock());
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);
        Assert.Equal(107, window.Work.Selected?.Number);

        Assert.True(window.NewKeyDownEvent(new Key('l')));
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.True(offered?.Open);
        Assert.Equal(new Key('l'), offered?.Key);
        Assert.Equal("board team0 shelve you 107 3", string.Join(' ', Assert.Single(calls)[..6]));
        Assert.Equal([""], reasons);
        Assert.DoesNotContain(window.Work.Items, item => item.Number == 107);
        Assert.Equal("#107 back on 23 Oct", window.Says());
    }

    [Fact]
    public void A_refused_shelve_leaves_the_card_where_it_was()
    {
        using var window = Open(
            run: _ => Task.FromResult<string?>("board.sh: only a Pitched pitch can be shelved (#107 is in 'Idea')"),
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (_, _, _, _, _, comment, _) => comment!.Shelve!.Run(2, "Later."));
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);

        window.NewKeyDownEvent(new Key('l'));
        window.Refresh();

        Assert.Contains(window.Work.Items, item => item.Number == 107);
        Assert.Equal(107, window.Work.Selected?.Number);
    }

    [Fact]
    public void H_on_a_held_card_releases_it_and_the_card_leaves_for_the_Dev_s_queue()
    {
        var calls = new List<string[]>();
        using var window = Open(
            read: team => Task.FromResult(new Reading(team == "team0" ? Question : "[]", null)),
            run: arguments =>
            {
                calls.Add(arguments);
                return Task.FromResult<string?>(null);
            });
        window.Refresh();
        LayOut(window, 120, 30);
        Assert.Equal(192, window.Work.Selected?.Number);

        var hold = window.Commands.Registered.Single(command => command.Id == "work.hold");
        Assert.Equal("H", hold.Keys);
        Assert.True(hold.OnCard);
        Assert.Equal("Release hold", window.Commands.Registered.Single(command => command.Id == "work.hold").Label);
        Assert.True(window.NewKeyDownEvent(new Key('h')));
        window.Refresh();

        Assert.Equal([["board", "team0", "release", "you", "192"]], calls);
        Assert.Equal(0, window.Work.Lanes[0].Columns.Single(column => column.Gate == "Blocked").Count);
        Assert.Equal("#192 back in the Dev's queue", window.Says());
    }

    [Fact]
    public void H_on_a_Ready_card_that_isn_t_held_asks_why_then_holds_it_and_reads_Work_again()
    {
        var calls = new List<string[]>();
        var posted = "";
        var asked = new List<int>();
        var reads = 0;
        using var window = Open(
            read: team =>
            {
                reads++;
                return Task.FromResult(new Reading(team == "team0" ? QuestionAndUpstream : "[]", null));
            },
            run: arguments =>
            {
                calls.Add(arguments);
                posted = File.ReadAllText(arguments[^1]);
                return Task.FromResult<string?>(null);
            },
            askHold: item =>
            {
                asked.Add(item.Number);
                return "wait for the designer";
            });
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorDown);
        Assert.Equal(469, window.Work.Selected?.Number);
        Assert.Equal("Hold", window.Commands.Registered.Single(command => command.Id == "work.hold").Label);
        var before = reads;

        Assert.True(window.NewKeyDownEvent(new Key('h')));
        window.Refresh();

        Assert.Equal([469], asked);
        Assert.Equal(["board", "team0", "hold", "you", "469"], calls.Single()[..5]);
        Assert.Equal("wait for the designer", posted);
        Assert.True(reads > before);
    }

    [Fact]
    public void Cancelling_Hold_holds_nothing()
    {
        var calls = new List<string[]>();
        using var window = Open(
            read: team => Task.FromResult(new Reading(team == "team0" ? QuestionAndUpstream : "[]", null)),
            run: arguments =>
            {
                calls.Add(arguments);
                return Task.FromResult<string?>(null);
            },
            askHold: _ => null);
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorDown);

        window.NewKeyDownEvent(new Key('h'));
        window.Refresh();

        Assert.Empty(calls);
    }

    [Fact]
    public void Hold_is_only_on_Ready_tasks()
    {
        using var window = Open(read: team => Task.FromResult(new Reading(team == "team0" ? Review() : "[]", null)));
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.NotNull(window.Work.Selected);
        Assert.False(window.Commands.IsEnabled("work.hold"));
    }

    [Fact]
    public void The_reader_offers_Release_hold_on_a_held_task_only()
    {
        var offered = new List<(int Number, ReaderRelease? Release)>();
        using var window = Open(
            read: team => Task.FromResult(new Reading(team == "team0" ? QuestionAndUpstream : "[]", null)),
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (item, _, _, _, _, comment, _) => offered.Add((item.Number, comment?.Release)));
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();
        window.NewKeyDownEvent(Key.CursorDown);
        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();

        Assert.Equal([192, 469], offered.Select(each => each.Number));
        Assert.NotNull(offered[0].Release);
        Assert.Null(offered[1].Release);
    }

    [Fact]
    public async Task Release_hold_from_the_reader_posts_what_you_wrote_then_releases()
    {
        var calls = new List<string[]>();
        var posted = "";
        ReaderRelease? release = null;
        using var window = Open(
            read: team => Task.FromResult(new Reading(team == "team0" ? Question : "[]", null)),
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            run: arguments =>
            {
                calls.Add(arguments);
                if (arguments[2] == "comment")
                    posted = File.ReadAllText(arguments[^1]);
                return Task.FromResult<string?>(null);
            },
            showBody: (_, _, _, _, _, comment, _) => release = comment?.Release);
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();

        Assert.Null(await release!.Run("Installed."));
        window.Refresh();

        Assert.Equal(["comment", "release"], calls.Select(call => call[2]));
        Assert.Equal("Installed.", posted);
    }

    [Fact]
    public void work_notNow_is_on_l_in_the_Cards_menu_and_the_reader_offers_it_on_pitches_only()
    {
        var offered = new List<(int Number, ReaderShelve? Shelve)>();
        using var window = Open(
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (item, _, _, _, _, comment, _) => offered.Add((item.Number, comment?.Shelve)));
        window.Refresh();
        LayOut(window, 120, 30);

        var notNow = window.Commands.Registered.Single(command => command.Id == "work.notNow");
        Assert.Equal("Not now", notNow.Label);
        Assert.Equal("L", notNow.Keys);
        Assert.True(notNow.OnCard);
        Assert.DoesNotContain(window.Commands.Registered,
            command => command.Id != "work.notNow" && command.Keys == "L" && command.Scopes.Contains(Regions.Work));
        Assert.False(window.Commands.IsEnabled("work.notNow"));
        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();
        window.NewKeyDownEvent(Key.CursorRight);
        Assert.True(window.Commands.IsEnabled("work.notNow"));
        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();

        Assert.Equal([6, 107], offered.Select(each => each.Number));
        Assert.Null(offered[0].Shelve);
        Assert.False(offered[1].Shelve!.Open);
    }

    [Fact]
    public void Decline_can_be_rebound_and_the_reader_offers_it_on_the_new_key()
    {
        Directory.CreateDirectory(_root);
        OldKeys.Save(Config, ("work.decline", "z"));
        ReaderDecline? offered = null;
        using var window = Open(
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (_, _, _, _, _, comment, _) => offered = comment?.Decline);
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.False(window.NewKeyDownEvent(new Key('x')));
        window.NewKeyDownEvent(new Key('z'));
        window.Refresh();

        Assert.Equal(new Key('z'), offered?.Key);
        Assert.True(offered?.Open);
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
        Assert.Equal("Merging PR #122…", window.Says());
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal([49], asked.Select(item => item.Number));
        Assert.Equal([["board", "team0", "accept", "you", "49"]], calls);
        Assert.Equal(["Review · 0"], Titles(window).Take(4).Where(title => title.StartsWith("Review")));
        Assert.Equal(before, reads);
        Assert.Equal("merged PR #122", window.Says());
    }

    /// <summary>Each board write it's asked for, left running until the test finishes it.</summary>
    /// <summary>Each write waits on its own <see cref="Finishes"/> entry, except one given a result up front.</summary>
    private sealed class Writes(params string?[] results)
    {
        internal List<string[]> Calls { get; } = [];
        internal List<TaskCompletionSource<string?>> Finishes { get; } = [];

        internal Task<string?> Run(string[] arguments)
        {
            Calls.Add(arguments);
            var finish = new TaskCompletionSource<string?>();
            if (Finishes.Count < results.Length)
                finish.SetResult(results[Finishes.Count]);
            Finishes.Add(finish);
            return finish.Task;
        }
    }

    private DashboardWindow OpenTwoReviews(Writes writes, Action<WaitingItem>? shown = null,
        Func<Write?, IReadOnlyList<Write>, bool>? confirmQuit = null, Func<WaitingItem, bool>? confirmAccept = null)
    {
        var window = Open(
            read: team => Task.FromResult(new Reading(team == "team0" ? TwoReviews : "[]", null)),
            run: writes.Run,
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (item, _, _, _, _, _, _) => shown?.Invoke(item),
            confirmAccept: confirmAccept ?? (_ => true),
            confirmQuit: confirmQuit);
        window.Refresh();
        LayOut(window, 120, 30);
        return window;
    }

    private static IEnumerable<int> Reviewing(DashboardWindow window) =>
        window.Work.Items.Where(item => item.Status == "In review").Select(item => item.Number);

    [Fact]
    public void Enter_on_another_card_opens_it_while_the_first_is_still_being_merged()
    {
        var writes = new Writes();
        var shown = new List<int>();
        using var window = OpenTwoReviews(writes, item => shown.Add(item.Number));
        window.NewKeyDownEvent(new Key('a'));

        window.NewKeyDownEvent(Key.CursorDown);
        window.NewKeyDownEvent(Key.CursorDown);
        Assert.Equal(50, window.Work.Selected?.Number);
        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();

        Assert.Equal([50], shown);
        Assert.Single(writes.Calls);
    }
}
