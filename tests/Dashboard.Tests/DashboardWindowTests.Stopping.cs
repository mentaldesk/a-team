using MentalDesk.Tui;
using System.Drawing;
using System.Text;
using MentalDesk.Tui.Links;
using Terminal.Gui;
using Terminal.Gui.App;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace ATeam.Dashboard.Tests;

public partial class DashboardWindowTests
{
    [Fact]
    public void Interrupting_a_run_reaches_its_line_on_the_next_refresh()
    {
        using var window = Open(agents: Agents(2), run: arguments =>
        {
            WriteHold(arguments[1], arguments[2]);
            return Task.FromResult<string?>(null);
        });
        WriteRunning("team0", "dev");
        window.Refresh();
        SelectAgent(window, 1);

        window.Commands.Execute("agent.interrupt");
        window.Refresh();

        Assert.True(window.Watches[1].Held);
        Assert.Equal("Let the chosen role start again", Label(window, "agent.interrupt"));
    }

    [Fact]
    public void A_held_role_is_offered_Let_it_start_again_which_resumes_it()
    {
        var calls = new List<string[]>();
        WriteHold("team0", "dev");
        using var window = Open(agents: Agents(2), run: arguments =>
        {
            calls.Add(arguments);
            return Task.FromResult<string?>(null);
        });
        window.Refresh();
        SelectAgent(window, 1);

        Assert.Equal("Let the chosen role start again", Label(window, "agent.interrupt"));
        Assert.True(window.NewKeyDownEvent(new Key('i')));

        Assert.Equal([["resume", "team0", "dev"]], calls);
    }

    [Fact]
    public void Interrupt_is_enabled_only_for_a_selected_role_that_is_running_or_held()
    {
        WriteHold("team0", "dev");
        WriteRunning("team1", "lead");
        using var window = Open(agents: Agents(4));
        window.Refresh();
        Assert.False(window.Commands.IsEnabled("agent.interrupt"));

        var enabled = Enumerable.Range(0, 4).Select(index =>
        {
            SelectAgent(window, index);
            return window.Commands.IsEnabled("agent.interrupt");
        }).ToList();

        Assert.Equal([false, true, true, false], enabled);
    }

    [Fact]
    public void i_does_nothing_on_an_idle_role()
    {
        var calls = 0;
        using var window = Open(agents: Agents(2), run: _ =>
        {
            calls++;
            return Task.FromResult<string?>(null);
        });
        window.Refresh();
        SelectAgent(window, 1);

        Assert.False(window.NewKeyDownEvent(new Key('i')));
        window.Commands.Execute("agent.interrupt");

        Assert.Equal(0, calls);
    }

    [Fact]
    public void An_interrupt_that_failed_shows_the_file_in_red()
    {
        using var window = Open(
            agents: Agents(2),
            run: _ => Task.FromResult<string?>("a-team stop: can't write /nope/team0.json"));
        WriteRunning("team0", "dev");
        window.Refresh();
        SelectAgent(window, 1);

        window.Commands.Execute("agent.interrupt");
        window.Refresh();

        Assert.Equal("interrupting team0's dev: a-team stop: can't write /nope/team0.json", window.Says());
        Assert.Equal(Severity.Error, window.Status.MessageSeverity);
        Assert.False(window.Watches[1].Held);
        Assert.Equal("Interrupt the chosen role", Label(window, "agent.interrupt"));
    }

    [Fact]
    public void i_on_a_run_with_a_session_hands_it_to_attach_instead_of_stopping_it()
    {
        var calls = new List<string[]>();
        var handed = new List<Handover>();
        using var window = Open(agents: Agents(4), handOver: handed.Add, run: arguments =>
        {
            calls.Add(arguments);
            return Task.FromResult<string?>(null);
        });
        WriteRunning("team1", "dev");
        WriteSession("team1", "dev");
        window.Refresh();
        SelectAgent(window, 3);

        Assert.True(window.NewKeyDownEvent(new Key('i')));

        var handover = Assert.IsType<AttachHandover>(Assert.Single(handed));
        Assert.Equal(["attach", "team1", "dev"], handover.Arguments);
        Assert.Equal((Area.Overseer, null), (handover.Area, handover.Card));
        Assert.Empty(calls);
    }

    [Fact]
    public void i_on_a_held_role_with_a_session_lets_it_start_again_rather_than_attaching()
    {
        var calls = new List<string[]>();
        var handed = new List<Handover>();
        WriteHold("team0", "dev");
        WriteSession("team0", "dev");
        using var window = Open(agents: Agents(2), handOver: handed.Add, run: arguments =>
        {
            calls.Add(arguments);
            return Task.FromResult<string?>(null);
        });
        window.Refresh();
        SelectAgent(window, 1);

        Assert.True(window.NewKeyDownEvent(new Key('i')));

        Assert.Equal([["resume", "team0", "dev"]], calls);
        Assert.Empty(handed);
    }

    [Fact]
    public void i_does_not_attach_to_a_finished_run()
    {
        var handed = new List<Handover>();
        WriteSession("team0", "dev");
        using var window = Open(agents: Agents(2), handOver: handed.Add);
        window.Refresh();
        SelectAgent(window, 1);

        Assert.False(window.NewKeyDownEvent(new Key('i')));
        Assert.Empty(handed);
    }

