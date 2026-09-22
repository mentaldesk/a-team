using System.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace ATeam.Dashboard.Tests;

public class HelpDialogTests
{
    [Fact]
    public void It_lists_the_palette_first_and_the_keys_worth_memorising_under_it()
    {
        using var dialog = Open();

        Assert.Equal(
            [
                "Ctrl+E      every command, by name",
                "Tab/arrows  select an agent",
                "Enter       expand the selected agent",
                "PgUp/PgDn   scroll the log",
                "s           settings",
                "Esc         back, or quit",
            ],
            Rows(dialog));
    }

    [Fact]
    public void Each_row_shows_the_key_the_registry_has_bound()
    {
        using var dialog = new HelpDialog(new CommandRegistry()
            .Register("commands", "Commands", () => { }, Key.P.WithCtrl)
            .Register("agent.expand", "Expand", () => { }, Key.Space)
            .Registered);

        Assert.Equal("Ctrl+P  every command, by name", Rows(dialog)[0]);
        Assert.Equal("Space   expand the selected agent", Rows(dialog)[2]);
    }

    [Fact]
    public void A_row_whose_commands_have_no_key_shows_none()
    {
        using var dialog = new HelpDialog(new CommandRegistry().Register("settings", "Settings", () => { }).Registered);

        Assert.Equal("every command, by name", Rows(dialog)[0].Trim());
        Assert.Equal("settings", Rows(dialog)[4].Trim());
    }

    [Fact]
    public void Esc_closes_it()
    {
        using var dialog = Open();

        Assert.True(dialog.NewKeyDownEvent(Key.Esc));

        Assert.True(dialog.Closed);
    }

    [Fact]
    public void Enter_does_nothing_because_help_has_nothing_to_confirm()
    {
        using var dialog = Open();

        dialog.NewKeyDownEvent(Key.Enter);

        Assert.False(dialog.Closed);
    }

    [Fact]
    public void Nothing_in_it_takes_focus_because_there_is_nothing_to_pick()
    {
        using var host = new View { Width = 48, Height = 12, CanFocus = true };
        using var dialog = Open();
        host.Add(dialog);
        host.Layout(new Size(48, 12));

        Assert.False(dialog.Keys.CanFocus);
        Assert.DoesNotContain(dialog.SubViews, view => view.CanFocus);
    }

    [Fact]
    public void It_is_only_as_big_as_the_keys_it_lists_so_none_of_them_wrap_or_fall_off()
    {
        using var host = new View { Width = 80, Height = 24 };
        using var dialog = Open();
        host.Add(dialog);

        host.Layout(new Size(80, 24));

        Assert.Equal(Rows(dialog), dialog.Keys.TextFormatter.GetLines());
    }

    [Fact]
    public void The_dialog_stays_inside_a_small_terminal()
    {
        using var host = new View { Width = 30, Height = 6 };
        using var dialog = Open();
        host.Add(dialog);

        host.Layout(new Size(30, 6));

        Assert.True(host.Viewport.Contains(dialog.Frame), $"{dialog.Frame} overhangs {host.Viewport}");
    }

    private static HelpDialog Open() => new(new CommandRegistry()
        .Register("agent.next", "Select the next agent", () => { }, Key.Tab)
        .Register("agent.right", "Select the agent to the right", () => { }, Key.CursorRight)
        .Register("agent.left", "Select the agent to the left", () => { }, Key.CursorLeft)
        .Register("agent.down", "Select the agent below", () => { }, Key.CursorDown)
        .Register("agent.up", "Select the agent above", () => { }, Key.CursorUp)
        .Register("agent.expand", "Expand the selected agent", () => { }, Key.Enter)
        .Register("log.pageUp", "Scroll the log up", () => { }, Key.PageUp)
        .Register("log.pageDown", "Scroll the log down", () => { }, Key.PageDown)
        .Register("settings", "Settings", () => { }, new Key('s'))
        .Register("commands", "Commands", () => { }, Key.E.WithCtrl)
        .Register("agent.collapse", "Back to the agent grid", () => { }, Key.Esc)
        .Registered);

    private static IReadOnlyList<string> Rows(HelpDialog dialog) =>
        [.. dialog.Keys.Text.Split('\n').Select(row => row.TrimEnd())];
}
