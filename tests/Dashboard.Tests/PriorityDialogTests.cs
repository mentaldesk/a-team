using System.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.Views;
using Terminal.Gui.ViewBase;

namespace ATeam.Dashboard.Tests;

/// <summary>What the dialog says the item is about, and the one write the app makes: which rank it opens on,
/// and what Enter and Esc do with it.</summary>
public class PriorityDialogTests
{
    private const string Prose = """
        ## Opportunity

        Switching between two files means finding your place again in both.

        ```
        │ a fenced block │
        ```
        """;

    private static WaitingItem Idea(string priority = "") =>
        new(136, "The agents can't say what they'd change", "Idea", "https://github.com/x/136", "a-team",
            "you", "waiting to be ranked", Priority: priority);

    private static PriorityDialog Open(IssueBody body, string priority = "", int width = 60, int height = 20)
    {
        var dialog = new PriorityDialog(Idea(priority), body);
        dialog.Layout(new Size(width, height));
        return dialog;
    }

    [Fact]
    public void It_opens_on_the_item_s_own_rank_with_the_keyboard_already_there()
    {
        using var dialog = new PriorityDialog(Idea("Medium"), new IssueBody());

        Assert.Equal(Rank.Medium, dialog.Ranks.Value);
        Assert.True(dialog.Ranks.HasFocus);
        Assert.Equal((int)Rank.Medium, dialog.Ranks.FocusedItem);
        Assert.Null(dialog.Chosen);
    }

    [Fact]
    public void An_item_with_no_rank_opens_on_None()
    {
        using var dialog = new PriorityDialog(Idea(), new IssueBody());

        Assert.Equal(Rank.None, dialog.Ranks.Value);
        Assert.Equal((int)Rank.None, dialog.Ranks.FocusedItem);
    }

    [Fact]
    public void The_options_are_the_fields_four_and_a_fifth_that_clears_it()
    {
        using var dialog = new PriorityDialog(Idea(), new IssueBody());

        Assert.Equal(["Urgent", "High", "Medium", "Low", "None"], dialog.Ranks.Labels);
        Assert.Equal(Orientation.Vertical, dialog.Ranks.Orientation);
    }

    [Fact]
    public void Each_option_wears_the_colour_that_ranks_number_already_wears_and_None_wears_none()
    {
        using var dialog = new PriorityDialog(Idea(), new IssueBody());

        Assert.Equal(
            ["Priority.Urgent", "Priority.High", "Priority.Medium", "Priority.Low", null],
            dialog.Ranks.SubViews.Select(row => row.SchemeName));
    }

    [Fact]
    public void Enter_sets_the_rank_the_keyboard_is_on_and_closes()
    {
        using var dialog = new PriorityDialog(Idea("Medium"), new IssueBody());

        dialog.Ranks.FocusedItem = (int)Rank.High;
        dialog.Ranks.NewKeyDownEvent(Key.Enter);

        Assert.Equal(Rank.High, dialog.Chosen);
    }

    [Fact]
    public void Esc_closes_it_and_sets_nothing()
    {
        using var dialog = new PriorityDialog(Idea("Medium"), new IssueBody());

        dialog.Ranks.FocusedItem = (int)Rank.High;
        Assert.True(dialog.NewKeyDownEvent(Key.Esc));

        Assert.Null(dialog.Chosen);
    }

    [Fact]
    public void The_title_carries_the_number_and_the_title_so_no_row_has_to()
    {
        using var dialog = new PriorityDialog(Idea(), new IssueBody());

        Assert.Equal("#136  The agents can't say what they'd change", dialog.Title);
        Assert.DoesNotContain(dialog.SubViews, view => view.Text == "#136");
    }

    [Fact]
    public void The_body_is_shown_as_written()
    {
        using var dialog = new PriorityDialog(Idea(), new IssueBody(Prose));

        Assert.Equal(Prose.Split('\n'), dialog.Body.Lines.Select(line => line.Text));
        Assert.All(dialog.Body.Lines, line => Assert.Equal(LogLineKind.Prose, line.Kind));
    }

    [Fact]
    public void A_body_that_came_back_with_carriage_returns_loses_them()
    {
        using var dialog = new PriorityDialog(Idea(), new IssueBody("## Opportunity\r\n\r\nOne line.\r\n"));

        Assert.Equal(["## Opportunity", "", "One line.", ""], dialog.Body.Lines.Select(line => line.Text));
    }

