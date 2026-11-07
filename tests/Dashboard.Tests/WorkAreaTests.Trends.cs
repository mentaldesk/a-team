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
    public void Trends_counts_no_Dependabot_PR_with_the_Dev_as_waiting_on_you()
    {
        const string bump = """
            [{"number": 653, "title": "Bump MentalDesk.Tui", "status": "In progress", "url": "https://github.com/x/pull/653", "team": "team0",
              "turn": "dev", "dependency": true, "pr": 653, "unready": "CI failing", "trouble": "CI failing", "base": "main"}]
            """;
        var opened = new List<IReadOnlyList<(string Team, int? Waiting)>>();
        using var window = Open(read: team => Task.FromResult(new Reading(team == "team0" ? bump : "[]", null)), showTrends: opened.Add);
        window.Refresh();

        window.Commands.Execute("trends");

        Assert.Equal([("team0", 0), ("team1", 0)], opened[0]);
    }

    [Theory]
    [InlineData("CI failing", 122, "#49 CI failing")]
    [InlineData("conflicts with main", 122, "#49 conflicts with main")]
    [InlineData("behind main", 122, "#49 behind main")]
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
        Assert.Equal("Closing #174…", window.Says());
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal([(174, 3)], asked.Select(item => (item.Number, item.Tasks)));
        Assert.Equal([["board", "team0", "accept", "you", "174"]], calls);
        Assert.DoesNotContain("Review · 1", Titles(window));
        Assert.Equal("accepted #174", window.Says());
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
        Assert.Equal("closing #174: board.sh: can't close #174 (gh: Resource not accessible (HTTP 403))", window.Says());
        Assert.Equal(Severity.Error, window.Status.MessageSeverity);
    }

    [Fact]
    public void work_accept_is_on_A_in_the_Cards_menu_and_enabled_only_on_a_Review_task()
    {
        using var window = Open();
        window.Refresh();
        LayOut(window, 120, 30);

        var accept = window.Commands.Registered.Single(command => command.Id == "work.accept");
        Assert.Equal("Accept", accept.Label);
        Assert.Equal("A", accept.Keys);
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
        OldKeys.Save(Config, ("work.accept", "z"));
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
}
