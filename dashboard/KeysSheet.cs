using System.Drawing;
using MentalDesk.Tui.Commands;
using MentalDesk.Tui.Dialogs;
using MentalDesk.Tui.Keys;
using Terminal.Gui.Input;

namespace ATeam.Dashboard;

/// <summary>What F1 shows: the global keys under the menu that holds them, beside the keys for the region the window
/// is in, chords included. Only commands that would run now and have a key are listed.</summary>
internal static class KeysSheet
{
    internal static KeySheet For(CommandRegistry commands, IReadOnlyList<CommandDescriptor> enabled, CommandScope here)
    {
        var offered = enabled.ToDictionary(command => command.Id);
        var bindings = commands.Keys.Bindings.Where(binding => offered.ContainsKey(binding.CommandId)).ToList();
        var local = bindings.Where(binding => binding.Scope == here).Select(Row).ToList();
        var global = bindings.Where(binding => binding.Scope == CommandScope.Global).ToList();
        var groups = new List<KeyGroup>();
        var listed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (title, ids) in AppMenu.Layout)
        {
            var rows = ids.Where(listed.Add).SelectMany(id => global.Where(binding => binding.CommandId == id)).Select(Row).ToList();
            if (rows.Count > 0)
                groups.Add(new KeyGroup(title.Replace("_", ""), rows));
        }
        var other = global.Where(binding => !listed.Contains(binding.CommandId)).Select(Row).ToList();
        if (other.Count > 0)
            groups.Add(new KeyGroup(KeySheet.OtherHeading, other));
        return new KeySheet(
            local.Count > 0 ? new KeyColumn(here.Name, [new KeyGroup(null, local)]) : null,
            new KeyColumn("Everywhere", groups));

        KeyRow Row(MentalDesk.Tui.Keys.KeyBinding binding) => new(binding.Display, offered[binding.CommandId].Label);
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

}
