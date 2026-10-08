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
        var record = new TeamRecord(On(5, 9), [new QueueCount(On(5, 9), 4), new QueueCount(On(5, 20), 6), new QueueCount(On(7, 9), 3)], [], 0);

        Assert.Equal([.. Enumerable.Repeat<int?>(null, 10), 6, null, 3, null], record.Series(TrendMeasure.Waiting, Dates, Zone));
    }

    [Fact]
    public void Accepted_counts_each_day_from_the_record_s_first_with_none_as_zero()
    {
        var record = new TeamRecord(On(5, 9), [], [On(5, 10), On(5, 11), On(8, 1)], 0);

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
            [Now.AddDays(-8), Now.AddDays(-2), Now.AddHours(-1)], 12.5m);

        Assert.Equal(new TeamTrendRow("demo", 7, 12, 2, 12.5m), TeamTrendRow.Of("demo", 7, record, Now));
        Assert.Equal(9, TeamTrendRow.Of("demo", null, record, Now).WaitingNow);
    }

    [Fact]
    public void Without_a_week_of_counts_or_any_runs_a_row_has_no_week_ago_and_costs_nothing()
    {
        var row = TeamTrendRow.Of("demo", null, new TeamRecord(Now.AddDays(-2), [], [], 0), Now);

        Assert.Equal(new TeamTrendRow("demo", null, null, 0, 0), row);
    }

    [Fact]
    public void Reads_what_the_board_reports()
    {
        var record = Read("""
            {"since": "2026-10-01T00:00:00Z", "queue": [{"at": "2026-10-02T00:00:00Z", "waiting": 4}],
             "accepted": ["2026-10-03T00:00:00Z"], "cost": 1.25}
            """)!;

        Assert.Equal(DateTimeOffset.Parse("2026-10-01T00:00:00Z"), record.Since);
        Assert.Equal([new QueueCount(DateTimeOffset.Parse("2026-10-02T00:00:00Z"), 4)], record.Queue);
        Assert.Equal([DateTimeOffset.Parse("2026-10-03T00:00:00Z")], record.Accepted);
        Assert.Equal(1.25m, record.Cost);
        Assert.Null(Read("""{"since": null, "queue": [], "accepted": [], "cost": 0}""")!.Since);
    }

    [Fact]
    public void A_failed_or_unreadable_read_is_no_record()
    {
        Assert.Null(TeamRecord.Of(new Reading("", "board.sh: rate limited")));
        Assert.Null(Read("[]"));
        Assert.Null(Read("not json"));
    }
}
