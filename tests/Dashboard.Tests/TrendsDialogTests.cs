using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
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
         "accepted": ["2026-10-07T10:00:00Z", "2026-10-08T10:00:00Z"],
         "cycles": [{"ready": "2026-10-07T02:00:00Z", "accepted": "2026-10-07T10:00:00Z", "review": 21600},
                    {"ready": "2026-10-08T00:00:00Z", "accepted": "2026-10-08T10:00:00Z", "review": 36000}], "cost": 212.4}
        """;

    private const string Nothing = """{"since": null, "queue": [], "accepted": [], "cycles": [], "cost": 0}""";

    [Fact]
    public void It_charts_waiting_first_and_lists_each_team_s_week_with_run_cost()
    {
        using var dialog = Open(("a-team", 4, Recorded), ("tuicode", null, Nothing));

        Assert.Equal(["Waiting on you", "Accepted per day", "Hours to accept", "Your time"], dialog.Measure.Labels);
        Assert.Equal(0, dialog.Measure.Value);
        Assert.True(dialog.Graph.Visible);
        Assert.False(dialog.Message.Visible);
        Assert.Equal([new TeamTrendRow("a-team", 4, 12, 2, 212.4m, 9, 16 / 18.0), new TeamTrendRow("tuicode", null, null, 0, 0, null, null)], dialog.Rows);
        Assert.Equal(new TeamTrendRow("All", 4, 12, 2, 212.4m, 9, 16 / 18.0), dialog.All);
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
    public void Choosing_hours_to_accept_charts_each_day_s_median_hours()
    {
        using var dialog = Open(("a-team", 4, Recorded), ("tuicode", null, Nothing));

        dialog.Measure.Value = 2;

        Assert.Equal([null, 8, 10], dialog.Series[0].TakeLast(3));
        Assert.All(dialog.Series[1], Assert.Null);
    }

    [Fact]
    public void Arrow_keys_choose_the_measure_they_move_to()
    {
        using var dialog = Open(("a-team", 4, Recorded));

        dialog.Measure.NewKeyDownEvent(Key.CursorRight);
        Assert.Equal(1, dialog.Measure.Value);
        Assert.Equal(1, dialog.Measure.FocusedItem);

        dialog.Measure.NewKeyDownEvent(Key.CursorLeft);
        dialog.Measure.NewKeyDownEvent(Key.CursorLeft);
        dialog.Measure.NewKeyDownEvent(Key.CursorLeft);
        Assert.Equal(2, dialog.Measure.Value);
        Assert.Equal([null, 8, 10], dialog.Series[0].TakeLast(3));
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

    private const string Timed = """
        {"since": "2026-09-20T00:00:00Z", "queue": [], "cycles": [], "cost": 0,
         "accepted": ["2026-10-07T10:00:00Z", "2026-10-08T10:00:00Z", "2026-10-08T11:00:00Z"],
         "spentSince": "2026-10-06T09:00:00Z",
         "spent": [{"at": "2026-10-06T09:00:00Z", "seconds": 600, "gate": "Triage"},
                   {"at": "2026-10-08T09:00:00Z", "seconds": 1500, "gate": "Review"},
                   {"at": "2026-10-08T10:00:00Z", "seconds": 300, "gate": "Other"}]}
        """;

    [Fact]
    public void Your_time_charts_minutes_per_day_from_when_timing_began()
    {
        using var dialog = Open(("a-team", 4, Timed), ("tuicode", null, Nothing));

        dialog.Measure.Value = 3;

        Assert.Equal([null, 10, 0, 30], dialog.Series[0].TakeLast(4));
        Assert.All(dialog.Series[1], Assert.Null);
    }

    [Fact]
    public void The_table_says_your_week_and_accepted_per_hour_of_it_and_nothing_before_timing_began()
    {
        using var dialog = Open(("a-team", 4, Timed), ("tuicode", null, Nothing));

        Assert.Equal(TimeSpan.FromMinutes(40), dialog.Rows[0].Yours);
        Assert.Equal("4.5", dialog.Rows[0].PerHour);
        Assert.Null(dialog.Rows[1].Yours);
        Assert.Null(dialog.Rows[1].PerHour);
        Assert.Equal(["40m", "4.5"], Cells(dialog.Table, 0, 7, 8));
        Assert.Equal(["–", "–"], Cells(dialog.Table, 1, 7, 8));
        Assert.Equal(["40m", "4.5"], Cells(dialog.Table, 2, 7, 8));
    }

    [Fact]
    public void Your_time_splits_the_week_by_gate_beside_the_legend()
    {
        using var dialog = Open(("a-team", 4, Timed), ("tuicode", null, Nothing));
        Assert.False(dialog.Gates.Visible);

        dialog.Measure.Value = 3;

        Assert.True(dialog.Gates.Visible);
        Assert.Equal(["This week", "a-team", "tuicode"], dialog.Gates.Table!.ColumnNames);
        Assert.Equal(
            [["Triage", "10m", "–"], ["Pitches", "0m", "–"], ["Questions", "0m", "–"], ["Review", "25m", "–"], ["Other", "5m", "–"]],
            Enumerable.Range(0, 5).Select(row => Cells(dialog.Gates, row, 0, 1, 2)));
    }

    [Fact]
    public void The_table_still_fits_a_120_wide_terminal()
    {
        var host = new View { Width = 120, Height = 40 };
        using var dialog = Open(("a-team", 4, Timed), ("tuicode", null, Nothing));
        host.Add(dialog);
        host.Layout(new System.Drawing.Size(120, 40));

        var table = dialog.Table.Table!;
        var needed = Enumerable.Range(0, table.Columns)
            .Sum(column => Enumerable.Range(0, table.Rows).Select(row => table[row, column].ToString()!.Length)
                .Append(table.ColumnNames[column].Length).Max() + 1);

        Assert.True(dialog.Table.Viewport.Width >= needed, $"{needed} wide in {dialog.Table.Viewport.Width}");
    }

    private static string[] Cells(TableView table, int row, params int[] columns) =>
        [.. columns.Select(column => table.Table![row, column].ToString()!)];

    [Fact]
    public void Esc_closes_it()
    {
        using var dialog = Open(("a-team", 4, Recorded));

        dialog.NewKeyDownEvent(Key.Esc);

        Assert.True(dialog.Closed);
    }
}
