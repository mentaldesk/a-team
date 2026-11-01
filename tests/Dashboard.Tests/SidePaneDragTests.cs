using System.Drawing;
using MentalDesk.Tui.Layout;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace ATeam.Dashboard.Tests;

/// <summary>Dragging a side pane's inner border with the mouse, in the reader and the Guide.</summary>
public class SidePaneDragTests
{
    private const int Columns = 120;

    private static readonly WaitingItem Item = new(180, "A pitch", "Pitched", "https://x/180", "a-team", Pitch: true);

    [Fact]
    public void Dragging_History_s_left_border_widens_it_and_the_body_gives_up_the_difference()
    {
        var widths = new PaneWidths();
        using var app = Started();
        using var dialog = new ReaderDialog(Item, new IssueBody("Body"), () => null, panes: new ReaderPanes(widths), width: Columns);
        Drag(app, dialog, dialog.HistoryLog.SuperView!, by: -10);

        Assert.Equal(ReaderPanes.HistoryWidth + 10, dialog.HistoryLog.SuperView!.Frame.Width);
        Assert.Equal(dialog.Viewport.Width - ReaderPanes.HistoryWidth - 10, dialog.Body.SuperView!.Frame.Width);
        Assert.Equal(ReaderPanes.HistoryWidth + 10, widths.Width(PaneWidths.History, 0));
    }

    [Fact]
    public void A_drag_stops_where_the_body_keeps_40_columns()
    {
        using var app = Started();
        using var dialog = new ReaderDialog(Item, new IssueBody("Body"), () => null, width: Columns);
        Drag(app, dialog, dialog.HistoryLog.SuperView!, by: -100);

        Assert.Equal(SidePanes.MinimumMainWidth, dialog.Body.SuperView!.Frame.Width);
    }

    [Fact]
    public void Dragging_Contents_right_border_widens_it()
    {
        var widths = new PaneWidths();
        using var app = Started();
        using var dialog = new GuideDialog(GuidePages.Bundled(), _ => default, widths: widths);
        Drag(app, dialog, dialog.Sidebar, by: 8, right: true);

        Assert.Equal(38, dialog.Sidebar.Frame.Width);
        Assert.Equal(38, widths.Width(PaneWidths.Contents, 0));
    }

    private static IApplication Started()
    {
        var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        app.Driver!.SetScreenSize(Columns, 24);
        return app;
    }

    private static void Drag(IApplication app, View dialog, View pane, int by, bool right = false)
    {
        app.Begin((IRunnable)dialog);
        app.LayoutAndDraw(true);
        var frame = pane.FrameToScreen();
        var border = new Point(right ? frame.Right - 1 : frame.X, frame.Y + frame.Height / 2);
        var to = border with { X = border.X + by };
        app.Mouse.RaiseMouseEvent(new Mouse { ScreenPosition = border, Flags = MouseFlags.LeftButtonPressed });
        app.Mouse.RaiseMouseEvent(new Mouse { ScreenPosition = to, Flags = MouseFlags.LeftButtonPressed | MouseFlags.PositionReport });
        app.Mouse.RaiseMouseEvent(new Mouse { ScreenPosition = to, Flags = MouseFlags.LeftButtonReleased });
        app.LayoutAndDraw(true);
    }
}
