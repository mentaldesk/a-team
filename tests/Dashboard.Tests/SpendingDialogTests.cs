using Terminal.Gui.Input;

namespace ATeam.Dashboard.Tests;

public class SpendingDialogTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private const string Recorded = """
        {"since": "2026-10-08T02:00:00Z",
         "runs": [{"started": "2026-10-14T02:00:00Z", "role": "lead", "cost": 2, "items": [81]},
                  {"started": "2026-10-14T03:00:00Z", "role": "dev", "cost": 5, "items": [517]},
                  {"started": "2026-10-01T03:00:00Z", "role": "dev", "cost": 40, "items": [517]},
                  {"started": "2026-10-14T04:00:00Z", "role": "lead", "cost": 1, "items": []}],
         "items": [{"number": 517, "title": "Spending", "url": "u517", "status": "Done", "parent": 81, "pitch": false, "theme": null},
                   {"number": 81, "title": "Bets", "url": "u81", "status": "Building", "parent": null, "pitch": true, "theme": "Think bigger"}]}
        """;

    private const string Cheap = """
        {"since": "2026-10-10T00:00:00Z", "runs": [{"started": "2026-10-14T02:00:00Z", "role": "lead", "cost": 0.5, "items": []}], "items": []}
        """;

    private const string Nothing = """{"since": null, "runs": [], "items": []}""";

    private static SpendingDialog Open(IEnumerable<string>? roles = null, Action<string, SpendingItem>? open = null,
        params (string Team, string Json)[] teams) => new(
        [.. teams.Select(team => team.Team)],
        Task.FromResult(teams.Select(team => new Reading(team.Json, null)).ToArray()),
        roles ?? ["lead", "dev"],
        Now,
        open);

    [Fact]
    public void It_opens_on_7_days_with_the_costliest_team_expanded_and_a_column_per_role()
    {
        using var dialog = Open(teams: [("tui", Cheap), ("a-team", Recorded)]);

        Assert.Equal(["7 days", "30 days", "Everything"], dialog.Period.Labels);
        Assert.Equal(0, dialog.Period.Value);
        Assert.True(dialog.Table.Visible);
        Assert.False(dialog.Message.Visible);
        Assert.Equal(["What", "Runs", "Lead $", "Dev $", "Total $"], dialog.Columns);
        Assert.Equal(["a-team", "tui"], dialog.Rows.Select(row => row.Team));
        Assert.Equal(["a-team", "Think bigger", SpendingRow.Discovery, SpendingRow.Other, "tui"], dialog.Shown.Select(row => row.Label));
        Assert.Equal("8.00", dialog.Table.Table![0, 4]);
        Assert.Equal("–", dialog.Table.Table[3, 3]);
        Assert.Equal("Costs at API prices, from " + new DateTimeOffset(2026, 10, 8, 2, 0, 0, TimeSpan.Zero).ToLocalTime().ToString("d MMM",
            System.Globalization.CultureInfo.InvariantCulture) + ".", dialog.Footer.Text);
    }

    [Fact]
    public void Choosing_a_longer_period_takes_in_older_runs_straight_away()
    {
        using var dialog = Open(teams: [("a-team", Recorded)]);

        dialog.Period.Value = 1;

        Assert.Equal(48, dialog.Rows[0].Total);
    }

    [Fact]
    public void A_role_a_team_has_on_gets_a_column()
    {
        using var dialog = Open(roles: ["lead", "dev", "reviewer"], teams: [("a-team", Recorded)]);

        Assert.Equal(["What", "Runs", "Lead $", "Dev $", "Reviewer $", "Total $"], dialog.Columns);
    }

    [Fact]
    public void Right_expands_a_row_and_Left_collapses_it()
    {
        using var dialog = Open(teams: [("a-team", Recorded)]);

        dialog.Table.SetSelection(0, 1, false);
        dialog.Table.NewKeyDownEvent(Key.CursorRight);
        Assert.Contains(dialog.Shown, row => row.Label == "#81 Bets");

        dialog.Table.NewKeyDownEvent(Key.CursorLeft);
        Assert.DoesNotContain(dialog.Shown, row => row.Label == "#81 Bets");
    }

    [Fact]
    public void Enter_on_a_pitch_opens_it_and_on_any_other_row_does_nothing()
    {
        var opened = new List<(string Team, int Number)>();
        using var dialog = Open(open: (team, pitch) => opened.Add((team, pitch.Number)), teams: [("a-team", Recorded)]);

        dialog.Table.SetSelection(0, 1, false);
        dialog.Table.NewKeyDownEvent(Key.CursorRight);
        dialog.Table.NewKeyDownEvent(Key.Enter);
        dialog.Table.SetSelection(0, 2, false);
        dialog.Table.NewKeyDownEvent(Key.Enter);

        Assert.Equal([("a-team", 81)], opened);
    }

    [Fact]
    public void With_no_runs_in_the_period_it_says_so_in_place_of_the_table()
    {
        using var dialog = Open(teams: [("a-team", Nothing)]);

        Assert.False(dialog.Table.Visible);
        Assert.True(dialog.Message.Visible);
        Assert.Equal("No runs in the last 7 days.", dialog.Message.Text);
    }

    [Fact]
    public void A_record_that_can_t_be_read_says_why()
    {
        using var dialog = new SpendingDialog(["a-team"], Task.FromResult(new[] { new Reading("", "no history") }), ["lead", "dev"], Now);

        Assert.Equal("Couldn't read the record: no history", dialog.Message.Text);
        Assert.False(dialog.Table.Visible);
    }

    [Fact]
    public void Esc_closes_it()
    {
        using var dialog = Open(teams: [("a-team", Recorded)]);

        dialog.NewKeyDownEvent(Key.Esc);

        Assert.True(dialog.HasClosed);
    }
}
