using Terminal.Gui.Input;

namespace ATeam.Dashboard.Tests;

public class CommandRegistryTests
{
    [Fact]
    public void A_registered_command_runs_by_name()
    {
        var ran = 0;
        var commands = new CommandRegistry().Register("greet", "Say hello", () => ran++);

        Assert.True(commands.Execute("greet"));
        Assert.Equal(1, ran);
    }

    [Fact]
    public void Only_commands_that_would_run_now_are_offered()
    {
        var on = true;
        var commands = new CommandRegistry()
            .Register("greet", "Say hello", () => { })
            .Register("wave", "Wave", () => { }, isEnabled: () => on);

        Assert.Equal(["greet", "wave"], commands.Enabled.Select(command => command.Id));
        on = false;
        Assert.Equal(["greet"], commands.Enabled.Select(command => command.Id));
    }

    [Fact]
    public void A_name_nobody_registered_runs_nothing()
    {
        var commands = new CommandRegistry().Register("greet", "Say hello", () => { });

        Assert.False(commands.Execute("farewell"));
    }

    [Fact]
    public void What_is_registered_reads_back_with_its_keys_and_blank_where_it_has_none()
    {
        var commands = new CommandRegistry()
            .Register("greet", "Say hello", () => { }, Key.F1)
            .Register("go", "Go", () => { }, chord: "Ctrl+G W")
            .Register("quit", "Quit", () => { });

        Assert.Equal([("greet", "Say hello", "F1"), ("go", "Go", "Ctrl+G W"), ("quit", "Quit", "")],
            commands.Registered.Select(command => (command.Id, command.Label, command.Keys)));
    }

    [Fact]
    public void A_command_reports_the_key_it_is_bound_to_and_nothing_for_one_nobody_registered()
    {
        var commands = new CommandRegistry()
            .Register("greet", "Say hello", () => { }, Key.F1)
            .Register("quit", "Quit", () => { });

        Assert.Equal(Key.F1, commands.KeyFor("greet"));
        Assert.Equal(Key.Empty, commands.KeyFor("quit"));
        Assert.Equal(Key.Empty, commands.KeyFor("nobody"));
    }

    [Fact]
    public void A_key_runs_what_it_is_bound_to_and_nothing_runs_for_an_unbound_one()
    {
        var ran = 0;
        var commands = new CommandRegistry().Register("greet", "Say hello", () => ran++, Key.F1);

        Assert.True(commands.Press(Key.F1));
        Assert.False(commands.Press(Key.F2));
        Assert.Equal(1, ran);
    }

    [Fact]
    public void A_key_bound_to_a_disabled_command_is_left_for_someone_else()
    {
        var ran = 0;
        var commands = new CommandRegistry()
            .Register("greet", "Say hello", () => ran++, Key.F1, isEnabled: () => false);

        Assert.False(commands.Press(Key.F1));
        Assert.Equal(0, ran);
    }

    [Fact]
    public void Whether_a_command_would_run_reads_back_by_name()
    {
        var enabled = false;
        var commands = new CommandRegistry()
            .Register("greet", "Say hello", () => { }, Key.F1, isEnabled: () => enabled)
            .Register("quit", "Quit", () => { });

        Assert.False(commands.IsEnabled("greet"));
        enabled = true;
        Assert.True(commands.IsEnabled("greet"));
        Assert.True(commands.IsEnabled("quit"));
        Assert.True(commands.IsEnabled("nobody"));
    }

    [Fact]
    public void A_disabled_command_still_runs_when_it_is_asked_for_by_name()
    {
        var ran = 0;
        var commands = new CommandRegistry()
            .Register("greet", "Say hello", () => ran++, Key.F1, isEnabled: () => false);

        Assert.True(commands.Execute("greet"));
        Assert.Equal(1, ran);
    }

    [Fact]
    public void A_keyless_command_is_not_run_by_the_empty_key()
    {
        var ran = 0;
        var commands = new CommandRegistry().Register("quit", "Quit", () => ran++);

        Assert.False(commands.Press(Key.Empty));
        Assert.Equal(0, ran);
    }

    [Fact]
    public void A_chord_runs_its_command_once_it_is_finished_and_not_before()
    {
        var ran = 0;
        var commands = new CommandRegistry().Register("work", "Work", () => ran++, chord: "Ctrl+G W");

        Assert.True(commands.Press(Key.G.WithCtrl));
        Assert.Equal("Ctrl+G", commands.PendingChord);
        Assert.Equal(0, ran);
        Assert.True(commands.Press(new Key('w')));
        Assert.Null(commands.PendingChord);
        Assert.Equal(1, ran);
    }

    [Fact]
    public void A_key_that_finishes_no_chord_cancels_it_says_so_and_runs_nothing()
    {
        var ran = 0;
        var commands = new CommandRegistry()
            .Register("work", "Work", () => ran++, chord: "Ctrl+G W")
            .Register("x", "Something else", () => ran++, new Key('x'));

        commands.Press(Key.G.WithCtrl);

        Assert.True(commands.Press(new Key('x'), out var cancelled));
        Assert.True(cancelled);
        Assert.Null(commands.PendingChord);
        Assert.Equal(0, ran);
    }

    [Fact]
    public void A_finished_chord_is_not_a_cancelled_one()
    {
        var commands = new CommandRegistry().Register("work", "Work", () => { }, chord: "Ctrl+G W");

        commands.Press(Key.G.WithCtrl);
        commands.Press(new Key('w'), out var cancelled);

        Assert.False(cancelled);
    }

