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
    public void It_stays_inside_a_small_terminal()
    {
        using var host = new View { Width = 30, Height = 6 };
        using var dialog = new AboutDialog("1.2.3");
        host.Add(dialog);

        host.Layout(new Size(30, 6));

        Assert.True(host.Viewport.Contains(dialog.Frame), $"{dialog.Frame} overhangs {host.Viewport}");
    }
}
