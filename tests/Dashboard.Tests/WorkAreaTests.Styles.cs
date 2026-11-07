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
    }

    [Fact]
    public void Whether_only_mine_is_on_is_what_the_app_opens_with_next_time()
    {
        using (var window = Open())
        {
            window.Refresh();
            window.Press("Ctrl+T M");
        }

        Assert.True(new DashboardSettings(Config).ReadOnlyMine());

        using var reopened = Open();
        reopened.Refresh();
        LayOut(reopened, 120, 30);

        Assert.True(reopened.Work.OnlyMine);
        Assert.Equal(["\U0001F4A1Triage · 1", "◇ Pitches · 1", "Blocked · 0", "Review · 0", "\U0001F4A1Triage · 0", "◇ Pitches · 1", "Blocked · 0", "Review · 0"],
            Titles(reopened));
    }

    [Theory]
    [InlineData("opened")]
    [InlineData("copied")]
    [InlineData("failed")]
    public void g_opens_the_selected_cards_issue_and_says_what_happened(string did)
    {
        const string url = "https://github.com/mentaldesk/team0/issues/107";
        var opened = new List<string>();
        using var window = Open(openUrl: Opening.Into(opened, did));
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.CursorRight);
        Assert.True(window.NewKeyDownEvent(new Key('g')));

        Assert.Equal([url], opened);
        Assert.Equal(Opening.Outcome(did, url).Message, window.Says());
        Assert.Equal(did == "failed", window.Status.MessageSeverity == Severity.Error);
    }

    [Theory]
    [InlineData("opened")]
    [InlineData("copied")]
    [InlineData("failed")]
    public void b_opens_the_board_of_the_selected_cards_team_and_says_what_happened(string did)
    {
        var teams = Path.Combine(Config, "teams");
        Directory.CreateDirectory(teams);
        File.WriteAllText(Path.Combine(teams, "team0.json"), """{"project": {"owner": "mentaldesk", "number": 9}}""");
        const string url = "https://github.com/orgs/mentaldesk/projects/9";
        var opened = new List<string>();
        using var window = Open(openUrl: Opening.Into(opened, did));
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.CursorRight);
        Assert.Equal("team0", window.Work.Selected?.Team);
        Assert.True(window.NewKeyDownEvent(new Key('b')));

        Assert.Equal([url], opened);
        Assert.Equal(Opening.Outcome(did, url).Message, window.Says());
        Assert.Equal(did == "failed", window.Status.MessageSeverity == Severity.Error);
    }

    [Fact]
    public void b_says_why_when_the_team_config_names_no_board()
    {
        var opened = new List<string>();
        using var window = Open(openUrl: Opening.Into(opened));
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.True(window.NewKeyDownEvent(new Key('b')));

        Assert.Empty(opened);
        Assert.Equal("team0's config names no project board", window.Says());
    }

    [Fact]
    public void g_on_the_row_under_a_card_opens_its_PR()
    {
        var opened = new List<string>();
        using var window = Open(openUrl: Opening.Into(opened));
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
        using var window = Open(openUrl: Opening.Into(opened));
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal("#6 · waiting to be ranked", window.Says());

        Assert.True(window.NewKeyDownEvent(new Key('g')));
        Assert.Equal(["https://github.com/mentaldesk/team0/issues/6"], opened);

        window.NewKeyDownEvent(Key.PageDown.WithCtrl);
        Assert.Equal("Pitches · team1", window.Work.Region);
    }

    [Fact]
    public void p_is_no_longer_a_key_the_Work_area_answers()
    {
        var opened = new List<string>();
        using var window = Open(openUrl: Opening.Into(opened));
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(new Key('p'));
        window.Refresh();

        Assert.Empty(opened);
        Assert.DoesNotContain("work.pr", window.Commands.Registered.Select(command => command.Id));
        Assert.Equal("#107 · awaiting your approval since 08:14", window.Says());
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
            readBody: _ => Task.FromResult(new Reading(Body, null)),
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
}
