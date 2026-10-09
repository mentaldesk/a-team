using Terminal.Gui.Input;

namespace ATeam.Dashboard;

/// <summary>The menu across the top of both areas. Every item runs a registered command by id, so the menu,
/// the key and the Commands dialog are three doors into one list. Titles and items carry a hot letter, marked
/// in the label with <c>_</c> and drawn underlined: Alt opens a menu, and a bare letter picks from the one
/// that's open.</summary>
internal sealed class AppMenu
{
    /// <summary>Holds whatever commands the registry marks as acting on the selected card, then its own group under a line.</summary>
    internal const string Cards = "_Cards";

    internal const string Agents = "_Agents";

    /// <summary>Stands in the layout for a line between two groups of items.</summary>
    internal const string Separator = "-";

    internal static readonly (string Title, string[] Ids)[] Layout =
    [
        ("_View", ["view.dashboard", "view.work", "view.overseer", Separator, "overseer.details", "overseer.fold", "overseer.github",
            "overseer.session", "overseer.refresh", "work.nextTeam", "work.previousTeam", "work.team", "team.board", "settings", "quit"]),
        (Cards, [Separator, "work.new", "work.refresh", "work.mine"]),
        (Agents, ["agent.hold", "agent.interrupt", "dispatch.pass", Separator, "agent.expand", "log.toolCalls", "agent.collapse", "log.copyLines", "log.copyAll", "log.editor"]),
        ("_Help", ["help", "guide", "commands", "about"]),
    ];

    private readonly CommandRegistry _commands;
    private readonly List<(string Id, MenuItem Item)> _items = [];
    private readonly List<MenuBarItem> _menus = [];
    private readonly Dictionary<MenuBarItem, List<View>> _order = [];
    private readonly MenuBarItem _cards;
    private readonly MenuBarItem _agents;

    internal AppMenu(CommandRegistry commands, Area area)
    {
        _commands = commands;
        Bar = new MenuBar { Menus = [.. Layout.Select(Menu)] };
        _cards = _menus.Single(menu => menu.Title == Cards);
        _agents = _menus.Single(menu => menu.Title == Agents);
        Bar.Disposing += (_, _) =>
        {
            _order.Values.SelectMany(views => views).Where(view => view.SuperView is null).ToList().ForEach(view => view.Dispose());
            _menus.Where(menu => menu.SuperView is null).ToList().ForEach(menu => menu.Dispose());
        };
        Show(area);
    }

    internal MenuBar Bar { get; }

    internal IReadOnlyList<(string Id, MenuItem Item)> Items => _items;

    /// <summary>The titles across the bar, each holding the items under it.</summary>
    internal IReadOnlyList<MenuBarItem> Menus => _menus;

    /// <summary>Cards only in Work and Agents only on the dashboard, closing whatever menu is open. The others are
    /// taken off the bar rather than hidden: the bar still spaces out a hidden title.</summary>
    internal void Show(Area area)
    {
        if (Bar.IsOpen())
            Bar.HideActiveItem();
        Bar.Menus = [.. _menus.Where(menu => menu == _cards ? area == Area.Work : menu != _agents || area == Area.Dashboard)];
    }

    /// <summary>Keeps each item reading as its command does now, like holding the selected agent's role, and
    /// showing the key it's bound to now. Setting the title sets the hot letter with it. A card command registered
    /// since the menu was built joins Cards.</summary>
    internal void Refresh()
    {
        foreach (var id in CardIds().Where(id => !_items.Exists(entry => entry.Id == id)))
            AddCard(Item(id));
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
        {
            Arrange(menu);
            Grey(menu);
        }
    }

    /// <summary>Terminal.Gui binds a hot key with and without Alt, whether or not the view has focus, so a bare
    /// title letter would open a menu from anywhere and swallow the app's own single-letter keys. A title keeps
    /// only its Alt forms; an item keeps its bare letter, which only its own menu answers.</summary>
    private MenuBarItem Menu((string Title, string[] Ids) entry)
    {
        var ids = entry.Title == Cards ? [.. CardIds(), .. entry.Ids] : entry.Ids;
        List<View> views = [.. ids.Select(id => id == Separator ? new Line() : (View)Item(id))];
        var menu = new MenuBarItem(entry.Title, [.. views]);
        _order[menu] = views;
        menu.HotKeyBindings.Remove(menu.HotKey);
        menu.HotKeyBindings.Remove(menu.HotKey.WithShift);
        // The app's quit key took Esc off the framework's Quit command, and the menu's close with it.
        menu.PopoverMenu?.KeyBindings.Add(Key.Esc, Command.Quit);
        menu.PopoverMenuOpenChanged += (_, _) =>
        {
            Shut(menu);
            Arrange(menu);
            Grey(menu);
        };
        Shut(menu);
        Arrange(menu);
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

    /// <summary>Holds only the items the menu shows now, in layout order. One it doesn't is taken out rather than
    /// hidden, since the menu still spaces out a hidden item.</summary>
    private void Arrange(MenuBarItem menu)
    {
        if (menu.PopoverMenu?.Root is not { } root)
            return;
        List<View> shown = [.. _order[menu].Where(view => view is not MenuItem item || _commands.IsInMenu(_items.Single(entry => entry.Item == item).Id))];
        if (root.SubViews.SequenceEqual(shown))
            return;
        root.SubViews.ToList().ForEach(view => root.Remove(view));
        shown.ForEach(view => root.Add(view));
    }

    /// <summary>A card command goes last in the first group, above the line.</summary>
    private void AddCard(MenuItem item)
    {
        var views = _order[_cards];
        var line = views.FindIndex(view => view is Line);
        views.Insert(line < 0 ? views.Count : line, item);
        Arrange(_cards);
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
    /// first letter otherwise, past any mark in front of it. A shifted letter only matches its capital.</summary>
    private static string Hot(string label, Key key)
    {
        var at = key.IsKeyCodeAtoZ && !key.IsCtrl && !key.IsAlt
            ? key.IsShift
                ? label.IndexOf((char)key.NoShift.KeyCode, StringComparison.Ordinal)
                : label.IndexOf((char)key.NoShift.KeyCode, StringComparison.OrdinalIgnoreCase)
            : -1;
        return label.Insert(at < 0 ? Math.Max(0, label.ToList().FindIndex(char.IsLetterOrDigit)) : at, "_");
    }
}
