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
    public void A_pass_overdue_but_still_going_is_not_stopped()
    {
        Install("/opt/homebrew/bin/a-team", "0.1.12", installedAt: Now - TimeSpan.FromHours(1));
        NextPass(-TimeSpan.FromMinutes(3));
        Pass(Environment.ProcessId);

        var state = Read();

        Assert.Equal("dispatcher · /opt/homebrew/bin/a-team 0.1.12 · next pass 0:00", state.Title());
        Assert.False(state.Error);
    }

    [Fact]
    public void A_pass_overdue_whose_process_has_gone_has_stopped()
    {
        Install("/opt/homebrew/bin/a-team", "0.1.12", installedAt: Now - TimeSpan.FromHours(1));
        NextPass(-TimeSpan.FromMinutes(3));
        Pass(int.MaxValue);

        Assert.Equal("dispatcher · stopped 5m ago", Read().Title());
    }

    [Fact]
    public void A_live_pass_says_nothing_for_a_dry_run_dispatcher_whose_own_pass_has_stopped()
    {
        Install("/opt/homebrew/bin/a-team", "0.1.12", dryRun: true, installedAt: Now - TimeSpan.FromHours(1));
        NextPass(-TimeSpan.FromMinutes(3), "dry-next-pass");
        Pass(Environment.ProcessId);

        Assert.Equal("dispatcher · stopped 5m ago", Read().Title());
    }

    [Fact]
    public void Without_a_record_an_overdue_pass_still_going_keeps_the_plain_title()
    {
        NextPass(-TimeSpan.FromMinutes(3));
        Pass(Environment.ProcessId);

        Assert.Equal("dispatcher", Read().Title());
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

    [Fact]
    public void Output_written_after_the_last_dispatch_line_is_its_tail()
    {
        Install("/opt/homebrew/bin/a-team", "0.1.12");
        Written("dispatch.log", Now - TimeSpan.FromMinutes(3), "2026-10-04T10:00:00Z a-team dev: started 1 on 0.1.12");
        Written("launchd.log", Now, [.. Enumerable.Range(1, 30).Select(n => $"jq: parse error {n}")]);

        var broke = DispatcherState.Broke(_root, 20);

        Assert.Equal([.. Enumerable.Range(11, 20).Select(n => $"jq: parse error {n}")], broke);
    }

    [Fact]
    public void Output_older_than_the_last_dispatch_line_is_not_shown()
    {
        Install("/opt/homebrew/bin/a-team", "0.1.12");
        Written("launchd.log", Now - TimeSpan.FromMinutes(3), "jq: parse error");
        Written("dispatch.log", Now, "2026-10-04T10:00:00Z a-team dev: started 1 on 0.1.12");

        Assert.Empty(DispatcherState.Broke(_root, 20));
    }

    [Fact]
    public void No_output_file_shows_nothing()
    {
        Install("/opt/homebrew/bin/a-team", "0.1.12");
        Written("dispatch.log", Now, "2026-10-04T10:00:00Z a-team dev: started 1 on 0.1.12");

        Assert.Empty(DispatcherState.Broke(_root, 20));
    }

    [Fact]
    public void Without_a_record_the_output_is_read_from_the_installers_usual_place()
    {
        Written("dispatch.log", Now - TimeSpan.FromMinutes(3), "2026-10-04T10:00:00Z a-team dev: started 1 on 0.1.12");
        Written("launchd.log", Now, "syntax error near unexpected token");

        Assert.Equal(["syntax error near unexpected token"], DispatcherState.Broke(_root, 20));
    }

    [Fact]
    public void A_failed_pass_ends_the_title_and_turns_it_to_Error()
    {
        Install("/opt/homebrew/bin/a-team", "0.1.12");
        NextPass(TimeSpan.FromSeconds(72));

        var failed = Read().Failed();

        Assert.Equal("dispatcher · /opt/homebrew/bin/a-team 0.1.12 · next pass 1:12 · last pass failed", failed.Title());
        Assert.True(failed.Error);
    }

    private DispatcherState Read() => DispatcherState.Read(_root, Now, Home);

    private void Install(string bin, string version, bool dryRun = false, DateTimeOffset? installedAt = null) =>
        File.WriteAllText(Path.Combine(_root, "dispatcher.json"), $$"""
            { "bin": "{{bin}}", "version": "{{version}}", "dryRun": {{(dryRun ? "true" : "false")}}, "interval": 120,
              "log": "{{_root}}/launchd.log", "installedAt": {{(installedAt ?? Now - TimeSpan.FromMinutes(5)).ToUnixTimeSeconds()}} }
            """);

    private void Written(string file, DateTimeOffset at, params string[] lines)
    {
        var path = Path.Combine(_root, file);
        File.WriteAllLines(path, lines);
        File.SetLastWriteTimeUtc(path, at.UtcDateTime);
    }

    private void Pass(int pid, string file = "pass") => File.WriteAllText(Path.Combine(_root, file), pid.ToString());

    private void NextPass(TimeSpan fromNow, string file = "next-pass") =>
        File.WriteAllText(Path.Combine(_root, file), (Now + fromNow).ToUnixTimeSeconds().ToString());
}
