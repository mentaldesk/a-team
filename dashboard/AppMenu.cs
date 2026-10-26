using MentalDesk.Tui.Chrome;
using Terminal.Gui.Input;

namespace ATeam.Dashboard;

/// <summary>The menu across the top of both areas. Every item runs a registered command by id, so the menu,
/// the key and the Commands dialog are three doors into one list. Titles and items carry a hot letter, marked
/// in the label with <c>_</c> and drawn underlined: Alt opens a menu, and a bare letter picks from the one
/// that's open. No two items in a menu share a letter, following the style guide's <i>Menu letters</i>.</summary>
internal sealed class AppMenu
{
    /// <summary>Holds whatever commands the registry marks as acting on the selected card, then its own group under a line.</summary>
    internal const string Cards = "_Cards";

    internal const string Agents = "_Agents";

    /// <summary>Stands in the layout for a line between two groups of items.</summary>
    internal const string Separator = "-";

    internal static readonly (string Title, string[] Ids)[] Layout =
    [
        ("_View", ["view.dashboard", "view.work", "view.overseer", "assistant", "work.whatsNew", Separator, "overseer.details", "overseer.fold", "overseer.github",
            "overseer.session", "overseer.refresh", "work.nextTeam", "work.previousTeam", "work.team", "team.board", "decisions", "settings", "quit"]),
        (Cards, [Separator, "work.new", "work.refresh", "work.mine"]),
        (Agents, ["agent.hold", "agent.interrupt", "dispatch.pass", Separator, "agent.expand", "log.toolCalls", "agent.collapse", "log.copyLines", "log.copyAll", "log.editor"]),
        ("_Help", ["help", "guide", "messages", "commands", "about"]),
    ];

    /// <summary>Items that take each other's place in a menu, sharing a letter so it stays put as they swap.</summary>
    internal static readonly (string Id, string Other)[] Swaps = [("agent.expand", "agent.collapse")];

    private readonly CommandRegistry _commands;
    private readonly List<(string Id, DimmedMenuItem Item)> _items = [];
    private readonly List<MenuBarItem> _menus = [];
    private readonly Dictionary<MenuBarItem, List<View>> _order = [];
    private readonly Dictionary<DimmedMenuItem, Dictionary<string, string>> _titles = [];
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

