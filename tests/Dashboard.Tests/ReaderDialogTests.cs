using System.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace ATeam.Dashboard.Tests;

/// <summary>The reader: the body as written, the keys that scroll it, and the two ways out.</summary>
public class ReaderDialogTests
{
    private const string Pitch = """
        ## Opportunity

        Switching between two files means finding your place again in both.

        ```
        ┌─ Work ───────────┐
        │ #180  a pitch    │
        └──────────────────┘
        ```

        | Option | Cost |
        |--------|------|
        | Reader | low  |
        """;

    private static readonly WaitingItem Item =
        new(180, "The dashboard tells me a pitch needs me", "Pitched", "https://github.com/x/180", "a-team",
            "you", "awaiting your approval since 22:25");

    private static ReaderDialog Open(string body, Action? onGitHub = null, int height = 20, Action? onApprove = null)
    {
        var dialog = new ReaderDialog(Item, new IssueBody(body), onGitHub ?? (() => { }), onApprove);
        dialog.Layout(new Size(60, height));
        return dialog;
    }

    [Fact]
    public void A_question_s_title_names_the_task_and_says_it_is_the_Dev_s_question()
    {
        var question = Item with { Status = "Ready", Question = "Which marker?" };
        using var dialog = new ReaderDialog(question, new IssueBody(question.Question), () => { });

        Assert.Equal("#180  The dashboard tells me a pitch needs me · the Dev's question", dialog.Title);
        Assert.Equal(["Which marker?"], dialog.Body.Lines.Select(line => line.Text));
    }

    [Fact]
    public void A_pitch_s_question_is_the_Lead_s()
    {
        var question = Item with { Pitch = true, Question = "## Needs your answer" };
        using var dialog = new ReaderDialog(question, new IssueBody(question.Question), () => { });

        Assert.Equal("#180  The dashboard tells me a pitch needs me · the Lead's question", dialog.Title);
    }

    [Fact]
    public void The_body_is_shown_character_for_character_fences_tables_and_all()
    {
        using var dialog = Open(Pitch);

        Assert.Equal(Pitch.Split('\n'), dialog.Body.Lines.Select(line => line.Text));
    }

    [Fact]
    public void The_body_is_read_as_markdown_on_the_reader_s_own_surface()
    {
        using var dialog = Open(Pitch);

        Assert.True(dialog.Body.ReadsMarkdown);
        Assert.Equal(LogSchemes.Reader, dialog.Body.SchemeName);
        Assert.Equal(LogLineKind.Heading, dialog.Body.Lines[0].Kind);
    }

    [Fact]
    public void The_title_carries_the_number_and_the_title()
    {
        using var dialog = Open(Pitch);

        Assert.Equal("#180  The dashboard tells me a pitch needs me", dialog.Title);
    }

    [Fact]
    public void It_opens_on_the_text_at_the_top()
    {
        using var dialog = Open(Long(), height: 10);

        Assert.True(dialog.Body.HasFocus);
        Assert.False(dialog.Body.Following);
        Assert.Equal(0, dialog.Body.Top);
    }

    [Fact]
    public void The_hints_name_the_reader_s_keys_on_its_last_row()
    {
        using var dialog = Open(Pitch);

        Assert.Equal("Up/Down/PgUp/PgDn scroll · g on GitHub · Esc close", dialog.Hints.Says);
        Assert.Equal(dialog.Viewport.Height - 1, dialog.Hints.Frame.Y);
    }

    [Fact]
    public void A_pitch_to_approve_puts_a_between_scrolling_and_GitHub()
    {
        using var dialog = Open(Pitch, onApprove: () => { });

        Assert.Equal("Up/Down/PgUp/PgDn scroll · a approve · g on GitHub · Esc close", dialog.Hints.Says);
    }

    [Fact]
    public void a_approves_at_once()
    {
        var approved = 0;
        using var dialog = Open(Pitch, onApprove: () => approved++);

        Assert.True(dialog.NewKeyDownEvent(new Key('a')));

        Assert.Equal(1, approved);
    }

    [Fact]
    public void Clicking_the_approve_hint_approves()
    {
        var approved = 0;
        using var dialog = Open(Pitch, onApprove: () => approved++);

        dialog.Hints.Hints.Single(hint => hint.Text == "a approve").InvokeCommand(Command.Accept);

        Assert.Equal(1, approved);
    }

    [Fact]
    public void A_task_to_accept_puts_its_key_after_scrolling()
    {
        using var dialog = new ReaderDialog(Item, new IssueBody(Pitch), () => { },
            accept: new ReaderCommand(new Key('A'), "accept", () => true));
        dialog.Layout(new Size(60, 20));

        Assert.Equal("Up/Down/PgUp/PgDn scroll · A accept · g on GitHub · Esc close", dialog.Hints.Says);
        Assert.True(dialog.Hints.Hints.Single(hint => hint.Text == "A accept").Enabled);
    }

