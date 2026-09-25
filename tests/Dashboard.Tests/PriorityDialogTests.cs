using System.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace ATeam.Dashboard.Tests;

/// <summary>The one write the app makes: which rank it opens on, what Enter and Esc do with it, and what it
/// says while a queue is still going.</summary>
public class PriorityDialogTests
{
    private static WaitingItem Idea(string priority = "", int number = 136) =>
        new(number, "The agents can't say what they'd change", "Idea", $"https://github.com/x/{number}", "a-team",
            "you", "waiting to be ranked", Priority: priority);

    private static PriorityDialog Asking(WaitingItem item, int left = 1)
    {
        var dialog = new PriorityDialog();
        dialog.Ask(item, left);
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
    public void The_options_are_the_fields_four_and_a_fifth_that_clears_it()
    {
        using var dialog = Asking(Idea());

        Assert.Equal(["Urgent", "High", "Medium", "Low", "None"], dialog.Ranks.Labels);
        Assert.Equal(Orientation.Vertical, dialog.Ranks.Orientation);
    }

    [Fact]
    public void Each_option_wears_the_colour_that_ranks_number_already_wears_and_None_wears_none()
    {
        using var dialog = Asking(Idea());

        Assert.Equal(
            ["Priority.Urgent", "Priority.High", "Priority.Medium", "Priority.Low", null],
            dialog.Ranks.SubViews.Select(row => row.SchemeName));
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
    public void The_title_names_the_item_it_would_rank()
    {
        using var dialog = Asking(Idea());

        Assert.Equal("Priority", dialog.Title);
        Assert.Equal("#136", dialog.Number);
    }

    [Fact]
    public void A_queue_still_going_counts_what_is_left_and_offers_to_stop_rather_than_cancel()
    {
        using var dialog = Asking(Idea(), left: 3);

        Assert.Equal("Priority · 3 left", dialog.Title);
        Assert.Equal("Enter set · Esc done", dialog.Hints);
    }

    [Fact]
    public void The_last_one_drops_the_count_and_Esc_is_a_cancel_again()
    {
        using var dialog = Asking(Idea(), left: 1);

        Assert.Equal("Priority", dialog.Title);
        Assert.Equal("Enter set · Esc cancel", dialog.Hints);
    }

    [Fact]
    public void Moving_on_renames_it_and_puts_the_keyboard_back_on_the_new_item_s_own_rank()
    {
        using var dialog = Asking(Idea(), left: 2);
        dialog.Ranks.FocusedItem = (int)Rank.High;

        dialog.Ask(Idea("Low", 139), left: 1);

        Assert.Equal("#139", dialog.Number);
        Assert.Equal(Rank.Low, dialog.Ranks.Value);
        Assert.Equal((int)Rank.Low, dialog.Ranks.FocusedItem);
        Assert.True(dialog.Ranks.HasFocus);
    }

    [Fact]
    public void It_stays_inside_a_small_terminal()
    {
        using var host = new View { Width = 16, Height = 6 };
        using var dialog = Asking(Idea(), left: 17);
        host.Add(dialog);

        host.Layout(new Size(16, 6));

        Assert.True(host.Viewport.Contains(dialog.Frame), $"{dialog.Frame} overhangs {host.Viewport}");
    }
}
