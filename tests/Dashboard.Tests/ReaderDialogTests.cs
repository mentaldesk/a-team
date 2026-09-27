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

        Assert.Equal("Up/Down/PgUp/PgDn scroll · o on GitHub · Esc close", dialog.Hints.Says);
        Assert.Equal(dialog.Viewport.Height - 1, dialog.Hints.Frame.Y);
    }

    [Fact]
    public void A_pitch_to_approve_puts_a_between_scrolling_and_GitHub()
    {
        using var dialog = Open(Pitch, onApprove: () => { });

        Assert.Equal("Up/Down/PgUp/PgDn scroll · a approve · o on GitHub · Esc close", dialog.Hints.Says);
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

        dialog.Hints.Hints.Single(hint => hint.Text == "o on GitHub").InvokeCommand(Command.Accept);

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
    public void o_opens_the_item_on_GitHub()
    {
        var opened = 0;
        using var dialog = Open(Pitch, () => opened++);

        Assert.True(dialog.NewKeyDownEvent(new Key('o')));

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
