using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;

namespace ATeam.Dashboard.Tests;

public class AgentPaneTests : IDisposable
{
    private static readonly string Base = SchemeManager.SchemesToSchemeName(Schemes.Base)!;
    private static readonly string Error = SchemeManager.SchemesToSchemeName(Schemes.Error)!;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"a-team-{Guid.NewGuid():n}");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData(false, PaneStatus.NeverRun, true, false, "○ a-team · dev")]
    [InlineData(true, PaneStatus.NeverRun, true, false, "▶ ○ a-team · dev")]
    [InlineData(true, PaneStatus.Running, true, false, "▶ ● a-team · dev")]
    [InlineData(true, PaneStatus.Running, false, false, "▶ ● a-team · dev [scrolled]")]
    [InlineData(true, PaneStatus.Running, true, true, "▶ ● a-team · dev [tool calls]")]
    [InlineData(true, PaneStatus.Running, false, true, "▶ ● a-team · dev [tool calls] [scrolled]")]
    [InlineData(false, PaneStatus.NeverRun, false, true, "○ a-team · dev [tool calls] [scrolled]")]
    [InlineData(false, PaneStatus.Ok, true, false, "✓ a-team · dev")]
    [InlineData(false, PaneStatus.Failed, true, false, "✗ a-team · dev")]
    [InlineData(false, PaneStatus.CutShort, true, false, "✗ a-team · dev")]
    [InlineData(true, PaneStatus.Failed, false, true, "▶ ✗ a-team · dev [tool calls] [scrolled]")]
    [InlineData(false, PaneStatus.Paused, true, false, "⏸ a-team · dev")]
    [InlineData(true, PaneStatus.Paused, false, true, "▶ ⏸ a-team · dev [tool calls] [scrolled]")]
    public void The_title_says_which_state_the_pane_is_in(
        bool selected, PaneStatus status, bool following, bool expanded, string expected) =>
        Assert.Equal(expected, AgentPane.Header("a-team · dev", selected, status, following, expanded));

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

        Assert.Equal(expected, AgentPane.Status(state, paused, verdict));
    }

    [Fact]
    public void An_agent_that_has_never_run_has_no_verdict_to_show()
    {
        var state = new AgentState(Running: false, LastStart: null, [], null);

        Assert.Equal(PaneStatus.NeverRun, AgentPane.Status(state, paused: false, RunVerdict.None));
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

        pane.Refresh(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1), paused: false);

        Assert.StartsWith("✗ ", pane.Title);
        Assert.Equal(Error, pane.SchemeName);
        Assert.Equal(Base, pane.SubViews.OfType<LogView>().Single().SchemeName);
    }

    [Fact]
    public void A_pane_whose_last_run_was_clean_is_left_alone()
    {
        using var pane = Open("""{"type":"result","num_turns":2,"total_cost_usd":0.1}""");

        pane.Refresh(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1), paused: false);

        Assert.StartsWith("✓ ", pane.Title);
        Assert.Equal(Base, pane.SchemeName);
    }

    private AgentPane Open(string session)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "last-start"), DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
        File.WriteAllText(Path.Combine(_dir, "latest.jsonl"), session + "\n");
        return new AgentPane("a-team", "dev", _dir, expandToolCalls: false);
    }
}