    [Fact]
    public void It_opens_at_the_top_of_the_body_rather_than_following_its_end()
    {
        using var dialog = Open(new IssueBody(Long()), height: 10);

        Assert.False(dialog.Body.Following);
        Assert.Equal(0, dialog.Body.Top);
    }

    [Fact]
    public void PgDn_and_PgUp_scroll_the_pane_and_Home_and_End_jump_it()
    {
        using var dialog = Open(new IssueBody(Long()), height: 10);

        Assert.True(dialog.NewKeyDownEvent(Key.PageDown));
        var down = dialog.Body.Top;
        Assert.True(down > 0);

        Assert.True(dialog.NewKeyDownEvent(Key.PageUp));
        Assert.Equal(0, dialog.Body.Top);

        Assert.True(dialog.NewKeyDownEvent(Key.End));
        Assert.True(dialog.Body.Top > down);
        var end = dialog.Body.Top;

        Assert.True(dialog.NewKeyDownEvent(Key.Home));
        Assert.Equal(0, dialog.Body.Top);
        Assert.True(end > 0);
    }

    [Fact]
    public void Up_and_Down_still_move_between_the_ranks_and_leave_the_pane_where_it_was()
    {
        using var dialog = Open(new IssueBody(Long()), "Urgent", height: 10);

        dialog.Ranks.FocusedItem = (int)Rank.Urgent;

        dialog.NewKeyDownEvent(Key.CursorDown);

        Assert.Equal((int)Rank.High, dialog.Ranks.FocusedItem);
        Assert.Equal(0, dialog.Body.Top);
    }

    [Fact]
    public void The_hints_name_the_three_keys_with_cancel_last()
    {
        using var dialog = new PriorityDialog(Idea(), new IssueBody());

        Assert.Equal(
            ["PgUp/PgDn scroll", " · ", "Enter set", " · ", "Esc cancel"],
            dialog.SubViews.Where(view => view is Button or Label && view != dialog.Message)
                .Select(view => view.Text));
    }

    [Fact]
    public void Nothing_to_say_means_no_message_row_and_the_hints_on_the_last_one()
    {
        using var dialog = Open(new IssueBody(Prose), height: 12);

        Assert.Equal(0, dialog.Message.Lines);
        Assert.Equal(
            dialog.Viewport.Height - 1,
            dialog.SubViews.Single(view => view.Text == "Esc cancel").Frame.Y);
    }

    [Fact]
    public void A_body_that_would_not_read_still_lets_you_rank_and_says_why_below_the_hints()
    {
        using var dialog = Open(new IssueBody(Failure: "board.sh: can't read #136 (gh: Not Found (HTTP 404))"),
            height: 12);

        Assert.DoesNotContain(dialog.Body.Lines, line => line.Text.Length > 0);
        Assert.Equal("board.sh: can't read #136 (gh: Not Found (HTTP 404))", dialog.Message.Says);
        Assert.Equal(dialog.Viewport.Height - 1, dialog.Message.Frame.Y);
        Assert.Equal(dialog.Viewport.Height - 2, dialog.SubViews.Single(view => view.Text == "Esc cancel").Frame.Y);

        dialog.Ranks.FocusedItem = (int)Rank.Low;
        dialog.Ranks.NewKeyDownEvent(Key.Enter);

        Assert.Equal(Rank.Low, dialog.Chosen);
    }

    [Fact]
    public void The_body_fills_the_screen_above_the_ranks()
    {
        using var dialog = Open(new IssueBody(Prose), width: 60, height: 20);

        Assert.Equal(new Rectangle(0, 0, 60, 20), dialog.Frame);
        Assert.Equal(dialog.Body.Frame.Bottom, dialog.Ranks.Frame.Y);
        Assert.Equal(Enum.GetValues<Rank>().Length, dialog.Ranks.Frame.Height);
    }

    [Fact]
    public void It_stays_inside_a_small_terminal()
    {
        using var host = new View { Width = 16, Height = 6 };
        using var dialog = new PriorityDialog(Idea(), new IssueBody(Prose));
        host.Add(dialog);

        host.Layout(new Size(16, 6));

        Assert.True(host.Viewport.Contains(dialog.Frame), $"{dialog.Frame} overhangs {host.Viewport}");
    }

    private static string Long() =>
        string.Join('\n', Enumerable.Range(1, 60).Select(line => $"line {line}"));
}
