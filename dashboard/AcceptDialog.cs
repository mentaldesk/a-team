namespace ATeam.Dashboard;

/// <summary>Asks before a task's PR is merged or a validated pitch is closed, saying what that does.</summary>
public sealed class AcceptDialog(WaitingItem item) : ConfirmDialog($"Accept #{item.Number}?", Says(item), "accept")
{
    internal static string Says(WaitingItem item) => item.Pitch
        ? $"Closes #{item.Number} as done. " +
          (item.Tasks == 1 ? "Its 1 task is already merged." : $"Its {item.Tasks} tasks are already merged.")
        : $"Merges PR #{item.Pr} into {(item.Base.Length > 0 ? item.Base : "main")}, squashed, and closes #{item.Number}.\n\n" +
        "Merged work reaches the teams when you next release.";

    public static bool Show(IApplication app, WaitingItem item)
    {
        using var dialog = new AcceptDialog(item);
        return Ask(app, dialog);
    }
}
