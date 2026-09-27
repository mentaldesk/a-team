using System.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.Views;
using Terminal.Gui.ViewBase;

namespace ATeam.Dashboard.Tests;

/// <summary>What the dialog says the item is about, and the one write the app makes: which rank it opens on,
/// what Enter and Esc do with it, and what it says while a queue is still going.</summary>
public class PriorityDialogTests
{
    private const string Prose = """
        ## Opportunity

        Switching between two files means finding your place again in both.

        ```
        │ a fenced block │
        ```
        """;

    private static WaitingItem Idea(string priority = "", int number = 136) =>
        new(number, "The agents can't say what they'd change", "Idea", $"https://github.com/x/{number}", "a-team",
            "you", "waiting to be ranked", Priority: priority);

    private static PriorityDialog Asking(WaitingItem item, IssueBody? body = null, int left = 1)
    {
        var dialog = new PriorityDialog();
        dialog.Ask(item, body ?? new IssueBody(), left);
        return dialog;
    }

    private static PriorityDialog Open(IssueBody body, string priority = "", int width = 60, int height = 20)
    {
        var dialog = Asking(Idea(priority), body);
        dialog.Layout(new Size(width, height));
        return dialog;
    }

    [Fact]
    public void It_opens_on_the_item_s_own_rank_with_the_keyboard_already_there()
    {
        using var dialog = Asking(Idea("Medium"));

        Assert.Equal(Rank.Medium, dialog.Ranks.Value);
        Assert.True(dialog.Ranks.HasFocus);
        Assert.Equal((int)Rank.Medium, dialog.Ranks.FocusedItem);
    }

    [Fact]
    public void An_item_with_no_rank_opens_on_None()
    {
        using var dialog = Asking(Idea());

        Assert.Equal(Rank.None, dialog.Ranks.Value);
        Assert.Equal((int)Rank.None, dialog.Ranks.FocusedItem);
    }

    [Fact]
    public void The_options_run_from_the_one_that_clears_the_field_up_to_the_most_urgent()
    {
        using var dialog = Asking(Idea());

        Assert.Equal(["None", "Low", "Medium", "High", "Urgent"], dialog.Ranks.Labels);
        Assert.Equal(Orientation.Horizontal, dialog.Ranks.Orientation);
    }

    [Fact]
    public void Each_option_wears_the_colour_that_ranks_number_already_wears_and_None_wears_none()
    {
        using var dialog = Asking(Idea());

        Assert.Equal(
            [null, "Priority.Low.Form", "Priority.Medium.Form", "Priority.High.Form", "Priority.Urgent.Form"],
            dialog.Ranks.SubViews.Select(row => row.SchemeName));
    }

    [Fact]
    public void Each_option_answers_to_the_letter_its_name_starts_with()
    {
        using var dialog = Asking(Idea());

        Assert.Equal(["_None", "_Low", "_Medium", "_High", "_Urgent"],
            dialog.Ranks.SubViews.Select(row => row.Title));
        Assert.Equal([Key.N, Key.L, Key.M, Key.H, Key.U], dialog.Ranks.SubViews.Select(row => row.HotKey));
    }

    [Theory]
    [InlineData("n", Rank.None)]
    [InlineData("l", Rank.Low)]
    [InlineData("m", Rank.Medium)]
    [InlineData("h", Rank.High)]
    [InlineData("u", Rank.Urgent)]
    public void That_letter_puts_the_keyboard_on_its_rank_without_setting_it(string key, Rank rank)
    {
        using var dialog = Open(new IssueBody(Prose), "Medium", height: 12);

        Assert.True(dialog.NewKeyDownEvent(new Key(key)));

        Assert.Equal(rank, dialog.Ranks.Value);
        Assert.Equal((int)rank, dialog.Ranks.FocusedItem);
    }

    [Fact]
    public void Enter_says_which_rank_the_keyboard_is_on_and_leaves_the_dialog_up()
    {
        var chosen = new List<Rank>();
        var dismissed = 0;
        using var dialog = Asking(Idea("Medium"));
        dialog.Set += chosen.Add;
        dialog.Dismissed += () => dismissed++;

        dialog.Ranks.FocusedItem = (int)Rank.High;
        dialog.Ranks.NewKeyDownEvent(Key.Enter);

        Assert.Equal([Rank.High], chosen);
        Assert.Equal(0, dismissed);
    }

    [Fact]
    public void Esc_dismisses_it_and_sets_nothing()
    {
        var chosen = 0;
        var dismissed = 0;
        using var dialog = Asking(Idea("Medium"));
        dialog.Set += _ => chosen++;
        dialog.Dismissed += () => dismissed++;

        dialog.Ranks.FocusedItem = (int)Rank.High;
        Assert.True(dialog.NewKeyDownEvent(Key.Esc));

        Assert.Equal(0, chosen);
        Assert.Equal(1, dismissed);
    }

    [Fact]
    public void The_title_carries_the_number_and_the_title_so_no_row_has_to()
    {
        using var dialog = Asking(Idea());

        Assert.Equal("#136  The agents can't say what they'd change", dialog.Title);
        Assert.DoesNotContain(dialog.SubViews, view => view.Text == "#136");
    }

