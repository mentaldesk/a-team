using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.Text;
using Terminal.Gui.Views;

namespace ATeam.Dashboard.Tests;

public class AgentPaneTests : IDisposable
{
    private static readonly string Base = SchemeManager.SchemesToSchemeName(Schemes.Base)!;
    private static readonly string Error = SchemeManager.SchemesToSchemeName(Schemes.Error)!;

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
    [InlineData(false, PaneStatus.NeverRun, true, false, "a-team · dev")]
    [InlineData(true, PaneStatus.NeverRun, true, false, "a-team · dev")]
    [InlineData(true, PaneStatus.Running, true, false, "a-team · dev")]
    [InlineData(true, PaneStatus.Running, false, false, "a-team · dev [scrolled]")]
    [InlineData(true, PaneStatus.Running, true, true, "a-team · dev [tool calls]")]
    [InlineData(true, PaneStatus.Running, false, true, "a-team · dev [tool calls] [scrolled]")]
    [InlineData(false, PaneStatus.NeverRun, false, true, "a-team · dev [tool calls] [scrolled]")]
    [InlineData(false, PaneStatus.Ok, true, false, "a-team · dev")]
    [InlineData(false, PaneStatus.Failed, true, false, "a-team · dev")]
    [InlineData(false, PaneStatus.CutShort, true, false, "a-team · dev")]
    [InlineData(true, PaneStatus.Failed, false, true, "a-team · dev [tool calls] [scrolled]")]
    [InlineData(false, PaneStatus.Paused, true, false, "a-team · dev")]
    [InlineData(true, PaneStatus.Paused, false, true, "a-team · dev [tool calls] [scrolled]")]
    public void A_title_carries_the_bare_name_with_the_icons_its_state_earns_in_front_of_it(
        bool selected, PaneStatus status, bool following, bool expanded, string name)
    {
        var title = AgentPane.Header("a-team · dev", selected, status, following, expanded, IconStyle.Unicode);

        Assert.Equal(name, title.Name);
        Assert.Equal(
            (selected ? Icons.Field(Icon.Selected, IconStyle.Unicode) : "") +
            Icons.Field(Icons.For(status), IconStyle.Unicode),
            title.Icons);
        Assert.Equal(title.Icons + name, title.ToString());
    }

    [Theory]
    [InlineData(IconStyle.Auto)]
    [InlineData(IconStyle.NerdFont)]
    [InlineData(IconStyle.Unicode)]
    public void A_titles_icons_cost_the_same_cells_whichever_style_it_is_drawn_in(IconStyle style) =>
        Assert.All(Enum.GetValues<PaneStatus>(), status =>
        {
            Assert.Equal(
                Icons.Width,
                AgentPane.Header("a-team · dev", false, status, true, false, style).Icons.GetColumns());
            Assert.Equal(
                Icons.Width * 2,
                AgentPane.Header("a-team · dev", true, status, true, false, style).Icons.GetColumns());
        });

    [Fact]
    public void A_dev_run_bound_to_a_task_names_it_and_any_other_run_keeps_the_bare_name()
    {
        Assert.Equal("a-team · dev · #244 Choose whose comments", AgentPane.Name("a-team", "dev", new RunTask(244, "Choose whose comments")));
        Assert.Equal("a-team · dev", AgentPane.Name("a-team", "dev", null));
        Assert.Equal("a-team · lead", AgentPane.Name("a-team", "lead", null));
    }

    [Fact]
    public void A_title_too_long_for_its_pane_cuts_the_name_and_keeps_the_icons_and_markers()
    {
        var title = AgentPane.Header(
            "a-team · dev · #244 Choose whose comments count", true, PaneStatus.Running, false, false, IconStyle.Unicode, 40);

        Assert.Equal(40, title.ToString().GetColumns());
        Assert.EndsWith("… [scrolled]", title.Name);
        Assert.StartsWith("a-team · dev · #244 Choo", title.Name);
        Assert.Equal(AgentPane.Header("a-team · dev", true, PaneStatus.Running, false, false, IconStyle.Unicode).Icons, title.Icons);
    }

