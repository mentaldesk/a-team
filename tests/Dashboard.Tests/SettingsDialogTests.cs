using Terminal.Gui.Input;

namespace ATeam.Dashboard.Tests;

public class SettingsDialogTests
{
    [Fact]
    public void Ctrl_Enter_keeps_the_theme_picked_in_the_dialog()
    {
        using var dialog = Open(out _);

        Assert.True(dialog.NewKeyDownEvent(Key.Enter.WithCtrl));
        Assert.True(dialog.Confirmed);
    }

    [Fact]
    public void Esc_closes_the_dialog_without_keeping_the_theme()
    {
        using var dialog = Open(out _);

        Assert.True(dialog.NewKeyDownEvent(Key.Esc));
        Assert.False(dialog.Confirmed);
    }

    [Fact]
    public void Enter_is_left_free_and_does_not_close_the_dialog()
    {
        using var dialog = Open(out _);
        var asked = false;
        dialog.Accepting += (_, _) => asked = true;

        dialog.MostFocused!.NewKeyDownEvent(Key.Enter);

        Assert.False(asked);
        Assert.Null(dialog.Result);
        Assert.False(dialog.Confirmed);
    }

    [Fact]
    public void The_OK_button_keeps_the_theme_and_Cancel_does_not()
    {
        using var ok = Open(out _);
        ok.Buttons.Single(b => b.Text == "OK").InvokeCommand(Command.Accept);
        Assert.True(ok.Confirmed);

        using var cancel = Open(out _);
        cancel.Buttons.Single(b => b.Text == "Cancel").InvokeCommand(Command.Accept);
        Assert.False(cancel.Confirmed);
    }

    private static SettingsDialog Open(out ThemeSetting theme)
    {
        theme = new ThemeSetting(BundledThemes.Midnight, _ => { }, _ => { });
        var dialog = new SettingsDialog(theme, () => { });
        dialog.SetFocus();
        return dialog;
    }
}
