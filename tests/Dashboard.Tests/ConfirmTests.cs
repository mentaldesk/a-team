using MentalDesk.Tui.Dialogs;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace ATeam.Dashboard.Tests;

public class ConfirmTests
{
    private static readonly WaitingItem Task =
        new(49, "I can't change any of the keys", "In review", "https://github.com/x/49", "a-team", Pr: 122, Base: "main");

    public static TheoryData<string> Dialogs => ["accept", "remove", "stop", "discard"];

    private static ConfirmDialog Open(string which)
    {
        var dialog = which switch
        {
            "accept" => AcceptDialog.Create(Task),
            "remove" => RemoveTeamDialog.Create("goose", "o/r", "goose.json.removed"),
            "stop" => StopRunDialog.Create(new RunTask(244, "Choose whose comments count")),
            _ => DiscardDialog.Create(DiscardDialog.Comment),
        };
        dialog.BeginInit();
        dialog.EndInit();
        return dialog;
    }

    private static Button Action(ConfirmDialog dialog) => dialog.ButtonRow[0];

    [Theory]
    [MemberData(nameof(Dialogs))]
    public void Enter_does_nothing_whichever_button_has_focus(string which)
    {
        using var dialog = Open(which);

        Assert.True(dialog.NewKeyDownEvent(Key.Enter));
        Action(dialog).SetFocus();
        Assert.True(dialog.NewKeyDownEvent(Key.Enter));

        Assert.False(dialog.Confirmed);
        Assert.Null(dialog.Chosen);
    }

    [Theory]
    [MemberData(nameof(Dialogs))]
    public void Ctrl_Enter_goes_ahead(string which)
    {
        using var dialog = Open(which);

        Assert.True(dialog.NewKeyDownEvent(Key.Enter.WithCtrl));

        Assert.True(dialog.Confirmed);
    }

    [Theory]
    [MemberData(nameof(Dialogs))]
    public void Esc_cancels(string which)
    {
        using var dialog = Open(which);
        Action(dialog).SetFocus();

        Assert.True(dialog.NewKeyDownEvent(Key.Esc));

        Assert.False(dialog.Confirmed);
        Assert.Null(dialog.Chosen);
    }

    [Theory]
    [MemberData(nameof(Dialogs))]
    public void Cancel_starts_focused(string which)
    {
        using var dialog = Open(which);

        Assert.True(dialog.CancelButton.HasFocus);
    }

    [Theory]
    [MemberData(nameof(Dialogs))]
    public void Tab_then_Space_presses_the_action(string which)
    {
        using var dialog = Open(which);

        // Tab is the application's key, so it can't reach a dialog without one.
        dialog.AdvanceFocus(NavigationDirection.Forward, TabBehavior.TabStop);
        Assert.True(Action(dialog).HasFocus);
        dialog.NewKeyDownEvent(Key.Space);

        Assert.True(dialog.Confirmed);
    }

    [Theory]
    [MemberData(nameof(Dialogs))]
    public void Left_and_right_move_between_the_buttons(string which)
    {
        using var dialog = Open(which);

        dialog.NewKeyDownEvent(Key.CursorLeft);
        Assert.True(Action(dialog).HasFocus);
        dialog.NewKeyDownEvent(Key.CursorRight);

        Assert.True(dialog.CancelButton.HasFocus);
        Assert.False(dialog.Confirmed);
    }

    [Theory]
    [InlineData("accept", " Ctrl+Enter Accept ", " Esc Cancel ")]
    [InlineData("remove", " Ctrl+Enter Remove ", " Esc Cancel ")]
    [InlineData("stop", " Ctrl+Enter Stop ", " Esc Cancel ")]
    [InlineData("discard", " Ctrl+Enter Discard ", " Esc Keep writing ")]
    public void Each_button_says_its_key(string which, string action, string cancel)
    {
        using var dialog = Open(which);

        Assert.Equal([action, cancel], dialog.ButtonRow.Select(button => button.Text));
    }

    [Fact]
    public void The_dashboard_has_no_confirm_dialog_of_its_own() =>
        Assert.Null(typeof(Confirm).Assembly.GetType("ATeam.Dashboard.ConfirmDialog"));

    [Fact]
    public void Accept_is_primary_and_the_others_are_danger()
    {
        Assert.Equal(ButtonKind.Primary, AcceptDialog.Accept.Kind);
        Assert.All([RemoveTeamDialog.Remove, StopRunDialog.Stop, DiscardDialog.Discard],
            action => Assert.Equal(ButtonKind.Danger, action.Kind));
    }

    [Fact]
    public void The_text_wraps_to_the_width_it_is_given()
    {
        using var dialog = RemoveTeamDialog.Create("goose", "aaif-goose/goose", "goose.json.removed", width: 36);

        Assert.All(dialog.Lines, line => Assert.True(line.Length <= 36, line));
        Assert.True(dialog.Lines.Count > 1);
    }

    [Fact]
    public void A_blank_line_between_paragraphs_stays() =>
        Assert.Equal(["one two", "three", "", "four"], Confirm.Lines("one two three\n\nfour", 7));

    [Fact]
    public void Wrapping_breaks_at_spaces() =>
        Assert.Equal(["one two", "three"], Confirm.Wrap("one two three", 7));

    [Theory]
    [InlineData(200, Confirm.Wide)]
    [InlineData(40, 36)]
    public void The_text_fits_the_screen(int screen, int width) =>
        Assert.Equal(width, Confirm.Fit(screen));
}
