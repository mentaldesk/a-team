using System.Drawing;
using Terminal.Gui.Input;

namespace ATeam.Dashboard.Tests;

/// <summary>The New idea dialog: its fields, what Ctrl+Enter adds, and what Esc throws away.</summary>
public class NewIdeaDialogTests
{
    private static NewIdeaDialog Open(
        Func<string[], Task<Reading>>? board = null, Func<bool>? confirmDiscard = null, string team = "team1")
    {
        var dialog = new NewIdeaDialog(["team0", "team1"], team,
            new IdeaFiler(board ?? (args => Task.FromResult(new Reading(args[2] == "new" ? "77" : "", null)))), confirmDiscard);
        dialog.Layout(new Size(80, 30));
        return dialog;
    }

    [Fact]
    public void It_opens_on_the_team_it_was_given_with_every_team_to_pick_from_and_the_title_to_type()
    {
        using var dialog = Open();

        Assert.Equal("New idea", dialog.Title);
        Assert.Equal("team1", dialog.Team.Text);
        Assert.Equal(["team0", "team1"], dialog.Team.Source!.ToList().Cast<string>());
        Assert.True(dialog.IdeaTitle.HasFocus);
        Assert.True(dialog.Description.WordWrap);
    }

    [Fact]
    public void Priority_starts_on_None_and_says_what_None_does()
    {
        using var dialog = Open();

        Assert.Equal(["None", "Low", "Medium", "High", "Urgent"], dialog.Ranks.Labels);
        Assert.Equal(Rank.None, dialog.Ranks.Value);
        Assert.Contains(dialog.SubViews, view => view.Text == "None leaves it in Triage to rank later.");
    }

    [Fact]
    public void Left_and_Right_set_the_rank_with_the_keyboard_on_it()
    {
        using var dialog = Open();
        dialog.Ranks.SetFocus();

        dialog.NewKeyDownEvent(Key.CursorRight);
        dialog.NewKeyDownEvent(Key.CursorRight);

        Assert.Equal(Rank.Medium, dialog.Ranks.Value);
        Assert.Equal((int)Rank.Medium, dialog.Ranks.FocusedItem);
        dialog.NewKeyDownEvent(Key.CursorLeft);
        Assert.Equal(Rank.Low, dialog.Ranks.Value);
    }

    [Fact]
    public void The_hints_are_the_comment_s()
    {
        using var dialog = Open();

        Assert.Equal("Ctrl+Enter add · Esc cancel", dialog.Hints.Says);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_title_adds_nothing_and_says_the_title_is_needed(string title)
    {
        var calls = 0;
        using var dialog = Open(board: _ =>
        {
            calls++;
            return Task.FromResult(new Reading("77", null));
        });
        dialog.IdeaTitle.Text = title;
        dialog.Description.Text = "Some words";

        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);

        Assert.Equal(0, calls);
        Assert.Null(dialog.Added);
        Assert.Equal("Nothing to add: the title is needed", dialog.Message.Says);
    }

    [Fact]
    public void Ctrl_Enter_adds_what_the_dialog_holds_and_closes_with_the_idea()
    {
        var calls = new List<string[]>();
        using var dialog = Open(board: args =>
        {
            calls.Add(args);
            return Task.FromResult(new Reading(args[2] == "new" ? "77" : "", null));
        });
        dialog.IdeaTitle.Text = "Remember my lane";
        dialog.Ranks.Value = Rank.High;

        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);

        Assert.Equal(("team1", 77), dialog.Added);
        Assert.Equal(["new", "add", "priority"], calls.Select(call => call[2]));
        Assert.All(calls, call => Assert.Equal("team1", call[1]));
    }

    [Fact]
    public void Enter_alone_adds_nothing()
    {
        var calls = 0;
        using var dialog = Open(board: _ =>
        {
            calls++;
            return Task.FromResult(new Reading("77", null));
        });
        dialog.IdeaTitle.Text = "Remember my lane";

        dialog.NewKeyDownEvent(Key.Enter);

        Assert.Equal(0, calls);
        Assert.Null(dialog.Added);
    }

    [Fact]
    public void A_step_that_fails_keeps_the_text_names_the_step_and_holds_the_team_once_the_issue_is_open()
    {
        using var dialog = Open(board: args => Task.FromResult(args[2] == "add"
            ? new Reading("", "gh: Not Found (HTTP 404)")
            : new Reading("77", null)));
        dialog.IdeaTitle.Text = "Remember my lane";
        dialog.Description.Text = "Every morning.";

        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);

        Assert.Null(dialog.Added);
        Assert.Equal("#77 is open, but couldn't go on team1's board: gh: Not Found (HTTP 404)", dialog.Message.Says);
        Assert.Equal("Remember my lane", dialog.IdeaTitle.Text);
        Assert.Equal("Every morning.", dialog.Description.Text);
        Assert.False(dialog.Team.Enabled);
    }

    [Fact]
    public void A_failure_before_the_issue_opens_leaves_the_team_to_change()
    {
        using var dialog = Open(board: _ => Task.FromResult(new Reading("", "gh: HTTP 502")));
        dialog.IdeaTitle.Text = "Remember my lane";

        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);

        Assert.Equal("couldn't open the issue: gh: HTTP 502", dialog.Message.Says);
        Assert.True(dialog.Team.Enabled);
    }

    [Fact]
    public void Esc_with_nothing_typed_closes_without_asking()
    {
        var asked = 0;
        using var dialog = Open(confirmDiscard: () =>
        {
            asked++;
            return false;
        });

        Assert.True(dialog.NewKeyDownEvent(Key.Esc));

        Assert.Equal(0, asked);
        Assert.Null(dialog.Added);
    }

    [Theory]
    [InlineData("A title", "")]
    [InlineData("", "A description")]
    public void Esc_with_something_typed_asks_before_throwing_it_away(string title, string description)
    {
        var asked = 0;
        using var dialog = Open(confirmDiscard: () =>
        {
            asked++;
            return false;
        });
        dialog.IdeaTitle.Text = title;
        dialog.Description.Text = description;

        dialog.NewKeyDownEvent(Key.Esc);

        Assert.Equal(1, asked);
        Assert.Equal(title, dialog.IdeaTitle.Text);
    }

    [Fact]
    public void Discarding_an_idea_asks_in_its_own_words()
    {
        Assert.Equal("Discard your idea?", DiscardDialog.Idea.Title);
        Assert.Contains("hasn't been added", DiscardDialog.Idea.Says);
    }
}
