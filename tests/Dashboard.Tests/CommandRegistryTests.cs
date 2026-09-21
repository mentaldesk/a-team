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
    public void A_name_nobody_registered_runs_nothing()
    {
        var commands = new CommandRegistry().Register("greet", "Say hello", () => { });

        Assert.False(commands.Execute("farewell"));
    }

    [Fact]
    public void What_is_registered_reads_back_with_its_key_and_blank_where_it_has_none()
    {
        var commands = new CommandRegistry()
            .Register("greet", "Say hello", () => { }, Key.F1)
            .Register("quit", "Quit", () => { });

        Assert.Equal([("greet", "Say hello", Key.F1), ("quit", "Quit", Key.Empty)],
            commands.Registered.Select(command => (command.Id, command.Label, command.Key)));
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
    public void The_hint_bar_names_the_commands_for_that_view_once_each_in_order()
    {
        var arrows = new Hint("arrows", "select", Mode.Grid);
        var commands = new CommandRegistry()
            .Register("left", "Left", () => { }, Key.CursorLeft, arrows)
            .Register("right", "Right", () => { }, Key.CursorRight, arrows)
            .Register("scroll", "Scroll", () => { }, Key.PageUp, new Hint("PgUp/PgDn", "scroll", Mode.Expanded))
            .Register("help", "Help", () => { }, Key.F1, new Hint("F1", "help"))
            .Register("nothing", "Not hinted", () => { }, Key.F2);

        Assert.Equal("arrows: select · F1: help", commands.Hints(Mode.Grid));
        Assert.Equal("PgUp/PgDn: scroll · F1: help", commands.Hints(Mode.Expanded));
    }

    [Fact]
    public void A_registry_with_nothing_hinted_has_an_empty_hint_bar()
    {
        var commands = new CommandRegistry().Register("quit", "Quit", () => { }, Key.F1);

        Assert.Equal("", commands.Hints(Mode.Grid));
    }
}
