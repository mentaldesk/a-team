using System.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace ATeam.Dashboard.Tests;

public class AboutDialogTests
{
    [Fact]
    public void It_says_which_version_you_are_running()
    {
        using var dialog = new AboutDialog("1.2.3");

        Assert.Equal("a-team 1.2.3", dialog.About.Text.Split('\n')[0]);
        Assert.Contains("github.com/mentaldesk/a-team", dialog.About.Text);
    }

    [Fact]
    public void Esc_closes_it_and_Enter_does_nothing()
    {
        using var dialog = new AboutDialog("1.2.3");

        dialog.NewKeyDownEvent(Key.Enter);
        Assert.False(dialog.Closed);

        Assert.True(dialog.NewKeyDownEvent(Key.Esc));
        Assert.True(dialog.Closed);
    }

    [Fact]
    public void The_van_drives_above_the_text_while_it_is_open_and_stops_once_it_closes()
    {
        using var host = new View { Width = 120, Height = 40 };
        using var dialog = new AboutDialog("1.2.3");
        host.Add(dialog);

        host.Layout(new Size(120, 40));

        Assert.True(dialog.Van.Visible);
        Assert.True(dialog.Van.Frame.Bottom < dialog.About.Frame.Y);
        Assert.True(dialog.Viewport.Contains(dialog.Van.Frame), $"{dialog.Van.Frame} overhangs {dialog.Viewport}");

        dialog.NewKeyDownEvent(Key.Esc);

        Assert.False(dialog.Van.Visible);
    }

    [Fact]
    public void A_terminal_too_small_for_the_van_shows_the_text_alone()
    {
        using var host = new View { Width = 60, Height = 12 };
        using var dialog = new AboutDialog("1.2.3");
        host.Add(dialog);

        host.Layout(new Size(60, 12));

        Assert.False(dialog.Van.Visible);
        Assert.Equal(0, dialog.About.Frame.Y);
        Assert.Equal("a-team 1.2.3", dialog.About.Text.Split('\n')[0]);
    }

    [Fact]
    public void It_stays_inside_a_small_terminal()
    {
        using var host = new View { Width = 30, Height = 6 };
        using var dialog = new AboutDialog("1.2.3");
        host.Add(dialog);

        host.Layout(new Size(30, 6));

        Assert.True(host.Viewport.Contains(dialog.Frame), $"{dialog.Frame} overhangs {host.Viewport}");
    }
}
