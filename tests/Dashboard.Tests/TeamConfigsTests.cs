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

    [Fact]
    public void A_team_with_no_config_at_all_reads_as_paused() =>
        Assert.True(new TeamConfigs(_root).IsPaused("demo"));

    private void Write(string team, string config, string extension = ".json")
    {
        var teams = Path.Combine(_root, "teams");
        Directory.CreateDirectory(teams);
        File.WriteAllText(Path.Combine(teams, team + extension), config);
    }
}