    [Fact]
    public void Review_the_vision_asks_for_one_for_the_selected_team_then_runs_a_pass_so_the_Lead_starts_now()
    {
        var calls = new List<string[]>();
        var passes = 0;
        using var window = Open(agents: Agents(4),
            run: arguments =>
            {
                calls.Add(arguments);
                return Task.FromResult<string?>(null);
            },
            pass: new DispatchPass(_root, "a-team", (_, _) =>
            {
                passes++;
                return new TaskCompletionSource<Reading>().Task;
            }));
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.False(window.Commands.IsEnabled("team.visionReview"));
        SelectAgent(window, 3);
        Assert.Equal("Review the vision · team1", window.Commands.Registered.Single(c => c.Id == "team.visionReview").Label);
        Assert.True(window.Commands.Execute("team.visionReview"));
        window.Refresh();

        Assert.Equal([["board", "team1", "vision-review", "you"]], calls);
        Assert.Equal(1, passes);
    }

    [Fact]
    public void A_refused_review_starts_no_pass_and_says_why()
    {
        var passes = 0;
        using var window = Open(agents: Agents(4),
            run: _ => Task.FromResult<string?>("board.sh: team1.json names no vision"),
            pass: new DispatchPass(_root, "a-team", (_, _) =>
            {
                passes++;
                return new TaskCompletionSource<Reading>().Task;
            }));
        window.Refresh();
        LayOut(window, 120, 30);
        SelectAgent(window, 3);

        window.Commands.Execute("team.visionReview");
        window.Refresh();

        Assert.Equal(0, passes);
        Assert.Equal("asking for team1's vision review: board.sh: team1.json names no vision", window.Says());
    }

    [Fact]
    public void Write_the_vision_with_me_names_the_selected_team_and_hands_its_interview_the_terminal()
    {
        var handed = new List<Handover>();
        using var window = Open(agents: Agents(4), handOver: handed.Add);
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.False(window.Commands.IsEnabled("team.vision"));
        SelectAgent(window, 3);
        Assert.True(window.Commands.IsEnabled("team.vision"));
        Assert.Equal("Write the vision with me · team1", window.Commands.Registered.Single(c => c.Id == "team.vision").Label);

        Assert.True(window.Commands.Execute("team.vision"));

        var handover = Assert.IsType<VisionHandover>(Assert.Single(handed));
        Assert.Equal(["vision", "team1"], handover.Arguments);
        Assert.Equal(Area.Overseer, handover.Area);
    }

    [Fact]
    public void Back_from_a_session_that_would_not_resume_the_team_s_panel_has_the_role_chosen_and_says_so_in_red()
    {
        using var window = Open(
            agents: Agents(4),
            resume: new AttachHandover("team1", "dev") { Failure = "attach team1 dev exited 1" });
        window.Refresh();
        LayOut(window, 120, 40);

        Assert.Equal(Area.Overseer, window.CurrentArea);
        Assert.Equal(("team1", "dev"), (window.Overseer.Board.Selected?.Team, window.Team.Role));
        Assert.False(window.SessionFull);
        Assert.Equal("attach team1 dev exited 1", window.Says());
        Assert.Equal(Severity.Error, window.Status.MessageSeverity);
    }

    [Fact]
    public void With_several_runs_i_says_to_interrupt_one_from_its_card()
    {
        var calls = 0;
        WriteRun("team0", "dev", 246, 100);
        WriteRun("team0", "dev", 303, 300);
        using var window = Open(agents: Agents(2), confirmStop: _ => throw new InvalidOperationException(), run: _ =>
        {
            calls++;
            return Task.FromResult<string?>(null);
        });
        SelectAgent(window, 1);

        Assert.True(window.NewKeyDownEvent(new Key('i')));

        Assert.Equal(0, calls);
        Assert.Equal("team0's dev is on 2 cards: interrupt one from its card", window.Says());
        Assert.EndsWith(" · #246, #303", window.Team.Lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void With_one_run_on_a_task_i_asks_to_stop_that_run_alone_as_on_its_card()
    {
        var calls = new List<string[]>();
        var asked = new List<RunTask>();
        WriteRun("team0", "dev", 246, 100);
        using var window = Open(agents: Agents(2), confirmStop: task =>
        {
            asked.Add(task);
            return true;
        }, run: arguments =>
        {
            calls.Add(arguments);
            return Task.FromResult<string?>(null);
        });
        SelectAgent(window, 1);

        window.Commands.Execute("agent.interrupt");

        Assert.Equal(new RunTask(246, "Task 246"), Assert.Single(asked));
        Assert.Equal([["stop", "team0", "dev", "246"]], calls);
    }

    [Fact]
    public void Declining_the_stop_leaves_the_run_going()
    {
        var calls = 0;
        WriteRun("team0", "dev", 246, 100);
        using var window = Open(agents: Agents(2), confirmStop: _ => false, run: _ =>
        {
            calls++;
            return Task.FromResult<string?>(null);
        });
        SelectAgent(window, 1);

        window.Commands.Execute("agent.interrupt");

        Assert.Equal(0, calls);
        Assert.Null(window.Status.Message);
    }
}
