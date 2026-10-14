using System.Globalization;
using System.Drawing;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
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

    private static void Type(View field, string text)
    {
        foreach (var c in text)
            field.NewKeyDownEvent(new Key(c));
    }

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

        Assert.Equal("Alt+↑/↓ remark · h show history · Shift+arrows select · g on GitHub · Esc close", dialog.Hints.Says);
        Assert.Equal(dialog.Viewport.Height - 1, dialog.Hints.Frame.Y);
    }

    [Fact]
    public void A_pitch_to_approve_puts_a_between_scrolling_and_GitHub()
    {
        using var dialog = Open(Pitch, onApprove: () => { });

        Assert.Equal("Alt+↑/↓ remark · h show history · Shift+arrows select · a approve · g on GitHub · Esc close", dialog.Hints.Says);
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

        Assert.Equal("Alt+↑/↓ remark · h show history · Shift+arrows select · A accept · g on GitHub · Esc close", dialog.Hints.Says);
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
            comment: Commenting());
        dialog.Layout(new Size(80, 20));

        Assert.Equal("Alt+↑/↓ remark · h show history · Shift+arrows select · a approve · c comment · g on GitHub · Esc close", dialog.Hints.Says);
    }

    [Fact]
    public void Without_a_pitch_to_approve_comment_is_still_offered()
    {
        using var dialog = new ReaderDialog(Item, new IssueBody(Pitch), () => { },
            comment: Commenting());
        dialog.Layout(new Size(80, 20));

        Assert.Equal("Alt+↑/↓ remark · h show history · Shift+arrows select · c comment · g on GitHub · Esc close", dialog.Hints.Says);
    }

    [Fact]
    public void A_posted_comment_leaves_the_reader_open_saying_so()
    {
        using var dialog = new ReaderDialog(Item, new IssueBody(Long()), () => { },
            comment: Commenting());
        dialog.Layout(new Size(60, 10));

        Say(dialog, Said.Body);

        Assert.Equal("commented on #180", dialog.Message.Says);
    }

    [Fact]
    public void A_posted_comment_joins_the_end_of_the_conversation_in_view()
    {
        using var dialog = new ReaderDialog(Item, new IssueBody(Long()), () => { },
            comment: Commenting());
        dialog.Layout(new Size(60, 10));
        var before = dialog.Body.Lines.Count;

        Say(dialog, Said.Body);
        dialog.Layout(new Size(60, 10));

        var added = dialog.Body.Lines.Skip(before).Select(line => line.Text).ToList();
        Assert.Contains(Said.Heading(), added);
        Assert.Equal("Shelve it until #150 lands.", added[^1]);
        var top = dialog.Body.Top;
        dialog.NewKeyDownEvent(Key.End.WithCtrl);
        dialog.Layout(new Size(60, 10));
        Assert.Equal(dialog.Body.Top, top);
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
    public void The_body_opens_with_a_cursor_that_the_arrows_PgUp_PgDn_Home_and_End_move()
    {
        using var dialog = Open(Long(), height: 10);

        Assert.True(dialog.Body.ShowsCaret);
        Assert.True(dialog.NewKeyDownEvent(Key.CursorDown));
        Assert.Equal((1, 0), dialog.Body.Caret);
        Assert.True(dialog.NewKeyDownEvent(Key.CursorRight));
        Assert.True(dialog.NewKeyDownEvent(Key.End));
        Assert.Equal((1, 6), dialog.Body.Caret);
        Assert.True(dialog.NewKeyDownEvent(Key.Home));
        Assert.Equal((1, 0), dialog.Body.Caret);

        Assert.True(dialog.NewKeyDownEvent(Key.PageDown));
        var page = dialog.Body.Top;
        Assert.True(page > 1);
        Assert.True(dialog.NewKeyDownEvent(Key.PageUp));
        Assert.Equal(0, dialog.Body.Top);

        Assert.True(dialog.NewKeyDownEvent(Key.End.WithCtrl));
        Assert.Equal(59, dialog.Body.Caret!.Value.Line);
        Assert.True(dialog.Body.Top > page);
        Assert.True(dialog.NewKeyDownEvent(Key.Home.WithCtrl));
        Assert.Equal(0, dialog.Body.Top);
    }

    [Fact]
    public void Ctrl_and_Option_arrows_move_the_body_cursor_a_word_and_select_with_Shift()
    {
        using var dialog = Open(Long(), height: 10);

        Assert.True(dialog.NewKeyDownEvent(Key.CursorRight.WithCtrl));
        Assert.Equal((0, 5), dialog.Body.Caret);
        Assert.True(dialog.NewKeyDownEvent(Key.F.WithAlt));
        Assert.Equal((0, 6), dialog.Body.Caret);
        Assert.True(dialog.NewKeyDownEvent(Key.CursorRight.WithAlt));
        Assert.Equal((1, 0), dialog.Body.Caret);
        Assert.True(dialog.NewKeyDownEvent(Key.CursorLeft.WithAlt));
        Assert.Equal((0, 5), dialog.Body.Caret);
        Assert.True(dialog.NewKeyDownEvent(Key.B.WithAlt));
        Assert.Equal((0, 0), dialog.Body.Caret);

        Assert.True(dialog.NewKeyDownEvent(Key.CursorRight.WithCtrl.WithShift));
        Assert.Equal(["line "], dialog.Body.MarkedText());
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

        Assert.Equal("Alt+↑/↓ remark · h show history · Shift+arrows select · t try · a accept · g on GitHub · Esc close", dialog.Hints.Says);
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

    [Fact]
    public void A_document_pitch_offers_e_edit_and_hands_over_what_it_shows_and_where()
    {
        var edited = new List<(IssueBody Body, int Top)>();
        var body = new IssueBody(Long());
        using var dialog = new ReaderDialog(Item, body, () => { },
            accept: new ReaderCommand(new Key('a'), "accept", () => true),
            edit: new ReaderEdit(new Key('e'), (shown, top) => edited.Add((shown, top))));
        dialog.Layout(new Size(80, 10));
        dialog.NewKeyDownEvent(Key.PageDown);
        var top = dialog.Body.Top;

        Assert.Contains("e edit · a accept", dialog.Hints.Says);
        Assert.True(dialog.NewKeyDownEvent(new Key('e')));

        Assert.Equal([(body, top)], edited);
    }

    [Fact]
    public void One_it_can_t_edit_says_why_and_stays_open()
    {
        var edited = 0;
        using var dialog = new ReaderDialog(Item, new IssueBody(Pitch), () => { },
            edit: new ReaderEdit(new Key('e'), (_, _) => edited++, "#180 changes 2 files, so it can't be edited here: g opens it on GitHub"));
        dialog.Layout(new Size(80, 20));

        Assert.True(dialog.NewKeyDownEvent(new Key('e')));

        Assert.Equal(0, edited);
        Assert.Equal("#180 changes 2 files, so it can't be edited here: g opens it on GitHub", dialog.Message.Says);
        Assert.Equal(SchemeManager.SchemesToSchemeName(Schemes.Error), dialog.Message.SchemeName);
    }

    [Fact]
    public void Reopened_after_an_edit_it_opens_where_it_was_saying_what_became_of_it()
    {
        using var dialog = new ReaderDialog(Item, new IssueBody(Long()), () => { },
            edit: new ReaderEdit(new Key('e'), (_, _) => { }, Top: 12, Said: "committed your edit to docs/vision.md"));
        dialog.Layout(new Size(60, 10));

        Assert.Equal(12, dialog.Body.Top);
        Assert.Equal("committed your edit to docs/vision.md", dialog.Message.Says);
        Assert.Equal(SchemeManager.SchemesToSchemeName(Schemes.Accent), dialog.Message.SchemeName);
    }

    [Fact]
    public void Anything_but_a_document_pitch_has_no_e()
    {
        using var dialog = Open(Pitch);

        Assert.DoesNotContain("edit", dialog.Hints.Says);
        Assert.False(dialog.NewKeyDownEvent(new Key('e')));
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
            comment: Commenting(), width: 120);
        dialog.Layout(new Size(120, 20));

        Say(dialog, Said.Body);

        Assert.EndsWith("you   commented", dialog.HistoryLog.Lines[0].Text);
        Assert.Equal(Recorded.Events.Count + 1, dialog.HistoryLog.Lines.Count);
    }

    [Fact]
    public void A_posted_comment_leaves_a_History_that_wouldnt_read_saying_so()
    {
        using var dialog = new ReaderDialog(Item,
            new IssueBody(Long(), History: new History([], Failure: "couldn't read #180's history")), () => { },
            comment: Commenting(), width: 120);

        Say(dialog, Said.Body);

        Assert.Equal(["couldn't read #180's history"], dialog.HistoryLog.Lines.Select(line => line.Text));
    }

    [Fact]
    public void A_wide_reader_shows_History_beside_the_body_newest_first()
    {
        using var dialog = Wide();

        Assert.True(dialog.HistoryShown);
        Assert.Equal("3 Oct 13:40  dev   In progress → In review", dialog.HistoryLog.Lines[0].Text);
        Assert.Equal(dialog.Viewport.Width - ReaderPanes.HistoryWidth, dialog.Body.SuperView!.Frame.Width);
        Assert.Equal("Alt+↑/↓ remark · Tab switch pane · h hide history · Shift+arrows select · g on GitHub · Esc close",
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
            Assert.Equal("Alt+↑/↓ remark · h show history · Shift+arrows select · g on GitHub · Esc close", dialog.Hints.Says);
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
        Assert.True(dialog.HistoryLog.ShowsCaret);
        Assert.False(dialog.Body.ShowsCaret);
        dialog.NewKeyDownEvent(Key.CursorDown);
        Assert.Equal((1, 0), dialog.HistoryLog.Caret);
        Assert.NotEqual((1, 0), dialog.Body.Caret);

        Assert.True(dialog.NewKeyDownEvent(Key.Tab));
        Assert.True(dialog.Body.HasFocus);
        dialog.NewKeyDownEvent(Key.CursorDown);
        Assert.Equal((1, 0), dialog.Body.Caret);
        Assert.Equal((1, 0), dialog.HistoryLog.Caret);
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
        Assert.Equal(Rank.High, dialog.Ranks!.Value);
        Assert.Equal((int)Rank.High, dialog.Ranks.FocusedItem);

        dialog.NewKeyDownEvent(Key.CursorLeft);
        Assert.Equal(Rank.Medium, dialog.Ranks.Value);
        Assert.Equal((int)Rank.Medium, dialog.Ranks.FocusedItem);
        Assert.Equal(0, dialog.Body.Top);
    }

    [Theory]
    [InlineData(Rank.Urgent, "Right", Rank.None)]
    [InlineData(Rank.None, "Left", Rank.Urgent)]
    public void Left_and_Right_wrap_at_the_ends(Rank start, string arrow, Rank rank)
    {
        using var dialog = Ranking(rank: start);

        dialog.NewKeyDownEvent(arrow == "Right" ? Key.CursorRight : Key.CursorLeft);

        Assert.Equal(rank, dialog.Ranks!.Value);
        Assert.Equal((int)rank, dialog.Ranks.FocusedItem);
    }

    [Fact]
    public void Arrows_initials_and_the_rank_hint_all_keep_the_mark_on_the_highlighted_rank()
    {
        using var dialog = Ranking(rank: Rank.None);
        var rankHint = dialog.Hints.Hints.Single(hint => hint.Text == "←/→ rank");

        foreach (var move in new Action[]
        {
            () => dialog.NewKeyDownEvent(Key.CursorRight),
            () => dialog.NewKeyDownEvent(new Key('h')),
            () => rankHint.InvokeCommand(Command.Accept),
            () => dialog.NewKeyDownEvent(Key.CursorLeft),
            () => dialog.NewKeyDownEvent(new Key('l')),
            () => dialog.NewKeyDownEvent(Key.CursorLeft),
        })
        {
            move();
            Assert.Equal(dialog.Ranks!.FocusedItem, (int)dialog.Ranks.Value!);
        }
        Assert.Equal(Rank.None, dialog.Ranks!.Value);
    }

    [Fact]
    public void Enter_sets_the_rank_the_keyboard_is_on_and_closes()
    {
        using var dialog = Ranking(rank: Rank.Medium);

        dialog.NewKeyDownEvent(Key.CursorRight);
        Assert.Equal(Rank.High, dialog.Ranks!.Value);
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
        using var dialog = Ranking(onApprove: () => { }, comment: Commenting(),
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
            comment: Commenting());

        dialog.NewKeyDownEvent(new Key('m'));
        Say(dialog, Said.Body);

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

    private static ReaderDialog Replying(
        int width = 140, Func<string, Task<string?>>? post = null, Func<bool>? confirmDiscard = null,
        Action? onApprove = null, ReaderCommand? accept = null, string? body = null, int height = 20)
    {
        var dialog = new ReaderDialog(Item, new IssueBody(body ?? Long(), History: Recorded), () => { }, onApprove,
            accept, Commenting(post), width: width, confirmDiscard: confirmDiscard);
        dialog.Layout(new Size(width, height));
        return dialog;
    }

    private static void Open(ReaderDialog dialog, int width = 140, int height = 20)
    {
        dialog.NewKeyDownEvent(new Key('c'));
        dialog.Layout(new Size(width, height));
    }

    [Fact]
    public void c_opens_the_comment_on_the_left_of_the_body_with_the_cursor_in_it()
    {
        using var dialog = Replying();

        Open(dialog);

        Assert.True(dialog.CommentShown);
        Assert.True(dialog.Field.HasFocus);
        Assert.True(dialog.Field.WordWrap);
        Assert.Equal(new Rectangle(0, 0, ReaderPanes.CommentWidth, dialog.Body.SuperView!.Frame.Height),
            dialog.Field.SuperView!.Frame);
        Assert.Equal(ReaderPanes.CommentWidth, dialog.Body.SuperView.Frame.X);
        Assert.True(dialog.HistoryShown);
        Assert.Equal(dialog.Viewport.Width - ReaderPanes.HistoryWidth, dialog.Body.SuperView.Frame.Right);
    }

    [Fact]
    public void Opening_the_comment_leaves_a_body_that_now_wraps_where_you_were_reading()
    {
        var body = string.Join('\n', Enumerable.Range(1, 12).Select(line => $"{line} {new string('x', 70)}"));
        using var dialog = Replying(body: body);

        Open(dialog);

        Assert.Equal(0, dialog.Body.Top);
    }

    [Fact]
    public void Tab_cycles_comment_body_and_History_and_Shift_Tab_goes_back()
    {
        using var dialog = Replying();
        Open(dialog);

        dialog.NewKeyDownEvent(Key.Tab);
        Assert.True(dialog.Body.HasFocus);
        dialog.NewKeyDownEvent(Key.Tab);
        Assert.True(dialog.HistoryLog.HasFocus);
        dialog.NewKeyDownEvent(Key.Tab);
        Assert.True(dialog.Field.HasFocus);
        dialog.NewKeyDownEvent(Key.Tab.WithShift);
        Assert.True(dialog.HistoryLog.HasFocus);
        dialog.NewKeyDownEvent(Key.Tab.WithShift);
        Assert.True(dialog.Body.HasFocus);
    }

    [Fact]
    public void Without_History_Tab_moves_between_the_comment_and_the_body()
    {
        using var dialog = Replying();
        dialog.NewKeyDownEvent(new Key('h'));
        Open(dialog);

        dialog.NewKeyDownEvent(Key.Tab);
        Assert.True(dialog.Body.HasFocus);
        dialog.NewKeyDownEvent(Key.Tab);
        Assert.True(dialog.Field.HasFocus);
    }

    [Fact]
    public void Esc_in_the_comment_goes_back_to_the_pane_you_came_from_and_c_returns_to_your_draft()
    {
        using var dialog = Replying();
        dialog.NewKeyDownEvent(Key.Tab);
        Open(dialog);
        dialog.Field.Text = "half a thought";

        Assert.True(dialog.NewKeyDownEvent(Key.Esc));
        Assert.True(dialog.HistoryLog.HasFocus);
        Assert.True(dialog.CommentShown);

        dialog.NewKeyDownEvent(new Key('c'));
        Assert.True(dialog.Field.HasFocus);
        Assert.Equal("half a thought", dialog.Field.Text);
    }

    [Fact]
    public void The_caret_starts_on_the_top_line_in_view_and_the_arrows_move_it()
    {
        using var dialog = Replying();
        dialog.NewKeyDownEvent(Key.CursorDown);
        dialog.NewKeyDownEvent(Key.CursorDown);
        Open(dialog);
        Assert.False(dialog.Body.ShowsCaret);

        dialog.NewKeyDownEvent(Key.Tab);
        Assert.True(dialog.Body.ShowsCaret);
        Assert.Equal((2, 0), dialog.Body.Caret);

        dialog.NewKeyDownEvent(Key.CursorRight);
        dialog.NewKeyDownEvent(Key.CursorDown);
        Assert.Equal((3, 1), dialog.Body.Caret);
        dialog.NewKeyDownEvent(Key.End);
        Assert.Equal((3, 6), dialog.Body.Caret);
        dialog.NewKeyDownEvent(Key.Home);
        dialog.NewKeyDownEvent(Key.CursorLeft);
        Assert.Equal((2, 6), dialog.Body.Caret);
    }

    [Fact]
    public void Up_and_Down_keep_the_caret_s_column_across_a_shorter_line()
    {
        using var dialog = Replying(body: "a longer line\nab\nanother line");
        Open(dialog);
        dialog.NewKeyDownEvent(Key.Tab);

        dialog.NewKeyDownEvent(Key.End);
        dialog.NewKeyDownEvent(Key.CursorDown);
        Assert.Equal((1, 2), dialog.Body.Caret);
        dialog.NewKeyDownEvent(Key.CursorDown);
        Assert.Equal((2, 12), dialog.Body.Caret);
    }

    [Fact]
    public void In_a_wrapped_line_the_caret_moves_a_row_at_a_time()
    {
        using var dialog = Replying(body: string.Join(' ', Enumerable.Repeat("word", 60)));
        Open(dialog);
        dialog.NewKeyDownEvent(Key.Tab);

        dialog.NewKeyDownEvent(Key.CursorDown);
        var (line, offset) = dialog.Body.Caret!.Value;
        dialog.NewKeyDownEvent(Key.End);
        dialog.NewKeyDownEvent(Key.Home);

        Assert.Equal(0, line);
        Assert.True(offset > 0);
        Assert.Equal((0, offset), dialog.Body.Caret);
    }

    [Fact]
    public void The_pane_scrolls_to_keep_the_caret_in_view()
    {
        using var dialog = Replying();
        Open(dialog);
        dialog.NewKeyDownEvent(Key.Tab);

        for (var i = 0; i < 30; i++)
            dialog.NewKeyDownEvent(Key.CursorDown);

        Assert.Equal((30, 0), dialog.Body.Caret);
        Assert.InRange(30, dialog.Body.Top, dialog.Body.Top + dialog.Body.Viewport.Height - 1);
        dialog.NewKeyDownEvent(Key.Home.WithCtrl);
        Assert.Equal(0, dialog.Body.Top);
    }

    [Fact]
    public void Shift_and_the_arrows_select_part_of_a_line_or_across_lines()
    {
        using var dialog = Replying();
        Open(dialog);
        dialog.NewKeyDownEvent(Key.Tab);
        for (var i = 0; i < 5; i++)
            dialog.NewKeyDownEvent(Key.CursorRight);

        dialog.NewKeyDownEvent(Key.End.WithShift);
        Assert.Equal(["1"], dialog.Body.MarkedText());
        dialog.NewKeyDownEvent(Key.CursorDown.WithShift);
        Assert.Equal(["1", "line 2"], dialog.Body.MarkedText());
        dialog.NewKeyDownEvent(Key.CursorLeft.WithShift);
        Assert.Equal(["1", "line "], dialog.Body.MarkedText());
        dialog.NewKeyDownEvent(Key.CursorUp.WithShift);
        Assert.Equal(0, dialog.Body.Marked);
        dialog.NewKeyDownEvent(Key.Home.WithShift);
        Assert.Equal(["line "], dialog.Body.MarkedText());
    }

    [Fact]
    public void Shift_Down_from_the_start_of_a_line_selects_that_line_alone()
    {
        using var dialog = Replying();
        Open(dialog);
        dialog.NewKeyDownEvent(Key.Tab);

        dialog.NewKeyDownEvent(Key.CursorDown.WithShift);
        Assert.Equal(["line 1"], dialog.Body.MarkedText());
        dialog.NewKeyDownEvent(Key.CursorDown.WithShift);
        Assert.Equal(["line 1", "line 2"], dialog.Body.MarkedText());
        Assert.Equal(2, dialog.Body.Marked);
    }

    [Fact]
    public void Moving_without_Shift_drops_the_selection()
    {
        using var dialog = Replying();
        Open(dialog);
        dialog.NewKeyDownEvent(Key.Tab);
        dialog.NewKeyDownEvent(Key.CursorDown.WithShift);

        dialog.NewKeyDownEvent(Key.CursorRight);

        Assert.Equal(0, dialog.Body.Marked);
    }

    [Fact]
    public void History_has_a_caret_of_its_own()
    {
        using var dialog = Replying();
        Open(dialog);
        dialog.NewKeyDownEvent(Key.Tab);
        dialog.NewKeyDownEvent(Key.CursorDown);
        dialog.NewKeyDownEvent(Key.Tab);

        Assert.False(dialog.Body.ShowsCaret);
        Assert.True(dialog.HistoryLog.ShowsCaret);
        dialog.NewKeyDownEvent(Key.CursorDown.WithShift);
        dialog.NewKeyDownEvent(Key.CursorDown.WithShift);

        Assert.Equal(dialog.HistoryLog.Lines.Take(2).Select(line => line.Text), dialog.HistoryLog.MarkedText());
        Assert.Equal(0, dialog.Body.Marked);
        Assert.Equal((1, 0), dialog.Body.Caret);
    }

    [Fact]
    public void Back_in_the_comment_neither_pane_shows_its_caret()
    {
        using var dialog = Replying();
        Open(dialog);
        dialog.NewKeyDownEvent(Key.Tab);

        dialog.NewKeyDownEvent(new Key('c'));

        Assert.False(dialog.Body.ShowsCaret);
        Assert.False(dialog.HistoryLog.ShowsCaret);
    }

    [Fact]
    public void The_hints_count_the_selected_lines_and_Esc_clears_them_first()
    {
        var asked = 0;
        using var dialog = Replying(confirmDiscard: () => ++asked > 0);
        Open(dialog);
        Assert.Equal(
            "Tab switch pane · Shift+arrows select · Ctrl+Enter post · g on GitHub · Esc back",
            dialog.Hints.Says);
        dialog.Field.Text = "draft";
        dialog.NewKeyDownEvent(Key.Tab);
        Assert.EndsWith("Shift+arrows select · Ctrl+Enter post · g on GitHub · Esc close", dialog.Hints.Says);

        dialog.NewKeyDownEvent(Key.CursorDown.WithShift);
        Assert.Contains("Tab switch pane · Ctrl+C copy · q quote 1 line · Ctrl+Enter post", dialog.Hints.Says);
        dialog.NewKeyDownEvent(Key.CursorDown.WithShift);
        Assert.Contains("q quote 2 lines", dialog.Hints.Says);
        Assert.EndsWith("Esc clear", dialog.Hints.Says);

        Assert.True(dialog.NewKeyDownEvent(Key.Esc));
        Assert.Equal(0, dialog.Body.Marked);
        Assert.DoesNotContain("quote", dialog.Hints.Says);
        Assert.Equal(0, asked);
    }

    [Fact]
    public void q_quotes_the_selection_into_the_comment_and_takes_the_keyboard_below_it()
    {
        using var dialog = Replying();
        Open(dialog);
        dialog.NewKeyDownEvent(Key.Tab);
        dialog.NewKeyDownEvent(Key.CursorDown.WithShift);
        dialog.NewKeyDownEvent(Key.CursorDown.WithShift);

        Assert.True(dialog.NewKeyDownEvent(new Key('q')));

        Assert.Equal("> line 1\n> line 2\n\n", dialog.Field.Text);
        Assert.True(dialog.Field.HasFocus);
        Type(dialog.Field, "Yes.");
        Assert.Equal("> line 1\n> line 2\n\nYes.", dialog.Field.Text);
        Assert.Equal(0, dialog.Body.Marked);
        Assert.DoesNotContain("quote", dialog.Hints.Says);

        dialog.NewKeyDownEvent(Key.Esc);
        Assert.True(dialog.Body.HasFocus);
    }

    [Fact]
    public void Quotes_go_in_where_the_comment_s_cursor_is_as_often_as_you_like()
    {
        using var dialog = Replying();
        Open(dialog);
        dialog.Field.Text = "Agreed.";
        dialog.Field.CaretOffset = dialog.Field.Text.Length;
        dialog.NewKeyDownEvent(Key.Tab);

        dialog.NewKeyDownEvent(Key.CursorDown.WithShift);
        dialog.NewKeyDownEvent(new Key('q'));
        dialog.NewKeyDownEvent(Key.Esc);
        dialog.NewKeyDownEvent(Key.CursorDown);
        dialog.NewKeyDownEvent(Key.CursorDown.WithShift);
        dialog.NewKeyDownEvent(new Key('q'));
        Type(dialog.Field, "But not this.");

        Assert.Equal("Agreed.\n> line 1\n\n> line 3\n\nBut not this.", dialog.Field.Text);
    }

    [Fact]
    public void A_quote_at_the_start_of_the_draft_goes_before_it()
    {
        using var dialog = Replying();
        Open(dialog);
        dialog.Field.Text = "Agreed.";
        dialog.Field.CaretOffset = 0;
        dialog.NewKeyDownEvent(Key.Tab);

        dialog.NewKeyDownEvent(Key.CursorDown.WithShift);
        dialog.NewKeyDownEvent(new Key('q'));

        Assert.Equal("> line 1\n\nAgreed.", dialog.Field.Text);
    }

    [Fact]
    public void A_quote_in_the_middle_of_a_line_starts_on_a_line_of_its_own()
    {
        using var dialog = Replying();
        Open(dialog);
        dialog.Field.Text = "Agreed, but";
        dialog.Field.CaretOffset = 7;
        dialog.NewKeyDownEvent(Key.Tab);

        dialog.NewKeyDownEvent(Key.CursorDown.WithShift);
        dialog.NewKeyDownEvent(new Key('q'));

        Assert.Equal("Agreed,\n> line 1\n\n but", dialog.Field.Text);
    }

    [Fact]
    public void A_quote_keeps_blank_lines_and_quotes_inside_it()
    {
        Assert.Equal("> ## Assumed\n>\n> > as written\n\n", ReaderDialog.Quote(["## Assumed", "", "> as written"], midLine: false));
        Assert.Equal("\n> one\n\n", ReaderDialog.Quote(["one"], midLine: true));
    }

    [Fact]
    public void Without_the_comment_open_q_opens_it_quoting_part_of_a_line()
    {
        using var dialog = Replying();
        dialog.NewKeyDownEvent(Key.CursorRight);
        dialog.NewKeyDownEvent(Key.CursorRight);

        dialog.NewKeyDownEvent(Key.End.WithShift);
        Assert.Contains("Ctrl+C copy · q quote 1 line", dialog.Hints.Says);
        Assert.True(dialog.NewKeyDownEvent(new Key('q')));

        Assert.True(dialog.CommentShown);
        Assert.True(dialog.Field.HasFocus);
        Assert.Equal("> ne 1\n\n", dialog.Field.Text);
    }

    [Fact]
    public void Without_a_selection_q_does_nothing()
    {
        using var dialog = Replying();

        Assert.False(dialog.NewKeyDownEvent(new Key('q')));

        Assert.False(dialog.CommentShown);
        Assert.DoesNotContain("copy", dialog.Hints.Says);
    }

    private static ReaderDialog Copying(FakeClipboard clipboard, string body, int width = 60)
    {
        var dialog = new ReaderDialog(Item, new IssueBody(body), () => { }, clipboard: clipboard);
        dialog.Layout(new Size(width, 20));
        return dialog;
    }

    [Fact]
    public void Ctrl_C_copies_part_of_a_line_and_says_how_many_characters()
    {
        var clipboard = new FakeClipboard();
        using var dialog = Copying(clipboard, "See #388 for why.");
        for (var i = 0; i < 4; i++)
            dialog.NewKeyDownEvent(Key.CursorRight);
        for (var i = 0; i < 4; i++)
            dialog.NewKeyDownEvent(Key.CursorRight.WithShift);

        Assert.True(dialog.NewKeyDownEvent(Key.C.WithCtrl));

        Assert.Equal("#388", clipboard.GetClipboardData());
        Assert.Equal("copied 4 characters", dialog.Message.Says);
    }

    [Fact]
    public void Ctrl_C_copies_across_lines()
    {
        var clipboard = new FakeClipboard();
        using var dialog = Copying(clipboard, "one\ntwo\nthree");
        dialog.NewKeyDownEvent(Key.CursorRight);
        dialog.NewKeyDownEvent(Key.CursorDown.WithShift);
        dialog.NewKeyDownEvent(Key.CursorDown.WithShift);

        dialog.NewKeyDownEvent(Key.C.WithCtrl);

        Assert.Equal("ne\ntwo\nt", clipboard.GetClipboardData());
        Assert.Equal("copied 8 characters", dialog.Message.Says);
    }

    [Fact]
    public void Ctrl_C_copies_a_wrapped_line_as_it_was_written()
    {
        var clipboard = new FakeClipboard();
        var line = string.Join(' ', Enumerable.Range(1, 30).Select(word => $"word{word}"));
        using var dialog = Copying(clipboard, line, width: 30);
        Assert.True(dialog.Body.Rows(dialog.Body.Viewport.Width).Count > 2);

        dialog.NewKeyDownEvent(Key.End.WithCtrl.WithShift);
        dialog.NewKeyDownEvent(Key.C.WithCtrl);

        Assert.Equal(line, clipboard.GetClipboardData());
    }

    [Fact]
    public void Ctrl_C_copies_from_History_too()
    {
        var clipboard = new FakeClipboard();
        using var dialog = new ReaderDialog(Item, new IssueBody("body", History: Recorded), () => { }, width: 140,
            clipboard: clipboard);
        dialog.Layout(new Size(140, 20));
        dialog.NewKeyDownEvent(Key.Tab);

        dialog.NewKeyDownEvent(Key.End.WithShift);
        dialog.NewKeyDownEvent(Key.C.WithCtrl);

        Assert.Equal(dialog.HistoryLog.Lines[0].Text, clipboard.GetClipboardData());
    }

    [Fact]
    public void Without_a_clipboard_copying_says_so()
    {
        using var dialog = Copying(new FakeClipboard(isSupportedAlwaysFalse: true), "text");
        dialog.NewKeyDownEvent(Key.End.WithShift);

        dialog.NewKeyDownEvent(Key.C.WithCtrl);

        Assert.Equal("there's no clipboard to copy to", dialog.Message.Says);
    }

    private static ReaderDialog CopyingBranch(FakeClipboard clipboard, string branch)
    {
        var dialog = new ReaderDialog(Item with { Pr = 459, Branch = branch }, new IssueBody("text"), () => { },
            clipboard: clipboard, copyBranch: new Key('y'));
        dialog.Layout(new Size(120, 20));
        return dialog;
    }

    [Fact]
    public void Y_copies_the_PR_s_branch_name()
    {
        var clipboard = new FakeClipboard();
        using var dialog = CopyingBranch(clipboard, "feat/path-pickers");

        Assert.Contains("y copy branch", dialog.Hints.Says);
        Assert.True(dialog.NewKeyDownEvent(new Key('y')));

        Assert.Equal("feat/path-pickers", clipboard.GetClipboardData());
        Assert.Equal("Copied feat/path-pickers", dialog.Message.Says);
    }

    [Fact]
    public void Y_without_a_clipboard_says_so()
    {
        using var dialog = CopyingBranch(new FakeClipboard(isSupportedAlwaysFalse: true), "feat/path-pickers");

        dialog.NewKeyDownEvent(new Key('y'));

        Assert.Equal("there's no clipboard to copy to", dialog.Message.Says);
    }

    [Fact]
    public void Y_copies_nothing_where_there_s_no_branch()
    {
        var clipboard = new FakeClipboard();
        using var dialog = CopyingBranch(clipboard, "");

        Assert.DoesNotContain("copy branch", dialog.Hints.Says);
        dialog.NewKeyDownEvent(new Key('y'));

        Assert.Equal("", clipboard.GetClipboardData() ?? "");
    }

    [Theory]
    [InlineData("x", "copied 1 character")]
    [InlineData("ab\nc", "copied 4 characters")]
    public void The_copy_message_counts_characters(string text, string said) =>
        Assert.Equal(said, ReaderDialog.Copied(text));

    [Fact]
    public void The_reader_s_letters_reach_its_commands_from_either_pane_and_leave_the_text_alone()
    {
        var opened = 0;
        var approved = 0;
        var dialog = new ReaderDialog(Item, new IssueBody(Long(), History: Recorded), () => opened++, () => approved++,
            comment: Commenting(), width: 140);
        dialog.Layout(new Size(140, 20));
        var body = dialog.Body.Lines;
        var history = dialog.HistoryLog.Lines;

        dialog.NewKeyDownEvent(Key.Tab);
        Assert.True(dialog.HistoryLog.HasFocus);
        Assert.True(dialog.NewKeyDownEvent(new Key('g')));
        Assert.True(dialog.NewKeyDownEvent(new Key('h')));
        Assert.False(dialog.HistoryShown);
        Assert.True(dialog.Body.HasFocus);
        dialog.NewKeyDownEvent(new Key('x'));
        Assert.True(dialog.NewKeyDownEvent(new Key('c')));
        Assert.True(dialog.CommentShown);
        dialog.NewKeyDownEvent(Key.Esc);
        Assert.True(dialog.NewKeyDownEvent(new Key('a')));

        Assert.Equal(1, opened);
        Assert.Equal(1, approved);
        Assert.Same(body, dialog.Body.Lines);
        Assert.Same(history, dialog.HistoryLog.Lines);
        dialog.Dispose();
    }

    [Fact]
    public void Ctrl_Enter_posts_from_the_body_and_closes_the_pane()
    {
        var posted = new List<string>();
        using var dialog = Replying(post: body =>
        {
            posted.Add(body);
            return Task.FromResult<string?>(null);
        });
        Open(dialog);
        dialog.Field.Text = "Ship it.";
        dialog.NewKeyDownEvent(Key.Tab);

        Assert.True(dialog.NewKeyDownEvent(Key.Enter.WithCtrl));

        Assert.Equal(["Ship it."], posted);
        Assert.False(dialog.CommentShown);
        Assert.Equal("", dialog.Field.Text);
        Assert.True(dialog.Body.HasFocus);
        Assert.Equal("commented on #180", dialog.Message.Says);
        Assert.Equal("Ship it.", dialog.Body.Lines[^1].Text);
        Assert.True(dialog.Body.ShowsCaret);
        Assert.EndsWith("you   commented", dialog.HistoryLog.Lines[0].Text);
    }

    [Fact]
    public void Typing_past_the_bottom_of_the_comment_keeps_the_cursor_in_view_and_posts_what_was_typed()
    {
        var posted = new List<string>();
        using var dialog = Replying(post: body =>
        {
            posted.Add(body);
            return Task.FromResult<string?>(null);
        });
        Open(dialog);
        var lines = Enumerable.Range(1, 30).Select(line => $"line {line}").ToList();

        foreach (var (line, i) in lines.Select((line, i) => (line, i)))
        {
            if (i > 0)
                dialog.NewKeyDownEvent(Key.Enter);
            Type(dialog.Field, line);
        }

        var viewport = dialog.Field.Viewport;
        Assert.True(viewport.Y > 0);
        Assert.InRange(lines.Count - 1, viewport.Y, viewport.Bottom - 1);
        Assert.True(dialog.NewKeyDownEvent(Key.Enter.WithCtrl));
        Assert.Equal([string.Join('\n', lines)], posted);
    }

    [Fact]
    public void A_comment_that_fails_to_post_stays_in_the_pane_saying_why()
    {
        using var dialog = Replying(post: _ => Task.FromResult<string?>("gh: HTTP 502"));
        Open(dialog);
        dialog.Field.Text = "Ship it.";

        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);

        Assert.True(dialog.CommentShown);
        Assert.Equal("Ship it.", dialog.Field.Text);
        Assert.Equal("gh: HTTP 502", dialog.Message.Says);
    }

    [Fact]
    public void An_empty_comment_is_not_posted()
    {
        var posted = 0;
        using var dialog = Replying(post: _ => Task.FromResult<string?>(++posted < 0 ? "" : null));
        Open(dialog);

        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);

        Assert.Equal(0, posted);
        Assert.Equal("Nothing to post: the comment is empty", dialog.Message.Says);
    }

    [Fact]
    public void Leaving_with_a_draft_asks_first_and_staying_keeps_it()
    {
        var asked = 0;
        var approved = 0;
        var accepted = 0;
        using var dialog = Replying(confirmDiscard: () => ++asked < 0, onApprove: () => approved++,
            accept: new ReaderCommand(new Key('m'), "accept", () => ++accepted > 0));
        Open(dialog);
        dialog.Field.Text = "half a thought";
        dialog.NewKeyDownEvent(Key.Tab);

        dialog.NewKeyDownEvent(Key.Esc);
        dialog.NewKeyDownEvent(new Key('a'));
        dialog.NewKeyDownEvent(new Key('m'));

        Assert.Equal(3, asked);
        Assert.Equal(0, approved);
        Assert.Equal(0, accepted);
        Assert.Equal("half a thought", dialog.Field.Text);
    }

    [Fact]
    public void Leaving_with_a_draft_goes_ahead_once_you_discard_it()
    {
        var approved = 0;
        using var dialog = Replying(confirmDiscard: () => true, onApprove: () => approved++);
        Open(dialog);
        dialog.Field.Text = "half a thought";
        dialog.NewKeyDownEvent(Key.Tab);

        dialog.NewKeyDownEvent(new Key('a'));

        Assert.Equal(1, approved);
    }

    [Fact]
    public void Leaving_with_an_empty_comment_asks_nothing()
    {
        var asked = 0;
        var approved = 0;
        using var dialog = Replying(confirmDiscard: () => ++asked < 0, onApprove: () => approved++);
        Open(dialog);
        dialog.NewKeyDownEvent(Key.Tab);

        dialog.NewKeyDownEvent(new Key('a'));

        Assert.Equal(0, asked);
        Assert.Equal(1, approved);
    }

    [Fact]
    public void Too_narrow_for_three_panes_History_hides_while_you_comment_and_h_still_shows_it()
    {
        var panes = new ReaderPanes();
        using var dialog = new ReaderDialog(Item, new IssueBody(Long(), History: Recorded), () => { },
            comment: Commenting(), panes: panes, width: ReaderPanes.NarrowestThree - 1);
        Assert.True(dialog.HistoryShown);

        dialog.NewKeyDownEvent(new Key('c'));
        Assert.False(dialog.HistoryShown);
        Assert.True(panes.HistoryShown);

        dialog.NewKeyDownEvent(Key.Tab);
        dialog.NewKeyDownEvent(new Key('h'));
        Assert.True(dialog.HistoryShown);
    }

    [Fact]
    public void Typing_in_the_comment_with_the_ranks_showing_writes_rather_than_ranks()
    {
        using var dialog = Ranking(Long(), Rank.Low, comment: Commenting());

        dialog.NewKeyDownEvent(new Key('c'));
        dialog.NewKeyDownEvent(new Key('h'));
        dialog.NewKeyDownEvent(Key.Enter);
        dialog.NewKeyDownEvent(new Key('m'));

        Assert.Equal("h\nm", dialog.Field.Text.ReplaceLineEndings("\n"));
        Assert.Equal(Rank.Low, dialog.Ranks!.Value);
        Assert.Null(dialog.Chosen);

        dialog.NewKeyDownEvent(Key.Esc);
        Assert.True(dialog.Ranks.HasFocus);
    }

    private static ReaderDialog Declining(
        Func<string, Task<string?>>? decline = null, bool open = false, WaitingItem? item = null, int width = 140)
    {
        var dialog = new ReaderDialog(item ?? Item, new IssueBody(Long(), History: Recorded), () => { },
            comment: Commenting(decline: decline ?? (_ => Task.FromResult<string?>(null)), open: open), width: width);
        dialog.Layout(new Size(width, 20));
        return dialog;
    }

    [Fact]
    public void An_Idea_or_pitch_puts_decline_after_comment()
    {
        using var dialog = Declining();

        Assert.Equal(
            "Alt+↑/↓ remark · Tab switch pane · h hide history · Shift+arrows select · c comment · x decline · g on GitHub · Esc close",
            dialog.Hints.Says);
    }

    [Fact]
    public void Without_anything_to_decline_x_is_neither_hinted_nor_handled()
    {
        using var dialog = Replying();

        Assert.DoesNotContain("decline", dialog.Hints.Says);
        dialog.NewKeyDownEvent(new Key('x'));
        Assert.False(dialog.CommentShown);
    }

    [Fact]
    public void x_opens_the_comment_pane_to_decline_with_the_cursor_in_it()
    {
        using var dialog = Declining();

        Assert.True(dialog.NewKeyDownEvent(new Key('x')));

        Assert.True(dialog.CommentShown);
        Assert.True(dialog.Declining);
        Assert.True(dialog.Field.HasFocus);
        Assert.Equal("Decline #180: why?", dialog.CommentTitle);
        Assert.Equal("Tab switch pane · Shift+arrows select · Ctrl+Enter decline · g on GitHub · Esc cancel", dialog.Hints.Says);
    }

    [Fact]
    public void Opened_to_decline_it_starts_in_the_reason()
    {
        using var dialog = Declining(open: true);

        Assert.True(dialog.Declining);
        Assert.True(dialog.Field.HasFocus);
        Assert.Contains("Ctrl+Enter decline", dialog.Hints.Says);
    }

    [Fact]
    public void x_keeps_a_comment_already_started_as_the_start_of_the_reason()
    {
        using var dialog = Declining();
        Open(dialog);
        dialog.Field.Text = "Covered by #46";
        dialog.NewKeyDownEvent(Key.Esc);

        dialog.NewKeyDownEvent(new Key('x'));

        Assert.True(dialog.Declining);
        Assert.Equal("Covered by #46", dialog.Field.Text);
    }

    [Fact]
    public void Lines_quoted_while_declining_go_into_the_reason()
    {
        using var dialog = Declining();
        dialog.NewKeyDownEvent(new Key('x'));
        dialog.NewKeyDownEvent(Key.Tab);
        dialog.NewKeyDownEvent(Key.CursorDown.WithShift);

        dialog.NewKeyDownEvent(new Key('q'));

        Assert.Equal("> line 1\n\n", dialog.Field.Text);
        Assert.True(dialog.Declining);
    }

    [Fact]
    public void An_empty_reason_declines_nothing_and_says_one_is_needed()
    {
        var declined = 0;
        using var dialog = Declining(_ => Task.FromResult<string?>(++declined < 0 ? "" : null));
        dialog.NewKeyDownEvent(new Key('x'));
        dialog.Field.Text = "  ";

        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);

        Assert.Equal(0, declined);
        Assert.Equal("A reason is needed to decline #180", dialog.Message.Says);
        Assert.True(dialog.CommentShown);
        Assert.False(dialog.Declined);
    }

    [Fact]
    public void Ctrl_Enter_declines_with_the_reason_rather_than_posting_it()
    {
        var reasons = new List<string>();
        var posted = 0;
        using var dialog = new ReaderDialog(Item, new IssueBody(Long(), History: Recorded), () => { },
            comment: Commenting(_ => Task.FromResult<string?>(++posted < 0 ? "" : null),
                reason => { reasons.Add(reason); return Task.FromResult<string?>(null); }), width: 140);
        dialog.NewKeyDownEvent(new Key('x'));
        dialog.Field.Text = "Already covered by #46.";

        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);

        Assert.Equal(["Already covered by #46."], reasons);
        Assert.Equal(0, posted);
        Assert.True(dialog.Declined);
    }

    [Fact]
    public void A_decline_that_fails_stays_in_the_pane_saying_why()
    {
        using var dialog = Declining(_ => Task.FromResult<string?>("board.sh: can't close #180 (gh: HTTP 403)"));
        dialog.NewKeyDownEvent(new Key('x'));
        dialog.Field.Text = "No.";

        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);

        Assert.False(dialog.Declined);
        Assert.True(dialog.Declining);
        Assert.Equal("No.", dialog.Field.Text);
        Assert.Equal("board.sh: can't close #180 (gh: HTTP 403)", dialog.Message.Says);
    }

    [Fact]
    public void Esc_turns_the_reason_back_into_a_comment_keeping_what_you_wrote()
    {
        var declined = 0;
        using var dialog = Declining(_ => Task.FromResult<string?>(++declined < 0 ? "" : null));
        dialog.NewKeyDownEvent(new Key('x'));
        dialog.Field.Text = "Not now";

        Assert.True(dialog.NewKeyDownEvent(Key.Esc));

        Assert.False(dialog.Declining);
        Assert.True(dialog.CommentShown);
        Assert.Equal("Comment", dialog.CommentTitle);
        Assert.Equal("Not now", dialog.Field.Text);
        Assert.Contains("Ctrl+Enter post", dialog.Hints.Says);
        Assert.Contains("x decline", dialog.Hints.Says);
        Assert.Equal(0, declined);
        Assert.False(dialog.Declined);
    }

    private static ReaderDialog Shelving(
        Func<int, string, Task<string?>>? shelve = null, bool open = false, Func<string, Task<string?>>? post = null)
    {
        var dialog = new ReaderDialog(Item, new IssueBody(Long(), History: Recorded), () => { },
            comment: new ReaderComment(new Key('c'), "comment", post ?? (_ => Task.FromResult<string?>(null)), new Epoch(),
                new ReaderDecline(new Key('x'), _ => Task.FromResult<string?>(null)),
                new ReaderShelve(new Key('l'), shelve ?? ((_, _) => Task.FromResult<string?>(null)), open)),
            width: 140);
        dialog.Layout(new Size(140, 20));
        return dialog;
    }

    [Fact]
    public void A_pitch_puts_not_now_after_decline()
    {
        using var dialog = Shelving();

        Assert.Equal(
            "Alt+↑/↓ remark · Tab switch pane · h hide history · Shift+arrows select · c comment · x decline · l not now · g on GitHub · Esc close",
            dialog.Hints.Says);
    }

    [Fact]
    public void l_opens_the_comment_pane_as_Not_now_with_two_weeks_above_the_reason()
    {
        using var dialog = Shelving();

        Assert.True(dialog.NewKeyDownEvent(new Key('l')));
        dialog.Layout(new Size(140, 20));

        Assert.True(dialog.Shelving);
        Assert.True(dialog.Field.HasFocus);
        Assert.Equal("Not now #180", dialog.CommentTitle);
        Assert.Equal(2, dialog.Weeks.Value);
        Assert.True(dialog.WeeksShown);
        Assert.Equal(2, dialog.Field.Frame.Y);
        Assert.Equal("Tab switch pane · Shift+arrows select · Ctrl+Enter not now · x decline · g on GitHub · Esc comment instead",
            dialog.Hints.Says);
    }

    [Fact]
    public void Opened_to_shelve_it_starts_in_the_reason()
    {
        using var dialog = Shelving(open: true);

        Assert.True(dialog.Shelving);
        Assert.True(dialog.Field.HasFocus);
    }

    [Fact]
    public void Shift_Tab_reaches_the_weeks_and_any_whole_number_from_one_goes()
    {
        using var dialog = Shelving(open: true);

        dialog.NewKeyDownEvent(Key.Tab.WithShift);
        Assert.True(dialog.Weeks.HasFocus);
        dialog.Weeks.Value = 19;
        dialog.Weeks.Value = 0;

        Assert.Equal(19, dialog.Weeks.Value);
        dialog.NewKeyDownEvent(Key.Tab);
        Assert.True(dialog.Field.HasFocus);
    }

    [Fact]
    public void Ctrl_Enter_shelves_for_the_weeks_with_or_without_a_reason()
    {
        var shelved = new List<(int Weeks, string Reason)>();
        var posted = 0;
        using var dialog = Shelving((weeks, reason) => { shelved.Add((weeks, reason)); return Task.FromResult<string?>(null); },
            post: _ => Task.FromResult<string?>(++posted < 0 ? "" : null));
        dialog.NewKeyDownEvent(new Key('l'));
        dialog.Weeks.Value = 3;

        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);

        Assert.Equal([(3, "")], shelved);
        Assert.Equal(0, posted);
        Assert.True(dialog.Shelved);
        Assert.False(dialog.Declined);
    }

    [Fact]
    public void Ctrl_Enter_from_the_weeks_shelves_too_with_the_reason()
    {
        var shelved = new List<(int Weeks, string Reason)>();
        using var dialog = Shelving((weeks, reason) => { shelved.Add((weeks, reason)); return Task.FromResult<string?>(null); });
        dialog.NewKeyDownEvent(new Key('l'));
        dialog.Field.Text = "Cost isn't what I'm watching yet.";
        dialog.NewKeyDownEvent(Key.Tab.WithShift);

        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);

        Assert.Equal([(2, "Cost isn't what I'm watching yet.")], shelved);
        Assert.True(dialog.Shelved);
    }

    [Fact]
    public void A_shelve_that_fails_stays_in_the_pane_saying_why()
    {
        using var dialog = Shelving((_, _) => Task.FromResult<string?>("board.sh: only a Pitched pitch can be shelved (#180 is in 'Idea')"));
        dialog.NewKeyDownEvent(new Key('l'));
        dialog.Field.Text = "Later.";

        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);

        Assert.False(dialog.Shelved);
        Assert.True(dialog.Shelving);
        Assert.Equal("Later.", dialog.Field.Text);
        Assert.Equal("board.sh: only a Pitched pitch can be shelved (#180 is in 'Idea')", dialog.Message.Says);
    }

    [Fact]
    public void Esc_turns_Not_now_back_into_a_comment_and_shelves_nothing()
    {
        var shelved = 0;
        using var dialog = Shelving((_, _) => Task.FromResult<string?>(++shelved < 0 ? "" : null));
        dialog.NewKeyDownEvent(new Key('l'));
        dialog.Field.Text = "Hmm";

        Assert.True(dialog.NewKeyDownEvent(Key.Esc));

        Assert.False(dialog.Shelving);
        Assert.False(dialog.WeeksShown);
        Assert.True(dialog.CommentShown);
        Assert.Equal("Comment", dialog.CommentTitle);
        Assert.Equal("Hmm", dialog.Field.Text);
        Assert.Contains("l not now", dialog.Hints.Says);
        Assert.Equal(0, shelved);
        Assert.False(dialog.Shelved);
    }

    [Fact]
    public void Esc_from_the_weeks_does_the_same_and_leaves_you_in_the_comment()
    {
        using var dialog = Shelving(open: true);
        dialog.NewKeyDownEvent(Key.Tab.WithShift);

        dialog.NewKeyDownEvent(Key.Esc);

        Assert.False(dialog.Shelving);
        Assert.True(dialog.Field.HasFocus);
    }

    [Fact]
    public void x_while_shelving_declines_instead_and_l_while_declining_shelves_instead()
    {
        using var dialog = Shelving();
        dialog.NewKeyDownEvent(new Key('l'));
        dialog.NewKeyDownEvent(Key.Tab);

        dialog.NewKeyDownEvent(new Key('x'));
        Assert.True(dialog.Declining);
        Assert.False(dialog.Shelving);
        Assert.False(dialog.WeeksShown);

        dialog.NewKeyDownEvent(Key.Tab);
        dialog.NewKeyDownEvent(new Key('l'));
        Assert.True(dialog.Shelving);
        Assert.False(dialog.Declining);
    }

    [Theory]
    [InlineData(2, 0, "2026-10-28", "28 Oct")]
    [InlineData(3, 0, "2026-11-04", "4 Nov")]
    [InlineData(19, 0, "2027-02-24", "24 Feb")]
    [InlineData(2, 13, "2026-10-29", "29 Oct")]
    [InlineData(2, -5, "2026-10-28", "28 Oct")]
    public void The_weeks_give_the_day_it_comes_back_on_the_local_calendar(int weeks, int offset, string until, string backOn)
    {
        var now = new DateTimeOffset(2026, 10, 14, 23, 30, 0, TimeSpan.Zero);
        var zone = TimeZoneInfo.CreateCustomTimeZone("test", TimeSpan.FromHours(offset), "test", "test");

        Assert.Equal(DateOnly.Parse(until, CultureInfo.InvariantCulture), ReaderShelve.Until(now, weeks, zone));
        Assert.Equal(backOn, ReaderShelve.BackOn(now, weeks, zone));
    }

    private sealed class Epoch : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch;
    }

    private static ReaderComment Commenting(
        Func<string, Task<string?>>? post = null, Func<string, Task<string?>>? decline = null, bool open = false) =>
        new(new Key('c'), "comment", post ?? (_ => Task.FromResult<string?>(null)), new Epoch(),
            decline is null ? null : new ReaderDecline(new Key('x'), decline, open));

    private static void Say(ReaderDialog dialog, string text)
    {
        dialog.NewKeyDownEvent(new Key('c'));
        dialog.Field.Text = text;
        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);
    }

    private static readonly DateTimeOffset Then = new(2026, 10, 12, 7, 0, 0, TimeSpan.Zero);

    private static readonly Remark[] Remarks =
    [
        new("dev", Then, Paragraph("First")),
        new("reviewer", Then.AddMinutes(10), Paragraph("Second")),
        new("dev", Then.AddMinutes(20), Paragraph("Third")),
    ];

    private static string Paragraph(string first) =>
        string.Join('\n', Enumerable.Range(1, 12).Select(line => $"{first} {line}"));

    private static IssueBody Told(DateTimeOffset? opened, string body = "", History? history = null) =>
        (new IssueBody(body.Length == 0 ? Long() : body, History: history) with { Opened = opened })
            .With(new Conversation(Remarks), opened);

    private static ReaderDialog Telling(IssueBody body, WaitingItem? item = null, int width = 60)
    {
        var dialog = new ReaderDialog(item ?? Item, body, () => { }, width: width);
        dialog.Layout(new Size(width, 14));
        return dialog;
    }

    [Fact]
    public void A_card_never_opened_opens_at_the_top_with_nothing_new()
    {
        using var dialog = Telling(Told(null));

        Assert.Equal(0, dialog.Body.Top);
        Assert.DoesNotContain(dialog.Body.Lines, line => line.Text.Contains(" · new "));
    }

    [Fact]
    public void A_card_opened_before_opens_at_the_first_remark_since_with_new_on_each_one_since()
    {
        var body = Told(Then.AddMinutes(5));
        using var dialog = Telling(body);

        Assert.Equal(body.Remarks[1].Line, dialog.Body.TopLine);
        Assert.Equal(body.Remarks[1].Line, dialog.Body.Caret?.Line);
        Assert.Equal([false, true, true], body.Remarks.Select(said => dialog.Body.Lines[said.Line].Text.EndsWith(" · new ─────")));
    }

    [Fact]
    public void A_PR_line_heads_the_body_and_a_card_still_opens_at_its_first_new_remark()
    {
        var body = Told(Then.AddMinutes(5)).Headed("PR #459 · feat/path-pickers → main");
        using var dialog = Telling(body);

        Assert.Equal("PR #459 · feat/path-pickers → main", dialog.Body.Lines[0].Text);
        Assert.Equal(body.Remarks[1].Line, dialog.Body.TopLine);
        Assert.EndsWith(" · new ─────", dialog.Body.Lines[body.Remarks[1].Line].Text);
    }

    [Fact]
    public void A_card_with_nothing_new_since_opens_at_the_newest_remark()
    {
        var body = Told(Then.AddMinutes(30));
        using var dialog = Telling(body);

        Assert.Equal(body.Remarks[2].Line, dialog.Body.TopLine);
        Assert.DoesNotContain(dialog.Body.Lines, line => line.Text.Contains(" · new "));
    }

    [Fact]
    public void A_task_to_try_opens_where_any_card_would()
    {
        var body = Told(Then.AddMinutes(30));
        using var dialog = new ReaderDialog(Item with { Pr = 9 }, body, () => { }, tryIt: new ReaderTry(new Key('t'), (_, _) => { }));
        dialog.Layout(new Size(60, 14));

        Assert.Equal(body.Remarks[2].Line, dialog.Body.TopLine);
    }

    [Fact]
    public void A_card_with_nothing_said_opens_at_the_top()
    {
        using var dialog = Telling((new IssueBody(Long()) with { Opened = Then }).With(new Conversation([]), Then));

        Assert.Equal(0, dialog.Body.Top);
    }

    [Fact]
    public void A_Dev_s_question_opens_at_the_Dev_s_comment()
    {
        var body = Told(Then.AddMinutes(5));
        using var dialog = Telling(body, Item with { Status = "Ready", Question = Remarks[0].Body });

        Assert.Equal(body.Remarks[0].Line, dialog.Body.TopLine);
    }

    [Fact]
    public void A_Dev_s_question_opens_at_its_marked_comment_though_the_Dev_spoke_after()
    {
        var body = (new IssueBody(Long()) with { Opened = null }).With(new Conversation(
        [
            new("dev", Then, $"{Paragraph("Asked")}\n\n<!-- a-team:dev -->"),
            new("dev", Then.AddMinutes(10), $"{Paragraph("Later")}\n\n<!-- a-team:dev -->"),
        ]), null);
        using var dialog = Telling(body, Item with { Status = "Ready", Question = Paragraph("Asked") });

        Assert.Equal(body.Remarks[0].Line, dialog.Body.TopLine);
    }

    [Fact]
    public void A_Lead_s_question_opens_at_the_pitch_s_Needs_your_answer_heading()
    {
        var body = Told(null, $"{Long()}\n\n## Needs your answer\n\n1. Which?\n\n{Long()}");
        using var dialog = Telling(body, Item with { Pitch = true, Question = "## Needs your answer\n\n1. Which?" });

        Assert.Equal("## Needs your answer", dialog.Body.Lines[dialog.Body.TopLine].Text);
        Assert.Equal("#180  The dashboard tells me a pitch needs me · the Lead's question", dialog.Title);
    }

    [Fact]
    public void Alt_arrows_go_between_the_remarks_headings_with_the_top_of_the_body_the_first()
    {
        var body = Told(null);
        var headings = body.Remarks.Select(said => said.Line).ToList();
        using var dialog = Telling(body);
        int? Jump(Key key)
        {
            dialog.NewKeyDownEvent(key);
            dialog.Layout(new Size(60, 14));
            return dialog.Body.Caret?.Line;
        }

        Assert.Equal(0, Jump(Key.CursorUp.WithAlt));
        Assert.Equal(headings[0], Jump(Key.CursorDown.WithAlt));
        Assert.Equal(headings[0], dialog.Body.TopLine);
        Assert.Equal(headings[1], Jump(Key.CursorDown.WithAlt));
        dialog.NewKeyDownEvent(Key.CursorDown);
        dialog.NewKeyDownEvent(Key.CursorDown);
        Assert.Equal(headings[1], Jump(Key.CursorUp.WithAlt));
        Assert.Equal(headings[2], Jump(Key.CursorDown.WithAlt));
        Assert.Equal(headings[2], Jump(Key.CursorDown.WithAlt));
        dialog.NewKeyDownEvent(Key.End.WithCtrl);
        Assert.Equal(headings[2], Jump(Key.CursorUp.WithAlt));
        Assert.Equal(headings[1], Jump(Key.CursorUp.WithAlt));
        Assert.Equal(headings[0], Jump(Key.CursorUp.WithAlt));
        Assert.Equal(0, Jump(Key.CursorUp.WithAlt));
    }

    private static readonly History Commented = new([
        new HistoryEvent(Then.AddMinutes(20).AddSeconds(4), "dev", "commented"),
        new HistoryEvent(Then.AddMinutes(10).AddSeconds(2), "reviewer", "commented"),
        new HistoryEvent(Then.AddMinutes(5), "dev", "Ready → In progress"),
        new HistoryEvent(Then.AddMinutes(1), "dev", "commented", new Run(Then.AddMinutes(4), 1.5m, "ok")),
        new HistoryEvent(Then, "dev", "commented"),
    ]);

    [Fact]
    public void Enter_on_a_comment_in_History_shows_that_remark_in_the_Body()
    {
        var body = Told(null, history: Commented);
        using var dialog = Telling(body, width: 120);
        dialog.NewKeyDownEvent(Key.Tab);
        Assert.True(dialog.HistoryLog.HasFocus);
        Assert.Contains("Enter show", dialog.Hints.Says);
        Assert.DoesNotContain("Alt+↑/↓ remark", dialog.Hints.Says);
        dialog.NewKeyDownEvent(Key.CursorDown);

        Assert.True(dialog.NewKeyDownEvent(Key.Enter));
        dialog.Layout(new Size(120, 14));

        Assert.True(dialog.Body.HasFocus);
        Assert.Equal(body.Remarks[1].Line, dialog.Body.TopLine);
        Assert.Equal(body.Remarks[1].Line, dialog.Body.Caret?.Line);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Enter_on_a_move_or_a_run_in_History_does_nothing(int line)
    {
        using var dialog = Telling(Told(null, history: Commented), width: 120);
        dialog.NewKeyDownEvent(Key.Tab);
        for (var i = 0; i < line; i++)
            dialog.NewKeyDownEvent(Key.CursorDown);

        dialog.NewKeyDownEvent(Key.Enter);

        Assert.True(dialog.HistoryLog.HasFocus);
        Assert.Equal(0, dialog.Body.Top);
    }

    [Fact]
    public void A_click_on_a_comment_in_History_shows_that_remark_and_a_click_on_a_move_does_nothing()
    {
        var body = Told(null, history: Commented);
        using var dialog = Telling(body, width: 120);
        void Click(int row) => dialog.HistoryLog.NewMouseEvent(new Mouse
        {
            Flags = MouseFlags.LeftButtonClicked, Position = new Point(2, row), ScreenPosition = new Point(2, row),
        });

        Click(2);
        Assert.Equal(0, dialog.Body.Top);

        Click(4);
        dialog.Layout(new Size(120, 14));
        Assert.True(dialog.Body.HasFocus);
        Assert.Equal(body.Remarks[0].Line, dialog.Body.TopLine);
    }

    private static ReaderDialog Attaching(List<(string Team, string File)> uploads, Func<string, Task<string?>>? post = null)
    {
        var dialog = new ReaderDialog(Item, new IssueBody(Pitch), () => { },
            comment: Commenting(post) with
            {
                Attach = new Attaching(Key.U.WithCtrl, (team, file) =>
                {
                    uploads.Add((team, file));
                    return Task.FromResult(new Reading("https://example.com/shot.png", null));
                }, new ImageAttachTests.Clipboard(png: 10), () => null),
            });
        dialog.Layout(new Size(140, 20));
        return dialog;
    }

    [Fact]
    public void Writing_a_comment_offers_paste_and_attach_file()
    {
        using var dialog = Attaching([]);

        dialog.NewKeyDownEvent(new Key('c'));

        Assert.Equal("Tab switch pane · Shift+arrows select · Ctrl+Enter post · Ctrl+V paste · Ctrl+U attach file · g on GitHub · Esc back", dialog.Hints.Says);
    }

    [Fact]
    public void Ctrl_V_in_the_comment_attaches_the_screenshot_to_the_item_s_team()
    {
        var uploads = new List<(string Team, string File)>();
        using var dialog = Attaching(uploads);
        dialog.NewKeyDownEvent(new Key('c'));

        dialog.Field.NewKeyDownEvent(Key.V.WithCtrl);

        Assert.Equal(["a-team"], uploads.Select(upload => upload.Team));
        Assert.Matches(@"^!\[Screenshot .*\]\(https://example.com/shot.png\)$", dialog.Field.Text);
    }

    [Fact]
    public void A_dropped_image_attaches_only_while_the_comment_has_the_keyboard()
    {
        var image = Path.Combine(Directory.CreateTempSubdirectory().FullName, "shot.png");
        File.WriteAllBytes(image, new byte[10]);
        var uploads = new List<(string Team, string File)>();
        using var dialog = Attaching(uploads);
        var reading = new PasteEventArgs(image);

        dialog.OnAppPaste(null, reading);
        dialog.NewKeyDownEvent(new Key('c'));
        var writing = new PasteEventArgs(image);
        dialog.OnAppPaste(null, writing);

        Assert.False(reading.Handled);
        Assert.True(writing.Handled);
        Assert.Equal([image], uploads.Select(upload => upload.File));
        Assert.Equal("![shot](https://example.com/shot.png)", dialog.Field.Text);
    }

    private static string Long() =>
        string.Join('\n', Enumerable.Range(1, 60).Select(line => $"line {line}"));

    private static readonly IssueBody Talked = new IssueBody("Body line 1\nBody line 2").With(new Conversation([
        new Remark("dev", DateTimeOffset.UnixEpoch, "first a\nfirst b", Url: "https://github.com/x/180#issuecomment-1"),
        new Remark("you", DateTimeOffset.UnixEpoch.AddHours(1), "second", Url: "https://github.com/x/180#issuecomment-2"),
    ]));

    [Fact]
    public void An_idea_from_a_comment_quotes_it_and_links_to_that_comment()
    {
        Assert.Equal(
            "> first a\n> first b\n\nFrom https://github.com/x/180#issuecomment-1",
            ReaderDialog.IdeaFrom(Talked, Item.Url, ["first a", "first b"], 5));
    }

    [Fact]
    public void An_idea_from_the_body_links_to_the_item()
    {
        Assert.Equal("> Body line 2\n\nFrom https://github.com/x/180", ReaderDialog.IdeaFrom(Talked, Item.Url, ["Body line 2"], 1));
    }

    [Fact]
    public void An_idea_from_two_remarks_links_to_the_first()
    {
        Assert.EndsWith(
            "> second\n\nFrom https://github.com/x/180#issuecomment-1",
            ReaderDialog.IdeaFrom(Talked, Item.Url, ["first b", "", "───── you ─────", "", "second"], 6));
    }

    [Fact]
    public void An_idea_with_nothing_selected_only_links_to_the_item()
    {
        Assert.Equal("From https://github.com/x/180", ReaderDialog.IdeaFrom(Talked, Item.Url, [], null));
    }

    [Fact]
    public void An_idea_from_below_the_last_remark_links_to_the_item()
    {
        var shown = Talked with { Text = Talked.Text + "\n\n---\n\n# docs/vision.md\n\nThe vision" };

        Assert.Equal("> The vision\n\nFrom https://github.com/x/180", ReaderDialog.IdeaFrom(shown, Item.Url, ["The vision"], 16));
    }

    private static ReaderDialog Ideas(List<string> filed, Func<string, string?>? file = null, Rank? rank = null)
    {
        var dialog = new ReaderDialog(Item, Talked, () => { }, rank: rank,
            idea: new ReaderIdea(new Key('n'), description =>
            {
                filed.Add(description);
                return file?.Invoke(description);
            }));
        dialog.Layout(new Size(140, 20));
        return dialog;
    }

    private static void SelectFirstComment(ReaderDialog dialog)
    {
        for (var i = 0; i < 5; i++)
            dialog.NewKeyDownEvent(Key.CursorDown);
        dialog.NewKeyDownEvent(Key.CursorDown.WithShift);
        dialog.NewKeyDownEvent(Key.CursorDown.WithShift);
    }

    [Fact]
    public void n_files_an_idea_from_the_selected_lines()
    {
        var filed = new List<string>();
        using var dialog = Ideas(filed);
        SelectFirstComment(dialog);

        Assert.True(dialog.NewKeyDownEvent(new Key('n')));

        Assert.Equal(["> first a\n> first b\n\nFrom https://github.com/x/180#issuecomment-1"], filed);
    }

    [Fact]
    public void n_with_nothing_selected_files_an_idea_linking_the_item()
    {
        var filed = new List<string>();
        using var dialog = Ideas(filed);

        dialog.NewKeyDownEvent(new Key('n'));

        Assert.Equal(["From https://github.com/x/180"], filed);
    }

    [Fact]
    public void An_added_idea_is_said_and_clears_the_selection_and_a_cancelled_one_keeps_it()
    {
        var filed = new List<string>();
        string? added = null;
        using var dialog = Ideas(filed, _ => added);
        SelectFirstComment(dialog);

        dialog.NewKeyDownEvent(new Key('n'));
        Assert.Equal(2, dialog.Body.Marked);
        Assert.Equal("", dialog.Message.Says);

        added = "#412 added to a-team's ideas";
        dialog.NewKeyDownEvent(new Key('n'));
        Assert.Equal(0, dialog.Body.Marked);
        Assert.Equal("#412 added to a-team's ideas", dialog.Message.Says);
    }

    [Fact]
    public void The_hints_offer_a_new_idea_and_count_the_lines_it_would_quote()
    {
        using var dialog = Replying();
        using var ideas = new ReaderDialog(Item, Talked, () => { }, comment: Commenting(null),
            idea: new ReaderIdea(new Key('n'), _ => null));
        ideas.Layout(new Size(140, 20));

        Assert.Contains("Shift+arrows select · n new idea · c comment", ideas.Hints.Says);
        SelectFirstComment(ideas);
        Assert.Contains("Ctrl+C copy · q quote 2 lines · n idea from 2 lines · c comment", ideas.Hints.Says);
        Assert.DoesNotContain("idea", dialog.Hints.Says);
    }

    [Fact]
    public void Commands_runs_the_reader_s_own_new_idea()
    {
        var filed = new List<string>();
        var idea = new ReaderIdea(new Key('n'), description =>
        {
            filed.Add(description);
            return null;
        });
        using var dialog = new ReaderDialog(Item, Talked, () => { }, idea: idea);
        dialog.Layout(new Size(140, 20));

        idea.Start!();

        Assert.Equal(["From https://github.com/x/180"], filed);
    }

    [Fact]
    public void With_a_row_of_ranks_n_is_still_None_and_no_idea_is_offered()
    {
        var filed = new List<string>();
        using var dialog = Ideas(filed, rank: Rank.High);

        dialog.NewKeyDownEvent(new Key('n'));

        Assert.Empty(filed);
        Assert.DoesNotContain("idea", dialog.Hints.Says);
    }

    [Fact]
    public void Keys_the_reader_leaves_alone_go_to_the_window()
    {
        var passed = new List<Key>();
        using var dialog = new ReaderDialog(Item, Talked, () => { }, window: key =>
        {
            passed.Add(key);
            return key == Key.F1;
        });
        dialog.Layout(new Size(140, 20));

        Assert.True(dialog.NewKeyDownEvent(Key.F1));
        dialog.NewKeyDownEvent(Key.CursorDown);

        Assert.Equal([Key.F1], passed);
    }
}
