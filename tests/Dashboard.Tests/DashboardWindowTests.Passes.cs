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
    public void A_pass_now_says_it_is_running_in_Accent_then_what_it_started()
    {
        var finish = new TaskCompletionSource<Reading>();
        using var window = Open(pass: new DispatchPass(_root, "a-team", (_, _) => finish.Task));

        window.Commands.Execute("dispatch.pass");

        Assert.Equal("Running a pass…", window.Says());
        Assert.Null(window.Status.MessageSeverity);

        WriteDispatchLog("2026-10-04T10:02:00Z a-team dev: started 42 on 0.1.12: #334: Ready task #334 to build");
        finish.SetResult(new Reading("", null));
        window.Refresh();

        Assert.Equal("Pass done: started 1 run.", window.Says());
        window.Commands.Execute("view.dispatcher");
        Assert.Equal("10:02 a-team dev: started 42 on 0.1.12: #334: Ready task #334 to build", Assert.Single(window.WholeDispatchLog.Lines).Text);
    }

    [Fact]
    public void A_pass_now_that_failed_says_its_first_line_in_Error()
    {
        using var window = Open(pass: new DispatchPass(_root, "a-team", (_, _) => Task.FromResult(new Reading("", "a-team: no such command"))));

        window.Commands.Execute("dispatch.pass");
        window.Refresh();

        Assert.Equal("a-team: no such command", window.Says());
        Assert.Equal(Severity.Error, window.Status.MessageSeverity);
    }

    [Fact]
    public void A_second_pass_now_while_one_runs_only_says_one_is_already_running()
    {
        var calls = 0;
        using var window = Open(pass: new DispatchPass(_root, "a-team", (_, _) =>
        {
            calls++;
            return new TaskCompletionSource<Reading>().Task;
        }));

        window.Commands.Execute("dispatch.pass");
        window.Commands.Execute("dispatch.pass");

        Assert.Equal(1, calls);
        Assert.Equal("A pass is already running.", window.Says());
    }

    [Fact]
    public void Pausing_a_role_already_being_paused_says_so_until_the_first_one_resolves()
    {
        var calls = 0;
        var finish = new TaskCompletionSource<string?>();
        using var window = Open(agents: Agents(4), run: _ =>
        {
            calls++;
            return finish.Task;
        });
        SelectAgent(window, 0);

        window.Commands.Execute("agent.hold");
        window.Commands.Execute("agent.hold");
        Assert.Equal(1, calls);
        Assert.Equal("team0's lead is already being paused", window.Says());

        finish.SetResult(null);
        window.Refresh();
        window.Commands.Execute("agent.hold");
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Pausing_interrupting_and_asking_for_a_vision_review_queue_behind_each_other()
    {
        var calls = new List<string[]>();
        var finishes = new List<TaskCompletionSource<string?>>();
        using var window = Open(agents: Agents(4), run: arguments =>
        {
            calls.Add(arguments);
            var finish = new TaskCompletionSource<string?>();
            finishes.Add(finish);
            return finish.Task;
        }, pass: new DispatchPass(_root, "a-team", (_, _) => new TaskCompletionSource<Reading>().Task));
        WriteRunning("team1", "dev");
        window.Refresh();
        LayOut(window, 120, 30);

        SelectAgent(window, 3);
        window.Commands.Execute("agent.interrupt");
        window.Commands.Execute("team.visionReview");
        SelectAgent(window, 0);
        window.Commands.Execute("agent.hold");

        Assert.Equal([["stop", "team1", "dev"]], calls);
        Assert.Equal("Interrupting… · 2 more queued", window.Says());

        for (var i = 0; i < 3; i++)
        {
            finishes[i].SetResult(null);
            window.Refresh();
        }

        Assert.Equal([["stop", "team1", "dev"], ["board", "team1", "vision-review", "you"], ["pause", "team0", "lead"]], calls);
    }
}
