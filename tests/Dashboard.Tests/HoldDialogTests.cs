using System.Drawing;
using MentalDesk.Tui.Theming;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

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

    private static Label WhyLabel(HoldDialog dialog) => dialog.SubViews.OfType<Label>().Single(label => label.Text == "Why:");

    [Fact]
    public void Hold_stays_enabled_and_Enter_with_no_reason_marks_Why_says_so_and_stays_open()
    {
        using var dialog = Open();
        dialog.Why.Text = "   ";

        Assert.True(dialog.Foot.Shown[0].Enabled);
        dialog.NewKeyDownEvent(Key.Enter);
        dialog.Foot.Shown[0].InvokeCommand(Command.Accept);

        Assert.Null(dialog.Chosen);
        Assert.True(dialog.Foot.Shown[0].Enabled);
        Assert.Equal(SchemeNames.Error, WhyLabel(dialog).SchemeName);
        Assert.Equal("Why is needed", dialog.Alert.Message);
        Assert.True(dialog.Why.HasFocus);
    }

    [Fact]
    public void Giving_a_reason_clears_the_mark_and_the_message_then_Enter_holds()
    {
        using var dialog = Open();
        dialog.NewKeyDownEvent(Key.Enter);

        dialog.Why.Text = "waiting on design";

        Assert.NotEqual(SchemeNames.Error, WhyLabel(dialog).SchemeName);
        Assert.Equal("", dialog.Alert.Message);
        dialog.NewKeyDownEvent(Key.Enter);
        Assert.Equal("waiting on design", dialog.Chosen);
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
