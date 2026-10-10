using MentalDesk.Tui.Dialogs;

namespace ATeam.Dashboard;

/// <summary>Asks before one of several Dev runs is stopped, naming its task.</summary>
public static class StopRunDialog
{
    internal static readonly ConfirmAction Stop = new("Stop", ButtonKind.Danger, Confirm.Chord);

    internal static string Says(RunTask task) =>
        $"Ends the run on #{task.Number} {task.Title}. The task stays where it is on the board, and no run starts on it " +
        "until you let it start again. The other Dev runs carry on, and new tasks can still start.";

    internal static ConfirmDialog Create(RunTask task, int width = Confirm.Wide) =>
        Confirm.Create($"Stop the Dev run on #{task.Number}?", Says(task), Stop, width);

    public static bool Show(IApplication app, RunTask task)
    {
        using var dialog = Create(task, Confirm.Fit(app.Screen.Width));
        return Confirm.Ask(app, dialog);
    }
}
