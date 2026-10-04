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
    public void What_is_registered_reads_back_with_its_key_and_blank_where_it_has_none()
    {
        var commands = new CommandRegistry()
            .Register("greet", "Say hello", () => { }, Key.F1)
            .Register("quit", "Quit", () => { });

        Assert.Equal([("greet", "Say hello", Key.F1), ("quit", "Quit", Key.Empty)],
            commands.Registered.Select(command => (command.Id, command.Label, command.Key)));
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
    public void A_rebound_command_runs_on_its_new_key_and_not_on_its_old_one()
    {
        var ran = 0;
        var commands = new CommandRegistry().Register("greet", "Say hello", () => ran++, Key.F1);

        commands.Apply([("greet", Key.F2)]);

        Assert.True(commands.Press(Key.F2));
        Assert.False(commands.Press(Key.F1));
        Assert.Equal(1, ran);
    }

    [Fact]
    public void A_command_nobody_rebound_keeps_the_key_it_was_registered_with()
    {
        var commands = new CommandRegistry()
            .Register("greet", "Say hello", () => { }, Key.F1)
            .Register("quit", "Quit", () => { }, Key.F3);

        commands.Apply([("greet", Key.F2)]);

        Assert.Equal(Key.F3, commands.Registered.Single(command => command.Id == "quit").Key);
    }

    [Fact]
    public void Rebinding_an_id_nobody_registered_changes_nothing()
    {
        var commands = new CommandRegistry().Register("greet", "Say hello", () => { }, Key.F1);

        commands.Apply([("farewell", Key.F2), ("greet", Key.F3)]);

        Assert.Equal([("greet", Key.F3)], commands.Registered.Select(command => (command.Id, command.Key)));
    }

    [Fact]
    public void Rebinding_to_no_key_leaves_the_command_unbound_even_beside_other_unbound_ones()
    {
        var commands = new CommandRegistry()
            .Register("greet", "Say hello", () => { }, Key.F1)
            .Register("quit", "Quit", () => { });

        commands.Apply([("greet", Key.Empty)]);

        Assert.Equal(Key.Empty, commands.KeyFor("greet"));
        Assert.False(commands.Press(Key.F1));
    }

    [Fact]
    public void A_key_another_command_holds_is_refused_and_leaves_both_where_they_were()
    {
        var commands = new CommandRegistry()
            .Register("greet", "Say hello", () => { }, Key.F1)
            .Register("quit", "Quit", () => { }, Key.F2);

        commands.Apply([("greet", Key.F2)]);

        Assert.Equal([("greet", Key.F1), ("quit", Key.F2)],
            commands.Registered.Select(command => (command.Id, command.Key)));
    }

    [Fact]
    public void A_key_an_earlier_rebinding_took_is_refused_the_second_time()
    {
        var commands = new CommandRegistry()
            .Register("greet", "Say hello", () => { }, Key.F1)
            .Register("quit", "Quit", () => { }, Key.F2);

        commands.Apply([("greet", Key.F3), ("quit", Key.F3)]);

        Assert.Equal([("greet", Key.F3), ("quit", Key.F2)],
            commands.Registered.Select(command => (command.Id, command.Key)));
    }

    [Fact]
    public void The_key_a_rebinding_freed_is_there_for_the_next_one_to_take()
    {
        var commands = new CommandRegistry()
            .Register("greet", "Say hello", () => { }, Key.F1)
            .Register("quit", "Quit", () => { }, Key.F2);

        commands.Apply([("greet", Key.F3), ("quit", Key.F1)]);

        Assert.Equal([("greet", Key.F3), ("quit", Key.F1)],
            commands.Registered.Select(command => (command.Id, command.Key)));
    }

    [Fact]
    public void Rebinding_nothing_leaves_every_key_as_it_was()
    {
        var commands = new CommandRegistry()
            .Register("greet", "Say hello", () => { }, Key.F1)
            .Register("quit", "Quit", () => { }, Key.F2);

        commands.Apply([]);

        Assert.Equal([("greet", Key.F1), ("quit", Key.F2)],
            commands.Registered.Select(command => (command.Id, command.Key)));
    }
}
