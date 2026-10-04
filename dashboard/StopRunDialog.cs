namespace ATeam.Dashboard;

/// <summary>Asks before one of several Dev runs is stopped, naming its task.</summary>
public sealed class StopRunDialog(RunTask task) : ConfirmDialog($"Stop the Dev run on #{task.Number}?", Says(task), "stop")
{
    internal static string Says(RunTask task) =>
        $"Ends the run on #{task.Number} {task.Title}. The task stays where it is on the board, and no run starts on it " +
        "until you let it start again. The other Dev runs carry on, and new tasks can still start.";

    public static bool Show(IApplication app, RunTask task)
    {
        using var dialog = new StopRunDialog(task);
        return Ask(app, dialog);
    }
}
