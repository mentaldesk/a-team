using System.Text.Json.Nodes;
using MentalDesk.Tui.Commands;
using MentalDesk.Tui.Keys;
using Terminal.Gui.Input;

namespace ATeam.Dashboard;

/// <summary>Keys saved before the dashboard moved onto MentalDesk.Tui's keymap, one per command, carried over as that
/// keymap's overrides. A saved key replaces its command's defaults in each region it works in, and takes the key from
/// any other command whose default it was.</summary>
internal static class SavedKeys
{
    internal static JsonObject Carry(IReadOnlyList<(string Id, Key Key)> saved, CommandRegistry commands)
    {
        var defaults = commands.Keys.Bindings.ToList();
        var bindings = defaults.ToList();
        var registered = commands.Registered;
        foreach (var (id, key) in saved)
        {
            if (registered.FirstOrDefault(command => command.Id == id) is not { } command)
                continue;
            bindings.RemoveAll(binding => binding.CommandId == id);
            if (key == Key.Empty)
                continue;
            foreach (var scope in command.Scopes.DefaultIfEmpty(CommandScope.Global))
            {
                bindings.RemoveAll(binding => binding.Scope == scope && binding.Chord is [var only] && only == key);
                bindings.Add(new MentalDesk.Tui.Keys.KeyBinding([key], id, scope));
            }
        }
        return KeyOverrides.Write(defaults, bindings, commands.Library);
    }
}
