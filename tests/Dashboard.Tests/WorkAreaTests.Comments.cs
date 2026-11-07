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
    public void F_in_the_reader_posts_the_comment_on_the_PR_and_closes_it_and_a_cancel_leaves_it_open()
    {
        var calls = new List<string[]>();
        var posted = "";
        var closes = new List<bool>();
        var answers = new Queue<string?>([null, "> Rename it.\n\nPlease fix these."]);
        using var window = OpenReviewed(
            run: arguments =>
            {
                calls.Add(arguments);
                posted = File.ReadAllText(arguments[^1]);
                return Task.FromResult<string?>(null);
            },
            askFix: (_, _) => answers.Dequeue(),
            showBody: (_, _, _, _, _, comment, _) =>
            {
                closes.Add(comment!.Fix!.Run());
                closes.Add(comment.Fix.Run());
            });

        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();
        window.Refresh();

        Assert.Equal([false, true], closes);
        Assert.Equal([["board", "team0", "comment", "you", "50"]], calls.Select(call => call[..^1]));
        Assert.Equal("> Rename it.\n\nPlease fix these.", posted);
        Assert.Equal("asked the Dev to fix PR #50", window.Says());
    }

    [Fact]
    public void The_reader_offers_no_f_where_it_could_not_read_the_points()
    {
        var shown = false;
        ReaderCommand? offered = null;
        using var window = Open(
            read: team => Task.FromResult(new Reading(team == "team0" ? Reviewed : "[]", null)),
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            readConversation: _ => Task.FromResult(new Reading("", "gh: HTTP 502")),
            showBody: (_, _, _, _, _, comment, _) =>
            {
                shown = true;
                offered = comment!.Fix;
            });
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();
        window.Refresh();

        Assert.True(shown);
        Assert.Null(offered);
    }

    [Fact]
    public void Cancelling_the_dialog_posts_nothing()
    {
        var calls = new List<string[]>();
        using var window = OpenReviewed(
            run: arguments =>
            {
                calls.Add(arguments);
                return Task.FromResult<string?>(null);
            },
            askFix: (_, _) => null);

        window.NewKeyDownEvent(new Key('f'));
        window.Refresh();
        window.Refresh();

        Assert.Empty(calls);
    }

    [Fact]
    public void A_review_that_can_t_be_read_says_so_and_asks_nothing()
    {
        var asked = 0;
        using var window = OpenReviewed(
            askFix: (_, _) =>
            {
                asked++;
                return null;
            },
            conversation: "[]");

        window.NewKeyDownEvent(new Key('f'));
        window.Refresh();

        Assert.Equal(0, asked);
        Assert.Equal("couldn't read what the Reviewer left on PR #50", window.Says());
    }

    [Fact]
    public void The_command_is_greyed_out_and_f_does_nothing_with_nothing_to_consider_or_no_review_yet()
    {
        var asked = 0;
        using var window = OpenReviewed(
            askFix: (_, _) =>
            {
                asked++;
                return null;
            });
        var command = window.Commands.Registered.Single(registered => registered.Id == "work.fix");
        Assert.Equal("Ask the Dev to fix…", command.Label);
        Assert.Equal("F", command.Keys);
        Assert.True(command.OnCard);
        Assert.True(window.Commands.IsEnabled("work.fix"));

        foreach (var number in new[] { 51, 53 })
        {
            window.NewKeyDownEvent(Key.CursorDown);
            if (window.Work.Selected?.Number != number)
                window.NewKeyDownEvent(Key.CursorDown);
            Assert.Equal(number, window.Work.Selected?.Number);
            Assert.False(window.Commands.IsEnabled("work.fix"));
            window.NewKeyDownEvent(new Key('f'));
            window.Refresh();
            Assert.Equal(0, asked);
        }
    }

    [Fact]
    public void F1_lists_f_under_Work()
    {
        using var window = OpenReviewed();

        Assert.Contains(new KeyRow("F", "Ask the Dev to fix…"), window.Keys.Here!.Groups.Single().Rows);
    }
}
