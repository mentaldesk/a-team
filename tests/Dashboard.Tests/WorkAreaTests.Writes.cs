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
    public void Two_accepts_in_a_row_both_land_in_the_order_they_were_made()
    {
        var writes = new Writes();
        using var window = OpenTwoReviews(writes);

        window.NewKeyDownEvent(new Key('a'));
        window.NewKeyDownEvent(Key.CursorDown);
        window.NewKeyDownEvent(Key.CursorDown);
        window.NewKeyDownEvent(new Key('a'));
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal(["49"], writes.Calls.Select(call => call[^1]));
        Assert.Equal("Merging PR #122… · 1 more queued", window.Says());
        Assert.Equal(["⋯", "⋯"], window.Work.Lanes[0].Columns[3].CardIcons.Where((_, row) => row % 2 == 0).Select(icon => icon.Glyph));

        writes.Finishes[0].SetResult(null);
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal(["49", "50"], writes.Calls.Select(call => call[^1]));
        Assert.Equal([50], Reviewing(window));
        Assert.Equal("merged PR #122", window.Says());
        Assert.Equal("Merging PR #123…", window.Status.RestingText);

        writes.Finishes[1].SetResult(null);
        window.Refresh();

        Assert.Empty(Reviewing(window));
        Assert.Equal("merged PR #123", window.Says());
    }

    [Fact]
    public void A_refused_first_merge_leaves_its_card_and_the_second_still_runs()
    {
        var writes = new Writes();
        using var window = OpenTwoReviews(writes);
        window.NewKeyDownEvent(new Key('a'));
        window.NewKeyDownEvent(Key.CursorDown);
        window.NewKeyDownEvent(Key.CursorDown);
        window.NewKeyDownEvent(new Key('a'));

        writes.Finishes[0].SetResult("board.sh: can't merge PR #122 (gh: Pull Request is not mergeable (HTTP 405))");
        window.Refresh();

        Assert.Equal("merging PR #122: board.sh: can't merge PR #122 (gh: Pull Request is not mergeable (HTTP 405))", window.Says());
        Assert.Equal(["49", "50"], writes.Calls.Select(call => call[^1]));

        writes.Finishes[1].SetResult(null);
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal([49], Reviewing(window));
        Assert.Equal("✓", window.Work.Lanes[0].Columns[3].CardIcons[0].Glyph);
    }

    [Fact]
    public void Each_refusal_names_the_card_it_was_for_even_when_the_board_doesn_t()
    {
        var writes = new Writes("the command didn't finish", "gh: HTTP 502");
        using var window = OpenTwoReviews(writes);
        window.NewKeyDownEvent(new Key('a'));
        window.NewKeyDownEvent(Key.CursorDown);
        window.NewKeyDownEvent(Key.CursorDown);
        window.NewKeyDownEvent(new Key('a'));
        window.Refresh();

        Assert.Equal("merging PR #122: the command didn't finish · merging PR #123: gh: HTTP 502", window.Says());
    }

    [Fact]
    public void Accepting_a_card_already_being_merged_says_so_and_merges_it_once()
    {
        var writes = new Writes();
        var asked = 0;
        using var window = OpenTwoReviews(writes, confirmAccept: _ => ++asked > 0);

        window.NewKeyDownEvent(new Key('a'));
        window.NewKeyDownEvent(new Key('a'));

        Assert.Equal(1, asked);
        Assert.Single(writes.Calls);
        Assert.Equal("#49 is already being merged", window.Says());

        writes.Finishes[0].SetResult(null);
        window.Refresh();

        Assert.Single(writes.Calls);
        Assert.Equal([50], Reviewing(window));
    }

    [Fact]
    public void Quitting_with_writes_queued_asks_and_drops_only_the_unstarted_ones()
    {
        var writes = new Writes();
        var asked = new List<(string? Running, string[] Queued)>();
        var quit = false;
        using var window = OpenTwoReviews(writes, confirmQuit: (running, queued) =>
        {
            asked.Add((running?.Doing, [.. queued.Select(write => write.Queued)]));
            return quit;
        });
        window.NewKeyDownEvent(new Key('a'));
        window.NewKeyDownEvent(Key.CursorDown);
        window.NewKeyDownEvent(Key.CursorDown);
        window.NewKeyDownEvent(new Key('a'));

        window.Commands.Execute("quit");
        writes.Finishes[0].SetResult(null);
        window.Refresh();

        Assert.Equal(2, writes.Calls.Count);

        writes.Finishes[1].SetResult(null);
        window.Refresh();
        window.NewKeyDownEvent(Key.CursorUp);
        quit = true;
        window.NewKeyDownEvent(Key.CursorDown);
        window.Commands.Execute("quit");

        Assert.Single(asked);
        Assert.Equal("Merging PR #122…", asked[0].Running);
        Assert.Equal(["merging PR #123"], asked[0].Queued);
    }

    [Fact]
    public void Quitting_drops_what_hasn_t_started_once_you_say_so()
    {
        var writes = new Writes();
        using var window = OpenTwoReviews(writes, confirmQuit: (_, _) => true);
        window.NewKeyDownEvent(new Key('a'));
        window.NewKeyDownEvent(Key.CursorDown);
        window.NewKeyDownEvent(Key.CursorDown);
        window.NewKeyDownEvent(new Key('a'));

        window.Commands.Execute("quit");
        writes.Finishes[0].SetResult(null);
        window.Refresh();

        Assert.Single(writes.Calls);
        Assert.Equal([50], Reviewing(window));
    }

    [Fact]
    public void Ranking_approving_and_declining_queue_behind_each_other()
    {
        var writes = new Writes();
        using var window = Open(
            run: writes.Run,
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (item, _, _, onApprove, _, comment, _) =>
            {
                if (item.Number == 107)
                    onApprove!();
                if (item.Number == 108)
                    Assert.Null(comment!.Decline!.Run("Not now.").Result);
            },
            chooseRank: (item, _, _) => item.Number == 6 ? Rank.High : null);
        window.Refresh();
        LayOut(window, 120, 30);

        window.Commands.Execute("work.priority");
        window.Refresh();
        window.NewKeyDownEvent(Key.CursorRight);
        Assert.Equal(107, window.Work.Selected?.Number);
        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();
        window.NewKeyDownEvent(Key.CursorDown);
        Assert.Equal(108, window.Work.Selected?.Number);
        window.NewKeyDownEvent(new Key('x'));
        window.Refresh();

        Assert.Equal("Setting #6 to High… · 2 more queued", window.Says());
        Assert.Equal(["priority"], writes.Calls.Select(call => call[2]));

        writes.Finishes[0].SetResult(null);
        window.Refresh();
        writes.Finishes[1].SetResult(null);
        window.Refresh();
        writes.Finishes[2].SetResult(null);
        window.Refresh();

        Assert.Equal(["priority", "approve", "decline"], writes.Calls.Select(call => call[2]));
        Assert.DoesNotContain(window.Work.Items, item => item.Number is 107 or 108);
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
        Assert.Equal("merged PR #352", window.Says());
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
        Assert.Equal("merging PR #122: board.sh: can't merge PR #122 (gh: Pull Request is not mergeable (HTTP 405))", window.Says());
        Assert.Equal(Severity.Error, window.Status.MessageSeverity);
    }

    [Fact]
    public void A_Dependabot_PR_with_the_Dev_is_not_counted_as_waiting_on_you()
    {
        const string bump = """
            [{"number": 653, "title": "Bump MentalDesk.Tui", "status": "In progress", "url": "https://github.com/x/pull/653", "team": "team0",
              "turn": "dev", "dependency": true, "pr": 653, "unready": "CI failing", "trouble": "CI failing", "base": "main"}]
            """;
        using var window = Open(read: team => Task.FromResult(new Reading(team == "team0" ? bump : "[]", null)));
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal("Work · 0 waiting on you", window.WorkTitle);
        Assert.Equal("1 with the Dev: PR #653 (CI failing)", window.Work.Lanes[0].Columns[3].Summary);
    }
}
