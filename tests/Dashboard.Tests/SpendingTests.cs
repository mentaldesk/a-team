namespace ATeam.Dashboard.Tests;

public class SpendingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private static SpendingRun Run(string role, decimal cost, params int[] items) => Run(Now.AddHours(-1), role, cost, items);

    private static SpendingRun Run(DateTimeOffset started, string role, decimal cost, params int[] items) => new(started, role, cost, items);

    private static SpendingItem Pitch(int number, string? theme = null) =>
        new(number, $"Pitch {number}", $"https://github.com/o/r/issues/{number}", "Building", true, null, theme);

    private static SpendingItem Task(int number, int? parent) =>
        new(number, $"Task {number}", $"https://github.com/o/r/issues/{number}", "Done", false, parent, null);

    private static SpendingRow Team(IReadOnlyList<SpendingRun> runs, IReadOnlyList<SpendingItem> items, DateTimeOffset? from = null) =>
        Assert.Single(Spending.Roll([("demo", new TeamSpending(Now.AddDays(-10), runs, items))], from));

    private static SpendingRow Child(SpendingRow row, string label) => Assert.Single(row.Children, child => child.Label == label);

    [Fact]
    public void A_task_s_runs_count_toward_its_pitch_under_the_pitch_s_theme()
    {
        var team = Team([Run("lead", 2, 81), Run("dev", 5, 517), Run("dev", 1, 518)],
            [Pitch(81, "Consistently good UX"), Task(517, 81), Task(518, 81)]);

        var theme = Child(team, "Consistently good UX");
        var pitch = Child(theme, "#81 Pitch 81");
        Assert.Equal(SpendingKind.Pitch, pitch.Kind);
        Assert.Equal(81, pitch.Pitch?.Number);
        Assert.Equal(3, pitch.Runs);
        Assert.Equal(2, pitch.Spent("lead"));
        Assert.Equal(6, pitch.Spent("dev"));
        Assert.Equal(8, pitch.Total);
        Assert.Equal(8, theme.Total);
    }

    [Fact]
    public void A_run_on_several_items_is_split_evenly_and_counted_once_in_each_row_it_reaches()
    {
        var team = Team([Run("lead", 3, 81, 82, 90)], [Pitch(81, "A"), Pitch(82, "B")]);

        Assert.Equal(1, Child(Child(team, "A"), "#81 Pitch 81").Spent("lead"));
        Assert.Equal(1, Child(team, "B").Total);
        Assert.Equal(1, Child(team, SpendingRow.Other).Total);
        Assert.Equal(1, Child(team, "A").Runs);
        Assert.Equal(1, team.Runs);
        Assert.Equal(3, team.Total);
    }

    [Fact]
    public void Runs_on_no_item_are_discovery_and_runs_on_ideas_or_unknown_items_are_other()
    {
        var team = Team([Run("lead", 4), Run("lead", 2, 40), Run("dev", 1, 41), Run("lead", 0.5m, 999)],
            [new SpendingItem(40, "An idea", "", "Idea", false, null, null), Task(41, 40)]);

        var discovery = Child(team, SpendingRow.Discovery);
        Assert.Equal(SpendingKind.Discovery, discovery.Kind);
        Assert.Equal(4, discovery.Total);
        var other = Child(team, SpendingRow.Other);
        Assert.Equal(3.5m, other.Total);
        Assert.Equal(3, other.Runs);
        Assert.Equal([SpendingRow.Discovery, SpendingRow.Other], team.Children.Select(child => child.Label));
    }

    [Fact]
    public void Totals_add_up_at_every_level()
    {
        var team = Team([Run("lead", 1.1m, 81), Run("dev", 2.2m, 82), Run("dev", 3.3m, 83), Run("lead", 0.7m), Run("customer", 0.9m, 7),
                Run("reviewer", 1.3m, 84, 81)],
            [Pitch(81, "A"), Pitch(82, "A"), Pitch(83), Task(84, 83)]);

        Assert.Equal(team.Total, team.Children.Sum(child => child.Total));
        foreach (var row in team.Children.Where(child => child.Children.Count > 0))
            Assert.Equal(row.Total, row.Children.Sum(child => child.Total));
        foreach (var role in new[] { "lead", "dev", "customer", "reviewer" })
            Assert.Equal(team.Spent(role), team.Children.Sum(child => child.Spent(role)));
        Assert.Equal(9.5m, team.Total);
        Assert.Equal(6, team.Runs);
    }

    [Fact]
    public void Themes_are_ordered_by_total_then_no_theme_then_the_fixed_rows_and_pitches_by_total()
    {
        var team = Team([Run("dev", 1, 81), Run("dev", 5, 82), Run("dev", 9, 83), Run("dev", 2, 84), Run("dev", 4, 85)],
            [Pitch(81, "Small"), Pitch(82, "Big"), Pitch(83), Pitch(84, "Big"), Pitch(85, "Big")]);

        Assert.Equal(["Big", "Small", SpendingRow.NoTheme, SpendingRow.Discovery, SpendingRow.Other], team.Children.Select(child => child.Label));
        Assert.Equal(SpendingKind.NoTheme, team.Children[2].Kind);
        Assert.Equal(["#82 Pitch 82", "#85 Pitch 85", "#84 Pitch 84"], team.Children[0].Children.Select(child => child.Label));
    }

    [Fact]
    public void A_label_naming_a_theme_no_longer_in_the_vision_still_groups_under_its_label()
    {
        var team = Team([Run("dev", 1, 81), Run("dev", 1, 82)], [Pitch(81, "Retired theme"), Pitch(82, "Retired theme")]);

        Assert.Equal(2, Child(team, "Retired theme").Children.Count);
    }

    [Fact]
    public void Teams_are_ordered_by_total_and_a_team_with_no_runs_in_the_period_is_left_out()
    {
        var rows = Spending.Roll([
            ("cheap", new TeamSpending(Now, [Run("lead", 1)], [])),
            ("dear", new TeamSpending(Now, [Run("lead", 7)], [])),
            ("idle", new TeamSpending(Now, [Run(Now.AddDays(-8), "lead", 50)], [])),
        ], Spending.From(0, Now));

        Assert.Equal(["dear", "cheap"], rows.Select(row => row.Team));
    }

    [Fact]
    public void Periods_take_runs_started_after_their_start_as_Trends_does()
    {
        var runs = new[]
        {
            Run(Now.AddDays(-7), "dev", 1), Run(Now.AddDays(-7).AddSeconds(1), "dev", 2),
            Run(Now.AddDays(-30), "dev", 4), Run(Now.AddDays(-30).AddSeconds(1), "dev", 8), Run(Now.AddDays(-90), "dev", 16),
        };

        Assert.Equal(2, Team(runs, [], Spending.From(0, Now)).Total);
        Assert.Equal(1 + 2 + 8, Team(runs, [], Spending.From(1, Now)).Total);
        Assert.Equal(31, Team(runs, [], Spending.From(2, Now)).Total);
        Assert.Null(Spending.From(2, Now));
    }

    [Fact]
    public void Lead_and_Dev_always_have_a_column_and_the_others_only_when_a_team_has_them_on_or_spent_on_them()
    {
        var rows = Spending.Roll([("demo", new TeamSpending(Now, [Run("lead", 1), Run("reviewer", 1)], []))], null);

        Assert.Equal(["lead", "dev"], Spending.Roles(["lead", "dev"], []));
        Assert.Equal(["lead", "dev", "customer"], Spending.Roles(["lead", "dev", "customer"], []));
        Assert.Equal(["lead", "dev", "reviewer"], Spending.Roles(["lead", "dev"], rows));
    }

    [Fact]
    public void The_board_s_reading_parses_into_runs_and_items()
    {
        var spending = TeamSpending.Of(new Reading("""
            {"since": "2026-10-08T02:01:45Z",
             "runs": [{"started": "2026-10-08T02:01:45Z", "role": "lead", "cost": 1.5, "items": []},
                      {"started": "2026-10-09T02:01:45Z", "role": "dev", "cost": 0, "items": [517]}],
             "items": [{"number": 517, "title": "Spending", "url": "u", "status": "In progress", "parent": 81, "pitch": false, "theme": null},
                       {"number": 81, "title": "Bets", "url": "v", "status": "Building", "parent": null, "pitch": true, "theme": "Think bigger"}]}
            """, null));

        Assert.NotNull(spending);
        Assert.Equal(new DateTimeOffset(2026, 10, 8, 2, 1, 45, TimeSpan.Zero), spending.Since);
        Assert.Equal([1.5m, 0m], spending.Runs.Select(run => run.Cost));
        Assert.Equal([517], spending.Runs[1].Items);
        Assert.Equal(new SpendingItem(81, "Bets", "v", "Building", true, null, "Think bigger"), spending.Items[1]);
        Assert.Equal(81, spending.Items[0].Parent);
        Assert.Null(TeamSpending.Of(new Reading("{}", null)));
        Assert.Null(TeamSpending.Of(new Reading("", "gh: rate limited")));
    }
}
