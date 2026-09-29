using Terminal.Gui.Input;
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
    [InlineData("_View", 'v')]
    [InlineData("_Cards", 'c')]
    [InlineData("_Agents", 'a')]
    [InlineData("_Help", 'h')]
    public void Alt_and_a_titles_letter_opens_that_menu(string title, char letter)
    {
        using var window = Open();
        var opened = Opened(window);

        Assert.True(window.NewKeyDownEvent(new Key(letter).WithAlt));

        Assert.Equal([title], opened);
    }

    [Theory]
    [InlineData('v', false)]
    [InlineData('c', false)]
    [InlineData('a', false)]
    [InlineData('h', true)]
    public void A_bare_title_letter_opens_no_menu_and_the_key_it_would_shadow_still_works(char letter, bool answered)
    {
        using var window = Open();
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
            Under(window, Cards(window)));
        Assert.Equal(["work.read", "work.priority", "work.try", "work.github"], Under(window, Cards(window)));
        Assert.All(Under(window, Cards(window)), id => Assert.Equal(window.Commands.KeyFor(id), Item(window, id).Key));
    }

    [Fact]
    public void Cards_items_are_short_and_wear_the_letter_of_their_own_key()
    {
        using var window = Open();

        Assert.Equal(
            ["_Open", "Set _priority", "_Try PR", "Open on _GitHub"],
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

        Assert.Equal("work.approve.card", Under(window, Cards(window)).Last());
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

        Assert.Equal(["agent.hold", "agent.interrupt"], Under(window, agents));
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
        Assert.DoesNotContain("menu", window.Commands.Hints(Mode.Grid));
        Assert.DoesNotContain("menu", window.Commands.Hints(Mode.Work));
    }

    private static MenuItem Item(DashboardWindow window, string id) =>
        window.MenuItems.Single(item => item.Id == id).Item;

    /// <summary>The item, with the menu holding it enabled, which is what the framework does as it shows it.</summary>
    private static MenuItem InOpenMenu(DashboardWindow window, string id)
    {
        window.Menus.Single(menu => Under(window, menu).Contains(id)).PopoverMenu!.Enabled = true;
        return Item(window, id);
    }

    private static MenuBarItem Cards(DashboardWindow window) => window.Menus.Single(menu => menu.Title == AppMenu.Cards);

    /// <summary>The ids of the items a menu holds, in the order it shows them.</summary>
    private static List<string> Under(DashboardWindow window, MenuBarItem menu) =>
        [.. menu.PopoverMenu!.Root!.SubViews.OfType<MenuItem>()
            .Select(shown => window.MenuItems.Single(item => item.Item == shown).Id)];

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

    private DashboardWindow Open(Func<string[], Task<string?>>? run = null)
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
            (_, _, _, _) => { },
            Area.Dashboard,
            IconStyle.Unicode);
    }

    private string Config => Path.Combine(_root, "config");
}
