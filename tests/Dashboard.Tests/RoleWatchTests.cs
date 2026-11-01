namespace ATeam.Dashboard.Tests;

public class RoleWatchTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch + TimeSpan.FromHours(1);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"a-team-{Guid.NewGuid():n}");
    private readonly List<System.Diagnostics.Process> _runs = [];

    public void Dispose()
    {
        foreach (var run in _runs)
        {
            if (!run.HasExited)
                run.Kill();
            run.Dispose();
        }
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"title":"no number"}""")]
    public void A_task_file_it_cant_read_leaves_the_run_without_one(string text)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "task"), text);

        Assert.Null(AgentState.ReadTask(Path.Combine(_dir, "task")));
    }

    [Fact]
    public void The_live_runs_are_listed_in_the_order_they_started()
    {
        var shown = Run();
        Write(_dir, shown, 300, 303);
        Write(Path.Combine(_dir, "runs", "303"), shown, 300, 303);
        Write(Path.Combine(_dir, "runs", "246"), Run(), 100, 246);
        Write(Path.Combine(_dir, "runs", "192"), Run(), 200, 192);
        Write(Path.Combine(_dir, "runs", "150"), Finished(), 50, 150);

        var state = AgentState.Read(_dir);

        Assert.Equal(303, state.Task!.Number);
        Assert.Equal([246, 192, 303], state.Runs.Select(run => run.Task!.Number));
        Assert.Equal([100, 200, 300], state.Runs.Select(run => run.Started!.Value.ToUnixTimeSeconds()));
        Assert.Equal(303, state.Latest!.Task!.Number);
    }

    [Fact]
    public void The_latest_run_is_kept_after_it_finishes_and_none_is_read_from_a_role_without_runs()
    {
        var shown = Finished();
        Write(_dir, shown, 300, 303);
        Write(Path.Combine(_dir, "runs", "303"), shown, 300, 303);

        var state = AgentState.Read(_dir);
        Assert.Empty(state.Runs);
        Assert.Equal(303, state.Latest!.Task!.Number);

        var nothing = AgentState.Read(Path.Combine(_dir, "nothing"));
        Assert.Empty(nothing.Runs);
        Assert.Null(nothing.Latest);
    }

    [Fact]
    public void A_role_with_several_runs_is_running_on_each_of_their_cards()
    {
        Write(_dir, Finished(), 300, 303);
        Write(Path.Combine(_dir, "runs", "246"), Run(), 100, 246);
        Write(Path.Combine(_dir, "runs", "192"), Run(), 200, 192);
        var watch = new RoleWatch("a-team", "dev", _dir);

        watch.Refresh(Now, Now.AddMinutes(1), paused: false, held: false);

        Assert.True(watch.Running);
        Assert.Equal([246, 192], watch.Working);
        Assert.Equal(2, watch.Runs.Count);
        Assert.StartsWith("running ", watch.Line, StringComparison.Ordinal);
        Assert.EndsWith(" · #246, #192", watch.Line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_role_s_latest_session_is_its_newest_run_s_not_the_one_its_own_files_last_followed()
    {
        Write(_dir, Finished(), 300, 303);
        File.WriteAllText(Path.Combine(_dir, "latest.jsonl"), "{}\n");
        Write(Path.Combine(_dir, "runs", "246"), Run(), 100, 246);
        Write(Path.Combine(_dir, "runs", "192"), Run(), 200, 192);
        File.WriteAllText(Path.Combine(_dir, "runs", "192", "latest.jsonl"), "{}\n");
        var watch = new RoleWatch("a-team", "dev", _dir);

        watch.Refresh(Now, Now.AddMinutes(1), paused: false, held: false);

        Assert.Equal(new FileInfo(Path.Combine(_dir, "runs", "192", "latest.jsonl")).FullName, watch.LogPath);
    }

    [Fact]
    public void An_idle_role_says_when_it_last_ran_and_reads_its_session_and_verdict()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "last-start"), (Now - TimeSpan.FromMinutes(12)).ToUnixTimeSeconds().ToString());
        File.WriteAllText(Path.Combine(_dir, "latest.jsonl"),
            """{"type":"system","subtype":"init","session_id":"s1"}""" + "\n" + """{"type":"result","is_error":true,"num_turns":2,"total_cost_usd":0.1}""" + "\n");
        var watch = new RoleWatch("a-team", "lead", _dir);

        watch.Refresh(Now, Now.AddSeconds(90), paused: false, held: false);

        Assert.Equal(RoleStatus.Failed, watch.Status);
        Assert.Equal("idle · ran 12m ago · failed · next check 1:30", watch.Line);
        Assert.Equal("s1", watch.SessionId);
        Assert.EndsWith("latest.jsonl", watch.LogPath, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, true, RunVerdict.Error, RoleStatus.Paused)]
    [InlineData(true, false, RunVerdict.Error, RoleStatus.Paused)]
    [InlineData(false, true, RunVerdict.Error, RoleStatus.Running)]
    [InlineData(false, true, RunVerdict.Ok, RoleStatus.Running)]
    [InlineData(false, false, RunVerdict.Ok, RoleStatus.Ok)]
    [InlineData(false, false, RunVerdict.Error, RoleStatus.Failed)]
    [InlineData(false, false, RunVerdict.None, RoleStatus.CutShort)]
    public void Pause_wins_then_running_then_the_verdict(bool paused, bool running, RunVerdict verdict, RoleStatus expected)
    {
        var state = new AgentState(running, DateTimeOffset.UnixEpoch, [], null);

        Assert.Equal(expected, RoleWatch.StatusOf(state, paused, held: false, verdict));
    }

    [Theory]
    [InlineData(false, RunVerdict.None, RoleStatus.StoppedByYou)]
    [InlineData(true, RunVerdict.None, RoleStatus.StoppedByYou)]
    [InlineData(false, RunVerdict.Ok, RoleStatus.Held)]
    [InlineData(false, RunVerdict.Error, RoleStatus.Held)]
    public void A_held_role_is_stopped_by_you_unless_its_last_run_finished(bool paused, RunVerdict verdict, RoleStatus expected)
    {
        var state = new AgentState(Running: false, DateTimeOffset.UnixEpoch, [], null);

        Assert.Equal(expected, RoleWatch.StatusOf(state, paused, held: true, verdict));
    }

    [Fact]
    public void A_held_role_with_a_run_still_going_is_running_and_says_it_is_paused()
    {
        var running = new AgentState(true, Now - TimeSpan.FromSeconds(75), [], null);

        Assert.Equal(RoleStatus.Running, RoleWatch.StatusOf(running, paused: false, held: true, RunVerdict.None));
        Assert.Equal("running 1:15 · #12 · paused", RoleWatch.Says(running, [12], Now, Now.AddMinutes(2), RoleStatus.Running, held: true));
    }

    [Fact]
    public void A_held_role_says_it_is_paused_and_when_you_stopped_it_or_it_last_ran()
    {
        var stopped = new AgentState(false, Now - TimeSpan.FromMinutes(30), [], null, Now - TimeSpan.FromMinutes(4));
        var finished = new AgentState(false, Now - TimeSpan.FromMinutes(5), [], null);

        Assert.Equal("paused · stopped by you 4m ago", RoleWatch.Says(stopped, [], Now, Now.AddMinutes(2), RoleStatus.StoppedByYou, held: true));
        Assert.Equal("paused · ran 5m ago", RoleWatch.Says(finished, [], Now, Now.AddMinutes(2), RoleStatus.Held, held: true));
    }

    [Fact]
    public void A_role_that_has_never_run_has_no_verdict_to_show()
    {
        var state = new AgentState(Running: false, LastStart: null, [], null);

        Assert.Equal(RoleStatus.NeverRun, RoleWatch.StatusOf(state, paused: false, held: false, RunVerdict.None));
        Assert.Equal("idle · never run · next check 2:00", RoleWatch.Says(state, [], Now, Now.AddMinutes(2), RoleStatus.NeverRun, held: false));
    }

    [Fact]
    public void An_idle_role_counts_down_to_the_next_check_unless_its_team_is_paused()
    {
        var idle = new AgentState(Running: false, Now - TimeSpan.FromMinutes(5), [], null);

        Assert.Equal("idle · ran 5m ago · next check 2:00", RoleWatch.Says(idle, [], Now, Now.AddMinutes(2), RoleStatus.Ok, held: false));
        Assert.Equal("idle · ran 5m ago · team paused",
            RoleWatch.Says(idle, [], Now, Now.AddMinutes(2), RoleStatus.Paused, held: false, teamPaused: true));
    }

    [Fact]
    public void A_pass_overdue_but_still_going_is_checking_not_a_stopped_dispatcher()
    {
        var idle = new AgentState(Running: false, Now - TimeSpan.FromMinutes(5), [], null);
        var overdue = Now - TimeSpan.FromMinutes(3);

        Assert.Equal("idle · ran 5m ago · checking now", RoleWatch.Says(idle, [], Now, overdue, RoleStatus.Ok, held: false, passing: true));
        Assert.Equal("idle · ran 5m ago · dispatcher not running", RoleWatch.Says(idle, [], Now, overdue, RoleStatus.Ok, held: false));
        Assert.Equal("idle · ran 5m ago · dispatcher hasn't run", RoleWatch.Says(idle, [], Now, null, RoleStatus.Ok, held: false));
    }

    [Fact]
    public void A_run_that_never_reported_a_result_was_cut_short()
    {
        var idle = new AgentState(Running: false, Now - TimeSpan.FromMinutes(12), [], null);

        Assert.Equal("idle · ran 12m ago · cut short · next check 1:30", RoleWatch.Says(idle, [], Now, Now.AddSeconds(90), RoleStatus.CutShort, held: false));
    }

    [Theory]
    [InlineData(false, false, false, RunVerdict.Ok)]
    [InlineData(false, false, false, RunVerdict.Error)]
    [InlineData(true, false, false, RunVerdict.Ok)]
    [InlineData(false, true, false, RunVerdict.Ok)]
    [InlineData(false, false, true, RunVerdict.None)]
    [InlineData(true, true, true, RunVerdict.Error)]
    public void A_misconfigured_team_outranks_every_other_state(bool paused, bool running, bool held, RunVerdict verdict)
    {
        var state = new AgentState(running, DateTimeOffset.UnixEpoch, [], null);

        Assert.Equal(RoleStatus.Misconfigured, RoleWatch.StatusOf(state, paused, held, verdict, misconfigured: true));
        Assert.Equal("⚠", Icons.Glyph(Icons.For(RoleStatus.Misconfigured), IconStyle.Unicode));
    }

    [Fact]
    public void A_misconfigured_team_says_what_is_wrong_in_the_words_the_check_gives()
    {
        Assert.Equal(
            "misconfigured: checkout ~/code/TuiCode/main isn't there: gh repo clone mentaldesk/TuiCode ~/code/TuiCode/main",
            RoleWatch.Misconfigured(new TeamProblem("checkout", "~/code/TuiCode/main isn't there: gh repo clone mentaldesk/TuiCode ~/code/TuiCode/main")));
    }

    private int Run()
    {
        var run = System.Diagnostics.Process.Start("sleep", "60");
        _runs.Add(run);
        return run.Id;
    }

    private int Finished()
    {
        var run = System.Diagnostics.Process.Start("true");
        _runs.Add(run);
        run.WaitForExit();
        return run.Id;
    }

    private static void Write(string dir, int pid, long started, int task)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "pid"), pid.ToString());
        File.WriteAllText(Path.Combine(dir, "last-start"), started.ToString());
        File.WriteAllText(Path.Combine(dir, "task"), $$"""{"number":{{task}},"title":"Task {{task}}"}""");
    }
}
