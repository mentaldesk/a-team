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
    [InlineData("_Team", 't')]
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
    [InlineData('t', true)]
    [InlineData('h', false)]
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
        var calls = new List<string[]>();
        using var window = Open(run: args =>
        {
            calls.Add(args);
            return Task.FromResult<string?>(null);
        });
        var pause = InOpenMenu(window, "team.pause");

        Assert.Equal(new Key('p'), pause.HotKey);
        Assert.True(pause.NewKeyDownEvent(pause.HotKey));

        Assert.Equal([["pause", "a-team"]], calls);
    }

    [Fact]
    public void Every_item_wears_the_first_letter_of_its_label_and_no_two_in_a_menu_share_one()
    {
        using var window = Open();

        Assert.Equal(['v', 't', 'h'], window.Menus.Select(menu => Letter(menu.HotKey)));
        foreach (var entry in AppMenu.Layout)
        {
            var letters = entry.Ids.Select(id => Letter(Item(window, id).HotKey)).ToList();
            Assert.Equal(letters.Count, letters.Distinct().Count());
            Assert.All(entry.Ids, id =>
                Assert.Equal(char.ToLowerInvariant(Label(window, id)[0]), Letter(Item(window, id).HotKey)));
        }
    }

    [Fact]
    public void The_pause_items_hot_letter_flips_with_its_label()
    {
        using var window = Open();
        var pause = Item(window, "team.pause");
        WriteTeam("a-team", enabled: true);
        window.Refresh();

        Assert.Equal("_Pause a-team", pause.Title);
        Assert.Equal(new Key('p'), pause.HotKey);

        WriteTeam("a-team", enabled: false);
        window.Refresh();

        Assert.Equal("_Resume a-team", pause.Title);
        Assert.Equal(new Key('r'), pause.HotKey);
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
        var title = AppMenu.Layout.Single(entry => entry.Ids.Contains(id)).Title;
        window.Menus.Single(menu => menu.Title == title).PopoverMenu!.Enabled = true;
        return Item(window, id);
    }

    private static string Label(DashboardWindow window, string id) =>
        window.Commands.Registered.Single(command => command.Id == id).Label;

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
            _ => { },
            dialog => dialog.Dispose(),
            Area.Dashboard,
            IconStyle.Unicode);
    }

    private string Config => Path.Combine(_root, "config");

    private void WriteTeam(string team, bool enabled)
    {
        var teams = Path.Combine(Config, "teams");
        Directory.CreateDirectory(teams);
        File.WriteAllText(
            Path.Combine(teams, team + ".json"),
            "{\"dispatch\": {\"enabled\": " + (enabled ? "true" : "false") + "}}");
    }
}
