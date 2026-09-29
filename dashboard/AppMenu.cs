using Terminal.Gui.Input;

namespace ATeam.Dashboard;

/// <summary>The menu across the top of both areas. Every item runs a registered command by id, so the menu,
/// the key and the Commands dialog are three doors into one list. Titles and items carry a hot letter, marked
/// in the label with <c>_</c> and drawn underlined: Alt opens a menu, and a bare letter picks from the one
/// that's open.</summary>
internal sealed class AppMenu
{
    /// <summary>Holds whatever commands the registry marks as acting on the selected card, rather than a fixed list.</summary>
    internal const string Cards = "_Cards";

    internal static readonly (string Title, string[] Ids)[] Layout =
    [
        ("_View", ["view.dashboard", "view.work", "settings", "quit"]),
        (Cards, []),
        ("_Agents", ["agent.hold", "agent.interrupt"]),
        ("_Help", ["help", "commands", "about"]),
    ];

    private readonly CommandRegistry _commands;
    private readonly List<(string Id, MenuItem Item)> _items = [];
    private readonly List<MenuBarItem> _menus = [];
    private readonly MenuBarItem _cards;

    internal AppMenu(CommandRegistry commands)
    {
        _commands = commands;
        Bar = new MenuBar { Menus = [.. Layout.Select(Menu)] };
        _cards = _menus.Single(menu => menu.Title == Cards);
    }

    internal MenuBar Bar { get; }

    internal IReadOnlyList<(string Id, MenuItem Item)> Items => _items;

    /// <summary>The titles across the bar, each holding the items under it.</summary>
    internal IReadOnlyList<MenuBarItem> Menus => _menus;

    /// <summary>Keeps each item reading as its command does now, like holding the selected agent's role, and
    /// showing the key it's bound to now. Setting the title sets the hot letter with it. A card command registered
    /// since the menu was built joins Cards.</summary>
    internal void Refresh()
    {
        foreach (var id in CardIds().Where(id => !_items.Exists(entry => entry.Id == id)))
            _cards.PopoverMenu?.Root?.Add(Item(id));
        var registered = _commands.Registered;
        foreach (var (id, item) in _items)
        {
            if (registered.FirstOrDefault(entry => entry.Id == id) is not { } command)
                continue;
            if (item.Title != Hot(command.MenuLabel, command.Key))
                item.Title = Hot(command.MenuLabel, command.Key);
            if (item.Key != command.Key)
                item.Key = command.Key;
        }
        foreach (var menu in _menus)
            Grey(menu);
    }

    /// <summary>Terminal.Gui binds a hot key with and without Alt, whether or not the view has focus, so a bare
    /// title letter would open a menu from anywhere and swallow the app's own single-letter keys. A title keeps
    /// only its Alt forms; an item keeps its bare letter, which only its own menu answers.</summary>
    private MenuBarItem Menu((string Title, string[] Ids) entry)
    {
        var ids = entry.Title == Cards ? CardIds() : entry.Ids;
        var menu = new MenuBarItem(entry.Title, ids.Select(Item).ToArray<View>());
        menu.HotKeyBindings.Remove(menu.HotKey);
        menu.HotKeyBindings.Remove(menu.HotKey.WithShift);
        // The app's quit key took Esc off the framework's Quit command, and the menu's close with it.
        menu.PopoverMenu?.KeyBindings.Add(Key.Esc, Command.Quit);
        menu.PopoverMenuOpenChanged += (_, _) =>
        {
            Shut(menu);
            Grey(menu);
        };
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

    /// <summary>Greys out what wouldn't run now, like trying a card with no PR. Only in an open menu: enabling an
    /// item in a shut one would have it answer its letter from anywhere.</summary>
    private void Grey(MenuBarItem menu)
    {
        if (menu.PopoverMenu is not { Enabled: true })
            return;
        foreach (var (id, item) in _items.Where(entry => entry.Item.SuperView == menu.PopoverMenu.Root))
            item.Enabled = _commands.IsEnabled(id);
    }

    private string[] CardIds() => [.. _commands.Registered.Where(command => command.OnCard).Select(command => command.Id)];

    private MenuItem Item(string id)
    {
        var item = new MenuItem
        {
            Title = _commands.Registered.FirstOrDefault(command => command.Id == id) is { } command
                ? Hot(command.MenuLabel, command.Key)
                : Hot(id, Key.Empty),
            Key = _commands.KeyFor(id),
            // The key is a label here: the window's registry already runs it, and a second binding would run it twice.
            BindKeyToApplication = false,
            Action = () => _commands.Execute(id),
        };
        _items.Add((id, item));
        return item;
    }

    /// <summary>The label with its command's own letter key marked as the hot one where the label has it, and its
    /// first letter otherwise.</summary>
    private static string Hot(string label, Key key)
    {
        var at = key.IsKeyCodeAtoZ && !key.IsCtrl && !key.IsAlt
            ? label.IndexOf((char)key.NoShift.KeyCode, StringComparison.OrdinalIgnoreCase)
            : -1;
        return at < 0 ? $"_{label}" : label.Insert(at, "_");
    }
}
