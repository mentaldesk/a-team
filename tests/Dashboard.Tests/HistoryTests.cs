using MentalDesk.Tui;
using Terminal.Gui.ViewBase;
namespace ATeam.Dashboard.Tests;

public class HistoryTests
{
    private static readonly TimeSpan Offset = TimeSpan.FromHours(13);
    private static readonly Times Clock = When.At(new DateTimeOffset(2026, 10, 20, 9, 0, 0, Offset),
        TimeZoneInfo.CreateCustomTimeZone("Test +13", Offset, "Test +13", "Test +13"));
    private static readonly DateTimeOffset Moved = new(2026, 10, 3, 13, 40, 0, Offset);
    private static readonly DateTimeOffset Added = new(2026, 10, 3, 10, 41, 0, Offset);

    private static History Read(string json) => History.Of(new Reading(json, null), 308);

    [Fact]
    public void Each_event_is_a_line_of_when_who_and_what_in_the_order_the_board_gave_newest_first()
    {
        var history = Read($$"""
            {"since": "{{Added.ToUniversalTime():O}}", "events": [
              {"at": "{{Moved.ToUniversalTime():O}}", "who": "dev", "what": "In progress → In review"},
              {"at": "{{Added.ToUniversalTime():O}}", "who": "lead", "what": "added as Ready"}]}
            """);

        Assert.Equal(
            ["3 Oct 13:40  dev   In progress → In review", "3 Oct 10:41  lead  added as Ready"],
            history.LinesAt(Clock).Select(line => line.Text));
    }

    [Fact]
    public void A_run_says_how_long_it_took_what_it_cost_and_how_it_ended_unless_it_finished()
    {
        string At(DateTimeOffset at) => $"\"{at.ToUniversalTime():O}\"";
        var history = Read($$$"""
            {"since": null, "events": [
              {"at": {{{At(Moved)}}}, "who": "dev", "what": "run", "run": {"ended": null, "cost": null, "outcome": null}},
              {"at": {{{At(Added)}}}, "who": "dev", "what": "run", "run": {"ended": {{{At(Added.AddMinutes(38))}}}, "cost": 4.12, "outcome": null}},
              {"at": {{{At(Added)}}}, "who": "dev", "what": "run", "run": {"ended": {{{At(Added.AddMinutes(120))}}}, "cost": 9.8, "outcome": "killed"}},
              {"at": {{{At(Added)}}}, "who": "lead", "what": "run", "run": {"ended": {{{At(Added.AddMinutes(3))}}}, "cost": 0.5, "outcome": "error"}},
              {"at": {{{At(Added)}}}, "who": "dev", "what": "run", "run": {"ended": {{{At(Added.AddSeconds(10))}}}, "cost": null, "outcome": "died"}}]}
            """);

        Assert.Equal(
        [
            "3 Oct 13:40  dev   running since 3 Oct 13:40",
            "3 Oct 10:41  dev   run 38m · $4.12",
            "3 Oct 10:41  dev   run 2h · $9.80 · killed",
            "3 Oct 10:41  lead  run 3m · $0.50 · error",
            "3 Oct 10:41  dev   run 1m · died",
        ], history.LinesAt(Clock).Select(line => line.Text));
    }

    [Fact]
    public void A_visit_says_how_long_you_spent_and_what_you_did_at_least_a_minute()
    {
        var history = Read($$"""
            {"since": null, "events": [
              {"at": "{{Moved.ToUniversalTime():O}}", "who": "you", "what": "approved", "spent": 372},
              {"at": "{{Added.ToUniversalTime():O}}", "who": "you", "what": "read", "spent": 14}]}
            """);

        Assert.Equal(["3 Oct 13:40  you   6m 12s · approved", "3 Oct 10:41  you   1m · read"], history.LinesAt(Clock).Select(line => line.Text));
    }

