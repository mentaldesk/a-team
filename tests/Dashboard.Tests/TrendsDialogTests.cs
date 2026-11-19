using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Terminal.Gui.Input;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;

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
         "cycles": [{"ready": "2026-10-07T02:00:00Z", "accepted": "2026-10-07T10:00:00Z", "withYou": 21600},
                    {"ready": "2026-10-08T00:00:00Z", "accepted": "2026-10-08T10:00:00Z", "withYou": 36000}], "cost": 212.4}
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
        Assert.Equal([new TeamTrendRow("a-team", 4, 12, 2, 212.4m, 9, 16 / 18.0, null, new(0, 0, 0, 0, 0)), new TeamTrendRow("tuicode", null, null, 0, 0, null, null)], dialog.Rows);
        Assert.Equal(new TeamTrendRow("All", 4, 12, 2, 212.4m, 9, 16 / 18.0, null, new(0, 0, 0, 0, 0)), dialog.All);
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
        Assert.Equal(["40m", "4.5"], Cells(dialog.Table, 0, 10, 11));
        Assert.Equal(["–", "–"], Cells(dialog.Table, 1, 10, 11));
        Assert.Equal(["40m", "4.5"], Cells(dialog.Table, 2, 10, 11));
    }

    private const string Touchy = """
        {"since": "2026-09-20T00:00:00Z", "queue": [], "cycles": [], "cost": 0, "accepted": [],
         "touches": [{"accepted": "2026-09-30T10:00:00Z", "touches": 5}, {"accepted": "2026-10-07T10:00:00Z", "touches": 0},
                     {"accepted": "2026-10-07T11:00:00Z", "touches": 0}, {"accepted": "2026-10-07T12:00:00Z", "touches": 0},
                     {"accepted": "2026-10-08T10:00:00Z", "touches": 3}],
         "finished": [{"at": "2026-09-30T10:00:00Z", "untouched": true}, {"at": "2026-10-07T10:00:00Z", "untouched": true},
                      {"at": "2026-10-08T10:00:00Z", "untouched": false}]}
        """;

    private const string OneClean = """
        {"since": "2026-09-20T00:00:00Z", "queue": [], "cycles": [], "cost": 0, "accepted": [],
         "touches": [{"accepted": "2026-10-07T10:00:00Z", "touches": 0}], "finished": [{"at": "2026-10-07T10:00:00Z", "untouched": false}]}
        """;

    private const string Quiet = """{"since": "2026-09-20T00:00:00Z", "queue": [], "accepted": [], "cycles": [], "cost": 0, "touches": [], "finished": []}""";

    [Fact]
    public void The_table_puts_first_time_touches_and_untouched_between_with_you_and_yours()
    {
        using var dialog = Open(("a-team", 4, Touchy));

        var names = Headers(dialog.Table);
        Assert.Equal(["With you", "First time", "Touches", "Untouched", "Yours (7d)"], names.Skip(6).Take(5));
    }

    [Fact]
    public void It_counts_the_last_7_days_and_leaves_older_work_out()
    {
        using var dialog = Open(("a-team", 4, Touchy));

        Assert.Equal(["75% of 4", "0.8", "1"], Cells(dialog.Table, 0, 7, 8, 9));
    }

    [Fact]
    public void All_is_over_every_team_s_tasks_not_an_average_of_the_teams()
    {
        using var dialog = Open(("a-team", 4, Touchy), ("tuicode", null, OneClean));

        Assert.Equal(["100% of 1", "0.0", "0"], Cells(dialog.Table, 1, 7, 8, 9));
        Assert.Equal(new WeekTouches(5, 4, 3, 3, 1), dialog.All!.Touches);
        Assert.Equal(["80% of 5", "0.6", "1"], Cells(dialog.Table, 2, 7, 8, 9));
    }

    [Fact]
    public void An_empty_week_shows_a_dash_and_a_week_before_the_record_began_is_blank()
    {
        using var dialog = Open(("a-team", 4, Touchy), ("quiet", null, Quiet), ("tuicode", null, Nothing));

        Assert.Equal(["–", "–", "–"], Cells(dialog.Table, 1, 7, 8, 9));
        Assert.Equal(["", "", ""], Cells(dialog.Table, 2, 7, 8, 9));
    }

    [Fact]
    public void Your_time_splits_the_week_by_gate_beside_the_legend()
    {
        using var dialog = Open(("a-team", 4, Timed), ("tuicode", null, Nothing));
        Assert.False(dialog.Gates.Visible);

        dialog.Measure.Value = 3;

        Assert.True(dialog.Gates.Visible);
        Assert.Equal(["This week", "a-team", "tuicode"], Headers(dialog.Gates));
        Assert.Equal(
            [["Triage", "10m", "–"], ["Pitches", "0m", "–"], ["Blocked", "0m", "–"], ["Review", "25m", "–"], ["Other", "5m", "–"],
             ["  Finding a card", "–", "–"], ["  Reports", "–", "–"], ["  Settings & other", "–", "–"]],
            Enumerable.Range(0, 8).Select(row => Cells(dialog.Gates, row, 0, 1, 2)));
    }

    private const string Split = """
        {"since": "2026-09-20T00:00:00Z", "queue": [], "cycles": [], "cost": 0, "accepted": [],
         "spentSince": "2026-10-06T09:00:00Z",
         "spent": [{"at": "2026-10-08T08:00:00Z", "seconds": 300, "gate": "Other"},
                   {"at": "2026-10-08T09:00:00Z", "seconds": 60, "gate": "Other", "doing": "Finding a card"},
                   {"at": "2026-10-08T09:01:00Z", "seconds": 240, "gate": "Other", "doing": "Reports"},
                   {"at": "2026-10-08T09:05:00Z", "seconds": 60, "gate": "Other", "doing": "Finding a card"},
                   {"at": "2026-10-08T09:06:00Z", "seconds": 120, "gate": "Review"}]}
        """;

    private const string Finding = """
        {"since": "2026-09-20T00:00:00Z", "queue": [], "cycles": [], "cost": 0, "accepted": [],
         "spentSince": "2026-10-06T09:00:00Z",
         "spent": [{"at": "2026-10-08T09:00:00Z", "seconds": 180, "gate": "Other", "doing": "Finding a card"}]}
        """;

    [Fact]
    public void Other_splits_into_what_the_time_between_cards_went_on_and_still_totals_it()
    {
        using var dialog = Open(("a-team", 4, Split), ("tuicode", null, Finding));

        dialog.Measure.Value = 3;

        Assert.Equal(
            [["Other", "11m", "3m"], ["  Finding a card", "2m", "3m"], ["  Reports", "4m", "–"], ["  Settings & other", "–", "–"]],
            Enumerable.Range(4, 4).Select(row => Cells(dialog.Gates, row, 0, 1, 2)));
        Assert.Equal(["Review", "2m", "0m"], Cells(dialog.Gates, 3, 0, 1, 2));
    }

    [Fact]
    public void Time_recorded_under_Questions_counts_as_Blocked()
    {
        using var dialog = Open(("a-team", 4, Timed.Replace("\"Other\"", "\"Questions\"")), ("tuicode", null, Nothing));

        dialog.Measure.Value = 3;

        Assert.Equal(["Blocked", "5m", "–"], Cells(dialog.Gates, 2, 0, 1, 2));
        Assert.Equal(["Other", "0m", "–"], Cells(dialog.Gates, 4, 0, 1, 2));
    }

    [Theory]
    [InlineData(140, 12)]
    [InlineData(120, 10)]
    public void The_dialog_is_wide_enough_for_every_column_and_narrower_the_last_ones_drop(int terminal, int columns)
    {
        var host = new View { Width = terminal, Height = 40 };
        using var dialog = Open(("a-team", 4, Timed), ("tuicode", null, Nothing));
        host.Add(dialog);
        host.Layout(new System.Drawing.Size(terminal, 40));

        var fit = dialog.Table.RowLayout.Fit(dialog.Table.Rows, dialog.Table.Viewport.Width);

        Assert.Equal(Headers(dialog.Table)[..columns], fit.Placed.Select(placed => placed.Column.Header));
    }

    [Fact]
    public void Wide_the_figures_and_their_headers_align_right()
    {
        using var dialog = Open(("a-team", 4, Recorded), ("tuicode", null, Nothing));

        var fit = dialog.Table.RowLayout.Fit(dialog.Table.Rows, 120);

        Assert.Equal("Team     Waiting now  A week ago", fit.Header![..32]);
        Assert.Equal("a-team             4          12", fit.Row(dialog.Rows[0])[..32]);
    }

    [Fact]
    public void Narrow_the_team_figures_drop_from_the_right_and_waiting_now_stays_longest()
    {
        using var dialog = Open(("a-team", 4, Recorded), ("tuicode", null, Nothing));

        Assert.Equal(["Team", "Waiting now", "A week ago", "Accepted (7d)"], Shown(dialog.Table, 50));
        Assert.Equal(["Team", "Waiting now"], Shown(dialog.Table, 22));
    }

    [Fact]
    public void Narrow_the_week_by_gate_cuts_its_row_names_before_it_drops_a_team()
    {
        using var dialog = Open(("a-team", 4, Timed), ("tuicode", null, Nothing));

        var fit = dialog.Gates.RowLayout.Fit(dialog.Gates.Rows, 30);

        Assert.Equal("This week      a-team  tuicode", fit.Header);
        Assert.Equal("  Settings &…       –        –", fit.Row(TeamRecord.SpentRows[^1]));
    }

    private static string[] Shown<T>(MentalDesk.Tui.Columns.ColumnTable<T> table, int width) =>
        [.. table.RowLayout.Fit(table.Rows, width).Placed.Select(placed => placed.Column.Header)];

    public static TheoryData<int, int> NameLengths() =>
        [.. Enumerable.Range(1, 24).SelectMany(length => new[] { (length, 0), (length, 3) })];

    [Theory]
    [MemberData(nameof(NameLengths))]
    public void Each_day_s_tick_sits_on_its_points_and_today_on_the_last_column(int nameLength, int measure)
    {
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        app.Driver!.SetScreenSize(160, 40);
        using var dialog = Open((new string('a', nameLength), 4, Recorded), ("tuicode", null, Nothing));
        app.Begin(dialog);
        dialog.Measure.Value = measure;
        dialog.Layout();
        dialog.Draw();

        var graph = dialog.Graph;
        var origin = graph.FrameToScreen();
        var axis = origin.Bottom - 1 - (int)graph.MarginBottom;
        var contents = app.Driver.Contents!;
        var ticks = Enumerable.Range(origin.X, origin.Width)
            .Where(x => contents[axis, x].Grapheme is "┼" or "┬").Select(x => x - origin.X).ToArray();
        var days = Enumerable.Range(0, TeamRecord.Days)
            .Select(day => graph.GraphSpaceToViewport(new System.Drawing.PointF(day * graph.AxisX.Increment, 0)).X).ToArray();

        Assert.True(graph.Viewport.Width - graph.MarginLeft >= TeamRecord.Days);
        Assert.Equal(days, ticks);
        Assert.Equal(graph.Viewport.Width - 1, days[^1]);
    }

    private static string[] Headers<T>(MentalDesk.Tui.Columns.ColumnTable<T> table) =>
        [.. table.RowLayout.Columns.Select(column => column.Header)];

    private static string[] Cells(TableView table, int row, params int[] columns) =>
        [.. columns.Select(column => table.Table![row, column].ToString()!)];

    [Fact]
    public void Esc_closes_it()
    {
        using var dialog = Open(("a-team", 4, Recorded));

        dialog.NewKeyDownEvent(Key.Esc);

        Assert.True(dialog.HasClosed);
    }

    [Fact]
    public void F1_lists_choosing_the_measure_and_Esc_Close_under_its_title()
    {
        using var dialog = Open(("a-team", 4, Recorded));

        var sheet = DialogTests.F1(dialog);

        Assert.Equal(dialog.Title, sheet.Here!.Heading);
        Assert.Equal([("←/→", "Measure"), ("Esc", "Close")], DialogTests.Rows(sheet));
    }
}
