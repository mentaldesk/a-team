namespace ATeam.Dashboard.Tests;

public class HistoryTests
{
    private static readonly DateTimeOffset Moved = new(new DateTime(2026, 10, 3, 13, 40, 0, DateTimeKind.Local));
    private static readonly DateTimeOffset Added = new(new DateTime(2026, 10, 3, 10, 41, 0, DateTimeKind.Local));

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
            history.Lines.Select(line => line.Text));
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
            "3 Oct 13:40  dev   running since 13:40",
            "3 Oct 10:41  dev   run 38 min · $4.12",
            "3 Oct 10:41  dev   run 120 min · $9.80 · killed",
            "3 Oct 10:41  lead  run 3 min · $0.50 · error",
            "3 Oct 10:41  dev   run 1 min · died",
        ], history.Lines.Select(line => line.Text));
    }

    [Fact]
    public void A_card_with_nothing_recorded_says_since_when_a_team_has_kept_history()
    {
        var history = Read($$"""{"since": "{{Added.ToUniversalTime():O}}", "events": []}""");

        Assert.Equal(["Nothing recorded yet. a-team keeps history from 3 Oct 2026."], history.Lines.Select(line => line.Text));
    }

    [Fact]
    public void With_no_record_at_all_it_keeps_history_from_now_on()
    {
        var history = Read("""{"since": null, "events": []}""");

        Assert.Equal(["Nothing recorded yet. a-team keeps history from now on."], history.Lines.Select(line => line.Text));
    }

    [Fact]
    public void A_history_that_wont_read_says_why_in_the_pane()
    {
        Assert.Equal(["board.sh: no such team"],
            History.Of(new Reading("", "board.sh: no such team"), 308).Lines.Select(line => line.Text));
        Assert.Equal(["couldn't read #308's history"], Read("[]").Lines.Select(line => line.Text));
        Assert.Equal(["couldn't read #308's history"], Read("not json").Lines.Select(line => line.Text));
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