    [Fact]
    public void Accept_on_a_PR_in_trouble_is_greyed_but_its_key_still_says_why()
    {
        var ran = 0;
        using var dialog = new ReaderDialog(Item, new IssueBody(Pitch), () => { },
            accept: new ReaderCommand(new Key('A'), "accept", () => ++ran < 0, Enabled: false));
        dialog.Layout(new Size(60, 20));

        Assert.False(dialog.Hints.Hints.Single(hint => hint.Text == "A accept").Enabled);
        Assert.True(dialog.NewKeyDownEvent(new Key('A')));
        Assert.Equal(1, ran);
    }

    [Fact]
    public void Accept_follows_its_key_not_a()
    {
        var ran = 0;
        using var dialog = new ReaderDialog(Item, new IssueBody(Pitch), () => { },
            accept: new ReaderCommand(new Key('z'), "accept", () => ++ran > 0));

        Assert.False(dialog.NewKeyDownEvent(new Key('A')));
        Assert.True(dialog.NewKeyDownEvent(new Key('z')));
        Assert.Equal(1, ran);
    }

    [Fact]
    public void Comment_sits_after_approve_and_before_GitHub()
    {
        using var dialog = new ReaderDialog(Item, new IssueBody(Pitch), () => { }, () => { },
            comment: new ReaderCommand(new Key('c'), "comment", () => false));
        dialog.Layout(new Size(80, 20));

        Assert.Equal("Up/Down/PgUp/PgDn scroll · a approve · c comment · g on GitHub · Esc close", dialog.Hints.Says);
    }

    [Fact]
    public void Without_a_pitch_to_approve_comment_is_still_offered()
    {
        using var dialog = new ReaderDialog(Item, new IssueBody(Pitch), () => { },
            comment: new ReaderCommand(new Key('c'), "comment", () => false));
        dialog.Layout(new Size(80, 20));

        Assert.Equal("Up/Down/PgUp/PgDn scroll · c comment · g on GitHub · Esc close", dialog.Hints.Says);
    }

    [Fact]
    public void A_posted_comment_leaves_the_reader_open_saying_so()
    {
        using var dialog = new ReaderDialog(Item, new IssueBody(Long()), () => { },
            comment: new ReaderCommand(new Key('c'), "comment", () => true));
        dialog.Layout(new Size(60, 10));
        dialog.NewKeyDownEvent(Key.PageDown);
        var top = dialog.Body.Top;

        Assert.True(dialog.NewKeyDownEvent(new Key('c')));

        Assert.Equal("commented on #180", dialog.Message.Says);
        Assert.Equal(top, dialog.Body.Top);
    }

    [Fact]
    public void A_cancelled_comment_says_nothing()
    {
        var asked = 0;
        using var dialog = new ReaderDialog(Item, new IssueBody(Pitch), () => { },
            comment: new ReaderCommand(new Key('c'), "comment", () => ++asked < 0));

        Assert.True(dialog.NewKeyDownEvent(new Key('c')));

        Assert.Equal(1, asked);
        Assert.Equal("", dialog.Message.Says);
    }

    [Fact]
    public void With_nothing_to_approve_a_does_nothing()
    {
        using var dialog = Open(Pitch);

        Assert.False(dialog.NewKeyDownEvent(new Key('a')));
    }

    [Fact]
    public void Clicking_a_hint_runs_its_key()
    {
        var opened = 0;
        using var dialog = Open(Pitch, () => opened++);

        dialog.Hints.Hints.Single(hint => hint.Text == "g on GitHub").InvokeCommand(Command.Accept);

        Assert.Equal(1, opened);
    }

    [Fact]
    public void Up_and_Down_move_a_line_PgUp_and_PgDn_a_page_and_Home_and_End_jump()
    {
        using var dialog = Open(Long(), height: 10);

        Assert.True(dialog.NewKeyDownEvent(Key.CursorDown));
        Assert.Equal(1, dialog.Body.Top);
        Assert.True(dialog.NewKeyDownEvent(Key.CursorUp));
        Assert.Equal(0, dialog.Body.Top);

        Assert.True(dialog.NewKeyDownEvent(Key.PageDown));
        var page = dialog.Body.Top;
        Assert.True(page > 1);
        Assert.True(dialog.NewKeyDownEvent(Key.PageUp));
        Assert.Equal(0, dialog.Body.Top);

        Assert.True(dialog.NewKeyDownEvent(Key.End));
        Assert.True(dialog.Body.Top > page);
        Assert.True(dialog.NewKeyDownEvent(Key.Home));
        Assert.Equal(0, dialog.Body.Top);
    }

    [Fact]
    public void g_opens_the_item_on_GitHub()
    {
        var opened = 0;
        using var dialog = Open(Pitch, () => opened++);

        Assert.True(dialog.NewKeyDownEvent(new Key('g')));

        Assert.Equal(1, opened);
    }

    [Fact]
    public void Esc_closes_it()
    {
        using var dialog = Open(Pitch);

        Assert.True(dialog.NewKeyDownEvent(Key.Esc));
    }

    private static string Long() =>
        string.Join('\n', Enumerable.Range(1, 60).Select(line => $"line {line}"));
}