    [Fact]
    public void A_queue_still_going_counts_what_is_left_and_offers_to_stop_rather_than_cancel()
    {
        using var dialog = Asking(Idea(), left: 3);

        Assert.Equal("#136  The agents can't say what they'd change · 3 left", dialog.Title);
        Assert.Equal("PgUp/PgDn scroll · Enter set · Esc done", dialog.Hints.Says);
    }

    [Fact]
    public void The_last_one_drops_the_count_and_Esc_is_a_cancel_again()
    {
        using var dialog = Asking(Idea(), left: 1);

        Assert.Equal("#136  The agents can't say what they'd change", dialog.Title);
        Assert.Equal("PgUp/PgDn scroll · Enter set · Esc cancel", dialog.Hints.Says);
    }

    [Fact]
    public void Moving_on_renames_it_and_puts_the_keyboard_back_on_the_new_item_s_own_rank()
    {
        using var dialog = Asking(Idea(), new IssueBody(Prose), left: 2);
        dialog.Ranks.FocusedItem = (int)Rank.High;

        dialog.Ask(Idea("Low", 139), new IssueBody("Another one."), left: 1);

        Assert.StartsWith("#139", dialog.Title);
        Assert.Equal(["Another one."], dialog.Body.Lines.Select(line => line.Text));
        Assert.Equal(Rank.Low, dialog.Ranks.Value);
        Assert.Equal((int)Rank.Low, dialog.Ranks.FocusedItem);
        Assert.True(dialog.Ranks.HasFocus);
    }

    [Fact]
    public void While_a_write_is_going_it_says_so_and_the_ranks_go_dead_with_the_set_hint()
    {
        using var dialog = Asking(Idea(), left: 3);

        dialog.Busy("Setting…");

        Assert.Equal("Setting…", dialog.Message.Says);
        Assert.False(dialog.Ranks.Enabled);
        Assert.Equal("PgUp/PgDn scroll · Esc done", dialog.Hints.Says);
    }

    [Fact]
    public void Enter_while_a_write_is_going_sets_nothing_and_Esc_still_stops_it()
    {
        var chosen = 0;
        var dismissed = 0;
        using var dialog = Asking(Idea(), left: 3);
        dialog.Set += _ => chosen++;
        dialog.Dismissed += () => dismissed++;

        dialog.Busy("Setting…");
        dialog.Ranks.NewKeyDownEvent(Key.Enter);
        Assert.Equal(0, chosen);

        Assert.True(dialog.NewKeyDownEvent(Key.Esc));
        Assert.Equal(1, dismissed);
    }

    [Fact]
    public void Moving_on_gives_the_ranks_and_the_set_hint_back()
    {
        using var dialog = Asking(Idea(), left: 3);
        dialog.Busy("Reading #139…");

        dialog.Ask(Idea("Low", 139), new IssueBody(Prose), left: 2);

        Assert.Equal("", dialog.Message.Says);
        Assert.True(dialog.Ranks.Enabled);
        Assert.Equal("PgUp/PgDn scroll · Enter set · Esc done", dialog.Hints.Says);
    }

    [Fact]
    public void Moving_on_from_a_body_that_would_not_read_takes_its_message_away()
    {
        using var dialog = Asking(Idea(), new IssueBody(Failure: "board.sh: can't read #136"), left: 2);

        dialog.Ask(Idea("Low", 139), new IssueBody(Prose), left: 1);

        Assert.Equal("", dialog.Message.Says);
    }

    [Fact]
    public void Moving_on_reads_the_new_body_from_its_top_rather_than_where_the_last_one_was_left()
    {
        using var dialog = Open(new IssueBody(Long()), height: 10);

        Assert.True(dialog.NewKeyDownEvent(Key.PageDown));
        Assert.True(dialog.Body.Top > 0);

        dialog.Ask(Idea("Low", 139), new IssueBody(Long()), left: 1);

        Assert.Equal(0, dialog.Body.Top);
    }

    [Fact]
    public void The_body_is_shown_as_written()
    {
        using var dialog = Asking(Idea(), new IssueBody(Prose));

        Assert.Equal(Prose.Split('\n'), dialog.Body.Lines.Select(line => line.Text));
        Assert.All(dialog.Body.Lines, line => Assert.Equal(LogLineKind.Prose, line.Kind));
    }