    [Fact]
    public void A_region_key_runs_only_while_that_region_has_focus_and_wins_over_the_same_key_everywhere()
    {
        var ran = new List<string>();
        var here = Regions.Work;
        var commands = new CommandRegistry()
            .Register("everywhere", "Everywhere", () => ran.Add("everywhere"), new Key('a'))
            .Register("work", "In Work", () => ran.Add("work"), new Key('a'), scopes: [Regions.Work]);
        commands.Keys.FocusedScope = () => here;

        commands.Press(new Key('a'));
        here = Regions.Overseer;
        commands.Press(new Key('a'));

        Assert.Equal(["work", "everywhere"], ran);
    }

    [Fact]
    public void A_command_in_several_regions_has_its_key_in_each_of_them()
    {
        var ran = 0;
        var here = Regions.Agents;
        var commands = new CommandRegistry().Register("hold", "Hold", () => ran++, new Key('h'), scopes: [Regions.Agents, Regions.Log]);
        commands.Keys.FocusedScope = () => here;

        commands.Press(new Key('h'));
        here = Regions.Log;
        commands.Press(new Key('h'));
        here = Regions.Work;
        commands.Press(new Key('h'));

        Assert.Equal(2, ran);
        Assert.Equal("H", commands.KeysFor("hold"));
    }

    [Fact]
    public void Two_defaults_on_one_key_in_one_place_are_refused()
    {
        var commands = new CommandRegistry().Register("greet", "Say hello", () => { }, Key.F1);

        Assert.Throws<InvalidOperationException>(() => commands.Register("wave", "Wave", () => { }, Key.F1));
    }

    [Fact]
    public void A_chord_is_not_a_single_key_to_answer_in_a_dialog()
    {
        var commands = new CommandRegistry().Register("work", "Work", () => { }, chord: "Ctrl+G W");

        Assert.Equal(Key.Empty, commands.KeyFor("work"));
        Assert.Equal("Ctrl+G W", commands.KeysFor("work"));
    }

    [Fact]
    public void A_key_saved_before_the_keymap_replaces_its_command_default_in_each_region_it_works_in()
    {
        var commands = new CommandRegistry()
            .Register("work", "Work", () => { }, chord: "Ctrl+G W")
            .Register("hold", "Hold", () => { }, new Key('h'), scopes: [Regions.Agents, Regions.Log]);

        var carried = SavedKeys.Carry([("work", new Key('w')), ("hold", new Key('z'))], commands);

        Assert.Equal("w", carried["work"]!.GetValue<string>());
        Assert.Equal(["Agents", "Log"], carried["hold"]!.AsArray().Select(entry => entry!["where"]!.GetValue<string>()));
    }

    [Fact]
    public void A_saved_bare_letter_the_defaults_dropped_still_runs_its_command_once_loaded()
    {
        var ran = 0;
        var commands = new CommandRegistry().Register("work", "Work", () => ran++, chord: "Ctrl+G W");
        var saved = SavedKeys.Carry([("work", new Key('w'))], commands);
        var overrides = new MentalDesk.Tui.Keys.KeyOverrides(commands.Library, commands.Keys, () => saved, _ => { });

        overrides.Load();
        commands.Press(new Key('w'));

        Assert.Equal(1, ran);
    }

    [Fact]
    public void A_saved_key_wins_over_a_new_default_it_clashes_with()
    {
        var ran = new List<string>();
        var commands = new CommandRegistry()
            .Register("new", "New idea", () => ran.Add("new"), Key.N.WithCtrl)
            .Register("next", "Next", () => ran.Add("next"), Key.F2);
        var saved = SavedKeys.Carry([("next", Key.N.WithCtrl)], commands);
        var overrides = new MentalDesk.Tui.Keys.KeyOverrides(commands.Library, commands.Keys, () => saved, _ => { });

        overrides.Load();
        commands.Press(Key.N.WithCtrl);

        Assert.Equal(["next"], ran);
        Assert.Equal("", commands.KeysFor("new"));
    }

    [Fact]
    public void A_saved_empty_key_leaves_its_command_with_none()
    {
        var commands = new CommandRegistry().Register("work", "Work", () => { }, chord: "Ctrl+G W");
        var saved = SavedKeys.Carry([("work", Key.Empty)], commands);
        new MentalDesk.Tui.Keys.KeyOverrides(commands.Library, commands.Keys, () => saved, _ => { }).Load();

        Assert.Equal("", commands.KeysFor("work"));
    }

    [Fact]
    public void A_saved_key_that_starts_a_chord_is_left_out_so_the_chord_still_works()
    {
        var ran = new List<string>();
        var commands = new CommandRegistry()
            .Register("work", "Work", () => ran.Add("work"), chord: "Ctrl+G W")
            .Register("next", "Next", () => ran.Add("next"), Key.F2);
        var saved = SavedKeys.Carry([("next", Key.G.WithCtrl)], commands);
        var overrides = new MentalDesk.Tui.Keys.KeyOverrides(commands.Library, commands.Keys, () => saved, _ => { });

        overrides.Load();
        commands.Press(Key.G.WithCtrl);
        commands.Press(new Key('w'));

        Assert.Equal(["work"], ran);
        Assert.Equal("F2", commands.KeysFor("next"));
    }
}
