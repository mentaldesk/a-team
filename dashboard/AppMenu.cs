using Terminal.Gui.Input;

namespace ATeam.Dashboard;

/// <summary>The menu across the top of both areas. Every item runs a registered command by id, so the menu,
/// the key and the Commands dialog are three doors into one list. Titles and items carry a hot letter, marked
/// in the label with <c>_</c> and drawn underlined: Alt opens a menu, and a bare letter picks from the one
/// that's open.</summary>
internal sealed class AppMenu
{
    internal static readonly (string Title, string[] Ids)[] Layout =
    [
        ("_View", ["view.dashboard", "view.work", "settings", "quit"]),
        ("_Team", ["team.pause"]),
        ("_Help", ["help", "commands", "about"]),
    ];

    private readonly CommandRegistry _commands;
    private readonly List<(string Id, MenuItem Item)> _items = [];
    private readonly List<MenuBarItem> _menus = [];

    internal AppMenu(CommandRegistry commands)
    {
        _commands = commands;
        Bar = new MenuBar { Menus = [.. Layout.Select(Menu)] };
    }

    internal MenuBar Bar { get; }

    internal IReadOnlyList<(string Id, MenuItem Item)> Items => _items;

    /// <summary>The titles across the bar, each holding the items under it.</summary>
    internal IReadOnlyList<MenuBarItem> Menus => _menus;

    /// <summary>Keeps each item reading as its command does now, like pausing the selected team. Setting the
    /// title sets the hot letter with it, so Pause's <c>P</c> becomes Resume's <c>R</c>.</summary>
    internal void Refresh()
    {
        var registered = _commands.Registered;
        foreach (var (id, item) in _items)
        {
            if (registered.FirstOrDefault(entry => entry.Id == id) is { } command && item.Title != Hot(command.Label))
                item.Title = Hot(command.Label);
        }
    }

    /// <summary>Terminal.Gui binds a hot key with and without Alt, whether or not the view has focus, so a bare
    /// title letter would open a menu from anywhere and swallow the app's own single-letter keys. A title keeps
    /// only its Alt forms; an item keeps its bare letter, which only its own menu answers.</summary>
    private MenuBarItem Menu((string Title, string[] Ids) entry)
    {
        var menu = new MenuBarItem(entry.Title, entry.Ids.Select(Item).ToArray<View>());
        menu.HotKeyBindings.Remove(menu.HotKey);
        menu.HotKeyBindings.Remove(menu.HotKey.WithShift);
        // The app's quit key took Esc off the framework's Quit command, and the menu's close with it.
        menu.PopoverMenu?.KeyBindings.Add(Key.Esc, Command.Quit);
        menu.PopoverMenuOpenChanged += (_, _) => Shut(menu);
        Shut(menu);
        _menus.Add(menu);
        return menu;
    }

    /// <summary>A menu the application still holds enabled answers its items' letters wherever the app is, open
    /// or shut, so a shut one is disabled. The framework enables it again as it shows it.</summary>
    private static void Shut(MenuBarItem menu)
    {
        if (menu is { PopoverMenuOpen: false, PopoverMenu: { } popover })
            popover.Enabled = false;
    }

    private View Item(string id)
    {
        var item = new MenuItem
        {
            Title = Hot(_commands.Registered.FirstOrDefault(command => command.Id == id)?.Label ?? id),
            Key = _commands.KeyFor(id),
            // The key is a label here: the window's registry already runs it, and a second binding would run it twice.
            BindKeyToApplication = false,
            Action = () => _commands.Execute(id),
        };
        _items.Add((id, item));
        return item;
    }

    /// <summary>The label with its first letter marked as the hot one.</summary>
    private static string Hot(string label) => $"_{label}";
}