    [Fact]
    public void A_title_that_fits_is_left_whole() =>
        Assert.Equal(
            "a-team · dev · #244 Choose",
            AgentPane.Header("a-team · dev · #244 Choose", false, PaneStatus.Ok, true, false, IconStyle.Unicode, 80).Name);

    [Fact]
    public void A_pane_names_the_task_its_run_was_started_for()
    {
        using var pane = Open("""{"type":"system","subtype":"init"}""");
        File.WriteAllText(Path.Combine(_dir, "task"), """{"number":302,"title":"The Dev pane says which task the run is on"}""");

        pane.Refresh(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1), paused: false, held: false);

        Assert.EndsWith("a-team · dev · #302 The Dev pane says which task the run is on", pane.Title);
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
        Assert.Equal(
            [100, 200, 300],
            state.Runs.Select(run => run.Started!.Value.ToUnixTimeSeconds()));
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
    public void Each_other_live_run_takes_one_line_above_the_status_row()
    {
        using var pane = Open("""{"type":"system","subtype":"init"}""");
        pane.Frame = new System.Drawing.Rectangle(0, 0, 60, 20);
        pane.Refresh(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1), paused: false, held: false);
        pane.Layout();
        var status = pane.SubViews.OfType<Label>().First();
        Assert.Equal(0, status.Frame.Y);

