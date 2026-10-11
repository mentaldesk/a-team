using MentalDesk.Tui.Dialogs;

namespace ATeam.Dashboard;

/// <summary>Asks before a task's PR is merged or a validated pitch is closed, saying what that does.</summary>
public static class AcceptDialog
{
    internal static readonly ConfirmAction Accept = new("Accept", ButtonKind.Primary, Confirm.Chord);

    internal static string Says(WaitingItem item) => item.Pitch
        ? $"Closes #{item.Number} as done. " +
          (item.Tasks == 1 ? "Its 1 task is already merged." : $"Its {item.Tasks} tasks are already merged.")
        : item.Docs
        ? $"Merges the Customer lead's docs PR #{item.Number} into {(item.Base.Length > 0 ? item.Base : "main")}, squashed. " +
          "The next pitch that's done starts a new one."
        : $"Merges PR #{item.Pr} into {(item.Base.Length > 0 ? item.Base : "main")}, squashed, and closes #{item.Number}.\n\n" +
        "Merged work reaches the teams when you next release.";

    internal static ConfirmDialog Create(WaitingItem item, int width = Confirm.Wide) =>
        Confirm.Create($"Accept #{item.Number}?", Says(item), Accept, width);

    public static bool Show(IApplication app, WaitingItem item)
    {
        using var dialog = Create(item, Confirm.Fit(app.Screen.Width));
        return Confirm.Ask(app, dialog);
    }
}
