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

    internal static KeysDialog Create(KeySheet sheet, Size screen)
    {
        var dialog = new KeysDialog(sheet, screen).Routed();
        dialog.CloseButton.X = Pos.Center();
        return dialog;
    }

    internal static void Show(IApplication app, KeySheet sheet)
    {
        using var dialog = Create(sheet, app.Screen.Size);
        app.Run(dialog);
    }

    private static KeyRow Row(CommandDescriptor command) => new(KeyNames.Short(command.Key), command.Label);
}
