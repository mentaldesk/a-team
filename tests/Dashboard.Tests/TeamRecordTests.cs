namespace ATeam.Dashboard.Tests;

public class TeamRecordTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("NZ", TimeSpan.FromHours(13), "NZ", "NZ");
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.FromHours(13));
    private static readonly IReadOnlyList<DateOnly> Dates = TeamRecord.Dates(Now, Zone);

    private static DateTimeOffset On(int day, int hour) => new(2026, 10, day, hour, 0, 0, TimeSpan.FromHours(13));

    private static TeamRecord? Read(string json) => TeamRecord.Of(new Reading(json, null));

    [Fact]
    public void The_chart_covers_the_last_14_days_in_the_local_zone_today_last()
    {
        Assert.Equal(14, Dates.Count);
        Assert.Equal(new DateOnly(2026, 9, 25), Dates[0]);
        Assert.Equal(new DateOnly(2026, 10, 8), Dates[^1]);
        Assert.Equal(new DateOnly(2026, 10, 8), TeamRecord.Dates(new DateTimeOffset(2026, 10, 7, 23, 0, 0, TimeSpan.Zero), Zone)[^1]);
    }

    [Fact]
    public void Waiting_is_each_day_s_last_count_blank_before_the_record_began_and_on_a_day_with_no_count()
    {
        var record = new TeamRecord(On(5, 9), [new QueueCount(On(5, 9), 4), new QueueCount(On(5, 20), 6), new QueueCount(On(7, 9), 3)], [], [], 0);

        Assert.Equal([.. Enumerable.Repeat<int?>(null, 10), 6, null, 3, null], record.Series(TrendMeasure.Waiting, Dates, Zone));
    }

    [Fact]
    public void Accepted_counts_each_day_from_the_record_s_first_with_none_as_zero()
    {
        var record = new TeamRecord(On(5, 9), [], [On(5, 10), On(5, 11), On(8, 1)], [], 0);

        Assert.Equal([.. Enumerable.Repeat<int?>(null, 10), 2, 0, 0, 1], record.Series(TrendMeasure.Accepted, Dates, Zone));
    }

    [Fact]
    public void A_team_with_no_record_is_blank_every_day()
    {
        Assert.All(TeamRecord.Empty.Series(TrendMeasure.Accepted, Dates, Zone), Assert.Null);
        Assert.All(TeamRecord.Empty.Series(TrendMeasure.Waiting, Dates, Zone), Assert.Null);
    }

    [Fact]
    public void A_row_takes_what_s_waiting_from_Work_a_week_ago_as_the_title_does_and_accepted_in_7_days()
    {
        var record = new TeamRecord(Now.AddDays(-20),
            [new QueueCount(Now.AddDays(-9), 20), new QueueCount(Now.AddDays(-7.5), 12), new QueueCount(Now.AddHours(-1), 9)],
            [Now.AddDays(-8), Now.AddDays(-2), Now.AddHours(-1)], [], 12.5m);

        Assert.Equal(new TeamTrendRow("demo", 7, 12, 2, 12.5m, null, null), TeamTrendRow.Of("demo", 7, record, Now));
        Assert.Equal(9, TeamTrendRow.Of("demo", null, record, Now).WaitingNow);
    }

    [Fact]
    public void Without_a_week_of_counts_or_any_runs_a_row_has_no_week_ago_and_costs_nothing()
    {
        var row = TeamTrendRow.Of("demo", null, new TeamRecord(Now.AddDays(-2), [], [], [], 0), Now);

        Assert.Equal(new TeamTrendRow("demo", null, null, 0, 0, null, null), row);
    }

    [Fact]
    public void The_all_row_sums_the_teams_leaves_a_count_none_has_blank_and_takes_the_cycles_of_them_all()
    {
        TeamTrendRow[] rows = [new("a", 3, null, 2, 1.5m, 2, 0.5), new("b", null, null, 1, 2.25m, null, null), new("c", 4, null, 0, 0, null, null)];
        Cycle[] week = [Took(Now, 2, 1), Took(Now, 6, 3), Took(Now, 10, 5)];

        Assert.Equal(new TeamTrendRow("All", 7, null, 3, 3.75m, 6, 0.5), TeamTrendRow.All(rows, week));
        Assert.Equal(new TeamTrendRow("All", 7, null, 3, 3.75m, null, null), TeamTrendRow.All(rows, []));
    }

    private static Cycle Took(DateTimeOffset accepted, double hours, double inReview) =>
        new(accepted.AddHours(-hours), accepted, TimeSpan.FromHours(inReview));

    [Fact]
    public void Hours_to_accept_is_each_day_s_median_blank_on_a_day_with_nothing_accepted()
    {
        var record = new TeamRecord(On(5, 9), [], [],
            [Took(On(5, 10), 3, 1), Took(On(6, 10), 4, 1), Took(On(6, 11), 9, 1), Took(On(6, 12), 20, 1), Took(On(8, 23), 5.6, 1)], 0);

        Assert.Equal([.. Enumerable.Repeat<int?>(null, 10), 3, 9, null, 6], record.Series(TrendMeasure.Cycle, Dates, Zone));
    }

    [Fact]
    public void A_day_is_the_local_one_the_task_was_accepted_on_however_long_it_took()
    {
        var record = new TeamRecord(On(1, 9), [], [], [Took(On(7, 23).AddMinutes(30), 30, 1), Took(On(8, 0).AddMinutes(30), 2, 1)], 0);

        Assert.Equal([30, 2], record.Series(TrendMeasure.Cycle, Dates, Zone).TakeLast(2));
    }

    [Fact]
    public void The_median_of_an_even_count_is_the_mean_of_the_middle_two_and_of_one_task_its_own()
    {
        Assert.Equal(5, Cycle.Median([Took(Now, 1, 0), Took(Now, 4, 0), Took(Now, 6, 0), Took(Now, 30, 0)]));
        Assert.Equal(8.4, Cycle.Median([Took(Now, 8.4, 0)])!.Value, 6);
        Assert.Null(Cycle.Median([]));
    }

    [Fact]
    public void With_you_is_the_share_of_all_the_cycles_time_spent_in_review()
    {
        Assert.Equal(0.75, Cycle.WithYou([Took(Now, 2, 2), Took(Now, 6, 4)])!.Value, 6);
        Assert.Null(Cycle.WithYou([]));
    }

    [Fact]
    public void A_row_takes_the_cycles_accepted_in_the_last_7_days()
    {
        var record = new TeamRecord(Now.AddDays(-20), [], [], [Took(Now.AddDays(-8), 100, 100), Took(Now.AddHours(-1), 8, 6), Took(Now.AddDays(-2), 4, 0)], 0);

        var row = TeamTrendRow.Of("demo", null, record, Now);

        Assert.Equal(6, row.Cycle);
        Assert.Equal(0.5, row.WithYou!.Value, 6);
    }

    [Fact]
    public void Reads_what_the_board_reports()
    {
        var record = Read("""
            {"since": "2026-10-01T00:00:00Z", "queue": [{"at": "2026-10-02T00:00:00Z", "waiting": 4}],
             "accepted": ["2026-10-03T00:00:00Z"],
             "cycles": [{"ready": "2026-10-02T00:00:00Z", "accepted": "2026-10-03T00:00:00Z", "review": 5400}], "cost": 1.25}
            """)!;

        Assert.Equal(DateTimeOffset.Parse("2026-10-01T00:00:00Z"), record.Since);
        Assert.Equal([new QueueCount(DateTimeOffset.Parse("2026-10-02T00:00:00Z"), 4)], record.Queue);
        Assert.Equal([DateTimeOffset.Parse("2026-10-03T00:00:00Z")], record.Accepted);
        Assert.Equal([new Cycle(DateTimeOffset.Parse("2026-10-02T00:00:00Z"), DateTimeOffset.Parse("2026-10-03T00:00:00Z"), TimeSpan.FromMinutes(90))], record.Cycles);
        Assert.Equal(1.25m, record.Cost);
        Assert.Null(Read("""{"since": null, "queue": [], "accepted": [], "cycles": [], "cost": 0}""")!.Since);
    }

    [Fact]
    public void A_failed_or_unreadable_read_is_no_record()
    {
        Assert.Null(TeamRecord.Of(new Reading("", "board.sh: rate limited")));
        Assert.Null(Read("[]"));
        Assert.Null(Read("not json"));
    }
}
