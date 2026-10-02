namespace ATeam.Dashboard;

/// <summary>Asks before a task's PR is merged, saying what merging does and when the teams get it.</summary>
public sealed class AcceptDialog(WaitingItem item) : ConfirmDialog($"Accept #{item.Number}?", Says(item), "accept")
{
    internal static string Says(WaitingItem item) =>
        $"Merges PR #{item.Pr} into {(item.Base.Length > 0 ? item.Base : "main")}, squashed, and closes #{item.Number}.\n\n" +
        "Merged work reaches the teams when you next release.";

    public static bool Show(IApplication app, WaitingItem item)
    {
        using var dialog = new AcceptDialog(item);
        return Ask(app, dialog);
    }
}
