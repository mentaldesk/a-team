namespace ATeam.Dashboard.Tests;

public class StopRunDialogTests
{
    [Fact]
    public void It_names_the_task_it_stops_and_says_the_other_runs_carry_on()
    {
        using var dialog = StopRunDialog.Create(new RunTask(244, "Choose whose comments count"));

        Assert.Equal("Stop the Dev run on #244?", dialog.Title);
        Assert.StartsWith("Ends the run on #244 Choose whose comments count.", string.Join(' ', dialog.Lines));
        Assert.Contains("other Dev runs carry on", string.Join(' ', dialog.Lines));
    }
}
