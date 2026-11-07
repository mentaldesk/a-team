using MentalDesk.Tui;
using System.Drawing;
using System.Text;
using MentalDesk.Tui.Links;
using Terminal.Gui;
using Terminal.Gui.App;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace ATeam.Dashboard.Tests;

public partial class DashboardWindowTests
{
    [Fact]
    public void Partway_through_a_chord_the_status_bar_shows_it_and_finishing_it_runs_the_command()
    {
        using var window = Open(agents: Agents(4));

        Assert.True(window.NewKeyDownEvent(Key.G.WithCtrl));
        Assert.Equal("Ctrl+G…", window.Status.Says);
        Assert.Equal(Area.Overseer, window.CurrentArea);

        window.NewKeyDownEvent(new Key('w'));

        Assert.Equal(Area.Work, window.CurrentArea);
        Assert.NotEqual("Ctrl+G…", window.Status.Says);
    }

    [Fact]
    public void A_finished_chord_is_off_the_status_bar_before_its_command_opens_anything()
    {
        using var window = Open(agents: Agents(4));
        string? said = null;
        window.Commands.Register("opens.dialog", "Opens a dialog", () => said = window.Status.Says, chord: "Ctrl+G Z");

        window.NewKeyDownEvent(Key.G.WithCtrl);
        window.NewKeyDownEvent(new Key('z'));

        Assert.NotNull(said);
        Assert.NotEqual("Ctrl+G…", said);
    }

    [Fact]
    public void A_key_that_finishes_no_chord_cancels_it_says_so_and_does_nothing_else()
    {
        using var window = Open(agents: Agents(4));
        SelectAgent(window, 0);

        window.NewKeyDownEvent(Key.G.WithCtrl);
        Assert.True(window.NewKeyDownEvent(Key.Tab));

        Assert.Equal("Ctrl+G Tab isn't a key", window.Says());
        Assert.Equal(Area.Overseer, window.CurrentArea);
        Assert.True(window.Overseer.Board.OnHeading);
    }

    [Fact]
    public void Esc_partway_through_a_chord_cancels_it()
    {
        using var window = Open(agents: Agents(4));

        window.NewKeyDownEvent(Key.G.WithCtrl);
        window.NewKeyDownEvent(Key.Esc);

        Assert.Equal("Ctrl+G cancelled", window.Says());
    }

    [Theory]
    [InlineData('d')]
    [InlineData('w')]
    [InlineData('o')]
    [InlineData('s')]
    [InlineData('n')]
    [InlineData('q')]
    [InlineData('u')]
    [InlineData('m')]
    public void The_bare_letters_the_defaults_dropped_do_nothing_now(char letter)
    {
        using var window = Open(agents: Agents(4));

        Assert.False(window.NewKeyDownEvent(new Key(letter)));
        Assert.Equal(Area.Overseer, window.CurrentArea);
    }

    [Fact]
    public void The_default_keys_follow_TuiCode_s_families()
    {
        using var window = Open(agents: Agents(4));

        Assert.Equal(
            [
                ("view.work", "Ctrl+G W"), ("view.overseer", "Ctrl+G O"), ("assistant", "Ctrl+G A"),
                ("teams", "Ctrl+G T"), ("work.team", ""), ("work.whatsNew", "Ctrl+G N"), ("log.toolCalls", "Ctrl+T T"), ("work.mine", "Ctrl+T M"),
                ("commands", "Ctrl+E"), ("settings", "Ctrl+,"), ("work.new", "Ctrl+N"), ("quit", "Ctrl+Q"), ("assistant.clear", "Ctrl+L"),
            ],
            new[] { "view.work", "view.overseer", "assistant", "teams", "work.team", "work.whatsNew", "log.toolCalls", "work.mine", "commands",
                "settings", "work.new", "quit", "assistant.clear" }.Select(id => (id, window.Commands.KeysFor(id))));
    }

    [Fact]
    public void The_card_and_log_keys_stay_single_letters()
    {
        using var window = Open(agents: Agents(4));

        Assert.Equal(["A", "T", "P", "X", "L", "H", "F", "G", "Y", "C", "E", "I"],
            new[] { "work.accept", "work.try", "work.priority", "work.decline", "work.notNow", "work.hold", "work.fix", "work.github", "work.copyBranch", "work.comment",
                "log.editor", "agent.interrupt" }.Select(id => window.Commands.KeysFor(id)));
    }

    [Fact]
    public void Help_opens_with_F1()
    {
        using var window = Open(agents: Agents(4));

        Assert.Equal("F1", window.Commands.Registered.Single(c => c.Id == "help").Keys);
    }

    [Theory]
    [InlineData(Area.Work, GuideDialog.Work)]
    [InlineData(Area.Overseer, GuideDialog.Overseer)]
    public void Guide_opens_on_the_page_for_the_area_and_has_no_key(Area area, string page)
    {
        var opened = new List<string>();
        using var window = Open(agents: Agents(4), area: area, showGuide: opened.Add);

        window.Commands.Execute("guide");

        Assert.Equal([page], opened);
        Assert.Equal(Key.Empty, window.Commands.KeyFor("guide"));
    }