    [Fact]
    public void A_row_from_today_gives_only_its_time_and_one_from_another_year_gives_its_year()
    {
        string At(DateTimeOffset at) => $"\"{at.ToUniversalTime():O}\"";
        var history = Read($$$"""
            {"since": null, "events": [
              {"at": {{{At(new DateTimeOffset(2026, 10, 20, 8, 5, 0, Offset))}}}, "who": "dev", "what": "run", "run": {"ended": null, "cost": null, "outcome": null}},
              {"at": {{{At(new DateTimeOffset(2025, 11, 9, 14, 5, 0, Offset))}}}, "who": "lead", "what": "added as Ready"}]}
            """);

        Assert.Equal(["08:05        dev   running since 08:05", "9 Nov 2025 14:05 lead  added as Ready"],
            history.LinesAt(Clock).Select(line => line.Text));
    }

    [Fact]
    public void A_record_kept_from_another_year_gives_its_year()
    {
        var history = Read($$"""{"since": "{{new DateTimeOffset(2025, 11, 9, 9, 0, 0, Offset).ToUniversalTime():O}}", "events": []}""");

        Assert.Equal(["Nothing recorded yet. a-team keeps history from 9 Nov 2025."], history.LinesAt(Clock).Select(line => line.Text));
    }

    [Fact]
    public void A_card_with_nothing_recorded_says_since_when_a_team_has_kept_history()
    {
        var history = Read($$"""{"since": "{{Added.ToUniversalTime():O}}", "events": []}""");

        Assert.Equal(["Nothing recorded yet. a-team keeps history from 3 Oct."], history.LinesAt(Clock).Select(line => line.Text));
    }

    [Fact]
    public void With_no_record_at_all_it_keeps_history_from_now_on()
    {
        var history = Read("""{"since": null, "events": []}""");

        Assert.Equal(["Nothing recorded yet. a-team keeps history from now on."], history.LinesAt(Clock).Select(line => line.Text));
    }

    [Fact]
    public void A_history_that_wont_read_says_why_in_the_pane()
    {
        Assert.Equal(["board.sh: no such team"],
            History.Of(new Reading("", "board.sh: no such team"), 308).LinesAt(Clock).Select(line => line.Text));
        Assert.Equal(["couldn't read #308's history"], Read("[]").LinesAt(Clock).Select(line => line.Text));
        Assert.Equal(["couldn't read #308's history"], Read("not json").LinesAt(Clock).Select(line => line.Text));
    }

    [Fact]
    public void Where_the_reader_leaves_History_out_follows_the_widths_chosen()
    {
        var widths = new PaneWidths();
        var panes = new ReaderPanes(widths);
        Assert.Equal(100, panes.NarrowestSplit);
        Assert.Equal(120, panes.NarrowestThree);

        using var row = new View();
        using var body = new View();
        using var history = new View();
        using var comment = new View { Visible = false };
        var sides = widths.For(row, () => PaneWidths.History, _ => { })
            .AddSide(comment, PaneWidths.Comment, ReaderPanes.CommentWidth).AddMain(body).AddSide(history, PaneWidths.History, ReaderPanes.HistoryWidth);
        row.Add(comment, body, history);
        row.Width = 120;
        row.Height = 10;
        row.Layout(new System.Drawing.Size(120, 10));
        sides.Narrow();
        sides.Narrow();

        Assert.Equal(90, panes.NarrowestSplit);
        Assert.Equal(110, panes.NarrowestThree);
        Assert.True(panes.OpensWithHistory(95));
    }

    [Theory]
    [InlineData(true, 120, true)]
    [InlineData(true, 99, false)]
    [InlineData(false, 120, false)]
    public void The_reader_opens_with_History_as_last_left_unless_the_terminal_is_too_narrow_for_both(
        bool shown, int width, bool opens)
    {
        Assert.Equal(opens, new ReaderPanes { HistoryShown = shown }.OpensWithHistory(width));
    }
}
