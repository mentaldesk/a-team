using Terminal.Gui.Input;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Views;

namespace ATeam.Dashboard.Tests;

/// <summary>The hot letters: Alt opens a menu, a bare letter picks from the one that's open, and neither
/// takes a key the app already answers.</summary>
[Collection("StaticConfiguration")]
public class AppMenuTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"a-team-{Guid.NewGuid():n}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData(Area.Dashboard, "_View", 'v')]
    [InlineData(Area.Work, "_Cards", 'c')]
    [InlineData(Area.Dashboard, "_Agents", 'a')]
    [InlineData(Area.Work, "_Help", 'h')]
    public void Alt_and_a_titles_letter_opens_that_menu(Area area, string title, char letter)
    {
        using var window = Open(area);
        var opened = Opened(window);

        Assert.True(window.NewKeyDownEvent(new Key(letter).WithAlt));

        Assert.Equal([title], opened);
    }

    [Theory]
    [InlineData(Area.Dashboard, 'c')]
    [InlineData(Area.Work, 'a')]
    public void Alt_and_a_hidden_menus_letter_opens_nothing(Area area, char letter)
    {
        using var window = Open(area);
        var opened = Opened(window);

        window.NewKeyDownEvent(new Key(letter).WithAlt);

        Assert.Empty(opened);
    }

    [Theory]
    [InlineData(Area.Dashboard, new[] { "_View", "_Agents", "_Help" })]
    [InlineData(Area.Work, new[] { "_View", "_Cards", "_Help" })]
    public void The_bar_shows_only_the_menus_for_the_area(Area area, string[] titles)
    {
        using var window = Open(area);

        Assert.Equal(titles, Shown(window));
    }

    [Fact]
    public void An_expanded_agent_keeps_the_dashboard_bar()
    {
        using var window = Open(Area.Dashboard);

        window.NewKeyDownEvent(Key.Tab);
        window.NewKeyDownEvent(Key.Enter);

        Assert.NotNull(window.ExpandedAgent);
        Assert.Equal(["_View", "_Agents", "_Help"], Shown(window));
    }

    [Theory]
    [InlineData('w', new[] { "_View", "_Cards", "_Help" })]
    [InlineData('d', new[] { "_View", "_Agents", "_Help" })]
    public void Switching_area_swaps_the_bar(char key, string[] titles)
    {
        using var window = Open(key == 'w' ? Area.Dashboard : Area.Work);

        window.NewKeyDownEvent(new Key(key));

        Assert.Equal(titles, Shown(window));
    }

    [Fact]
    public void Switching_area_from_an_open_menu_closes_it_and_shows_the_other_bar()
    {
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        using var window = Open(Area.Dashboard);
        app.Begin(window);
        window.NewKeyDownEvent(new Key('v').WithAlt);
        Assert.True(window.Menu.IsOpen());

        window.MenuItems.Single(item => item.Id == "view.work").Item.Action!();

        Assert.False(window.Menu.IsOpen());
        Assert.All(window.Menus, menu => Assert.False(menu.PopoverMenuOpen, menu.Title));
        Assert.Equal(["_View", "_Cards", "_Help"], Shown(window));
    }

    [Theory]
    [InlineData(Area.Dashboard, 'w')]
    [InlineData(Area.Work, 'd')]
    public void The_titles_sit_side_by_side_leaving_no_gap_for_the_other_areas_menu(Area area, char other)
    {
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        using var window = Open(area);
        app.Begin(window);
        window.NewKeyDownEvent(new Key(other));
        window.NewKeyDownEvent(new Key(area == Area.Dashboard ? 'd' : 'w'));
        window.Layout();

        var frames = window.Menu.SubViews.OfType<MenuBarItem>().Select(menu => menu.Frame).ToList();

        Assert.Equal(3, frames.Count);
        Assert.All(frames.Zip(frames.Skip(1)), pair => Assert.Equal(pair.First.Right, pair.Second.X));
    }

    [Fact]
    public void A_menu_back_on_the_bar_opens()
    {
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        using var window = Open(Area.Work);
        app.Begin(window);
        window.NewKeyDownEvent(new Key('d'));

        window.NewKeyDownEvent(new Key('a').WithAlt);

        Assert.Same(window.Menus.Single(menu => menu.Title == AppMenu.Agents).PopoverMenu, app.Popovers!.GetActivePopover());
    }

    [Fact]
    public void A_hidden_menus_letters_don_t_fire_from_the_other_area()
    {
        using var window = Open(Area.Work);
        var agents = window.Menus.Single(menu => menu.Title == AppMenu.Agents);

        Assert.Null(agents.SuperView);
        Assert.False(agents.PopoverMenu!.Enabled);
    }

    [Theory]
    [InlineData('v', false)]
    [InlineData('c', false)]
    [InlineData('a', false)]
    [InlineData('h', true)]
    public void A_bare_title_letter_opens_no_menu_and_the_key_it_would_shadow_still_works(char letter, bool answered)
    {
        using var window = Open(Area.Dashboard);
        window.NewKeyDownEvent(Key.Tab);
        var opened = Opened(window);

        Assert.Equal(answered, window.NewKeyDownEvent(new Key(letter)));

        Assert.Empty(opened);
    }

    [Fact]
    public void No_titles_bare_letter_opens_a_menu_however_many_titles_there_are()
    {
        using var window = Open();
        var opened = Opened(window);

        foreach (var menu in window.Menus)
        {
            window.NewKeyDownEvent(menu.HotKey);
            window.NewKeyDownEvent(menu.HotKey.WithShift);
        }

        Assert.Empty(opened);
    }

    [Fact]
    public void A_bare_letter_in_an_open_menu_runs_that_item()
    {
        using var window = Open();
        var work = InOpenMenu(window, "view.work");

        Assert.Equal(new Key('w'), work.HotKey);
        Assert.True(work.NewKeyDownEvent(work.HotKey));

        Assert.Equal(Area.Work, window.CurrentArea);
    }

    [Fact]
    public void The_letter_reaches_an_item_whose_command_has_no_key_of_its_own()
    {
        var ran = false;
        using var window = Open();
        window.Commands.Register("card.zap", "Zap", () => ran = true, onCard: true);
        window.Refresh();
        var zap = InOpenMenu(window, "card.zap");

        Assert.Equal(new Key('z'), zap.HotKey);
        Assert.True(zap.NewKeyDownEvent(zap.HotKey));

        Assert.True(ran);
    }

    [Fact]
    public void No_two_items_in_a_menu_share_a_letter()
    {
        using var window = Open();

        Assert.Equal(['v', 'c', 'a', 'h'], window.Menus.Select(menu => Letter(menu.HotKey)));
        foreach (var menu in window.Menus)
        {
            var ids = Under(window, menu);
            var letters = ids.Select(id => Letter(Item(window, id).HotKey)).ToList();
            Assert.Equal(letters.Count, letters.Distinct().Count());
        }
    }

    [Fact]
    public void Cards_sits_between_View_and_Agents_holding_the_card_commands_in_registration_order_with_their_keys()
    {
        using var window = Open();

        Assert.Equal(["_View", "_Cards", "_Agents", "_Help"], window.Menus.Select(menu => menu.Title));
        Assert.Equal(
            window.Commands.Registered.Where(command => command.OnCard).Select(command => command.Id),
            Above(window, Cards(window)));
        Assert.Equal(["work.read", "work.priority", "work.try", "work.github", "work.accept"], Above(window, Cards(window)));
        Assert.All(Under(window, Cards(window)), id => Assert.Equal(window.Commands.KeyFor(id), Item(window, id).Key));
    }

    [Fact]
    public void Cards_items_are_short_and_wear_the_letter_of_their_own_key()
    {
        using var window = Open();

        Assert.Equal(
            ["_Open", "Set _priority", "_Try PR", "Open on _GitHub", "_Accept", "_Refresh", "Show only _mine"],
            Under(window, Cards(window)).Select(id => Item(window, id).Title));
    }

    [Fact]
    public void Picking_a_Cards_item_runs_its_command()
    {
        var ran = new List<string>();
        using var window = Open();
        window.Commands.Register("work.probe", "Probe the selected item", () => ran.Add("work.probe"), onCard: true);
        window.Refresh();
        Item(window, "work.probe").Action!();

        Assert.Equal(["work.probe"], ran);
    }

    [Fact]
    public void A_card_command_registered_after_the_window_was_built_joins_Cards()
    {
        using var window = Open();

        window.Commands.Register("work.approve.card", "Approve the selected pitch", () => { }, new Key('a'), onCard: true);
        window.Refresh();

        Assert.Equal("work.approve.card", Above(window, Cards(window)).Last());
        Assert.Equal(["work.refresh", "work.mine"], Below(window, Cards(window)));
        Assert.Equal(new Key('a'), Item(window, "work.approve.card").Key);
    }

    [Fact]
    public void Rebinding_try_changes_the_key_Cards_shows_for_it()
    {
        using var window = Open();

        window.Commands.Apply([("work.try", new Key('y'))]);
        window.Refresh();

        Assert.Equal(new Key('y'), Item(window, "work.try").Key);
    }

    [Fact]
    public void A_rebinding_in_settings_is_the_key_Cards_opens_with()
    {
        Directory.CreateDirectory(Config);
        new DashboardSettings(Config).WriteKeys([("work.try", new Key('y'))]);

        using var window = Open();

        Assert.Equal(new Key('y'), Item(window, "work.try").Key);
        Assert.Contains(window.Commands.Registered, command => command.Id == "work.try" && command.Key == new Key('y'));
    }

    [Fact]
    public void An_open_Cards_menu_greys_out_what_the_selection_can_t_do()
    {
        using var window = Open();
        Cards(window).PopoverMenu!.Enabled = true;

        window.Refresh();

        Assert.All(Under(window, Cards(window)), id => Assert.False(Item(window, id).Enabled, id));
    }


    [Fact]
    public void Agents_holds_pause_this_role_then_interrupt_which_shows_its_key()
    {
        using var window = Open();
        var agents = window.Menus.Single(menu => menu.Title == "_Agents");

        Assert.Equal(["agent.hold", "agent.interrupt"], Above(window, agents));
        Assert.Equal("Pause t_his role", Item(window, "agent.hold").Title);
        Assert.Equal("_Interrupt", Item(window, "agent.interrupt").Title);
        Assert.Equal(new Key('i'), Item(window, "agent.interrupt").Key);
    }

    [Fact]
    public void Rebinding_interrupt_changes_the_key_Agents_shows_for_it()
    {
        Directory.CreateDirectory(Config);
        new DashboardSettings(Config).WriteKeys([("agent.interrupt", new Key('x'))]);

        using var window = Open();

        Assert.Equal(new Key('x'), Item(window, "agent.interrupt").Key);
    }

    [Fact]
    public void On_the_grid_Agents_has_expand_and_tool_calls_under_a_line_with_their_keys()
    {
        using var window = Open();

        Assert.Equal(["agent.expand", "log.toolCalls"], Below(window, Agents(window)));
        Assert.Equal(["_Expand", "Show _tool calls in full"], Below(window, Agents(window)).Select(id => Item(window, id).Title));
        Assert.Equal([Key.Enter, new Key('t')], Below(window, Agents(window)).Select(id => Item(window, id).Key));
    }

    [Fact]
    public void Expanded_Agents_has_tool_calls_and_back_in_place_of_expand()
    {
        using var window = Open();
        window.NewKeyDownEvent(Key.Tab);
        window.NewKeyDownEvent(Key.Enter);

        window.Refresh();

        Assert.Equal(["log.toolCalls", "agent.collapse"], Below(window, Agents(window)));
        Assert.Equal("_Back to all agents", Item(window, "agent.collapse").Title);
        Assert.Equal(Key.Esc, Item(window, "agent.collapse").Key);
    }

    [Fact]
    public void Opening_Agents_after_going_back_to_the_grid_offers_expand_again()
    {
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        using var window = Open();
        app.Begin(window);
        window.NewKeyDownEvent(Key.Tab);
        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();
        window.NewKeyDownEvent(Key.Esc);

        window.NewKeyDownEvent(new Key('a').WithAlt);

        Assert.Equal(["agent.expand", "log.toolCalls"], Below(window, Agents(window)));
    }

    [Fact]
    public void Cards_has_refresh_and_the_filter_under_a_line_with_their_keys()
    {
        using var window = Open();

        Assert.Equal(["work.refresh", "work.mine"], Below(window, Cards(window)));
        Assert.Equal([Key.F5, new Key('m')], Below(window, Cards(window)).Select(id => Item(window, id).Key));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void An_open_Agents_menu_greys_out_what_needs_a_selected_agent(int keys, bool enabled)
    {
        using var window = Open();
        foreach (var key in new[] { Key.Tab, Key.Enter }.Take(keys))
            window.NewKeyDownEvent(key);
        Agents(window).PopoverMenu!.Enabled = true;

        window.Refresh();

        Assert.All(Below(window, Agents(window)), id => Assert.Equal(enabled, Item(window, id).Enabled));
    }

    [Fact]
    public void No_shut_menu_answers_its_items_letters_however_many_titles_there_are()
    {
        using var window = Open();

        Assert.All(window.Menus, menu => Assert.False(menu.PopoverMenu!.Enabled, menu.Title));
    }

    [Fact]
    public void Esc_closes_whichever_menu_is_open()
    {
        using var window = Open();

        Assert.All(
            window.Menus,
            menu => Assert.Contains(Command.Quit, menu.PopoverMenu!.KeyBindings.GetCommands(Key.Esc)));
    }

    [Fact]
    public void Nothing_advertises_a_key_for_the_menu()
    {
        using var window = Open();

        Assert.DoesNotContain(window.Commands.Registered, command => command.Id == "menu");
    }

    private static MenuItem Item(DashboardWindow window, string id) =>
        window.MenuItems.Single(item => item.Id == id).Item;

    /// <summary>The item, with the menu holding it enabled, which is what the framework does as it shows it.</summary>
    private static MenuItem InOpenMenu(DashboardWindow window, string id)
    {
        window.Menus.Single(menu => Under(window, menu).Contains(id)).PopoverMenu!.Enabled = true;
        return Item(window, id);
    }

    /// <summary>The ids above the menu's line, and below it.</summary>
    private static List<string> Above(DashboardWindow window, MenuBarItem menu) => Group(window, menu, above: true);

    private static List<string> Below(DashboardWindow window, MenuBarItem menu) => Group(window, menu, above: false);

    private static List<string> Group(DashboardWindow window, MenuBarItem menu, bool above)
    {
        var views = menu.PopoverMenu!.Root!.SubViews.ToList();
        var line = views.FindIndex(view => view is Line);
        Assert.True(line > 0, menu.Title);
        return [.. (above ? views.Take(line) : views.Skip(line + 1))
            .Select(shown => window.MenuItems.Single(item => item.Item == shown).Id)];
    }

    private static MenuBarItem Agents(DashboardWindow window) => window.Menus.Single(menu => menu.Title == AppMenu.Agents);

    private static MenuBarItem Cards(DashboardWindow window) => window.Menus.Single(menu => menu.Title == AppMenu.Cards);

    /// <summary>The ids of the items a menu holds, in the order it shows them.</summary>
    private static List<string> Under(DashboardWindow window, MenuBarItem menu) =>
        [.. menu.PopoverMenu!.Root!.SubViews.OfType<MenuItem>()
            .Select(shown => window.MenuItems.Single(item => item.Item == shown).Id)];

    private static List<string> Shown(DashboardWindow window) =>
        [.. window.Menu.SubViews.OfType<MenuBarItem>().Select(menu => menu.Title)];

    private static char Letter(Key key) => char.ToLowerInvariant((char)key);

    /// <summary>The titles that have been asked to open, in the order they were.</summary>
    private static List<string> Opened(DashboardWindow window)
    {
        var opened = new List<string>();
        foreach (var menu in window.Menus)
        {
            var title = menu.Title;
            menu.Activating += (_, _) => opened.Add(title);
        }
        return opened;
    }

    private DashboardWindow Open(Area area = Area.Dashboard, Func<string[], Task<string?>>? run = null)
    {
        Directory.CreateDirectory(_root);
        return new DashboardWindow(
            [("a-team", "lead"), ("a-team", "dev")],
            _root,
            new DashboardSettings(Config),
            new TeamConfigs(Config),
            run ?? (_ => Task.FromResult<string?>(null)),
            _ => Task.FromResult(new Reading("[]", null)),
            _ => Task.FromResult(new Reading("{}", null)),
            _ => { },
            (_, _) => null,
            (_, _, _, _, _, _) => { },
            area,
            IconStyle.Unicode);
    }

    private string Config => Path.Combine(_root, "config");
}
