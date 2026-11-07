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
    public void The_window_has_no_border_and_no_title_so_the_menu_is_row_zero()
    {
        using var window = Open(agents: Agents(4));

        LayOut(window, 120, 30);

        Assert.Equal("", window.Title);
        Assert.Equal(LineStyle.None, window.BorderStyle);
        Assert.Equal(new Size(120, 30), window.Viewport.Size);
        Assert.Equal(new Rectangle(0, 0, 120, 1), window.Menu.Frame);
    }

    [Fact]
    public void Every_dispatcher_line_gets_one_row_of_its_own_coloured_by_what_it_says()
    {
        WriteDispatchLog(
            "2026-09-20T16:53:09Z a-team lead: would start: 3 Ideas to shape",
            "2026-09-20T17:01:17Z tuicode dev: triggers failed: gh: Not Found (HTTP 404)",
            "2026-09-20T17:04:02Z tuicode dev: started 41234: 1 task Ready");
        using var window = Open(agents: Agents(4));

        window.Commands.Execute("view.dispatcher");

        Assert.Equal(
            ["16:53 a-team lead: would start: 3 Ideas to shape",
             "17:01 tuicode dev: triggers failed: gh: Not Found (HTTP 404)",
             "17:04 tuicode dev: started 41234: 1 task Ready"],
            window.WholeDispatchLog.Lines.Select(line => line.Text));
        Assert.Equal(
            [LogLineKind.DispatchSkipped, LogLineKind.DispatchFailed, LogLineKind.Prose],
            window.WholeDispatchLog.Lines.Select(line => line.Kind));
    }

    [Fact]
    public void With_nothing_installed_the_dispatcher_s_log_says_so_in_Error_and_its_lines_do_not()
    {
        using var window = Open(agents: Agents(4));
        LayOut(window, 120, 30);

        window.Commands.Execute("view.dispatcher");

        Assert.Equal("dispatcher · nothing installed · run: a-team install", window.Dispatcher.Title);
        Assert.Equal(SchemeManager.SchemesToSchemeName(Schemes.Error), window.Dispatcher.SchemeName);
        Assert.Equal(SchemeManager.SchemesToSchemeName(Schemes.Base), window.WholeDispatchLog.SchemeName);
    }

    [Fact]
    public void The_message_block_takes_no_rows_until_there_is_something_to_say()
    {
        using var window = Open(agents: Agents(4));

        LayOut(window, 120, 30);

        Assert.Equal("", window.Says());
        Assert.Equal(window.Viewport.Height - 2, window.Overseer.Frame.Height);
    }

    [Fact]
    public void Typing_pass_in_the_Commands_palette_finds_running_one_now()
    {
        using var window = Open();

        Assert.Equal(["dispatch.pass"], window.Commands.Enabled
            .Where(command => command.Label.Contains("pass", StringComparison.OrdinalIgnoreCase)).Select(command => command.Id));
    }
}
