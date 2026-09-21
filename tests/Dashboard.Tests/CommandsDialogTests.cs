using System.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace ATeam.Dashboard.Tests;

public class CommandsDialogTests
{
    [Fact]
    public void It_opens_on_the_filter_with_everything_listed_and_keyless_rows_left_blank()
    {
        using var dialog = Open(out _);

        Assert.True(dialog.Filter.HasFocus);
        Assert.Equal(["expand", "scroll", "quit"], dialog.Matches.Select(command => command.Id));
        Assert.Equal(
            ["Expand the agent  Enter", "Scroll the log    PageUp", "Quit"],
            Rows(dialog));
    }

    [Fact]
    public void Typing_narrows_the_list_whatever_case_it_is_typed_in()
    {
        using var dialog = Open(out _);

        Type(dialog, "SCROLL");

        Assert.Equal(["scroll"], dialog.Matches.Select(command => command.Id));
        Assert.Equal(0, dialog.List.Value);
    }

    [Fact]
    public void A_filter_nothing_matches_leaves_the_list_empty()
    {
        using var dialog = Open(out _);

        Type(dialog, "zzz");

        Assert.Empty(dialog.Matches);
        Assert.Null(dialog.List.Value);
    }

    [Fact]
    public void Enter_runs_the_highlighted_command_and_closes()
    {
        using var dialog = Open(out var commands);
        Type(dialog, "scroll");

        Assert.True(dialog.NewKeyDownEvent(Key.Enter));

        Assert.Equal("scroll", dialog.Chosen);
        Assert.True(commands.Execute(dialog.Chosen!));
        Assert.Equal(["scroll"], Ran);
    }

    [Fact]
    public void Enter_with_nothing_matching_runs_nothing_and_stays_put()
    {
        using var dialog = Open(out _);
        Type(dialog, "zzz");

        Assert.True(dialog.NewKeyDownEvent(Key.Enter));

        Assert.Null(dialog.Chosen);
        Assert.Empty(Ran);
    }

    [Fact]
    public void Esc_closes_without_running_anything()
    {
        using var dialog = Open(out _);
        Type(dialog, "scroll");

        Assert.True(dialog.NewKeyDownEvent(Key.Esc));

        Assert.Null(dialog.Chosen);
        Assert.Empty(Ran);
    }

    [Fact]
    public void Up_and_Down_move_the_selection_without_leaving_the_filter()
    {
        using var dialog = Open(out _);

        Assert.True(dialog.NewKeyDownEvent(Key.CursorDown));
        Assert.True(dialog.NewKeyDownEvent(Key.CursorDown));
        Assert.Equal(2, dialog.List.Value);
        Assert.True(dialog.Filter.HasFocus);

        Assert.True(dialog.NewKeyDownEvent(Key.CursorUp));
        Assert.Equal(1, dialog.List.Value);
        Assert.True(dialog.Filter.HasFocus);
    }

    [Fact]
    public void The_selection_stops_at_both_ends_of_the_list()
    {
        using var dialog = Open(out _);

        dialog.NewKeyDownEvent(Key.CursorUp);
        Assert.Equal(0, dialog.List.Value);

        for (var i = 0; i < 5; i++)
            dialog.NewKeyDownEvent(Key.CursorDown);
        Assert.Equal(2, dialog.List.Value);
    }

    [Fact]
    public void Home_and_End_jump_to_the_ends_of_the_list()
    {
        using var dialog = Open(out _);

        Assert.True(dialog.NewKeyDownEvent(Key.End));
        Assert.Equal(2, dialog.List.Value);

        Assert.True(dialog.NewKeyDownEvent(Key.Home));
        Assert.Equal(0, dialog.List.Value);
    }

    [Fact]
    public void PageDown_and_PageUp_move_the_selection_a_screenful_at_a_time()
    {
        using var host = new View { Width = 40, Height = 20 };
        using var dialog = new CommandsDialog(Many(20));
        host.Add(dialog);
        host.Layout(new Size(40, 20));

        var page = dialog.List.Viewport.Height;
        Assert.InRange(page, 2, 19);

        Assert.True(dialog.NewKeyDownEvent(Key.PageDown));
        Assert.Equal(page, dialog.List.Value);

        Assert.True(dialog.NewKeyDownEvent(Key.PageUp));
        Assert.Equal(0, dialog.List.Value);
    }

    [Fact]
    public void Paging_stops_at_both_ends_of_the_list()
    {
        using var host = new View { Width = 40, Height = 20 };
        using var dialog = new CommandsDialog(Many(20));
        host.Add(dialog);
        host.Layout(new Size(40, 20));

        Assert.True(dialog.NewKeyDownEvent(Key.PageUp));
        Assert.Equal(0, dialog.List.Value);

        for (var i = 0; i < 20; i++)
            dialog.NewKeyDownEvent(Key.PageDown);
        Assert.Equal(19, dialog.List.Value);
    }

    [Fact]
    public void Paging_a_list_nothing_matches_leaves_nothing_selected()
    {
        using var dialog = Open(out _);
        Type(dialog, "zzz");

        Assert.True(dialog.NewKeyDownEvent(Key.PageDown));
        Assert.True(dialog.NewKeyDownEvent(Key.End));

        Assert.Null(dialog.List.Value);
    }

    [Fact]
    public void Enter_runs_whatever_the_arrows_left_highlighted()
    {
        using var dialog = Open(out var commands);

        dialog.NewKeyDownEvent(Key.CursorDown);
        dialog.NewKeyDownEvent(Key.Enter);
        commands.Execute(dialog.Chosen!);

        Assert.Equal(["scroll"], Ran);
    }

    [Fact]
    public void Tab_hands_focus_to_the_list_and_back()
    {
        using var dialog = Open(out _);

        Assert.True(dialog.NewKeyDownEvent(Key.Tab));
        Assert.True(dialog.List.HasFocus);

        Assert.True(dialog.NewKeyDownEvent(Key.Tab));
        Assert.True(dialog.Filter.HasFocus);
    }

    [Fact]
    public void The_dialog_stays_inside_a_small_terminal()
    {
        using var host = new View { Width = 40, Height = 10 };
        using var dialog = new CommandsDialog(Registry(out _).Registered);
        host.Add(dialog);

        host.Layout(new Size(40, 10));

        Assert.True(host.Viewport.Contains(dialog.Frame), $"{dialog.Frame} overhangs {host.Viewport}");
    }

    private readonly List<string> Ran = [];

    private CommandRegistry Registry(out CommandRegistry commands) => commands = new CommandRegistry()
        .Register("expand", "Expand the agent", () => Ran.Add("expand"), Key.Enter)
        .Register("scroll", "Scroll the log", () => Ran.Add("scroll"), Key.PageUp)
        .Register("quit", "Quit", () => Ran.Add("quit"));

    private CommandsDialog Open(out CommandRegistry commands) => new(Registry(out commands).Registered);

    private static IReadOnlyList<CommandDescriptor> Many(int count)
    {
        var registry = new CommandRegistry();
        for (var i = 0; i < count; i++)
            registry.Register($"command{i}", $"Command {i}", () => { });
        return registry.Registered;
    }

    private static IEnumerable<string> Rows(CommandsDialog dialog) =>
        dialog.List.Source!.ToList().Cast<string>().Select(row => row.TrimEnd());

    private static void Type(CommandsDialog dialog, string text)
    {
        foreach (var character in text)
            Assert.True(dialog.NewKeyDownEvent(new Key(character)));
    }
}
