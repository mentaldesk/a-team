using Terminal.Gui.Input;

namespace ATeam.Dashboard.Tests;

public class StopRunDialogTests
{
    [Fact]
    public void It_names_the_task_it_stops_and_says_the_other_runs_carry_on()
    {
        using var dialog = new StopRunDialog(new RunTask(244, "Choose whose comments count"));

        Assert.Equal("Stop the Dev run on #244?", dialog.Title);
        Assert.StartsWith("Ends the run on #244 Choose whose comments count.", dialog.Body.Text.Replace('\n', ' '));
        Assert.Contains("other Dev runs carry on", dialog.Body.Text.Replace('\n', ' '));
        Assert.Equal(["Enter stop", "Esc cancel"], dialog.Hints.Select(hint => hint.Text));
    }

    [Fact]
    public void Esc_cancels()
    {
        using var dialog = new StopRunDialog(new RunTask(244, "Choose whose comments count"));

        Assert.True(dialog.NewKeyDownEvent(Key.Esc));

        Assert.False(dialog.Confirmed);
    }
}
