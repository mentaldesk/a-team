using MentalDesk.Tui.Dialogs;
using MentalDesk.Tui.Keys;

namespace ATeam.Dashboard;

/// <summary>What MentalDesk.Tui's AppShell does for a dialog, which the dashboard hasn't got: the dialog's keys reach
/// its keymap before any of its views, and F1 shows them.</summary>
public static class DialogHost
{
    /// <summary>Each view hears a key before the dialog does, so every one of them hands it to the dialog's keys first.</summary>
    public static T Routed<T>(this T dialog) where T : AppDialog
    {
        Hook(dialog);
        return dialog;

        void Hook(View view)
        {
            view.KeyDown += (_, key) => key.Handled = key.Handled || dialog.Scope.Handle(key) != KeyResult.Pass;
            view.SubViewAdded += (_, added) =>
            {
                if (added.SubView is { } sub)
                    Hook(sub);
            };
            foreach (var sub in view.SubViews)
                Hook(sub);
        }
    }

    /// <summary>Runs <paramref name="dialog"/>, showing Help over it on F1.</summary>
    public static void Run(IApplication app, AppDialog dialog)
    {
        EventHandler<KeySheet> show = (_, sheet) => ShowKeys(dialog, sheet, keys => KeysSheet.Show(app, keys));
        dialog.KeysRequested += show;
        try
        {
            app.Run(dialog);
        }
        finally
        {
            dialog.KeysRequested -= show;
        }
    }

    /// <summary>Focus goes back where it was once Help closes, so a comment half written carries on at its cursor.</summary>
    internal static void ShowKeys(AppDialog dialog, KeySheet sheet, Action<KeySheet> show)
    {
        var focused = dialog.MostFocused;
        show(dialog is DashboardDialog ours ? ours.Sheet : sheet);
        focused?.SetFocus();
    }
}
