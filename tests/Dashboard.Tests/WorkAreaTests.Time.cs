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
    public void Time_in_Work_between_cards_is_the_selected_lane_s_and_Overseer_s_is_nobody_s()
    {
        var clock = new Clock();
        var visits = new List<Visit>();
        var attention = new Attention(clock, visits.Add);
        using var window = Open(clock: clock, attention: attention);
        window.Refresh();
        LayOut(window, 120, 30);

        clock.Now += TimeSpan.FromMinutes(3);
        attention.Key();
        window.Commands.Execute("view.overseer");
        window.Refresh();
        clock.Now += TimeSpan.FromMinutes(4);
        attention.Key();
        window.Commands.Execute("view.work");
        window.Refresh();
        clock.Now += TimeSpan.FromMinutes(1);
        attention.Stop();

        Assert.Equal([("team0", (int?)null, "Other", 3.0), ("team0", null, "Other", 1.0)],
            visits.Select(visit => (visit.Team, visit.Item, visit.Gate, visit.Spent.TotalMinutes)));
    }

    [Fact]
    public void A_try_from_a_card_counts_the_whole_session_against_it_up_to_half_an_hour()
    {
        var clock = new Clock();
        var visits = new List<Visit>();
        var attention = new Attention(clock, visits.Add);
        var handed = new List<Handover>();
        using var first = Open(clock: clock, attention: attention, handOver: handed.Add);
        first.Refresh();
        LayOut(first, 120, 30);
        first.NewKeyDownEvent(Key.CursorRight);
        first.NewKeyDownEvent(Key.CursorRight);
        first.NewKeyDownEvent(Key.CursorRight);
        first.Commands.Execute("work.try");
        clock.Now += TimeSpan.FromMinutes(40);

        using var back = Open(clock: clock, attention: attention, resume: handed.Single());

        var visit = Assert.Single(visits);
        Assert.Equal(("team0", 49, "Review", TimeSpan.FromMinutes(30), "tried"), (visit.Team, visit.Item, visit.Gate, visit.Spent, visit.What));
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
    public void Back_from_a_try_the_reader_reopens_once_the_window_runs_and_t_in_it_tries_again_after_a_comment()
    {
        var handed = new List<Handover>();
        var shown = new IssueBody("## Opportunity");
        using var first = Open(
            handOver: handed.Add,
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (_, _, _, _, _, _, tryIt) => tryIt!.Run(shown, 7));
        OpenTheTaskInReview(first);
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        var posted = new List<string?>();
        using var back = Open(
            handOver: handover =>
            {
                handed.Add(handover);
                app.RequestStop();
            },
            showBody: (_, body, _, _, _, comment, tryIt) =>
            {
                posted.Add(comment!.Post("looks good").GetAwaiter().GetResult());
                tryIt!.Run(body, 9);
            },
            resume: handed.Single());

        app.Begin(back);
        Assert.Empty(posted);
        app.TimedEvents!.RunTimers();

        Assert.Equal([null], posted);
        var again = Assert.IsType<TryHandover>(handed[^1]);
        Assert.Equal(2, handed.Count);
        Assert.Equal(["try", "team0", "122"], again.Arguments);
        Assert.Equal(9, again.Reader?.Top);
        Assert.True(back.StopRequested);
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
        Assert.NotEqual("try team0 122 exited 1", back.Says());
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
}
