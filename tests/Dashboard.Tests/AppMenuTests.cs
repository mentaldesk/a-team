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

    [Theory]
    [InlineData(Area.Dashboard)]
    [InlineData(Area.Work)]
    [InlineData(Area.Overseer)]
    public void No_two_items_in_a_menu_share_a_letter_dimmed_ones_included(Area area)
    {
        using var window = Open(area);
        window.Refresh();

        Assert.Equal(['v', 'c', 'a', 'h'], window.Menus.Select(menu => Letter(menu.HotKey)));
        foreach (var menu in window.Menus)
        {
            var letters = Under(window, menu).Select(id => Letter(Item(window, id).HotKey)).ToList();
            Assert.DoesNotContain('\0', letters);
            Assert.Equal(letters.Count, letters.Distinct().Count());
        }
    }

    [Fact]
    public void View_on_the_Overseer_wears_the_letters_of_the_pitch_s_mockup()
    {
        using var window = Open(Area.Overseer);
        window.Refresh();

        Assert.Equal(
            ["_Dashboard", "_Work", "_Overseer", "Ass_istant", "Go _to what's new", "S_how details", "_Fold the lane", "Open on _GitHub",
                "Go to the _agent's session", "_Refresh", "_Next team", "_Previous team", "Go to t_eam…", "Open team's _board on GitHub",
                "_Settings", "_Quit"],
            Under(window, View(window)).Select(id => Item(window, id).Title));
    }

    [Theory]
    [InlineData(Area.Work)]
    [InlineData(Area.Dashboard)]
    public void A_dimmed_item_keeps_its_letter_and_pressing_it_runs_nothing(Area area)
    {
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        using var window = Open(area);
        app.Begin(window);
        var ran = new List<string>();
        foreach (var (id, item) in window.MenuItems)
            item.Action = () => ran.Add(id);

        window.NewKeyDownEvent(new Key('v').WithAlt);
        Assert.True(Item(window, "overseer.details").Dimmed);
        Assert.Equal(new Key('h'), Item(window, "overseer.details").HotKey);
        app.Keyboard.RaiseKeyDownEvent(new Key('h'));

        Assert.Empty(ran);
    }

    [Fact]
    public void Letters_stay_put_as_items_dim_enable_or_swap()
    {
        using var window = Open();
        window.Refresh();
        var before = window.MenuItems.ToDictionary(entry => entry.Id, entry => entry.Item.HotKey);

        window.NewKeyDownEvent(Key.Tab);
        window.Refresh();
        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();

        Assert.All(window.MenuItems, entry => Assert.Equal(before[entry.Id], entry.Item.HotKey));
        Assert.Equal(Item(window, "agent.expand").HotKey, Item(window, "agent.collapse").HotKey);
    }

    [Fact]
    public void A_command_s_key_letter_wins_over_first_letters()
    {
        Assert.Equal(["S_ave", "_Settings"], Titles(("Save", Key.Empty), ("Settings", new Key('s'))));
    }

    [Fact]
    public void A_key_letter_an_earlier_item_marked_goes_to_the_earlier_item()
    {
        Assert.Equal(["_Show", "S_ettings"], Titles(("Show", new Key('s')), ("Settings", new Key('s'))));
    }

    [Fact]
    public void A_letter_the_label_marks_itself_beats_a_key_letter()
    {
        Assert.Equal(["_Show kind", "Show _keys"], Titles(("Show kind", new Key('k')), ("Show _keys", Key.F1)));
    }

    [Fact]
    public void A_shifted_key_only_matches_its_capital()
    {
        Assert.Equal(["Copy whole _log", "_Copy whole log"], Titles(("Copy whole log", new Key('l')), ("Copy whole log", new Key('L'))));
    }

    [Fact]
    public void Labels_that_swap_share_a_letter_they_all_have()
    {
        var slots = AppMenu.Letters([(["Expand", "Back to all agents"], Key.Enter), (["Open in editor"], new Key('e'))]);

        Assert.Equal("Exp_and", slots[0]["Expand"]);
        Assert.Equal("Back to _all agents", slots[0]["Back to all agents"]);
        Assert.Equal("Open in _editor", slots[1]["Open in editor"]);
    }

    [Fact]
    public void A_key_letter_one_swapped_label_lacks_gives_way_to_one_they_share()
    {
        var slots = AppMenu.Letters([(["Show only mine", "Show all"], new Key('m'))]);

        Assert.Equal(["_Show only mine", "_Show all"], slots[0].Values);
    }

    [Fact]
    public void An_item_with_no_letter_free_gets_none()
    {
        Assert.Equal(["_Ab", "_Ba", "Ab"], Titles(("Ab", Key.Empty), ("Ba", Key.Empty), ("Ab", Key.Empty)));
    }

    [Fact]
    public void Help_holds_show_keys_guide_commands_and_about_each_with_its_letter()
    {
        using var window = Open();

        Assert.Equal(["Show _keys", "_Guide", "_Commands", "_About"], Under(window, Help(window)).Select(id => Item(window, id).Title));
        Assert.Equal(Key.F1, Item(window, "help").Key);
    }

    [Fact]
    public void No_two_items_in_an_expanded_agent_s_menu_share_a_letter_or_take_one_from_what_they_swapped_out()
    {
        using var window = Open();
        window.NewKeyDownEvent(Key.Tab);
        window.NewKeyDownEvent(Key.Enter);

        window.Refresh();

        var letters = Under(window, Agents(window)).Select(id => Letter(Item(window, id).HotKey)).ToList();
        Assert.Equal(letters.Count, letters.Distinct().Count());
        Assert.Contains(Letter(Item(window, "agent.expand").HotKey), letters);
    }

    [Fact]
    public void Cards_sits_between_View_and_Agents_holding_the_card_commands_in_registration_order_with_their_keys()
    {
        using var window = Open();

        Assert.Equal(["_View", "_Cards", "_Agents", "_Help"], window.Menus.Select(menu => menu.Title));
        Assert.Equal(
            window.Commands.Registered.Where(command => command.OnCard).Select(command => command.Id),
            Above(window, Cards(window)));
        Assert.Equal(["work.read", "work.priority", "work.try", "work.copyBranch", "work.github", "work.accept", "work.decline", "work.notNow", "work.hold"], Above(window, Cards(window)));
        Assert.All(Under(window, Cards(window)), id => Assert.Equal(window.Commands.KeyFor(id), Item(window, id).Key));
    }

    [Fact]
    public void Cards_items_are_short_and_wear_the_letter_of_their_own_key()
    {
        using var window = Open();

        Assert.Equal(
            ["_Open", "Set _priority", "_Try", "Cop_y branch name", "Open on _GitHub", "_Accept", "_Decline", "Not no_w", "_Hold", "_New idea", "_Refresh", "_Show only mine"],
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
        Assert.Equal(["work.new", "work.refresh", "work.mine"], Below(window, Cards(window)));
        Assert.Equal(new Key('a'), Item(window, "work.approve.card").Key);
    }

    [Fact]
    public void Rebinding_try_changes_the_key_Cards_shows_for_it()
    {
        using var window = Open();

        window.Commands.Apply([("work.try", new Key('z'))]);
        window.Refresh();

        Assert.Equal(new Key('z'), Item(window, "work.try").Key);
    }

    [Fact]
    public void A_rebinding_in_settings_is_the_key_Cards_opens_with()
    {
        Directory.CreateDirectory(Config);
        new DashboardSettings(Config).WriteKeys([("work.try", new Key('z'))]);

        using var window = Open();

        Assert.Equal(new Key('z'), Item(window, "work.try").Key);
        Assert.Contains(window.Commands.Registered, command => command.Id == "work.try" && command.Key == new Key('z'));
    }

    [Fact]
    public void Cards_dims_what_the_selection_can_t_do_in_place_with_its_key()
    {
        using var window = Open();

        window.Refresh();

        Assert.All(Under(window, Cards(window)), id =>
        {
            Assert.True(Item(window, id).Dimmed, id);
            Assert.Equal(window.Commands.KeyFor(id), Item(window, id).Key);
        });
    }

    [Fact]
    public void A_dimmed_item_runs_nothing_when_picked()
    {
        var ran = false;
        using var window = Open();
        window.Commands.Register("card.zap", "Zap", () => ran = true, isEnabled: () => false, onCard: true);
        window.Refresh();
        var zap = InOpenMenu(window, "card.zap");

        Assert.True(zap.Dimmed);
        zap.InvokeCommand(Command.Accept);
        zap.NewKeyDownEvent(zap.HotKey);

        Assert.False(ran);
    }

    [Fact]
    public void View_lists_the_Work_and_Overseer_items_on_the_dashboard_dimmed()
    {
        using var window = Open();

        window.Refresh();

        string[] elsewhere = ["overseer.details", "overseer.fold", "overseer.github", "overseer.session", "overseer.refresh",
            "work.nextTeam", "work.previousTeam", "work.team"];
        Assert.Subset(Under(window, View(window)).ToHashSet(), elsewhere.ToHashSet());
        Assert.All(elsewhere, id => Assert.True(Item(window, id).Dimmed, id));
        Assert.False(Item(window, "view.work").Dimmed);
    }


    [Fact]
    public void Agents_holds_pause_this_role_then_interrupt_which_shows_its_key_then_a_pass_now()
    {
        using var window = Open();
        var agents = window.Menus.Single(menu => menu.Title == "_Agents");

        Assert.Equal(["agent.hold", "agent.interrupt", "dispatch.pass"], Above(window, agents));
        Assert.Equal("_Run a dispatch pass now", Item(window, "dispatch.pass").Title);
        Assert.Equal("Pause t_his role", Item(window, "agent.hold").Title);
        Assert.Equal("_Interrupt", Item(window, "agent.interrupt").Title);
        Assert.Equal(new Key('i'), Item(window, "agent.interrupt").Key);
    }

    [Fact]
    public void Rebinding_interrupt_changes_the_key_Agents_shows_for_it()
    {
        Directory.CreateDirectory(Config);
        new DashboardSettings(Config).WriteKeys([("agent.interrupt", new Key('z'))]);

        using var window = Open();

        Assert.Equal(new Key('z'), Item(window, "agent.interrupt").Key);
    }

    [Fact]
    public void On_the_grid_Agents_has_expand_and_tool_calls_then_the_log_s_items_dimmed_under_a_line_with_their_keys()
    {
        using var window = Open();
        window.NewKeyDownEvent(Key.Tab);

        window.Refresh();

        Assert.Equal(["agent.expand", "log.toolCalls", "log.copyLines", "log.copyAll", "log.editor"], Below(window, Agents(window)));
        Assert.Equal(["Exp_and", "Show _tool calls in full"], Below(window, Agents(window)).Take(2).Select(id => Item(window, id).Title));
        Assert.Equal([Key.Enter, new Key('t'), new Key('l'), new Key('L'), new Key('e')], Below(window, Agents(window)).Select(id => Item(window, id).Key));
        Assert.Equal([false, false, true, true, true], Below(window, Agents(window)).Select(id => Item(window, id).Dimmed));
    }

    [Fact]
    public void Expanded_Agents_has_tool_calls_and_back_in_place_of_expand()
    {
        using var window = Open();
        window.NewKeyDownEvent(Key.Tab);
        window.NewKeyDownEvent(Key.Enter);

        window.Refresh();

        Assert.Equal(["log.toolCalls", "agent.collapse", "log.copyLines", "log.copyAll", "log.editor"], Below(window, Agents(window)));
        Assert.Equal("Open whole log in editor", Item(window, "log.editor").Title.Replace("_", ""));
        Assert.Equal(new Key('e'), Item(window, "log.editor").Key);
        Assert.Equal("Back to _all agents", Item(window, "agent.collapse").Title);
        Assert.Equal(Key.Esc, Item(window, "agent.collapse").Key);
        Assert.Equal(["Copy selected lines", "Copy whole log"], new[] { "log.copyLines", "log.copyAll" }.Select(id => Item(window, id).Title.Replace("_", "")));
        Assert.Equal([new Key('l'), new Key('L')], new[] { "log.copyLines", "log.copyAll" }.Select(id => Item(window, id).Key));
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

        Assert.Equal(["agent.expand", "log.toolCalls", "log.copyLines", "log.copyAll", "log.editor"], Below(window, Agents(window)));
    }

    [Fact]
    public void Cards_has_new_idea_refresh_and_the_filter_under_a_line_with_their_keys()
    {
        using var window = Open();

        Assert.Equal(["work.new", "work.refresh", "work.mine"], Below(window, Cards(window)));
        Assert.Equal([new Key('n'), Key.F5, new Key('m')], Below(window, Cards(window)).Select(id => Item(window, id).Key));
    }

    [Theory]
    [InlineData(0, new[] { true, true, true, true, true })]
    [InlineData(1, new[] { false, false, true, true, true })]
    [InlineData(2, new[] { false, false, false, false, false })]
    public void Agents_dims_what_needs_a_selected_agent_or_an_expanded_log(int keys, bool[] dimmed)
    {
        using var window = Open();
        foreach (var key in new[] { Key.Tab, Key.Enter }.Take(keys))
            window.NewKeyDownEvent(key);

        window.Refresh();

        Assert.Equal(dimmed, Below(window, Agents(window)).Select(id => Item(window, id).Dimmed));
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

    private static DimmedMenuItem Item(DashboardWindow window, string id) =>
        window.MenuItems.Single(item => item.Id == id).Item;

    /// <summary>The item, with the menu holding it enabled and visible, which is what the framework does as it shows it.</summary>
    private static DimmedMenuItem InOpenMenu(DashboardWindow window, string id)
    {
        var popover = window.Menus.Single(menu => Under(window, menu).Contains(id)).PopoverMenu!;
        popover.Enabled = true;
        popover.Visible = true;
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

    private static MenuBarItem View(DashboardWindow window) => window.Menus.Single(menu => menu.Title == "_View");

    private static MenuBarItem Help(DashboardWindow window) => window.Menus.Single(menu => menu.Title == "_Help");

    private static MenuBarItem Cards(DashboardWindow window) => window.Menus.Single(menu => menu.Title == AppMenu.Cards);

    /// <summary>The ids of the items a menu holds, in the order it shows them.</summary>
    private static List<string> Under(DashboardWindow window, MenuBarItem menu) =>
        [.. menu.PopoverMenu!.Root!.SubViews.OfType<MenuItem>()
            .Select(shown => window.MenuItems.Single(item => item.Item == shown).Id)];

    private static List<string> Shown(DashboardWindow window) =>
        [.. window.Menu.SubViews.OfType<MenuBarItem>().Select(menu => menu.Title)];

    private static char Letter(Key key) => char.ToLowerInvariant((char)key);

    private static List<string> Titles(params (string Label, Key Key)[] items) =>
        [.. AppMenu.Letters([.. items.Select(item => ((IReadOnlyList<string>)[item.Label], item.Key))]).Select((titles, i) => titles[items[i].Label])];

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
            (_, _, _, _, _, _, _, _, _, _, _, _) => null,
            area,
            IconStyle.Unicode);
    }

    private string Config => Path.Combine(_root, "config");
}