    [Fact]
    public void A_body_that_came_back_with_carriage_returns_loses_them()
    {
        using var dialog = Asking(Idea(), new IssueBody("## Opportunity\r\n\r\nOne line.\r\n"));

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
    public void A_body_too_long_for_the_pane_gets_a_scroll_bar_and_one_that_fits_does_not()
    {
        using var overflows = Open(new IssueBody(Long()), height: 10);
        using var fits = Open(new IssueBody("One line."), height: 10);

        Assert.True(overflows.Body.VerticalScrollBar.Visible);
        Assert.False(fits.Body.VerticalScrollBar.Visible);
    }

    [Fact]
    public void The_bar_says_how_far_down_the_body_you_are_and_dragging_it_takes_you_there()
    {
        using var dialog = Open(new IssueBody(Long()), height: 10);
        var bar = dialog.Body.VerticalScrollBar;

        Assert.Equal(0, bar.Value);

        Assert.True(dialog.NewKeyDownEvent(Key.PageDown));
        Assert.Equal(dialog.Body.Top, bar.Value);
        Assert.True(bar.Value > 0);

        bar.Value = 0;
        Assert.Equal(0, dialog.Body.Top);
    }

    [Fact]
    public void Left_and_Right_move_between_the_ranks_and_leave_the_pane_where_it_was()
    {
        using var dialog = Open(new IssueBody(Long()), "Medium", height: 10);

        dialog.Ranks.FocusedItem = (int)Rank.Medium;

        dialog.NewKeyDownEvent(Key.CursorRight);

        Assert.Equal((int)Rank.High, dialog.Ranks.FocusedItem);

        dialog.NewKeyDownEvent(Key.CursorLeft);

        Assert.Equal((int)Rank.Medium, dialog.Ranks.FocusedItem);
        Assert.Equal(0, dialog.Body.Top);
    }

    [Fact]
    public void The_hints_name_the_three_keys_with_cancel_last_in_the_window_s_own_band()
    {
        using var dialog = Asking(Idea());

        Assert.Equal("PgUp/PgDn scroll · Enter set · Esc cancel", dialog.Hints.Says);
        Assert.Equal(StatusBar.Scheme, dialog.Hints.SchemeName);
    }

    [Fact]
    public void Nothing_to_say_means_no_message_row_and_the_hints_on_the_last_one()
    {
        using var dialog = Open(new IssueBody(Prose), height: 12);

        Assert.Equal(0, dialog.Message.Lines);
        Assert.Equal(dialog.Viewport.Height - 1, dialog.Hints.Frame.Y);
    }

    [Fact]
    public void A_body_that_would_not_read_still_lets_you_rank_and_says_why_below_the_hints()
    {
        using var dialog = Open(new IssueBody(Failure: "board.sh: can't read #136 (gh: Not Found (HTTP 404))"),
            height: 12);

        Assert.DoesNotContain(dialog.Body.Lines, line => line.Text.Length > 0);
        Assert.Equal("board.sh: can't read #136 (gh: Not Found (HTTP 404))", dialog.Message.Says);
        Assert.Equal(dialog.Viewport.Height - 1, dialog.Message.Frame.Y);
        Assert.Equal(dialog.Viewport.Height - 2, dialog.Hints.Frame.Y);

        var chosen = new List<Rank>();
        dialog.Set += chosen.Add;
        dialog.Ranks.FocusedItem = (int)Rank.Low;
        dialog.Ranks.NewKeyDownEvent(Key.Enter);

        Assert.Equal([Rank.Low], chosen);
    }

    [Fact]
    public void The_body_fills_the_screen_above_the_band_the_ranks_sit_in()
    {
        using var dialog = Open(new IssueBody(Prose), width: 60, height: 20);

        Assert.Equal(new Rectangle(0, 0, 60, 20), dialog.Frame);
        Assert.Equal(dialog.Body.Frame.Bottom, dialog.Band.Frame.Y);
        Assert.Equal(dialog.Hints.Frame.Y, dialog.Band.Frame.Bottom);
    }

    [Fact]
    public void The_ranks_sit_in_a_band_of_their_own_a_blank_row_above_and_below_them()
    {
        using var dialog = Open(new IssueBody(Prose), width: 60, height: 20);

        Assert.Equal(LogSchemes.Form, dialog.Band.SchemeName);
        Assert.Equal(new Rectangle(0, dialog.Hints.Frame.Y - 3, dialog.Viewport.Width, 3), dialog.Band.Frame);
        Assert.Equal(1, dialog.Ranks.Frame.Y);
        Assert.Equal(1, dialog.Ranks.Frame.Height);
    }

    [Fact]
    public void The_ranks_are_centred_across_that_band()
    {
        using var dialog = Open(new IssueBody(Prose), width: 60, height: 20);

        Assert.Equal(dialog.Band.Viewport.Width - dialog.Ranks.Frame.Right, dialog.Ranks.Frame.X);
        Assert.True(dialog.Ranks.Frame.X > 0, $"{dialog.Ranks.Frame} is not centred in {dialog.Band.Viewport}");
    }

    [Fact]
    public void It_stays_inside_a_small_terminal()
    {
        using var host = new View { Width = 16, Height = 6 };
        using var dialog = Asking(Idea(), new IssueBody(Prose), left: 17);
        host.Add(dialog);

        host.Layout(new Size(16, 6));

        Assert.True(host.Viewport.Contains(dialog.Frame), $"{dialog.Frame} overhangs {host.Viewport}");
    }

    private static string Long() =>
        string.Join('\n', Enumerable.Range(1, 60).Select(line => $"line {line}"));
}
