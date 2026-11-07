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
        Assert.Equal("_Show only mine", FilterTitle(window));

        MenuItem(window, "work.mine").Action!();
        Assert.True(window.Work.OnlyMine);
        Assert.Equal("_Show all", FilterTitle(window));

        window.Press("Ctrl+T M");
        Assert.False(window.Work.OnlyMine);
        Assert.Equal("_Show only mine", FilterTitle(window));
    }

    public static TheoryData<string> CardKinds() =>
        [Waiting("team0"), Review(), Question, PitchQuestion, DocumentPitch(), ReviewPitch(), Queue];

    [Theory]
    [MemberData(nameof(CardKinds))]
    public void Cards_repeats_no_letter_whichever_card_is_selected(string cards)
    {
        using var window = Open(read: team => Task.FromResult(new Reading(team == "team0" ? cards : "[]", null)));
        window.Refresh();
        var cardsMenu = window.Menus.Single(menu => menu.Title == AppMenu.Cards);

        for (var step = 0; step < 6; step++)
        {
            window.Refresh();
            var letters = cardsMenu.PopoverMenu!.Root!.SubViews.OfType<MenuItem>().Select(item => char.ToLowerInvariant((char)item.HotKey)).ToList();
            Assert.Equal(letters.Count, letters.Distinct().Count());
            window.NewKeyDownEvent(step % 2 == 0 ? Key.CursorRight : Key.CursorDown);
        }
    }

    [Fact]
    public void Typing_gt_in_Commands_puts_Go_to_team_at_the_top()
    {
        using var window = Open();
        window.Refresh();
        using var palette = new CommandsDialog(window.Commands.Enabled);

        foreach (var character in "gt")
            palette.NewKeyDownEvent(new Key(character));

        Assert.Equal("work.team", palette.Matches[0].Id);
    }

    [Fact]
    public void The_Commands_palette_and_the_keys_here_both_show_F5_for_refresh()
    {
        using var window = Open();

        using var palette = new CommandsDialog(window.Commands.Registered);

        Assert.Contains(new KeyRow("F5", "Read what's waiting again"), window.Keys.Here!.Groups.Single().Rows);
        Assert.Contains(
            palette.List.Source!.ToList().Cast<string>(),
            row => row.StartsWith("Read what's waiting again") && row.TrimEnd().EndsWith(" F5"));
    }

    private const string Reviewed = """
        [{"number": 49, "title": "Reviewed task", "status": "In review", "team": "team0", "url": "u", "pr": 50, "prUrl": "p",
          "reviewed": true, "consider": 2},
         {"number": 51, "title": "Clean review", "status": "In review", "team": "team0", "url": "u", "pr": 52, "prUrl": "p",
          "reviewed": true, "consider": 0},
         {"number": 53, "title": "Unreviewed", "status": "In review", "team": "team0", "url": "u", "pr": 54, "prUrl": "p"}]
        """;

    private const string ReviewConversation = """
        [{"who": "reviewer", "at": "2026-11-01T08:00:00Z", "pr": 50,
          "body": "## Needs changing\n- a.sh:1 off.\n\n## Worth considering\n- Rename it.\n- Split it\n  in two."}]
        """;

    private DashboardWindow OpenReviewed(
        Func<string[], Task<string?>>? run = null, Func<WaitingItem, IReadOnlyList<string>, string?>? askFix = null, string conversation = ReviewConversation,
        Action<WaitingItem, IssueBody, Func<LinkOutcome?>, Func<bool>?, ReaderCommand?, ReaderComment?, ReaderTry?>? showBody = null)
    {
        var window = Open(
            read: team => Task.FromResult(new Reading(team == "team0" ? Reviewed : "[]", null)),
            run: run,
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            readConversation: _ => Task.FromResult(new Reading(conversation, null)),
            askFix: askFix,
            showBody: showBody);
        window.Refresh();
        LayOut(window, 120, 30);
        return window;
    }

    [Fact]
    public void F_on_a_reviewed_card_reads_its_points_asks_which_and_posts_one_comment_on_the_PR()
    {
        var calls = new List<string[]>();
        var posted = "";
        IReadOnlyList<string>? offered = null;
        using var window = OpenReviewed(
            run: arguments =>
            {
                calls.Add(arguments);
                posted = File.ReadAllText(arguments[^1]);
                return Task.FromResult<string?>(null);
            },
            askFix: (_, points) =>
            {
                offered = points;
                return Considered.Request(points.Take(1), "");
            });
        Assert.Equal(49, window.Work.Selected?.Number);

        Assert.True(window.NewKeyDownEvent(new Key('f')));
        window.Refresh();
        window.Refresh();

        Assert.Equal(["Rename it.", "Split it\nin two."], offered);
        Assert.Equal([["board", "team0", "comment", "you", "50"]], calls.Select(call => call[..^1]));
        Assert.Equal("> Rename it.\n\nPlease fix these.", posted);
        Assert.Equal("asked the Dev to fix PR #50", window.Says());
    }
}
