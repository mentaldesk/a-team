using System.Drawing;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
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

    private static readonly Remark Said = new("you", DateTimeOffset.UnixEpoch, "Shelve it until #150 lands.");

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

        Assert.Equal("Up/Down/PgUp/PgDn scroll · h show history · g on GitHub · Esc close", dialog.Hints.Says);
        Assert.Equal(dialog.Viewport.Height - 1, dialog.Hints.Frame.Y);
    }

    [Fact]
    public void A_pitch_to_approve_puts_a_between_scrolling_and_GitHub()
    {
        using var dialog = Open(Pitch, onApprove: () => { });

        Assert.Equal("Up/Down/PgUp/PgDn scroll · h show history · a approve · g on GitHub · Esc close", dialog.Hints.Says);
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

        Assert.Equal("Up/Down/PgUp/PgDn scroll · h show history · A accept · g on GitHub · Esc close", dialog.Hints.Says);
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
            comment: new ReaderComment(new Key('c'), "comment", () => null));
        dialog.Layout(new Size(80, 20));

        Assert.Equal("Up/Down/PgUp/PgDn scroll · h show history · a approve · c comment · g on GitHub · Esc close", dialog.Hints.Says);
    }

    [Fact]
    public void Without_a_pitch_to_approve_comment_is_still_offered()
    {
        using var dialog = new ReaderDialog(Item, new IssueBody(Pitch), () => { },
            comment: new ReaderComment(new Key('c'), "comment", () => null));
        dialog.Layout(new Size(80, 20));

        Assert.Equal("Up/Down/PgUp/PgDn scroll · h show history · c comment · g on GitHub · Esc close", dialog.Hints.Says);
    }

    [Fact]
    public void A_posted_comment_leaves_the_reader_open_saying_so()
    {
        using var dialog = new ReaderDialog(Item, new IssueBody(Long()), () => { },
            comment: new ReaderComment(new Key('c'), "comment", () => Said));
        dialog.Layout(new Size(60, 10));

        Assert.True(dialog.NewKeyDownEvent(new Key('c')));

        Assert.Equal("commented on #180", dialog.Message.Says);
    }

    [Fact]
    public void A_posted_comment_joins_the_end_of_the_conversation_in_view()
    {
        using var dialog = new ReaderDialog(Item, new IssueBody(Long()), () => { },
            comment: new ReaderComment(new Key('c'), "comment", () => Said));
        dialog.Layout(new Size(60, 10));
        var before = dialog.Body.Lines.Count;

        dialog.NewKeyDownEvent(new Key('c'));
        dialog.Layout(new Size(60, 10));

        var added = dialog.Body.Lines.Skip(before).Select(line => line.Text).ToList();
        Assert.Contains(Said.Heading, added);
        Assert.Equal("Shelve it until #150 lands.", added[^1]);
        var top = dialog.Body.Top;
        dialog.NewKeyDownEvent(Key.End);
        dialog.Layout(new Size(60, 10));
        Assert.Equal(dialog.Body.Top, top);
    }

    [Fact]
    public void A_cancelled_comment_says_nothing()
    {
        var asked = 0;
        using var dialog = new ReaderDialog(Item, new IssueBody(Pitch), () => { },
            comment: new ReaderComment(new Key('c'), "comment", () => ++asked < 0 ? Said : null));

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

    [Fact]
    public void A_task_with_a_PR_puts_try_between_scrolling_and_accept()
    {
        using var dialog = new ReaderDialog(Item, new IssueBody(Pitch), () => { },
            accept: new ReaderCommand(new Key('a'), "accept", () => true),
            tryIt: new ReaderTry(new Key('t'), (_, _) => { }));
        dialog.Layout(new Size(60, 20));

        Assert.Equal("Up/Down/PgUp/PgDn scroll · h show history · t try · a accept · g on GitHub · Esc close", dialog.Hints.Says);
    }

    [Fact]
    public void Without_a_PR_t_is_neither_hinted_nor_handled()
    {
        using var dialog = Open(Pitch);

        Assert.DoesNotContain("try", dialog.Hints.Says);
        Assert.False(dialog.NewKeyDownEvent(new Key('t')));
    }

    [Fact]
    public void Try_hands_over_what_the_reader_shows_and_where_it_s_scrolled_to()
    {
        var tried = new List<(IssueBody Body, int Top)>();
        var body = new IssueBody(Long());
        using var dialog = new ReaderDialog(Item, body, () => { },
            tryIt: new ReaderTry(new Key('T'), (shown, top) => tried.Add((shown, top))));
        dialog.Layout(new Size(60, 10));
        dialog.NewKeyDownEvent(Key.PageDown);
        var top = dialog.Body.Top;

        Assert.True(dialog.NewKeyDownEvent(new Key('T')));

        Assert.True(top > 0);
        Assert.Equal([(body, top)], tried);
    }

    [Fact]
    public void Reopened_after_a_try_it_opens_where_it_was_saying_what_went_wrong()
    {
        using var dialog = new ReaderDialog(Item, new IssueBody(Long()), () => { },
            tryIt: new ReaderTry(new Key('t'), (_, _) => { }, Top: 12, Failure: "try team0 122 exited 1"));
        dialog.Layout(new Size(60, 10));

        Assert.Equal(12, dialog.Body.Top);
        Assert.Equal("try team0 122 exited 1", dialog.Message.Says);
        Assert.Equal(SchemeManager.SchemesToSchemeName(Schemes.Error), dialog.Message.SchemeName);
    }

    private static readonly History Recorded = new([
        new HistoryEvent(new(new DateTime(2026, 10, 3, 13, 40, 0, DateTimeKind.Local)), "dev", "In progress → In review"),
        .. Enumerable.Range(1, 40).Select(n =>
            new HistoryEvent(new(new DateTime(2026, 10, 2, 9, 0, 0, DateTimeKind.Local)), "lead", $"commented {n}")),
    ]);

    private static ReaderDialog Wide(ReaderPanes? panes = null, int width = 120)
    {
        var dialog = new ReaderDialog(Item, new IssueBody(Long(), History: Recorded), () => { }, panes: panes,
            width: width);
        dialog.Layout(new Size(width, 20));
        return dialog;
    }

    [Fact]
    public void A_posted_comment_tops_History_without_reopening_the_reader()
    {
        using var dialog = new ReaderDialog(Item, new IssueBody(Long(), History: Recorded), () => { },
            comment: new ReaderComment(new Key('c'), "comment", () => Said), width: 120);
        dialog.Layout(new Size(120, 20));

        dialog.NewKeyDownEvent(new Key('c'));

        Assert.EndsWith("you   commented", dialog.HistoryLog.Lines[0].Text);
        Assert.Equal(Recorded.Events.Count + 1, dialog.HistoryLog.Lines.Count);
    }

    [Fact]
    public void A_posted_comment_leaves_a_History_that_wouldnt_read_saying_so()
    {
        using var dialog = new ReaderDialog(Item,
            new IssueBody(Long(), History: new History([], Failure: "couldn't read #180's history")), () => { },
            comment: new ReaderComment(new Key('c'), "comment", () => Said), width: 120);

        dialog.NewKeyDownEvent(new Key('c'));

        Assert.Equal(["couldn't read #180's history"], dialog.HistoryLog.Lines.Select(line => line.Text));
    }

    [Fact]
    public void A_wide_reader_shows_History_beside_the_body_newest_first()
    {
        using var dialog = Wide();

        Assert.True(dialog.HistoryShown);
        Assert.Equal("3 Oct 13:40  dev   In progress → In review", dialog.HistoryLog.Lines[0].Text);
        Assert.Equal(dialog.Viewport.Width - ReaderPanes.HistoryWidth, dialog.Body.SuperView!.Frame.Width);
        Assert.Equal("Up/Down/PgUp/PgDn scroll · Tab switch pane · h hide history · g on GitHub · Esc close",
            dialog.Hints.Says);
    }

    [Fact]
    public void h_hides_History_for_every_reader_after_and_the_body_takes_the_full_width()
    {
        var panes = new ReaderPanes();
        using (var dialog = Wide(panes))
        {
            Assert.True(dialog.NewKeyDownEvent(new Key('h')));
            dialog.Layout(new Size(120, 20));

            Assert.False(dialog.HistoryShown);
            Assert.Equal(dialog.Viewport.Width, dialog.Body.SuperView!.Frame.Width);
            Assert.Equal("Up/Down/PgUp/PgDn scroll · h show history · g on GitHub · Esc close", dialog.Hints.Says);
        }

        using var next = Wide(panes);
        Assert.False(next.HistoryShown);
        Assert.True(next.NewKeyDownEvent(new Key('h')));
        Assert.True(next.HistoryShown);
        Assert.True(panes.HistoryShown);
    }

    [Fact]
    public void A_narrow_terminal_opens_without_History_and_leaves_the_choice_as_it_was()
    {
        var panes = new ReaderPanes();
        using var dialog = Wide(panes, width: 80);

        Assert.False(dialog.HistoryShown);
        Assert.True(panes.HistoryShown);
    }

    [Fact]
    public void Tab_moves_between_the_panes_and_the_arrows_scroll_the_one_with_focus()
    {
        using var dialog = Wide();

        Assert.True(dialog.NewKeyDownEvent(Key.Tab));
        Assert.True(dialog.HistoryLog.HasFocus);
        dialog.NewKeyDownEvent(Key.CursorDown);
        Assert.Equal(1, dialog.HistoryLog.Top);
        Assert.Equal(0, dialog.Body.Top);

        Assert.True(dialog.NewKeyDownEvent(Key.Tab));
        Assert.True(dialog.Body.HasFocus);
        dialog.NewKeyDownEvent(Key.CursorDown);
        Assert.Equal(1, dialog.Body.Top);
        Assert.Equal(1, dialog.HistoryLog.Top);
    }

    [Fact]
    public void Hiding_History_while_it_has_focus_gives_focus_back_to_the_body()
    {
        using var dialog = Wide();
        dialog.NewKeyDownEvent(Key.Tab);

        dialog.NewKeyDownEvent(new Key('h'));

        Assert.True(dialog.Body.HasFocus);
    }

    private static ReaderDialog Ranking(
        string body = Pitch, Rank rank = Rank.None, int width = 60, int height = 20, Action? onApprove = null,
        ReaderComment? comment = null, Action? onGitHub = null, IssueBody? read = null)
    {
        var dialog = new ReaderDialog(Item, read ?? new IssueBody(body), onGitHub ?? (() => { }), onApprove,
            comment: comment, width: width, rank: rank);
        dialog.Layout(new Size(width, height));
        return dialog;
    }

    [Fact]
    public void Without_a_rank_there_is_no_row_of_ranks()
    {
        using var dialog = Open(Pitch);

        Assert.Null(dialog.Ranks);
        Assert.DoesNotContain("Enter set", dialog.Hints.Says);
        Assert.DoesNotContain("rank", dialog.Hints.Says);
    }

    [Theory]
    [InlineData(Rank.None)]
    [InlineData(Rank.Medium)]
    public void The_ranks_open_on_the_rank_given_with_the_keyboard_already_there(Rank rank)
    {
        using var dialog = Ranking(rank: rank);

        Assert.Equal(rank, dialog.Ranks!.Value);
        Assert.True(dialog.Ranks.HasFocus);
        Assert.Equal((int)rank, dialog.Ranks.FocusedItem);
    }

    [Fact]
    public void The_ranks_are_the_Priority_field_s_options_and_None_in_its_colours()
    {
        using var dialog = Ranking();

        Assert.Equal(["None", "Low", "Medium", "High", "Urgent"], dialog.Ranks!.Labels);
        Assert.Equal(Orientation.Horizontal, dialog.Ranks.Orientation);
        Assert.Equal(
            ["", "Priority.Low.Form", "Priority.Medium.Form", "Priority.High.Form", "Priority.Urgent.Form"],
            dialog.Ranks.SubViews.Select(row => row.SchemeName ?? ""));
    }

    [Theory]
    [InlineData("n", Rank.None)]
    [InlineData("l", Rank.Low)]
    [InlineData("m", Rank.Medium)]
    [InlineData("h", Rank.High)]
    [InlineData("u", Rank.Urgent)]
    public void A_rank_s_initial_puts_the_keyboard_on_it_without_setting_it(string key, Rank rank)
    {
        using var dialog = Ranking(rank: Rank.Low);

        dialog.NewKeyDownEvent(new Key(key[0]));

        Assert.Equal(rank, dialog.Ranks!.Value);
        Assert.Equal((int)rank, dialog.Ranks.FocusedItem);
        Assert.Null(dialog.Chosen);
    }

    [Fact]
    public void Left_and_Right_move_between_the_ranks_and_leave_the_body_where_it_was()
    {
        using var dialog = Ranking(Long(), Rank.Medium, height: 12);

        dialog.NewKeyDownEvent(Key.CursorRight);
        Assert.Equal((int)Rank.High, dialog.Ranks!.FocusedItem);

        dialog.NewKeyDownEvent(Key.CursorLeft);
        Assert.Equal((int)Rank.Medium, dialog.Ranks.FocusedItem);
        Assert.Equal(0, dialog.Body.Top);
    }

    [Fact]
    public void Enter_sets_the_rank_the_keyboard_is_on_and_closes()
    {
        using var dialog = Ranking(rank: Rank.Medium);

        dialog.NewKeyDownEvent(Key.CursorRight);
        dialog.NewKeyDownEvent(Key.Enter);

        Assert.Equal(Rank.High, dialog.Chosen);
    }

    [Fact]
    public void Esc_closes_it_and_sets_nothing()
    {
        using var dialog = Ranking(rank: Rank.Medium);

        dialog.NewKeyDownEvent(Key.CursorRight);
        Assert.True(dialog.NewKeyDownEvent(Key.Esc));

        Assert.Null(dialog.Chosen);
    }

    [Fact]
    public void The_scrolling_keys_still_scroll_the_body_with_the_ranks_showing()
    {
        using var dialog = Ranking(Long(), Rank.Low, height: 12);

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
        Assert.Equal(Rank.Low, dialog.Ranks!.Value);
    }

    [Fact]
    public void Tab_with_the_ranks_showing_scrolls_History_and_leaves_the_keyboard_on_the_ranks()
    {
        using var dialog = Ranking(Long(), Rank.Low, width: 120, height: 12, read: new IssueBody(Long())
        {
            History = new History([.. Enumerable.Range(1, 30).Select(day => new HistoryEvent(
                DateTimeOffset.UnixEpoch.AddDays(day), "lead", "moved"))]),
        });

        Assert.True(dialog.NewKeyDownEvent(Key.Tab));
        dialog.NewKeyDownEvent(Key.CursorDown);

        Assert.Equal(1, dialog.HistoryLog.Top);
        Assert.Equal(0, dialog.Body.Top);
        Assert.True(dialog.Ranks!.HasFocus);
    }

    [Fact]
    public void The_hints_add_rank_and_set_and_leave_h_to_High()
    {
        using var dialog = Ranking(onApprove: () => { }, comment: new ReaderComment(new Key('c'), "comment", () => null),
            width: 120);

        Assert.Equal(
            "Up/Down/PgUp/PgDn scroll · Tab switch pane · ←/→ rank · Enter set · a approve · c comment · g on GitHub · Esc close",
            dialog.Hints.Says);
    }

    [Fact]
    public void Clicking_set_sets_the_rank_and_clicking_rank_moves_to_the_next()
    {
        using var dialog = Ranking(rank: Rank.Low);

        dialog.Hints.Hints.Single(hint => hint.Text == "←/→ rank").InvokeCommand(Command.Accept);
        Assert.Equal(Rank.Medium, dialog.Ranks!.Value);
        dialog.Hints.Hints.Single(hint => hint.Text == "Enter set").InvokeCommand(Command.Accept);

        Assert.Equal(Rank.Medium, dialog.Chosen);
    }

    [Fact]
    public void Commenting_keeps_the_reader_open_with_the_rank_chosen_and_Enter_still_sets_it()
    {
        using var dialog = Ranking(Long(), Rank.None, height: 12,
            comment: new ReaderComment(new Key('c'), "comment", () => Said));

        dialog.NewKeyDownEvent(new Key('m'));
        Assert.True(dialog.NewKeyDownEvent(new Key('c')));

        Assert.Equal("commented on #180", dialog.Message.Says);
        Assert.Equal("Shelve it until #150 lands.", dialog.Body.Lines[^1].Text);
        Assert.Equal(Rank.Medium, dialog.Ranks!.Value);
        Assert.Null(dialog.Chosen);

        dialog.NewKeyDownEvent(Key.Enter);
        Assert.Equal(Rank.Medium, dialog.Chosen);
    }

    [Fact]
    public void g_and_a_still_work_with_the_ranks_showing()
    {
        var opened = 0;
        var approved = 0;
        using var dialog = Ranking(onGitHub: () => opened++, onApprove: () => approved++);

        Assert.True(dialog.NewKeyDownEvent(new Key('g')));
        Assert.True(dialog.NewKeyDownEvent(new Key('a')));

        Assert.Equal(1, opened);
        Assert.Equal(1, approved);
        Assert.Null(dialog.Chosen);
    }

    [Fact]
    public void The_ranks_sit_in_a_band_of_their_own_between_the_panes_and_the_hints()
    {
        using var dialog = Ranking();

        Assert.Equal(LogSchemes.Form, dialog.Band!.SchemeName);
        Assert.Equal(new Rectangle(0, dialog.Hints.Frame.Y - 3, dialog.Viewport.Width, 3), dialog.Band.Frame);
        Assert.Equal(dialog.Band.Frame.Y, dialog.Body.SuperView!.Frame.Bottom);
        Assert.Equal(1, dialog.Ranks!.Frame.Y);
        Assert.Equal(dialog.Band.Viewport.Width - dialog.Ranks.Frame.Right, dialog.Ranks.Frame.X);
    }

    [Fact]
    public void A_body_that_would_not_read_still_lets_you_rank_and_says_why()
    {
        using var dialog = Ranking(read: new IssueBody(Failure: "board.sh: can't read #180"));

        Assert.Equal("board.sh: can't read #180", dialog.Message.Says);
        dialog.NewKeyDownEvent(new Key('l'));
        dialog.NewKeyDownEvent(Key.Enter);

        Assert.Equal(Rank.Low, dialog.Chosen);
    }

    private static string Long() =>
        string.Join('\n', Enumerable.Range(1, 60).Select(line => $"line {line}"));
}