    [Fact]
    public void Settings_opens_with_Ctrl_comma_and_s_is_gone()
    {
        using var window = Open(agents: Agents(4));

        Assert.Equal("Ctrl+,", window.Commands.Registered.Single(c => c.Id == "settings").Keys);
        Assert.DoesNotContain(window.Commands.Registered, c => c.Keys == "S");
        Assert.False(window.NewKeyDownEvent(new Key('s')));
    }

    [Fact]
    public void A_key_the_file_names_runs_that_command_and_the_one_in_the_source_no_longer_does()
    {
        using var window = Open(keys: "{ \"view.work\": \"z\" }");

        Assert.True(window.NewKeyDownEvent(new Key('z')));
        Assert.Equal(Area.Work, window.CurrentArea);
        window.Commands.Execute("view.overseer");
        window.Press("Ctrl+G W");

        Assert.Equal(Area.Overseer, window.CurrentArea);
    }

    [Fact]
    public void A_command_the_file_says_nothing_about_keeps_the_key_it_had()
    {
        using var window = Open(keys: "{ \"log.toolCalls\": \"x\" }");

        Assert.Equal("F1", window.Commands.Registered.Single(command => command.Id == "help").Keys);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{ \"nobody.registered.this\": \"F4\" }")]
    [InlineData("{ \"settings\": \"nonsense\" }")]
    public void An_override_we_cannot_use_is_ignored_on_its_own_and_leaves_every_default_alone(string keys)
    {
        using var window = Open(keys: keys);

        Assert.Equal("Ctrl+,", window.Commands.Registered.Single(command => command.Id == "settings").Keys);
        Assert.Equal("Ctrl+T T", window.Commands.KeysFor("log.toolCalls"));
    }

    [Theory]
    [InlineData("{ \"nobody.registered.this\": \"F4\", \"help\": \"F2\" }")]
    [InlineData("{ \"settings\": \"nonsense\", \"help\": \"F2\" }")]
    public void The_rest_of_the_file_still_applies_around_an_override_we_cannot_use(string keys)
    {
        using var window = Open(keys: keys);

        Assert.Equal("F2", window.Commands.Registered.Single(command => command.Id == "help").Keys);
    }

    [Fact]
    public void The_commands_list_names_the_key_the_file_bound()
    {
        using var window = Open(agents: Agents(4), keys: "{ \"commands\": \"Ctrl+K\" }");

        using var dialog = new CommandsDialog(window.Commands.Registered);

        Assert.Contains(dialog.Matches.Select(command => command.Keys), keys => keys == "Ctrl+K");
    }

    [Fact]
    public void The_status_bar_s_one_hint_is_press_F1_for_help_at_its_right_end_in_Overseer_a_full_screen_log_and_Work()
    {
        using var window = Open(agents: Agents(4));
        AssertOnlyKeysHint(window);

        SelectAgent(window, 0);
        AssertOnlyKeysHint(window);

        window.Commands.Execute("view.dispatcher");
        AssertOnlyKeysHint(window);

        window.Commands.Execute("view.work");
        AssertOnlyKeysHint(window);

        window.Commands.Execute("view.overseer");
        AssertOnlyKeysHint(window);
    }

    [Fact]
    public void The_status_bar_s_hint_follows_the_keys_command_s_key()
    {
        using var window = Open(agents: Agents(4), keys: "{ \"help\": \"F2\" }");

        Assert.Equal("Press F2 for help", window.Status.Hints.Says);
    }

    [Fact]
    public void The_status_bar_is_the_last_row_of_the_window_in_a_band_of_its_own()
    {
        using var window = Open(agents: Agents(4));

        LayOut(window, 120, 30);

        Assert.Equal(new Rectangle(0, window.Viewport.Height - 1, window.Viewport.Width, 1), window.Status.Frame);
        Assert.Equal(StatusBar.Scheme, window.Status.SchemeName);
        Assert.Null(window.Status.Message);
    }

    [Fact]
    public void What_is_running_shows_on_the_status_bar_and_takes_no_row_of_its_own()
    {
        using var window = Open(agents: Agents(4), run: _ => new TaskCompletionSource<string?>().Task);
        LayOut(window, 120, 30);
        SelectAgent(window, 0);
        LayOut(window, 120, 30);
        var status = window.Status.Frame;
        var lanes = window.Overseer.Frame;

        window.Commands.Execute("agent.hold");
        LayOut(window, 120, 30);

        Assert.Equal("Pausing this role…", window.Says());
        Assert.Null(window.Status.MessageSeverity);
        Assert.Equal(status, window.Status.Frame);
        Assert.Equal(lanes, window.Overseer.Frame);
    }

    [Fact]
    public void The_help_hint_is_a_button_you_can_click()
    {
        using var window = Open(agents: Agents(4));

        Assert.Equal("Press F1 for help", Assert.IsType<Button>(window.Status.Hints.SubViews.Single()).Text);
    }
}
