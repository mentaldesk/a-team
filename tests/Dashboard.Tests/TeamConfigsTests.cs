namespace ATeam.Dashboard.Tests;

public class TeamConfigsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"a-team-{Guid.NewGuid():n}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Every_configured_team_is_listed_in_name_order_paused_or_not()
    {
        Write("zulu", """{"dispatch": {"enabled": true}}""");
        Write("alpha", """{"dispatch": {"enabled": false}}""");
        Write("notes", "not a team", extension: ".txt");

        Assert.Equal(["alpha", "zulu"], new TeamConfigs(_root).Names());
    }

    [Fact]
    public void A_config_directory_that_isn_t_there_has_no_teams() =>
        Assert.Empty(new TeamConfigs(_root).Names());

    [Theory]
    [InlineData("""{"dispatch": {"enabled": true}}""", false)]
    [InlineData("""{"dispatch": {"enabled": false}}""", true)]
    [InlineData("""{"dispatch": {}}""", true)]
    [InlineData("{}", true)]
    [InlineData("{ not json", true)]
    public void Paused_is_whatever_the_dispatcher_skips(string config, bool paused)
    {
        Write("demo", config);

        Assert.Equal(paused, new TeamConfigs(_root).IsPaused("demo"));
    }

    [Theory]
    [InlineData("""{"dispatch": {"enabled": true, "hold": ["dev"]}}""", "dev", true)]
    [InlineData("""{"dispatch": {"enabled": true, "hold": ["dev"]}}""", "lead", false)]
    [InlineData("""{"dispatch": {"enabled": true, "hold": []}}""", "dev", false)]
    [InlineData("""{"dispatch": {"enabled": true}}""", "dev", false)]
    [InlineData("""{"dispatch": {"hold": "dev"}}""", "dev", false)]
    [InlineData("{ not json", "dev", false)]
    public void Held_is_a_role_named_in_dispatch_hold(string config, string role, bool held)
    {
        Write("demo", config);

        Assert.Equal(held, new TeamConfigs(_root).IsHeld("demo", role));
    }

    [Theory]
    [InlineData("""{"roles": {"customer": true}}""", new[] { "lead", "dev", "customer" })]
    [InlineData("""{"roles": {"customer": false}}""", new[] { "lead", "dev" })]
    [InlineData("{}", new[] { "lead", "dev" })]
    [InlineData("{ not json", new[] { "lead", "dev" })]
    public void A_team_runs_a_Lead_and_a_Dev_and_a_Customer_lead_only_where_it_is_on(string config, string[] roles)
    {
        Write("demo", config);

        Assert.Equal(roles, new TeamConfigs(_root).Roles("demo"));
    }

    [Fact]
    public void A_team_with_no_config_at_all_reads_as_paused() =>
        Assert.True(new TeamConfigs(_root).IsPaused("demo"));

    [Theory]
    [InlineData(
        "{\n  \"repo\": \"o/r\",\n  \"dispatch\": { \"enabled\": true, \"hold\": [\"dev\"] },\n  \"later\": 1\n}\n",
        false,
        "{\n  \"repo\": \"o/r\",\n  \"dispatch\": { \"enabled\": false, \"hold\": [\"dev\"] },\n  \"later\": 1\n}\n")]
    [InlineData(
        "{\n  \"dispatch\": {\n    \"retryAfter\": 60\n  }\n}",
        true,
        "{\n  \"dispatch\": {\n    \"enabled\": true,\n    \"retryAfter\": 60\n  }\n}")]
    [InlineData(
        "{\n    \"repo\": \"o/r\"\n}",
        true,
        "{\n    \"dispatch\": {\"enabled\": true},\n    \"repo\": \"o/r\"\n}")]
    [InlineData("{}", false, "{\"dispatch\": {\"enabled\": false}}")]
    [InlineData("""{"dispatch": {}}""", true, """{"dispatch": {"enabled": true}}""")]
    [InlineData("""{"dispatch": false}""", true, """{"dispatch": {"enabled": true}}""")]
    [InlineData("""{"x": {"dispatch": 1}, "dispatch": {"enabled": "yes"}}""", true, """{"x": {"dispatch": 1}, "dispatch": {"enabled": true}}""")]
    public void Starting_or_pausing_a_team_changes_dispatch_enabled_and_nothing_else(string before, bool working, string after)
    {
        Write("demo", before);

        new TeamConfigs(_root).SetWorking("demo", working);

        Assert.Equal(after, File.ReadAllText(Path.Combine(_root, "teams", "demo.json")));
        Assert.Equal(!working, new TeamConfigs(_root).IsPaused("demo"));
    }

    [Fact]
    public void A_file_that_does_not_parse_is_left_as_it_was()
    {
        Write("demo", "{\"dispatch\": {\"enabled\": true},}");

        Assert.ThrowsAny<System.Text.Json.JsonException>(() => new TeamConfigs(_root).SetWorking("demo", false));
        Assert.Equal("{\"dispatch\": {\"enabled\": true},}", File.ReadAllText(Path.Combine(_root, "teams", "demo.json")));
    }

    [Fact]
    public void A_row_carries_the_repo_and_whether_the_team_is_paused()
    {
        Write("demo", """{"repo": "mentaldesk/demo", "dispatch": {"enabled": true}}""");

        Assert.Equal(new TeamRow("demo", "mentaldesk/demo", false, null), new TeamConfigs(_root).Row("demo"));
    }

    [Theory]
    [InlineData("{\n  \"repo\": \"o/r\",\n}", "line 3: ")]
    [InlineData("{\n\n  \"repo\" \"o/r\"\n}", "line 3: ")]
    [InlineData("[]", "it isn't a JSON object")]
    public void A_row_for_a_file_that_does_not_parse_says_where_and_why(string config, string problem)
    {
        Write("demo", config);

        var row = new TeamConfigs(_root).Row("demo");

        Assert.StartsWith(problem, row.Problem);
        Assert.DoesNotContain("LineNumber", row.Problem);
        Assert.True(row.Paused);
    }

    [Fact]
    public void Removing_a_team_moves_its_file_aside_and_leaves_its_state_alone()
    {
        Write("alpha", "{}");
        Write("beta", """{"repo": "o/beta"}""");
        var state = Path.Combine(_root, "state", "beta", "dev");
        Directory.CreateDirectory(state);
        var teams = new TeamConfigs(_root);

        Assert.Equal("beta.json.removed", teams.Remove("beta"));

        Assert.Equal(["alpha"], teams.Names());
        Assert.Equal("""{"repo": "o/beta"}""", File.ReadAllText(Path.Combine(_root, "teams", "beta.json.removed")));
        Assert.True(Directory.Exists(state));
    }

    [Fact]
    public void Removing_a_team_again_keeps_the_older_removed_file()
    {
        Write("beta", "older", extension: ".json.removed");
        Write("beta", "newer");
        var teams = new TeamConfigs(_root);

        Assert.Equal("beta.json.removed.2", teams.RemovedName("beta"));
        Assert.Equal("beta.json.removed.2", teams.Remove("beta"));

        Assert.Equal("older", File.ReadAllText(Path.Combine(_root, "teams", "beta.json.removed")));
        Assert.Equal("newer", File.ReadAllText(Path.Combine(_root, "teams", "beta.json.removed.2")));
    }

    private void Write(string team, string config, string extension = ".json")
    {
        var teams = Path.Combine(_root, "teams");
        Directory.CreateDirectory(teams);
        File.WriteAllText(Path.Combine(teams, team + extension), config);
    }
}
