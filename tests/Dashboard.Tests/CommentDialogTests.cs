using System.Drawing;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;

namespace ATeam.Dashboard.Tests;

/// <summary>The comment dialog: what you write, Ctrl+Enter to post it as you and Esc to leave it.</summary>
public class CommentDialogTests
{
    private static readonly WaitingItem Item =
        new(180, "The dashboard tells me a pitch needs me", "Pitched", "https://github.com/x/180", "a-team", Pitch: true);

    private static CommentDialog Open(Func<string, Task<string?>>? post = null)
    {
        var dialog = new CommentDialog(Item, post ?? (_ => Task.FromResult<string?>(null)));
        dialog.Layout(new Size(80, 20));
        return dialog;
    }

    [Fact]
    public void It_opens_titled_with_the_item_s_number_on_an_empty_field()
    {
        using var dialog = Open();

        Assert.Equal("Comment on #180", dialog.Title);
        Assert.Equal("", dialog.Field.Text);
        Assert.True(dialog.Field.HasFocus);
        Assert.False(dialog.Field.TabKeyAddsTab);
    }

    [Fact]
    public void The_hints_name_post_and_cancel()
    {
        using var dialog = Open();

        Assert.Equal("Ctrl+Enter post · Esc cancel", dialog.Hints.Says);
    }

    [Fact]
    public void Ctrl_Enter_posts_what_you_wrote()
    {
        var posted = new List<string>();
        using var dialog = Open(body =>
        {
            posted.Add(body);
            return Task.FromResult<string?>(null);
        });
        dialog.Field.Text = "Not yet: shelve it until #150 lands.";

        Assert.True(dialog.NewKeyDownEvent(Key.Enter.WithCtrl));

        Assert.Equal(["Not yet: shelve it until #150 lands."], posted);
        Assert.True(dialog.Posted);
    }

    [Fact]
    public void Enter_in_the_field_does_not_post()
    {
        var posted = 0;
        using var dialog = Open(_ => Task.FromResult<string?>(++posted < 0 ? "" : null));
        dialog.Field.Text = "first line";

        dialog.NewKeyDownEvent(Key.Enter);

        Assert.Equal(0, posted);
        Assert.False(dialog.Posted);
    }

    [Fact]
    public void Esc_cancels_and_posts_nothing()
    {
        var posted = 0;
        using var dialog = Open(_ => Task.FromResult<string?>(++posted < 0 ? "" : null));
        dialog.Field.Text = "never mind";

        Assert.True(dialog.NewKeyDownEvent(Key.Esc));

        Assert.Equal(0, posted);
        Assert.False(dialog.Posted);
    }

    [Fact]
    public void An_empty_comment_posts_nothing_and_says_so()
    {
        var posted = 0;
        using var dialog = Open(_ => Task.FromResult<string?>(++posted < 0 ? "" : null));
        dialog.Field.Text = "  \n ";

        Assert.True(dialog.NewKeyDownEvent(Key.Enter.WithCtrl));

        Assert.Equal(0, posted);
        Assert.False(dialog.Posted);
        Assert.Equal("Nothing to post: the comment is empty", dialog.Message.Says);
    }

    [Fact]
    public void A_failed_post_keeps_the_dialog_open_with_your_text_and_the_reason_in_red()
    {
        using var dialog = Open(_ => Task.FromResult<string?>("gh: HTTP 502"));
        dialog.Field.Text = "Shelve it, please.";

        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);

        Assert.False(dialog.Posted);
        Assert.Equal("Shelve it, please.", dialog.Field.Text);
        Assert.Equal("gh: HTTP 502", dialog.Message.Says);
        Assert.Equal(SchemeManager.SchemesToSchemeName(Schemes.Error), dialog.Message.SchemeName);
    }

    [Fact]
    public void A_post_that_throws_is_a_failure_too()
    {
        using var dialog = Open(_ => Task.FromException<string?>(new InvalidOperationException("no network")));
        dialog.Field.Text = "Shelve it, please.";

        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);

        Assert.False(dialog.Posted);
        Assert.Equal("no network", dialog.Message.Says);
    }

    [Fact]
    public void Clicking_the_post_hint_posts()
    {
        var posted = new List<string>();
        using var dialog = Open(body =>
        {
            posted.Add(body);
            return Task.FromResult<string?>(null);
        });
        dialog.Field.Text = "Yes, but later.";

        dialog.Hints.Hints.Single(hint => hint.Text == "Ctrl+Enter post").InvokeCommand(Command.Accept);

        Assert.Equal(["Yes, but later."], posted);
    }
}
