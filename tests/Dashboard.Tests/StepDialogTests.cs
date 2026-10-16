using Terminal.Gui.Input;

namespace ATeam.Dashboard.Tests;

[Collection("StaticConfiguration")]
public class StepDialogTests
{
    private static readonly Step Choice = new("Get to work?", ["line"], "Get to work", "Keep the team paused for now", Choose: true);

    [Fact]
    public void The_arrows_choose_an_option_without_Space()
    {
        using var dialog = new StepDialog(Choice);
        using var app = DialogTests.Begin(dialog);

        dialog.NewKeyDownEvent(Key.CursorDown);

        Assert.Equal(1, dialog.Choice!.Value);
        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);
        Assert.False(dialog.Done);
        Assert.False(dialog.Cancelled);
    }

    [Fact]
    public void F1_lists_its_keys_under_the_step_s_title()
    {
        using var dialog = new StepDialog(Choice);

        var sheet = DialogTests.F1(dialog);

        Assert.Equal("Get to work?", sheet.Here!.Heading);
        Assert.Equal(
            [("Ctrl+Enter", "Choose"), ("Backspace", "Back"), ("↑", "Previous option"), ("↓", "Next option"), ("Esc", "Cancel new team")],
            DialogTests.Rows(sheet));
    }

    [Fact]
    public void Clicking_a_button_does_what_its_key_does()
    {
        using var dialog = new StepDialog(new Step("Clone?", ["line"], "clone", "skip"));

        dialog.Foot.Shown.Single(button => button.Text.Contains("Back")).InvokeCommand(Command.Accept);

        Assert.True(dialog.WentBack);
    }
}
