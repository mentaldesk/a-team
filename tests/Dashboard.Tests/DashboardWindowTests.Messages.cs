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
    public void A_command_that_worked_leaves_the_message_block_empty_again()
    {
        using var window = Open(agents: Agents(4));
        SelectAgent(window, 0);

        window.Commands.Execute("agent.hold");
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal("team0", window.Says());
        Assert.Null(window.Status.Message);
    }

    [Fact]
    public void A_command_that_failed_shows_one_line_and_leaves_the_dashboard_usable()
    {
        using var window = Open(
            agents: Agents(4),
            run: _ => Task.FromResult<string?>("a-team pause: can't write /nope/team0.json\nstack\ntrace"));
        SelectAgent(window, 0);

        window.Commands.Execute("agent.hold");
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal("pausing team0's lead: a-team pause: can't write /nope/team0.json", window.Says());
        Assert.True(window.NewKeyDownEvent(Key.CursorDown));
        Assert.Equal(("team1", true), (window.Overseer.Board.Selected?.Team, window.Overseer.Board.OnHeading));
    }

    [Fact]
    public void Teams_and_New_team_are_commands_and_nothing_pauses_a_team_from_outside_them()
    {
        using var window = Open(agents: Agents(4));

        var ids = window.Commands.Registered.Select(command => command.Id).ToList();
        Assert.DoesNotContain("team.pause", ids);
        Assert.Contains("teams", ids);
        Assert.Contains("teams.new", ids);
        Assert.DoesNotContain(window.MenuItems, item => item.Id == "team.pause");
    }

    [Fact]
    public void Pause_this_role_holds_the_selected_role_without_stopping_its_run()
    {
        var calls = new List<string[]>();
        using var window = Open(agents: Agents(4), run: arguments =>
        {
            calls.Add(arguments);
            return new TaskCompletionSource<string?>().Task;
        });
        WriteRunning("team1", "dev");
        window.Refresh();
        SelectAgent(window, 3);

        Assert.True(window.NewKeyDownEvent(new Key('h')));

        Assert.Equal([["pause", "team1", "dev"]], calls);
        Assert.Equal("Pausing this role…", window.Says());
    }

    [Fact]
    public void Pause_this_role_reads_Let_this_role_start_again_once_held_which_resumes_it()
    {
        var calls = new List<string[]>();
        using var window = Open(agents: Agents(2), run: arguments =>
        {
            calls.Add(arguments);
            if (arguments[0] == "pause")
                WriteHold(arguments[1], arguments[2]);
            return Task.FromResult<string?>(null);
        });
        window.Refresh();
        SelectAgent(window, 1);
        Assert.True(window.Commands.IsEnabled("agent.hold"));
        Assert.Equal("Pause this role", Label(window, "agent.hold"));

        window.Commands.Execute("agent.hold");
        window.Refresh();

        Assert.True(window.Watches[1].Held);
        Assert.StartsWith(Icons.Field(Icon.StoppedByYou, IconStyle.Unicode) + "Dev           paused", window.Team.Lines[1], StringComparison.Ordinal);
        Assert.Equal("Let this role start again", Label(window, "agent.hold"));
        Assert.Equal("Let t_his role start again", window.MenuItems.Single(item => item.Id == "agent.hold").Item.Title);

        window.Commands.Execute("agent.hold");

        Assert.Equal([["pause", "team0", "dev"], ["resume", "team0", "dev"]], calls);
    }

    [Fact]
    public void i_interrupts_the_selected_agents_run_and_says_so_while_it_runs()
    {
        var calls = new List<string[]>();
        using var window = Open(agents: Agents(4), run: arguments =>
        {
            calls.Add(arguments);
            return new TaskCompletionSource<string?>().Task;
        });
        WriteRunning("team1", "dev");
        window.Refresh();
        SelectAgent(window, 3);

        Assert.True(window.NewKeyDownEvent(new Key('i')));

        Assert.Equal([["stop", "team1", "dev"]], calls);
        Assert.Equal("Interrupting…", window.Says());
    }
}
