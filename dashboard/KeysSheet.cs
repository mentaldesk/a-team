using System.Drawing;
using MentalDesk.Tui.Commands;
using MentalDesk.Tui.Dialogs;
using MentalDesk.Tui.Keys;
using Terminal.Gui.Input;

namespace ATeam.Dashboard;

/// <summary>What F1 shows: the global keys under the menu that holds them, beside the keys for the region the window
/// is in. Only commands that would run now and have a key are listed.</summary>
internal static class KeysSheet
{
    internal static KeySheet For(IReadOnlyList<CommandDescriptor> enabled, CommandScope here)
    {
        var keyed = enabled.Where(command => command.Key != Key.Empty).ToList();
        var local = keyed.Where(command => command.Scopes.Contains(here)).Select(Row).ToList();
        var global = keyed.Where(command => command.Scopes.Count == 0).ToList();
        var groups = new List<KeyGroup>();
        var listed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (title, ids) in AppMenu.Layout)
        {
            var rows = ids.Where(listed.Add).SelectMany(id => global.Where(command => command.Id == id)).Select(Row).ToList();
            if (rows.Count > 0)
                groups.Add(new KeyGroup(title.Replace("_", ""), rows));
        }
        var other = global.Where(command => !listed.Contains(command.Id)).Select(Row).ToList();
        if (other.Count > 0)
            groups.Add(new KeyGroup(KeySheet.OtherHeading, other));
        return new KeySheet(
            local.Count > 0 ? new KeyColumn(here.Name, [new KeyGroup(null, local)]) : null,
            new KeyColumn("Everywhere", groups));
    }

    /// <summary>The library binds the dialog's keys only under an AppShell the dashboard doesn't have, so they're
    /// routed here.</summary>
    internal static KeysDialog Create(KeySheet sheet, Size screen)
    {
        // TuiCode puts Everywhere on the left, and the place's keys on top once stacked; the library does the reverse.
        var dialog = new KeysDialog(sheet.Here is { } here ? new KeySheet(sheet.Everywhere, here) : sheet, screen);
        if (dialog.Stacked)
        {
            dialog.Dispose();
            dialog = new KeysDialog(sheet, screen);
        }
        dialog.Hints.Visible = false;
        var close = AppButton.Secondary("Esc Close");
        close.X = Pos.Center();
        close.Y = Pos.Top(dialog.Hints);
        close.CanFocus = false;
        close.Accepting += (_, args) =>
        {
            args.Handled = true;
            dialog.RequestStop();
        };
        dialog.Add(close);
        dialog.KeyDown += (_, key) => key.Handled = Handle(dialog, key);
        return dialog;
    }

    internal static void Show(IApplication app, KeySheet sheet)
    {
        using var dialog = Create(sheet, app.Screen.Size);
        app.Run(dialog);
    }

    private static bool Handle(KeysDialog dialog, Key key)
    {
        var page = dialog.Body.Viewport.Height;
        if (key == Key.Esc)
            dialog.RequestStop();
        else if (key == Key.CursorUp || key == Key.CursorDown)
            dialog.Body.ScrollVertical(key == Key.CursorUp ? -1 : 1);
        else if (key == Key.PageUp || key == Key.PageDown)
            dialog.Body.ScrollVertical(key == Key.PageUp ? -page : page);
        return true;
    }

    private static KeyRow Row(CommandDescriptor command) => new(KeyNames.Short(command.Key), command.Label);
}
