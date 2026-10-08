using Terminal.Gui.Input;

namespace ATeam.Dashboard.Tests;

public class TrendsDialogTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private static TrendsDialog Open(params (string Team, int? Waiting, string Json)[] teams) => new(
        [.. teams.Select(team => (team.Team, team.Waiting))],
        Task.FromResult(teams.Select(team => new Reading(team.Json, null)).ToArray()),
        Now,
        TimeZoneInfo.Utc);

    private const string Recorded = """
        {"since": "2026-09-20T00:00:00Z", "queue": [{"at": "2026-10-01T00:30:00Z", "waiting": 12}, {"at": "2026-10-08T09:00:00Z", "waiting": 9}],
         "accepted": ["2026-10-07T10:00:00Z", "2026-10-08T10:00:00Z"], "cost": 212.4}
        """;

    private const string Nothing = """{"since": null, "queue": [], "accepted": [], "cost": 0}""";

    [Fact]
    public void It_charts_waiting_first_and_lists_each_team_s_week_with_run_cost()
    {
        using var dialog = Open(("a-team", 4, Recorded), ("tuicode", null, Nothing));

        Assert.Equal(["Waiting on you", "Accepted per day"], dialog.Measure.Labels);
        Assert.Equal(0, dialog.Measure.Value);
        Assert.True(dialog.Graph.Visible);
        Assert.False(dialog.Message.Visible);
        Assert.Equal([new TeamTrendRow("a-team", 4, 12, 2, 212.4m), new TeamTrendRow("tuicode", null, null, 0, 0)], dialog.Rows);
        Assert.Equal(9, dialog.Series[0][^1]);
        Assert.All(dialog.Series[1], Assert.Null);
    }

    [Fact]
    public void Choosing_accepted_charts_accepted_per_day()
    {
        using var dialog = Open(("a-team", 4, Recorded));

        dialog.Measure.Value = 1;

        Assert.Equal([1, 1], dialog.Series[0].TakeLast(2));
    }

    [Fact]
    public void With_nothing_recorded_it_says_so_instead_of_charting()
    {
        using var dialog = Open(("a-team", 4, Nothing), ("tuicode", 1, Nothing));

        Assert.False(dialog.Graph.Visible);
        Assert.False(dialog.Measure.Visible);
        Assert.Equal("Nothing recorded yet. a-team keeps its record from the day it's installed.", dialog.Message.Text);
    }

    [Fact]
    public void A_record_that_can_t_be_read_says_why()
    {
        using var dialog = new TrendsDialog([("a-team", 4)], Task.FromResult(new[] { new Reading("", "board.sh: no such team") }), Now);

        Assert.False(dialog.Graph.Visible);
        Assert.Equal("Couldn't read the record: board.sh: no such team", dialog.Message.Text);
    }

    [Fact]
    public void Esc_closes_it()
    {
        using var dialog = Open(("a-team", 4, Recorded));

        dialog.NewKeyDownEvent(Key.Esc);

        Assert.True(dialog.Closed);
    }
}
