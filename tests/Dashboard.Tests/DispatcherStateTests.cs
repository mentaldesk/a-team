namespace ATeam.Dashboard.Tests;

public class DispatcherStateTests : IDisposable
{
    private const string Home = "/Users/me";
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"a-team-{Guid.NewGuid():n}");

    public DispatcherStateTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_live_dispatcher_is_named_by_its_binary_and_version_with_the_next_pass()
    {
        Install("/opt/homebrew/bin/a-team", "0.1.12");
        NextPass(TimeSpan.FromSeconds(72));

        var state = Read();

        Assert.Equal("dispatcher · /opt/homebrew/bin/a-team 0.1.12 · next pass 1:12", state.Title());
        Assert.False(state.Error);
    }

    [Fact]
    public void A_dispatcher_installed_from_a_worktree_is_named_by_its_own_path_under_home()
    {
        Install($"{Home}/code/a-team/palette/bin/a-team", "0.1.13-alpha.0.7");
        NextPass(TimeSpan.FromSeconds(64));

        Assert.Equal("dispatcher · ~/code/a-team/palette/bin/a-team 0.1.13-alpha.0.7 · next pass 1:04", Read().Title());
    }

    [Fact]
    public void A_dry_run_dispatcher_says_nothing_will_start_in_the_Error_scheme()
    {
        Install("/opt/homebrew/bin/a-team", "0.1.12", dryRun: true);
        NextPass(TimeSpan.FromSeconds(48), "dry-next-pass");

        var state = Read();

        Assert.Equal("dispatcher · dry run: nothing will actually start · next pass 0:48", state.Title());
        Assert.True(state.Error);
    }

    [Fact]
    public void A_live_next_pass_left_from_before_says_nothing_about_a_dry_run_dispatcher()
    {
        Install("/opt/homebrew/bin/a-team", "0.1.12", dryRun: true, installedAt: Now - TimeSpan.FromHours(1));
        NextPass(TimeSpan.FromSeconds(48));

        Assert.Equal("dispatcher · stopped 1h00m ago", Read().Title());
    }

    [Fact]
    public void A_dispatcher_with_no_pass_for_the_interval_and_a_minute_has_stopped_in_the_Error_scheme()
    {
        Install("/opt/homebrew/bin/a-team", "0.1.12", installedAt: Now - TimeSpan.FromHours(1));
        NextPass(TimeSpan.FromSeconds(120) - TimeSpan.FromMinutes(14));

        var state = Read();

        Assert.Equal("dispatcher · stopped 14m ago", state.Title());
        Assert.True(state.Error);
    }

    [Fact]
    public void A_pass_just_overdue_is_not_yet_stopped()
    {
        Install("/opt/homebrew/bin/a-team", "0.1.12", installedAt: Now - TimeSpan.FromHours(1));
        NextPass(-TimeSpan.FromSeconds(30));

        Assert.Equal("dispatcher · /opt/homebrew/bin/a-team 0.1.12 · next pass 0:00", Read().Title());
    }

    [Fact]
    public void Nothing_installed_says_how_to_install_it_in_the_Error_scheme()
    {
        var state = Read();

        Assert.Equal("dispatcher · nothing installed · run: a-team install", state.Title());
        Assert.True(state.Error);
    }

    [Fact]
    public void A_dispatcher_installed_before_the_record_existed_keeps_the_plain_title_while_it_passes()
    {
        NextPass(TimeSpan.FromSeconds(30));

        var state = Read();

        Assert.Equal("dispatcher", state.Title());
        Assert.False(state.Error);
    }

    [Fact]
    public void Without_a_record_a_stale_next_pass_is_nothing_installed()
    {
        NextPass(-TimeSpan.FromMinutes(10));

        Assert.Equal("dispatcher · nothing installed · run: a-team install", Read().Title());
    }

    [Fact]
    public void A_narrow_title_cuts_the_binary_from_its_start_and_keeps_the_state()
    {
        Install("/opt/homebrew/bin/a-team", "0.1.12");
        NextPass(TimeSpan.FromSeconds(72));

        Assert.Equal("dispatcher · …/bin/a-team 0.1.12 · next pass 1:12", Read().Title(49));
    }

    [Fact]
    public void Narrower_still_the_binary_goes_altogether_before_the_state_is_touched()
    {
        Install("/opt/homebrew/bin/a-team", "0.1.12");
        NextPass(TimeSpan.FromSeconds(72));

        Assert.Equal("dispatcher · next pass 1:12", Read().Title(30));
    }

    private DispatcherState Read() => DispatcherState.Read(_root, Now, Home);

    private void Install(string bin, string version, bool dryRun = false, DateTimeOffset? installedAt = null) =>
        File.WriteAllText(Path.Combine(_root, "dispatcher.json"), $$"""
            { "bin": "{{bin}}", "version": "{{version}}", "dryRun": {{(dryRun ? "true" : "false")}}, "interval": 120,
              "log": "{{_root}}/launchd.log", "installedAt": {{(installedAt ?? Now - TimeSpan.FromMinutes(5)).ToUnixTimeSeconds()}} }
            """);

    private void NextPass(TimeSpan fromNow, string file = "next-pass") =>
        File.WriteAllText(Path.Combine(_root, file), (Now + fromNow).ToUnixTimeSeconds().ToString());
}
