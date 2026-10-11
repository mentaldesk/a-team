namespace ATeam.Dashboard.Tests;

public class WorkTrendTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private static WorkTrend? Read(string json) => WorkTrend.Of(new Reading(json, null));

    [Fact]
    public void A_record_a_week_old_compares_with_a_week_ago_and_counts_accepted_in_7_days()
    {
        var trends = new[] { new WorkTrend(Now.AddDays(-20), 9, 17), new WorkTrend(Now.AddDays(-8), 3, 6) };

        Assert.Equal("Work · 7 waiting on you (12 a week ago) · 23 accepted in 7 days", WorkTrend.Title(7, trends, Now));
    }

    [Fact]
    public void Until_the_record_holds_a_week_the_comparison_is_left_out_and_accepted_counts_the_days_it_has()
    {
        var trends = new[] { new WorkTrend(Now.AddDays(-1.5), null, 3) };

        Assert.Equal("Work · 7 waiting on you · 3 accepted in 2 days", WorkTrend.Title(7, trends, Now));
    }

    [Fact]
    public void A_record_begun_today_counts_accepted_in_1_day()
    {
        var trends = new[] { new WorkTrend(Now.AddHours(-2), null, 0) };

        Assert.Equal("Work · 7 waiting on you · 0 accepted in 1 day", WorkTrend.Title(7, trends, Now));
    }

    [Fact]
    public void One_team_without_a_week_of_record_leaves_the_comparison_out_for_all_of_them()
    {
        var trends = new[] { new WorkTrend(Now.AddDays(-20), 9, 17), new WorkTrend(Now.AddDays(-2), null, 1) };

        Assert.Equal("Work · 7 waiting on you · 18 accepted in 7 days", WorkTrend.Title(7, trends, Now));
    }

    [Fact]
    public void With_nothing_recorded_the_title_shows_only_what_is_waiting_now()
    {
        Assert.Equal("Work · 7 waiting on you", WorkTrend.Title(7, [new WorkTrend(null, null, 0)], Now));
        Assert.Equal("Work · 7 waiting on you", WorkTrend.Title(7, [], Now));
    }

    [Fact]
    public void Once_your_time_is_recorded_the_title_ends_with_your_week_s_hours_and_accepted_per_hour()
    {
        var trends = new[]
        {
            new WorkTrend(Now.AddDays(-20), 9, 17, Now.AddDays(-3), TimeSpan.FromMinutes(125)),
            new WorkTrend(Now.AddDays(-8), 3, 6, Now.AddDays(-1), TimeSpan.FromMinutes(65)),
        };

        Assert.Equal("Work · 7 waiting on you (12 a week ago) · 23 accepted in 7 days · 3h 10m of yours · 7.3 per hour",
            WorkTrend.Title(7, trends, Now));
    }

    [Fact]
    public void Until_your_time_is_recorded_the_title_says_neither_and_under_a_minute_has_no_rate()
    {
        Assert.Equal("Work · 7 waiting on you · 3 accepted in 2 days",
            WorkTrend.Title(7, [new WorkTrend(Now.AddDays(-1.5), null, 3)], Now));
        Assert.Equal("Work · 7 waiting on you · 3 accepted in 2 days · 0m of yours",
            WorkTrend.Title(7, [new WorkTrend(Now.AddDays(-1.5), null, 3, Now.AddDays(-1), TimeSpan.FromSeconds(20))], Now));
    }

    [Fact]
    public void Before_the_first_read_the_title_is_just_Work()
    {
        Assert.Equal("Work", WorkTrend.Title(null, [new WorkTrend(Now.AddDays(-20), 9, 17)], Now));
    }

    [Fact]
    public void Reads_what_the_board_reports()
    {
        Assert.Equal(new WorkTrend(Now, 12, 23), Read("""{"since": "2026-10-08T12:00:00Z", "weekAgo": 12, "accepted": 23}"""));
        Assert.Equal(new WorkTrend(null, null, 0), Read("""{"since": null, "weekAgo": null, "accepted": 0}"""));
        Assert.Equal(new WorkTrend(Now, 12, 23, Now, TimeSpan.FromMinutes(6)),
            Read("""{"since": "2026-10-08T12:00:00Z", "weekAgo": 12, "accepted": 23, "spentSince": "2026-10-08T12:00:00Z", "spent": 360}"""));
    }

    [Fact]
    public void A_failed_or_unreadable_read_is_no_trend()
    {
        Assert.Null(WorkTrend.Of(new Reading("", "board.sh: rate limited")));
        Assert.Null(Read("[]"));
        Assert.Null(Read("not json"));
    }
}
