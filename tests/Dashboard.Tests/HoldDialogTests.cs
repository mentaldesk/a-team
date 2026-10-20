using System.Drawing;
using Terminal.Gui.Input;

namespace ATeam.Dashboard.Tests;

public class HoldDialogTests
{
    private static HoldDialog Open()
    {
        var dialog = new HoldDialog(575);
        dialog.Layout(new Size(80, 20));
        return dialog;
    }

    [Fact]
    public void It_asks_why_under_the_task_s_number_with_Hold_and_Cancel()
    {
        using var dialog = Open();

        Assert.Equal("Hold #575", dialog.Title);
        Assert.Equal(["Enter Hold", "Esc Cancel"], dialog.ButtonLabels);
    }

    [Fact]
    public void Enter_holds_with_the_reason_typed()
    {
        using var dialog = Open();
        dialog.Why.Text = " wait until I've talked to the designer ";

        Assert.True(dialog.NewKeyDownEvent(Key.Enter));

        Assert.Equal("wait until I've talked to the designer", dialog.Chosen);
    }

    [Fact]
    public void It_won_t_hold_with_no_reason()
    {
        using var dialog = Open();
        dialog.Why.Text = "   ";

        dialog.NewKeyDownEvent(Key.Enter);
        dialog.Foot.Shown[0].InvokeCommand(Command.Accept);

        Assert.Null(dialog.Chosen);
        Assert.False(dialog.Foot.Shown[0].Enabled);
    }

    [Fact]
    public void Esc_cancels()
    {
        using var dialog = Open();
        dialog.Why.Text = "later";

        Assert.True(dialog.NewKeyDownEvent(Key.Esc));

        Assert.Null(dialog.Chosen);
    }
}
