using System.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace ATeam.Dashboard.Tests;

/// <summary>The one write the app makes: which rank it opens on, and what Enter and Esc do with it.</summary>
public class PriorityDialogTests
{
    private static WaitingItem Idea(string priority = "") =>
        new(136, "The agents can't say what they'd change", "Idea", "https://github.com/x/136", "a-team",
            "you", "waiting to be ranked", Priority: priority);

    [Fact]
    public void It_opens_on_the_item_s_own_rank_with_the_keyboard_already_there()
    {
        using var dialog = new PriorityDialog(Idea("Medium"));

        Assert.Equal(Rank.Medium, dialog.Ranks.Value);
        Assert.True(dialog.Ranks.HasFocus);
        Assert.Equal((int)Rank.Medium, dialog.Ranks.FocusedItem);
        Assert.Null(dialog.Chosen);
    }

    [Fact]
    public void An_item_with_no_rank_opens_on_None()
    {
        using var dialog = new PriorityDialog(Idea());

        Assert.Equal(Rank.None, dialog.Ranks.Value);
        Assert.Equal((int)Rank.None, dialog.Ranks.FocusedItem);
    }

    [Fact]
    public void The_options_are_the_fields_four_and_a_fifth_that_clears_it()
    {
        using var dialog = new PriorityDialog(Idea());

        Assert.Equal(["Urgent", "High", "Medium", "Low", "None"], dialog.Ranks.Labels);
        Assert.Equal(Orientation.Vertical, dialog.Ranks.Orientation);
    }

    [Fact]
    public void Each_option_wears_the_colour_that_ranks_number_already_wears_and_None_wears_none()
    {
        using var dialog = new PriorityDialog(Idea());

        Assert.Equal(
            ["Priority.Urgent", "Priority.High", "Priority.Medium", "Priority.Low", null],
            dialog.Ranks.SubViews.Select(row => row.SchemeName));
    }

    [Fact]
    public void Enter_sets_the_rank_the_keyboard_is_on_and_closes()
    {
        using var dialog = new PriorityDialog(Idea("Medium"));

        dialog.Ranks.FocusedItem = (int)Rank.High;
        dialog.Ranks.NewKeyDownEvent(Key.Enter);

        Assert.Equal(Rank.High, dialog.Chosen);
    }

    [Fact]
    public void Esc_closes_it_and_sets_nothing()
    {
        using var dialog = new PriorityDialog(Idea("Medium"));

        dialog.Ranks.FocusedItem = (int)Rank.High;
        Assert.True(dialog.NewKeyDownEvent(Key.Esc));

        Assert.Null(dialog.Chosen);
    }

    [Fact]
    public void The_title_names_the_item_it_would_rank()
    {
        using var dialog = new PriorityDialog(Idea());

        Assert.Equal("Priority", dialog.Title);
        Assert.Contains(dialog.SubViews, view => view.Text == "#136");
    }

    [Fact]
    public void It_stays_inside_a_small_terminal()
    {
        using var host = new View { Width = 16, Height = 6 };
        using var dialog = new PriorityDialog(Idea());
        host.Add(dialog);

        host.Layout(new Size(16, 6));

        Assert.True(host.Viewport.Contains(dialog.Frame), $"{dialog.Frame} overhangs {host.Viewport}");
    }
}