    internal IReadOnlyList<(string Id, DimmedMenuItem Item)> Items => _items;

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
    /// since the menu was built joins Cards, and Cards picks its letters again.</summary>
    internal void Refresh()
    {
        var added = CardIds().Where(id => !_items.Exists(entry => entry.Id == id)).ToList();
        foreach (var id in added)
            AddCard(Item(id));
        if (added.Count > 0)
            Letter(_cards);
        var registered = _commands.Registered;
        foreach (var (id, item) in _items)
        {
            if (registered.FirstOrDefault(entry => entry.Id == id) is not { } command)
                continue;
            if (item.Title != Title(item, command.MenuLabel))
                item.Title = Title(item, command.MenuLabel);
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
        Letter(menu);
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

    /// <summary>Dims what wouldn't run now, like trying a card with no PR, in place.</summary>
    private void Grey(MenuBarItem menu)
    {
        foreach (var (id, item) in _items.Where(entry => _order[menu].Contains(entry.Item)))
            item.Dimmed = !_commands.IsEnabled(id);
    }

    /// <summary>Of two items that undo each other, like Expand and Back, holds only the one that applies. The other is
    /// taken out rather than hidden, since the menu still spaces out a hidden item.</summary>
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

    /// <summary>Picks the menu's letters once over every item it can show, so none moves as items dim or swap.</summary>
    private void Letter(MenuBarItem menu)
    {
        var registered = _commands.Registered;
        var items = _items.Where(entry => _order[menu].Contains(entry.Item)).ToList();
        var slots = items
            .Where(entry => !Swaps.Any(swap => swap.Other == entry.Id && items.Exists(other => other.Id == swap.Id)))
            .Select(entry => (Ids: (string[])[entry.Id, .. Swaps.Where(swap => swap.Id == entry.Id).Select(swap => swap.Other)], entry.Item))
            .ToList();
        var titles = Letters([.. slots.Select(slot => (
            (IReadOnlyList<string>)[.. slot.Ids.SelectMany(id => registered.FirstOrDefault(command => command.Id == id)?.MenuLabels ?? [id]).Distinct()],
            _commands.KeyFor(slot.Ids[0])))]);
        foreach (var ((ids, _), picked) in slots.Zip(titles))
        {
            foreach (var (_, item) in items.Where(entry => ids.Contains(entry.Id)))
                _titles[item] = picked;
        }
        foreach (var (id, item) in items)
            item.Title = Title(item, registered.FirstOrDefault(command => command.Id == id)?.MenuLabel ?? id);
    }

    private string Title(DimmedMenuItem item, string label) =>
        _titles.TryGetValue(item, out var titles) && titles.TryGetValue(label, out var title) ? title : label;

    /// <summary>Each slot's labels, with the letter they share marked: one a label marks itself, then its command's
    /// own letter key where every label has it, unless an earlier slot marked that letter, then the guide's first
    /// letter, later word or any letter. A slot is an item, and any item it swaps with. A shifted key only matches
    /// its capital.</summary>
    internal static Dictionary<string, string>[] Letters(IReadOnlyList<(IReadOnlyList<string> Labels, Key Key)> slots)
    {
        var titles = slots.Select(slot => slot.Labels[0]).ToArray();
        var taken = titles.Select(Marked).OfType<char>().Select(char.ToLowerInvariant).ToHashSet();
        for (var i = 0; i < titles.Length; i++)
        {
            if (Marked(titles[i]) is null && KeyLetter(slots[i]) is { } letter && taken.Add(char.ToLowerInvariant(letter)))
                titles[i] = titles[i].Insert(At(titles[i], letter, slots[i].Key.IsShift), "_");
        }
        var none = new HashSet<int>();
        while (true)
        {
            var picked = MenuLetters.Pick(titles);
            var split = Enumerable.Range(0, slots.Count)
                .FirstOrDefault(i => Marked(titles[i]) is null && Marked(picked[i]) is { } letter && !slots[i].Labels.All(label => Has(label, letter, false)), -1);
            if (split < 0)
                return [.. slots.Select((slot, i) => slot.Labels.ToDictionary(
                    label => label,
                    label => none.Contains(i) ? label : label == slot.Labels[0] ? picked[i] : Mark(label, Marked(picked[i]))))];
            // A letter the swapped-in label lacks would move, so the slot takes one they all have.
            var others = picked.Where((_, i) => i != split).Select(Marked).OfType<char>().Select(char.ToLowerInvariant).ToHashSet();
            var at = titles[split].ToList().FindIndex(c =>
                char.IsLetterOrDigit(c) && !others.Contains(char.ToLowerInvariant(c)) && slots[split].Labels.All(label => Has(label, c, false)));
            if (at < 0)
                none.Add(split);
            titles[split] = at < 0 ? "" : titles[split].Insert(at, "_");
        }
    }

    private static char? KeyLetter((IReadOnlyList<string> Labels, Key Key) slot)
    {
        var key = slot.Key;
        if (!key.IsKeyCodeAtoZ || key.IsCtrl || key.IsAlt)
            return null;
        var letter = (char)key.NoShift.KeyCode;
        return slot.Labels.All(label => Has(label, letter, key.IsShift)) ? key.IsShift ? letter : char.ToLowerInvariant(letter) : null;
    }

    private static char? Marked(string title) =>
        title.IndexOf('_') is var at and >= 0 && at + 1 < title.Length ? title[at + 1] : null;

    private static bool Has(string label, char letter, bool exact) =>
        label.Contains(letter, exact ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    private static string Mark(string label, char? letter) =>
        label.Contains('_') || letter is not { } found || !Has(label, found, false) ? label : label.Insert(At(label, found, false), "_");

    /// <summary>Where <paramref name="letter"/> starts a word in the label, else where it first appears.</summary>
    private static int At(string label, char letter, bool exact)
    {
        var comparison = exact ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var all = Enumerable.Range(0, label.Length).Where(at => string.Compare(label, at, letter.ToString(), 0, 1, comparison) == 0).ToList();
        return all.FirstOrDefault(at => at == 0 || !char.IsLetterOrDigit(label[at - 1]), all[0]);
    }

    /// <summary>A card command goes last in the first group, above the line.</summary>
    private void AddCard(DimmedMenuItem item)
    {
        var views = _order[_cards];
        var line = views.FindIndex(view => view is Line);
        views.Insert(line < 0 ? views.Count : line, item);
        Arrange(_cards);
    }

    private string[] CardIds() => [.. _commands.Registered.Where(command => command.OnCard).Select(command => command.Id)];

    private DimmedMenuItem Item(string id)
    {
        var item = new DimmedMenuItem
        {
            Key = _commands.KeyFor(id),
            // The key is a label here: the window's registry already runs it, and a second binding would run it twice.
            BindKeyToApplication = false,
            Action = () => _commands.Execute(id),
        };
        _items.Add((id, item));
        return item;
    }
}