        Write(Path.Combine(_dir, "runs", "246"), Run(), 100, 246);
        Write(Path.Combine(_dir, "runs", "192"), Run(), 200, 192);
        Write(Path.Combine(_dir, "runs", "303"), Run(), 300, 303);
        pane.Refresh(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1), paused: false, held: false);
        pane.Layout();

        Assert.Equal(2, status.Frame.Y);
    }

    [Fact]
    public void Choosing_another_run_shows_its_task_and_folds_the_one_that_was_showing_to_a_bar_below()
    {
        using var pane = Open("""{"type":"system","subtype":"init"}""");
        pane.Frame = new System.Drawing.Rectangle(0, 0, 60, 20);
        Write(Path.Combine(_dir, "runs", "246"), Run(), 100, 246);
        Write(Path.Combine(_dir, "runs", "303"), Run(), 300, 303);
        pane.Refresh(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1), paused: false, held: false);
        Assert.EndsWith("#303 Task 303", pane.Title);

        Assert.True(pane.MoveRun(-1));
        pane.Layout();

        Assert.EndsWith("#246 Task 246", pane.Title);
        var labels = pane.SubViews.OfType<Label>().ToList();
        Assert.Equal(0, labels[0].Frame.Y);
        Assert.Contains("#303 Task 303", labels.Last().Text);
        Assert.False(pane.MoveRun(-1));
    }

    [Fact]
    public void A_bar_shows_the_run_s_icon_its_task_and_how_long_it_has_run_at_the_right()
    {
        var now = DateTimeOffset.UnixEpoch + TimeSpan.FromHours(1);
        var bars = AgentPane.Bars(
            [new DevRun("246", new RunTask(246, "Remove a team"), now - TimeSpan.FromSeconds(400)),
             new DevRun("192", new RunTask(192, "Reply to a pitch without leaving the dashboard"), now - TimeSpan.FromSeconds(52))],
            now, IconStyle.Unicode, 40);

        var icon = Icons.Field(Icons.For(PaneStatus.Running), IconStyle.Unicode);
        Assert.Equal(2, bars.Count);
        Assert.All(bars, bar => Assert.Equal(40, bar.GetColumns()));
        Assert.StartsWith(icon + "#246 Remove a team ", bars[0]);
        Assert.EndsWith(" 6:40", bars[0]);
        Assert.StartsWith(icon + "#192 Reply to a pitch", bars[1]);
        Assert.EndsWith("… 0:52", bars[1]);
        Assert.Empty(AgentPane.Bars([], now, IconStyle.Unicode, 40));
    }

    [Fact]
    public void A_run_stopped_by_task_shows_as_held_in_its_bar()
    {
        var now = DateTimeOffset.UnixEpoch + TimeSpan.FromHours(1);
        var bars = AgentPane.Bars(
            [new DevRun("246", new RunTask(246, "Remove a team"), now - TimeSpan.FromSeconds(400)) { Held = now }],
            now, IconStyle.Unicode, 40);

        Assert.StartsWith(Icons.Field(Icons.For(PaneStatus.StoppedByYou), IconStyle.Unicode) + "#246 Remove a team ", bars[0]);
        Assert.EndsWith(" held", bars[0]);
    }

    [Theory]
    [InlineData(true, true, RunVerdict.Error, PaneStatus.Paused)]
    [InlineData(true, false, RunVerdict.Error, PaneStatus.Paused)]
    [InlineData(false, true, RunVerdict.Error, PaneStatus.Running)]
    [InlineData(false, true, RunVerdict.Ok, PaneStatus.Running)]
    [InlineData(false, false, RunVerdict.Ok, PaneStatus.Ok)]
    [InlineData(false, false, RunVerdict.Error, PaneStatus.Failed)]
    [InlineData(false, false, RunVerdict.None, PaneStatus.CutShort)]
    public void Pause_wins_then_running_then_the_verdict(
        bool paused, bool running, RunVerdict verdict, PaneStatus expected)
    {
        var state = new AgentState(running, DateTimeOffset.UnixEpoch, [], null);

        Assert.Equal(expected, AgentPane.Status(state, paused, held: false, verdict));
    }

    [Theory]
    [InlineData(false, RunVerdict.None, PaneStatus.StoppedByYou)]
    [InlineData(true, RunVerdict.None, PaneStatus.StoppedByYou)]
    [InlineData(false, RunVerdict.Ok, PaneStatus.Held)]
    [InlineData(false, RunVerdict.Error, PaneStatus.Held)]
    public void A_held_role_is_stopped_by_you_unless_its_last_run_finished(
        bool paused, RunVerdict verdict, PaneStatus expected)
    {
        var state = new AgentState(Running: false, DateTimeOffset.UnixEpoch, [], null);

        Assert.Equal(expected, AgentPane.Status(state, paused, held: true, verdict));
    }

    [Fact]
    public void A_held_role_with_a_run_still_going_shows_it_running()
    {
        var state = new AgentState(Running: true, DateTimeOffset.UnixEpoch, [], null);

        Assert.Equal(PaneStatus.Running, AgentPane.Status(state, paused: false, held: true, RunVerdict.None));
    }

    [Fact]
    public void A_held_role_with_a_run_still_going_says_it_is_held()
    {
        var now = DateTimeOffset.UnixEpoch + TimeSpan.FromHours(1);
        var running = new AgentState(true, now - TimeSpan.FromSeconds(75), [], null);

        Assert.Equal("running 1:15 · held", AgentPane.Describe(running, now, now.AddMinutes(2), PaneStatus.Running, held: true));
    }

    [Fact]
    public void A_held_pane_says_you_stopped_it_or_how_its_last_run_went()
    {
        var now = DateTimeOffset.UnixEpoch + TimeSpan.FromHours(1);
        var stopped = new AgentState(false, now - TimeSpan.FromMinutes(30), [], null, now - TimeSpan.FromMinutes(4));
        var finished = new AgentState(false, now - TimeSpan.FromMinutes(5), [], null);

        Assert.Equal("stopped by you 4m ago · held", AgentPane.Describe(stopped, now, now.AddMinutes(2), PaneStatus.StoppedByYou));
        Assert.Equal("ran 5m ago · held", AgentPane.Describe(finished, now, now.AddMinutes(2), PaneStatus.Held));
    }

    [Fact]
    public void An_agent_that_has_never_run_has_no_verdict_to_show()
    {
        var state = new AgentState(Running: false, LastStart: null, [], null);

        Assert.Equal(PaneStatus.NeverRun, AgentPane.Status(state, paused: false, held: false, RunVerdict.None));
    }

    [Fact]
    public void A_paused_pane_says_so_where_it_would_count_down_to_the_next_check()
    {
        var now = DateTimeOffset.UnixEpoch + TimeSpan.FromHours(1);
        var idle = new AgentState(Running: false, now - TimeSpan.FromMinutes(5), [], null);

        Assert.Equal("ran 5m ago · next check 2:00", AgentPane.Describe(idle, now, now.AddMinutes(2), PaneStatus.Ok));
        Assert.Equal("ran 5m ago · paused", AgentPane.Describe(idle, now, now.AddMinutes(2), PaneStatus.Paused));
    }

    [Fact]
    public void A_run_that_never_reported_a_result_was_cut_short()
    {
        var now = DateTimeOffset.UnixEpoch + TimeSpan.FromHours(1);
        var idle = new AgentState(Running: false, now - TimeSpan.FromMinutes(12), [], null);

        Assert.Equal(
            "ran 12m ago · cut short · next check 1:30",
            AgentPane.Describe(idle, now, now.AddSeconds(90), PaneStatus.CutShort));
    }

    [Fact]
    public void A_running_agent_is_timed_however_its_last_run_went()
    {
        var now = DateTimeOffset.UnixEpoch + TimeSpan.FromHours(1);
        var running = new AgentState(Running: true, now - TimeSpan.FromSeconds(75), [], null);

        Assert.Equal("running 1:15", AgentPane.Describe(running, now, now.AddMinutes(2), PaneStatus.Running));
    }

    [Theory]
    [InlineData(PaneStatus.Failed)]
    [InlineData(PaneStatus.CutShort)]
    public void A_failed_run_reddens_the_frame_and_the_status_row_but_never_the_body(PaneStatus status)
    {
        var schemes = AgentPane.SchemesFor(status, "ran 12m ago · next check 1:30");

        Assert.Equal(Error, schemes.Frame);
        Assert.Equal(Error, schemes.Status);
        Assert.Equal(Base, schemes.Body);
    }

    [Theory]
    [InlineData(PaneStatus.NeverRun)]
    [InlineData(PaneStatus.Ok)]
    [InlineData(PaneStatus.Running)]
    [InlineData(PaneStatus.Paused)]
    [InlineData(PaneStatus.Held)]
    [InlineData(PaneStatus.StoppedByYou)]
    public void Nothing_but_a_failed_run_reddens_the_pane(PaneStatus status)
    {
        var schemes = AgentPane.SchemesFor(status, "ran 5m ago · next check 1:30");

        Assert.Equal(new PaneSchemes(Base, Base, LogSchemes.Dimmed, Base), schemes);
    }

    [Fact]
    public void A_stopped_dispatcher_reddens_the_status_row_alone()
    {
        var schemes = AgentPane.SchemesFor(PaneStatus.Ok, "ran 5m ago · dispatcher not running");

        Assert.Equal(new PaneSchemes(Base, Error, LogSchemes.Dimmed, Base), schemes);
    }

    [Fact]
    public void The_why_row_is_dimmed_whatever_the_verdict() =>
        Assert.All(
            Enum.GetValues<PaneStatus>(),
            status => Assert.Equal(LogSchemes.Dimmed, AgentPane.SchemesFor(status, "").Why));

    [Fact]
    public void The_log_body_keeps_its_own_colours_whatever_the_verdict() =>
        Assert.All(
            Enum.GetValues<PaneStatus>(),
            status => Assert.Equal(Base, AgentPane.SchemesFor(status, "ran 5m ago · dispatcher not running").Body));

    [Fact]
    public void A_pane_whose_last_run_failed_draws_its_frame_from_the_error_scheme()
    {
        using var pane = Open("""{"type":"result","is_error":true,"num_turns":2,"total_cost_usd":0.1}""");

        pane.Refresh(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1), paused: false, held: false);

        Assert.StartsWith(Icons.Field(Icon.Failed, IconStyle.Unicode), pane.Title);
        Assert.Equal(Error, pane.SchemeName);
        Assert.Equal(Base, pane.SubViews.OfType<LogView>().Single().SchemeName);
    }

    [Fact]
    public void A_pane_whose_last_run_was_clean_is_left_alone()
    {
        using var pane = Open("""{"type":"result","num_turns":2,"total_cost_usd":0.1}""");

        pane.Refresh(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1), paused: false, held: false);

        Assert.StartsWith(Icons.Field(Icon.Ok, IconStyle.Unicode), pane.Title);
        Assert.Equal(Base, pane.SchemeName);
    }

    [Fact]
    public void A_run_you_stopped_wears_the_paused_icon_and_colours_not_cut_short()
    {
        using var pane = Open("""{"type":"system","subtype":"init"}""");
        File.WriteAllText(Path.Combine(_dir, "stopped"), DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());

        pane.Refresh(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1), paused: false, held: true);

        Assert.StartsWith(Icons.Field(Icon.StoppedByYou, IconStyle.Unicode), pane.Title);
        Assert.Equal(Base, pane.SchemeName);
        Assert.Equal("stopped by you <1m ago · held", pane.SubViews.OfType<Label>().First().Text);
    }

    [Theory]
    [InlineData(false, false, false, RunVerdict.Ok)]
    [InlineData(false, false, false, RunVerdict.Error)]
    [InlineData(false, false, false, RunVerdict.None)]
    [InlineData(true, false, false, RunVerdict.Ok)]
    [InlineData(false, true, false, RunVerdict.Ok)]
    [InlineData(false, false, true, RunVerdict.None)]
    [InlineData(true, true, true, RunVerdict.Error)]
    public void A_misconfigured_team_outranks_every_other_state(bool paused, bool running, bool held, RunVerdict verdict)
    {
        var state = new AgentState(running, DateTimeOffset.UnixEpoch, [], null);

        Assert.Equal(PaneStatus.Misconfigured, AgentPane.Status(state, paused, held, verdict, misconfigured: true));
    }

    [Fact]
    public void A_misconfigured_pane_wears_the_warning_reddens_its_frame_and_says_what_is_wrong_above_an_unchanged_why()
    {
        using var pane = Open("""{"type":"result","num_turns":2,"total_cost_usd":0.1}""");
        File.WriteAllText(Path.Combine(_dir, "last-reasons"), "work to do\n");

        pane.Refresh(
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1), paused: true, held: false,
            new TeamProblem("checkout", "~/code/TuiCode/main isn't there: gh repo clone mentaldesk/TuiCode ~/code/TuiCode/main"));

        Assert.StartsWith(Icons.Field(Icon.Misconfigured, IconStyle.Unicode), pane.Title);
        Assert.Equal("⚠", Icons.Glyph(Icons.For(PaneStatus.Misconfigured), IconStyle.Unicode));
        Assert.Equal(Error, pane.SchemeName);
        var labels = pane.SubViews.OfType<Label>().ToList();
        Assert.Equal(
            "misconfigured: checkout ~/code/TuiCode/main isn't there: gh repo clone mentaldesk/TuiCode ~/code/TuiCode/main",
            labels[0].Text);
        Assert.Equal(Error, labels[0].SchemeName);
        Assert.Equal("why: work to do", labels[1].Text);
        Assert.Equal(LogSchemes.Dimmed, labels[1].SchemeName);
        Assert.Equal(Base, pane.SubViews.OfType<LogView>().Single().SchemeName);
    }

    [Fact]
    public void A_pane_whose_team_is_fixed_again_goes_back_to_how_its_last_run_went()
    {
        using var pane = Open("""{"type":"result","num_turns":2,"total_cost_usd":0.1}""");
        pane.Refresh(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1), paused: false, held: false, new TeamProblem("app", "no key"));

        pane.Refresh(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1), paused: false, held: false);

        Assert.StartsWith(Icons.Field(Icon.Ok, IconStyle.Unicode), pane.Title);
        Assert.Equal(Base, pane.SchemeName);
        Assert.StartsWith("ran <1m ago", pane.SubViews.OfType<Label>().First().Text);
    }

    [Fact]
    public void A_pane_wears_its_title_icons_in_the_style_it_is_shown()
    {
        using var pane = Open("""{"type":"result","num_turns":2,"total_cost_usd":0.1}""");
        pane.Refresh(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1), paused: false, held: false);

        pane.ShowIcons(IconStyle.NerdFont);

        Assert.StartsWith(Icons.Field(Icon.Ok, IconStyle.NerdFont), pane.Title);
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

    private AgentPane Open(string session)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "last-start"), DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
        File.WriteAllText(Path.Combine(_dir, "latest.jsonl"), session + "\n");
        return new AgentPane("a-team", "dev", _dir, expandToolCalls: false);
    }
}
