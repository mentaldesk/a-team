using System.Text;

namespace ATeam.Dashboard;

/// <summary>The menu across the top of both areas. Every item runs a registered command by id, so the menu,
/// the key and the Commands dialog are three doors into one list.</summary>
internal sealed class AppMenu
{
    internal static readonly (string Title, string[] Ids)[] Layout =
    [
        ("View", ["view.dashboard", "view.work", "settings", "quit"]),
        ("Team", ["team.pause"]),
        ("Help", ["help", "commands", "about"]),
    ];

    private readonly CommandRegistry _commands;
    private readonly List<(string Id, MenuItem Item)> _items = [];

    internal AppMenu(CommandRegistry commands)
    {
        _commands = commands;
        Bar = new MenuBar
        {
            Menus =
            [
                .. Layout.Select(entry => new MenuBarItem(entry.Title, entry.Ids.Select(Item).ToArray<View>())
                {
                    // No hot key: the app's own keys are single letters, and a menu hot key would swallow them.
                    HotKeySpecifier = (Rune)0xffff,
                })
            ],
        };
    }

    internal MenuBar Bar { get; }

    internal IReadOnlyList<(string Id, MenuItem Item)> Items => _items;

    /// <summary>Keeps each item reading as its command does now, like pausing the selected team.</summary>
    internal void Refresh()
    {
        var registered = _commands.Registered;
        foreach (var (id, item) in _items)
        {
            var command = registered.FirstOrDefault(entry => entry.Id == id);
            if (command is not null && item.Title != command.Label)
                item.Title = command.Label;
        }
    }

    private View Item(string id)
    {
        var item = new MenuItem
        {
            Title = _commands.Registered.FirstOrDefault(command => command.Id == id)?.Label ?? id,
            Key = _commands.KeyFor(id),
            // The key is a label here: the window's registry already runs it, and a second binding would run it twice.
            BindKeyToApplication = false,
            Action = () => _commands.Execute(id),
        };
        _items.Add((id, item));
        return item;
    }
}
