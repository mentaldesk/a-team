using MentalDesk.Tui.Dialogs;

namespace ATeam.Dashboard;

/// <summary>Asks before a pitch is approved, saying what that starts.</summary>
public static class ApproveDialog
{
    internal static readonly ConfirmAction Approve = new("Approve", ButtonKind.Primary, Confirm.Chord);

    internal static string Says(WaitingItem item) =>
        $"Moves #{item.Number} to Approved. The Lead's next run breaks it into tasks.";

    internal static ConfirmDialog Create(WaitingItem item, int width = Confirm.Wide) =>
        Confirm.Create($"Approve #{item.Number}?", Says(item), Approve, width);

    public static bool Show(IApplication app, WaitingItem item)
    {
        using var dialog = Create(item, Confirm.Fit(app.Screen.Width));
        return Confirm.Ask(app, dialog);
    }
}
