using System.Drawing;
using System.Text;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace ATeam.Dashboard.Tests;

public class AskFixDialogTests
{
    private static AskFixDialog Open(params string[] points)
    {
        var dialog = new AskFixDialog(605, points);
        dialog.Layout(new Size(80, 30));
        return dialog;
    }

    private static void Tick(AskFixDialog dialog, int point) => dialog.Boxes[point].Value = CheckState.Checked;

    [Fact]
    public void One_box_per_point_a_note_and_Ask_and_Cancel()
    {
        using var dialog = Open("Rename it.", "Split it.");

        Assert.Equal("Ask the Dev to fix #605", dialog.Title);
        Assert.Equal(["Rename it.", "Split it."], dialog.Boxes.Select(box => box.Text));
        Assert.Contains(dialog.SubViews.OfType<Label>(), label => label.Text == "Note:");
        Assert.Equal(["Ctrl+Enter Ask", "Esc Cancel"], dialog.ButtonLabels);
    }

    [Fact]
    public void Ask_is_disabled_until_a_box_is_ticked_and_again_once_none_is()
    {
        using var dialog = Open("Rename it.", "Split it.");
        Assert.False(dialog.Foot.Shown[0].Enabled);
        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);
        Assert.Null(dialog.Chosen);

        Tick(dialog, 1);
        Assert.True(dialog.Foot.Shown[0].Enabled);

        dialog.Boxes[1].Value = CheckState.UnChecked;
        Assert.False(dialog.Foot.Shown[0].Enabled);
    }

    [Fact]
    public void Ask_quotes_only_what_s_ticked_with_the_note()
    {
        using var dialog = Open("Rename it.", "Split it.", "Move it.");
        Tick(dialog, 0);
        Tick(dialog, 2);
        dialog.Note.Text = "keep the old name";

        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);

        Assert.Equal("> Rename it.\n\n> Move it.\n\nPlease fix these. keep the old name", dialog.Chosen);
    }

    [Fact]
    public void Enter_never_asks()
    {
        using var dialog = Open("Rename it.");
        Tick(dialog, 0);

        dialog.NewKeyDownEvent(Key.Enter);

        Assert.Null(dialog.Chosen);
    }

    [Fact]
    public void Esc_cancels()
    {
        using var dialog = Open("Rename it.");
        Tick(dialog, 0);

        Assert.True(dialog.NewKeyDownEvent(Key.Esc));

        Assert.Null(dialog.Chosen);
    }

    [Fact]
    public void A_point_over_several_lines_is_one_box_wrapped_under_its_text()
    {
        using var dialog = Open("The merge check\ncould share " + string.Join(' ', Enumerable.Repeat("is_merged()", 12)));
        dialog.Layout(new Size(80, 30));

        var lines = dialog.Boxes.Single().Text.Split('\n');
        Assert.True(lines.Length > 1);
        Assert.StartsWith("The merge check could share", lines[0]);
        Assert.All(lines.Skip(1), line => Assert.StartsWith("  is_merged()", line));
        Assert.All(lines, line => Assert.True(line.Length <= dialog.Viewport.Width - 2));
        Assert.Equal(lines.Length, dialog.Boxes.Single().Frame.Height);
    }

    [Fact]
    public void An_underscore_in_a_point_is_no_hot_key()
    {
        using var dialog = Open("Share is_merged() with accept.");

        Assert.Equal((Rune)0xffff, dialog.Boxes.Single().HotKeySpecifier);
        Assert.Equal(Key.Empty, dialog.Boxes.Single().HotKey);
    }

    [Fact]
    public void Points_that_don_t_fit_scroll_and_a_box_given_focus_scrolls_into_view()
    {
        using var screen = new View { Width = 80, Height = 20 };
        var dialog = new AskFixDialog(605, [.. Enumerable.Range(1, 40).Select(n => $"Point {n}.")]);
        screen.Add(dialog);
        screen.Layout(new Size(80, 20));
        screen.Layout(new Size(80, 20));
        Assert.True(dialog.Frame.Height <= 20);

        dialog.Boxes[30].SetFocus();
        screen.Layout(new Size(80, 20));

        var list = dialog.Boxes[30].SuperView!;
        Assert.True(list.Viewport.Height < 40);
        Assert.InRange(dialog.Boxes[30].Frame.Y, list.Viewport.Y, list.Viewport.Bottom - 1);
    }

    [Theory]
    [InlineData("short", 10, new[] { "short" })]
    [InlineData("one two three", 7, new[] { "one two", "three" })]
    [InlineData("averyverylongword", 5, new[] { "avery", "veryl", "ongwo", "rd" })]
    [InlineData("", 5, new[] { "" })]
    public void Lines_wrap_at_spaces_and_break_only_what_won_t_fit(string text, int width, string[] lines) =>
        Assert.Equal(lines, AskFixDialog.Lines(text, width));
}
