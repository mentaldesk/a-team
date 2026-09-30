using Terminal.Gui.Input;

namespace ATeam.Dashboard.Tests;

public class PickerTests
{
    [Fact]
    public void The_filter_has_focus_and_the_hints_read_Space_mark_Enter_done_Esc_cancel()
    {
        using var picker = new Picker("Skills for a-team", [], "", "");
        picker.SetFocus();

        Assert.True(picker.Filter.HasFocus);
        Assert.Equal("Space mark · Enter done · Esc cancel", picker.Hints.Says);
    }

    [Fact]
    public void What_is_chosen_is_marked_and_stays_marked_when_the_list_arrives()
    {
        using var picker = new Picker("Stakeholders", ["jamescrosswell"], "", "Reading…");

        Assert.Equal([("jamescrosswell", true)], picker.Rows);

        picker.ShowChoices(["jamescrosswell", "octocat"], "2 can push.");

        Assert.Equal([("jamescrosswell", true), ("octocat", false)], picker.Rows);
        Assert.Equal("2 can push.", picker.Message.Says);
    }

    [Fact]
    public void Space_in_the_filter_marks_the_selected_row_rather_than_typing()
    {
        using var picker = new Picker("Skills", [], "", "");
        picker.ShowChoices(["a-team", "tuicode"], "");
        picker.SetFocus();
        picker.List.Value = 1;

        Assert.True(picker.Filter.NewKeyDownEvent(Key.Space));

        Assert.Equal("", picker.Filter.Text);
        Assert.Equal([("a-team", false), ("tuicode", true)], picker.Rows);
    }

    [Fact]
    public void Marks_survive_narrowing_the_list_and_Enter_returns_them_in_the_order_chosen()
    {
        using var picker = new Picker("Skills", ["a-team"], "", "");
        picker.ShowChoices(["a-team", "structural-analysis", "tuicode"], "");

        picker.Filter.Text = "tui";
        Assert.Equal([("tuicode", false)], picker.Rows);
        picker.Toggle();
        picker.Filter.Text = "";

        Assert.Equal([("a-team", true), ("structural-analysis", false), ("tuicode", true)], picker.Rows);
        picker.Done();
        Assert.Equal(["a-team", "tuicode"], picker.Picked);
    }

    [Fact]
    public void Unmarking_a_chosen_name_drops_it()
    {
        using var picker = new Picker("Skills", ["a-team", "tuicode"], "", "");
        picker.ShowChoices(["a-team", "tuicode"], "");

        picker.Toggle();
        picker.Done();

        Assert.Equal(["tuicode"], picker.Picked);
    }

    [Fact]
    public void A_typed_name_that_matches_nothing_is_added_on_Enter()
    {
        using var picker = new Picker("Skills", ["a-team"], "", "");
        picker.ShowChoices(["a-team", "tuicode"], "");

        picker.Filter.Text = "sentry-dotnet";
        Assert.Empty(picker.Rows);
        picker.Done();

        Assert.Equal(["a-team", "sentry-dotnet"], picker.Picked);
    }

    [Fact]
    public void A_typed_name_that_matches_something_adds_nothing()
    {
        using var picker = new Picker("Skills", [], "", "");
        picker.ShowChoices(["tuicode"], "");

        picker.Filter.Text = "tui";
        picker.Done();

        Assert.Empty(picker.Picked!);
    }

    [Fact]
    public void A_chosen_name_the_list_has_not_got_sits_on_top_and_can_be_unmarked()
    {
        using var picker = new Picker("Skills", ["gone"], "(not installed)", "");
        picker.ShowChoices(["a-team"], "");

        Assert.Equal([("gone (not installed)", true), ("a-team", false)], picker.Rows);
        picker.Toggle();
        Assert.Equal([("gone (not installed)", false), ("a-team", false)], picker.Rows);
        picker.Done();
        Assert.Empty(picker.Picked!);
    }

    [Fact]
    public void Esc_cancels_with_nothing_picked()
    {
        using var picker = new Picker("Skills", ["a-team"], "", "");
        picker.Toggle();

        Assert.True(picker.NewKeyDownEvent(Key.Esc));

        Assert.Null(picker.Picked);
    }
}
