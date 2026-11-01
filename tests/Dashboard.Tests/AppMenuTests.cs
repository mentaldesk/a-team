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
    [InlineData(Area.Overseer, "_View", 'v')]
    [InlineData(Area.Work, "_Go", 'g')]
    [InlineData(Area.Work, "_Cards", 'c')]
    [InlineData(Area.Overseer, "_Agents", 'a')]
    [InlineData(Area.Work, "_Help", 'h')]
    public void Alt_and_a_titles_letter_opens_that_menu(Area area, string title, char letter)
    {
        using var window = Open(area);
        var opened = Opened(window);

        Assert.True(window.NewKeyDownEvent(new Key(letter).WithAlt));

        Assert.Equal([title], opened);
    }

    [Theory]
    [InlineData(Area.Overseer, 'c')]
    [InlineData(Area.Work, 'a')]
    public void Alt_and_a_hidden_menus_letter_opens_nothing(Area area, char letter)
    {
        using var window = Open(area);
        var opened = Opened(window);

        window.NewKeyDownEvent(new Key(letter).WithAlt);

        Assert.Empty(opened);
    }

    [Theory]
    [InlineData(Area.Overseer, new[] { "_View", "_Go", "_Agents", "_Help" })]
    [InlineData(Area.Work, new[] { "_View", "_Go", "_Cards", "_Help" })]
    public void The_bar_shows_only_the_menus_for_the_area(Area area, string[] titles)
    {
        using var window = Open(area);

        Assert.Equal(titles, Shown(window));
    }

    [Theory]
    [InlineData('W', new[] { "_View", "_Go", "_Cards", "_Help" })]
    [InlineData('O', new[] { "_View", "_Go", "_Agents", "_Help" })]
    public void Switching_area_swaps_the_bar(char key, string[] titles)
    {
        using var window = Open(key == 'W' ? Area.Overseer : Area.Work);

        window.Press($"Ctrl+G {key}");

        Assert.Equal(titles, Shown(window));
    }

    [Fact]
    public void Switching_area_from_an_open_menu_closes_it_and_shows_the_other_bar()
    {
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        using var window = Open(Area.Overseer);
        app.Begin(window);
        window.NewKeyDownEvent(new Key('v').WithAlt);
        Assert.True(window.Menu.IsOpen());

        window.MenuItems.Single(item => item.Id == "view.work").Item.Action!();

        Assert.False(window.Menu.IsOpen());
        Assert.All(window.Menus, menu => Assert.False(menu.PopoverMenuOpen, menu.Title));
        Assert.Equal(["_View", "_Go", "_Cards", "_Help"], Shown(window));
    }

    [Theory]
    [InlineData(Area.Overseer, 'W')]
    [InlineData(Area.Work, 'O')]
    public void The_titles_sit_side_by_side_leaving_no_gap_for_the_other_areas_menu(Area area, char other)
    {
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        using var window = Open(area);
        app.Begin(window);
        window.Press($"Ctrl+G {other}");
        window.Press(area == Area.Overseer ? "Ctrl+G O" : "Ctrl+G W");
        window.Layout();

        var frames = window.Menu.SubViews.OfType<MenuBarItem>().Select(menu => menu.Frame).ToList();

        Assert.Equal(4, frames.Count);
        Assert.All(frames.Zip(frames.Skip(1)), pair => Assert.Equal(pair.First.Right, pair.Second.X));
    }

    [Fact]
    public void A_menu_back_on_the_bar_opens()
    {
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        using var window = Open(Area.Work);
        app.Begin(window);
        window.Press("Ctrl+G O");

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
    [InlineData('g', false)]
    [InlineData('c', false)]
    [InlineData('a', false)]
    [InlineData('h', true)]
    public void A_bare_title_letter_opens_no_menu_and_the_key_it_would_shadow_still_works(char letter, bool answered)
    {
        using var window = Open(Area.Overseer);
        window.Overseer.SelectHeading("a-team");
        window.Refresh();
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
    [InlineData(Area.Work)]
    [InlineData(Area.Overseer)]
    public void No_two_items_in_a_menu_share_a_letter_dimmed_ones_included(Area area)
    {
        using var window = Open(area);
        window.Refresh();

        Assert.Equal(['v', 'g', 'c', 'a', 'h'], window.Menus.Select(menu => Letter(menu.HotKey)));
        foreach (var menu in window.Menus)
        {
            var letters = Under(window, menu).Select(id => Letter(Item(window, id).HotKey)).ToList();
            Assert.DoesNotContain('\0', letters);
            Assert.Equal(letters.Count, letters.Distinct().Count());
        }
    }

    [Fact]
    public void View_on_the_Overseer_wears_a_letter_on_each_item()
    {
        using var window = Open(Area.Overseer);
        window.Refresh();

        Assert.Equal(
            ["_Show the session full-screen", "_Fold the lane", "Open on _GitHub", "_Read the boards that failed again", "R_efresh", "_Next team",
                "_Previous team", "Open team's _board on GitHub", "_Dispatcher log", "De_cisions…", "Se_ttings", "_Quit"],
            Under(window, View(window)).Select(id => Item(window, id).Title));
    }

    [Theory]
    [InlineData(Area.Work)]
    [InlineData(Area.Overseer)]
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
        Assert.Equal(new Key('s'), Item(window, "overseer.details").HotKey);
        app.Keyboard.RaiseKeyDownEvent(new Key('s'));

        Assert.Empty(ran);
    }

    [Fact]
    public void Letters_stay_put_as_items_dim_enable_or_swap()
    {
        using var window = Open();
        window.Refresh();
        var before = window.MenuItems.ToDictionary(entry => entry.Id, entry => entry.Item.HotKey);

        window.Overseer.SelectHeading("a-team");
        window.Refresh();
        window.Commands.Execute("view.dispatcher");
        window.Refresh();

        Assert.All(window.MenuItems, entry => Assert.Equal(before[entry.Id], entry.Item.HotKey));
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
    public void Go_sits_after_View_with_the_screens_and_the_Assistant_then_team_and_what_s_new_under_a_line_each_with_its_chord()
    {
        using var window = Open(Area.Work);
        window.Refresh();

        Assert.Equal(["_View", AppMenu.Go], window.Menus.Take(2).Select(menu => menu.Title));
        Assert.Equal(["view.work", "view.overseer", "assistant", "view.details"], Above(window, Go(window)));
        Assert.Equal(["work.team", "work.whatsNew"], Below(window, Go(window)));
        Assert.Equal(["Ctrl+G W", "Ctrl+G O", "Ctrl+G A", "Ctrl+G I", "Ctrl+G T", "Ctrl+G N"], Under(window, Go(window)).Select(id => Keys(window, id)));
        Assert.DoesNotContain(Under(window, View(window)), id => id is "view.work" or "view.overseer" or "view.details" or "assistant" or "work.team" or "work.whatsNew");
    }

    [Fact]
    public void Go_checks_the_screen_you_re_on_and_moves_the_check_when_you_go_elsewhere()
    {
        using var window = Open(Area.Work);
        window.Refresh();

        Assert.Equal([$"{AppMenu.Check} _Work", "  _Overseer", "  _Team…"], new[] { "view.work", "view.overseer", "work.team" }.Select(id => Item(window, id).Title));

        window.Press("Ctrl+G O");
        window.Refresh();

        Assert.Equal(["  _Work", $"{AppMenu.Check} _Overseer"], new[] { "view.work", "view.overseer" }.Select(id => Item(window, id).Title));
    }

    [Fact]
    public void Help_holds_show_keys_guide_messages_commands_and_about_each_with_its_letter()
    {
        using var window = Open();

        Assert.Equal(["Show _keys", "_Guide", "_Messages…", "_Commands", "_About"], Under(window, Help(window)).Select(id => Item(window, id).Title));
        Assert.Equal("F1", Keys(window, "help"));
    }

    [Fact]
    public void Cards_sits_between_Go_and_Agents_holding_the_card_commands_in_registration_order_with_their_keys()
    {
        using var window = Open();

        Assert.Equal(["_View", "_Go", "_Cards", "_Agents", "_Help"], window.Menus.Select(menu => menu.Title));
        Assert.Equal(
            window.Commands.Registered.Where(command => command.OnCard).Select(command => command.Id),
            Above(window, Cards(window)));
        Assert.Equal(["work.read", "work.priority", "work.try", "work.copyBranch", "work.github", "work.accept", "work.decline", "work.notNow", "work.hold"], Above(window, Cards(window)));
        Assert.All(Under(window, Cards(window)), id => Assert.Equal(window.Commands.KeysFor(id, Regions.Work), Keys(window, id)));
    }

    [Fact]
    public void Cards_items_are_short_and_wear_the_letter_of_their_own_key()
    {
        using var window = Open();

        Assert.Equal(
            ["_Open", "Set _priority", "_Try", "Cop_y branch name", "Open on _GitHub", "_Accept", "_Decline", "_Not now", "_Hold", "New _idea", "_Refresh", "_Show only mine"],
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

        window.Commands.Register("work.approve.card", "Approve the selected pitch", () => { }, new Key('z'), onCard: true);
        window.Refresh();

        Assert.Equal("work.approve.card", Above(window, Cards(window)).Last());
        Assert.Equal(["work.new", "work.refresh", "work.mine"], Below(window, Cards(window)));
        Assert.Equal("Z", Keys(window, "work.approve.card"));
    }

    [Fact]
    public void Rebinding_try_changes_the_key_Cards_shows_for_it()
    {
        using var window = Open();

        window.Commands.Keys.Unbind([new Key('t')], Regions.Work);
        window.Commands.Keys.Bind("z", "work.try");
        window.Refresh();

        Assert.Equal("Z", Keys(window, "work.try"));
    }

    [Fact]
    public void A_rebinding_in_settings_is_the_key_Cards_opens_with()
    {
        Directory.CreateDirectory(Config);
        OldKeys.Save(Config, ("work.try", "z"));

        using var window = Open();

        Assert.Equal("Z", Keys(window, "work.try"));
        Assert.Contains(window.Commands.Registered, command => command.Id == "work.try" && command.Keys == "Z");
    }

    [Fact]
    public void Cards_dims_what_the_selection_can_t_do_in_place_with_its_key()
    {
        using var window = Open();

        window.Refresh();

        Assert.All(Under(window, Cards(window)), id =>
        {
            Assert.True(Item(window, id).Dimmed, id);
            Assert.Equal(window.Commands.KeysFor(id, Regions.Work), Keys(window, id));
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
    public void View_lists_the_Work_and_Overseer_items_in_the_other_area_dimmed()
    {
        using var window = Open(Area.Work);

        window.Refresh();

        string[] elsewhere = ["overseer.details", "overseer.fold", "overseer.github", "overseer.retry", "overseer.refresh"];
        Assert.Subset(Under(window, View(window)).Concat(Under(window, Go(window))).ToHashSet(), elsewhere.ToHashSet());
        Assert.All(elsewhere, id => Assert.True(Item(window, id).Dimmed, id));
        Assert.False(Item(window, "view.overseer").Dimmed);
        Assert.False(Item(window, "view.dispatcher").Dimmed);
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
        Assert.Equal("I", Keys(window, "agent.interrupt"));
    }

    [Fact]
    public void Rebinding_interrupt_changes_the_key_Agents_shows_for_it()
    {
        Directory.CreateDirectory(Config);
        OldKeys.Save(Config, ("agent.interrupt", "z"));

        using var window = Open();

        Assert.Equal("Z", Keys(window, "agent.interrupt"));
    }

    [Fact]
    public void Agents_has_the_log_s_items_under_a_line_with_their_keys_dimmed_until_a_log_is_full_screen()
    {
        using var window = Open();
        window.Refresh();

        Assert.Equal(["log.toolCalls", "agent.collapse", "log.copyLines", "log.copyAll", "log.editor"], Below(window, Agents(window)));
        Assert.Equal(["Ctrl+T T", "Esc", "L", "A", "E"], Below(window, Agents(window)).Select(id => Keys(window, id)));
        Assert.All(Below(window, Agents(window)), id => Assert.True(Item(window, id).Dimmed, id));
        Assert.Equal("_Back", Item(window, "agent.collapse").Title);
        Assert.Equal("Open whole log in editor", Item(window, "log.editor").Title.Replace("_", ""));
        Assert.Equal(["Copy selected lines", "Copy whole log"], new[] { "log.copyLines", "log.copyAll" }.Select(id => Item(window, id).Title.Replace("_", "")));
    }

    [Fact]
    public void Cards_has_new_idea_refresh_and_the_filter_under_a_line_with_their_keys()
    {
        using var window = Open();

        Assert.Equal(["work.new", "work.refresh", "work.mine"], Below(window, Cards(window)));
        Assert.Equal(["Ctrl+N", "F5", "Ctrl+T M"], Below(window, Cards(window)).Select(id => Keys(window, id)));
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

    private static string Keys(DashboardWindow window, string id) => Item(window, id).KeyView.Text;

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

    private static MenuBarItem Go(DashboardWindow window) => window.Menus.Single(menu => menu.Title == AppMenu.Go);

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

    private DashboardWindow Open(Area area = Area.Overseer, Func<string[], Task<string?>>? run = null)
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
            Opening.Opened,
            (_, _, _, _, _, _, _, _, _, _, _, _) => null,
            area,
            IconStyle.Unicode);
    }

    private string Config => Path.Combine(_root, "config");
}
