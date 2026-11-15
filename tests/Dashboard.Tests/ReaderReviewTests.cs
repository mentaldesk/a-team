using System.Drawing;
using MentalDesk.Tui.Layout;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;

namespace ATeam.Dashboard.Tests;

/// <summary>The reader's Review tab: the Reviewer's comment on a reviewed task's PR, beside History.</summary>
public class ReaderReviewTests
{
    private const string Review = "## Needs changing\n\nNothing needs changing.\n\n## Worth considering\n\n- Rename it.\n- Match the branch too.";

    private static readonly DateTimeOffset At = new(2026, 11, 15, 9, 12, 0, TimeSpan.Zero);

    private static readonly WaitingItem Task =
        new(829, "Ctrl+F in Overseer filters every lane", "In review", "https://github.com/x/829", "a-team", "you",
            Pr: 830, Reviewed: true, Consider: 2);

    private static IssueBody Body(params Remark[] remarks) =>
        new IssueBody("## Context\n\nFinding a card means reading every lane.", History: new History([]))
            .With(new Conversation(remarks));

    private static IssueBody Reviewed() => Body(
        new Remark("dev", At, "## Worth considering\n\n- The Dev's own.", 830),
        new Remark("reviewer", At, Review, 830),
        new Remark("you", At, "Looks good.", 830));

    private static ReaderDialog Open(
        WaitingItem item, IssueBody body, ReaderPanes? panes = null, ReaderComment? comment = null, int width = 140)
    {
        var dialog = new ReaderDialog(item, body, () => null, comment: comment, panes: panes, width: width);
        dialog.Layout(new Size(width, 30));
        return dialog;
    }

    private static ReaderComment Commenting(ReaderCommand? fix = null, ReaderReview? review = null) =>
        new(new Key('c'), "comment", _ => System.Threading.Tasks.Task.FromResult<string?>(null), Fix: fix, Review: review);

    private static string Text(LogView log) => string.Join('\n', log.Lines.Select(line => line.Text));

    [Fact]
    public void A_reviewed_task_shows_History_and_Review_tabs_with_the_review_as_written()
    {
        using var dialog = Open(Task, Reviewed());

        Assert.NotNull(dialog.SideTabs);
        Assert.Equal(["History", "Review · 2"], dialog.SideTabs!.SubViews.Select(tab => tab.Title).Order());
        var review = Text(dialog.ReviewLog);
        Assert.Contains("reviewer", review);
        Assert.True(review.IndexOf("Needs changing", StringComparison.Ordinal) < review.IndexOf("Worth considering", StringComparison.Ordinal));
        Assert.DoesNotContain("The Dev's own.", review);
        Assert.DoesNotContain("Looks good.", review);
        Assert.Contains("v Review", dialog.ButtonLabels);
    }

    [Fact]
    public void A_card_the_Reviewer_has_not_reviewed_shows_History_alone()
    {
        using var unreviewed = Open(Task with { Reviewed = false, Consider = 0 }, Body(new Remark("dev", At, "Marked ready.", 830)));
        using var pitch = Open(Task with { Pr = 0, Reviewed = false, Consider = 0, Pitch = true, Status = "Pitched" }, Body());

        foreach (var dialog in new[] { unreviewed, pitch })
        {
            Assert.Null(dialog.SideTabs);
            Assert.True(dialog.HistoryShown);
            Assert.DoesNotContain("v Review", dialog.ButtonLabels);
            Assert.False(dialog.NewKeyDownEvent(new Key('v')));
        }
    }

    [Fact]
    public void Only_the_Reviewer_s_comment_on_the_task_s_own_PR_is_the_review()
    {
        var body = Body(
            new Remark("reviewer", At, "On another PR.", 12),
            new Remark("dev", At, "The Dev's.", 830),
            new Remark("you", At, "Yours.", 830),
            new Remark("reviewer", At, "This one.", 830));

        Assert.Equal("This one.", body.Review(830)?.Body);
        Assert.Null(body.Review(831));
        Assert.Null(body.Review(0));
    }

    [Theory]
    [InlineData(2, "Review · 2", "reviewed · 2 to consider")]
    [InlineData(1, "Review · 1", "reviewed · 1 to consider")]
    [InlineData(0, "Review", "reviewed")]
    public void The_tab_counts_the_points_to_consider_as_the_card_does(int consider, string title, string card)
    {
        var item = Task with { Consider = consider };
        using var dialog = Open(item, Reviewed());

        Assert.Equal(title, dialog.ReviewTabTitle);
        Assert.Equal(card, item.Review);
    }

    [Fact]
    public void v_brings_Review_forward_with_focus_from_the_body_and_from_History()
    {
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        using var dialog = Open(Task, Reviewed());
        app.Begin(dialog);
        dialog.Layout(new Size(140, 30));
        Assert.True(dialog.Body.HasFocus);
        Assert.False(dialog.ReviewInFront);

        Assert.True(dialog.NewKeyDownEvent(new Key('v')));
        Assert.True(dialog.ReviewInFront);
        Assert.True(dialog.ReviewLog.HasFocus);

        dialog.SideTabs!.Value = dialog.SideTabs.SubViews.Single(tab => tab.Title == "History");
        Assert.True(dialog.HistoryLog.HasFocus);
        Assert.True(dialog.NewKeyDownEvent(new Key('v')));
        Assert.True(dialog.ReviewInFront);
        Assert.True(dialog.ReviewLog.HasFocus);
    }

    [Fact]
    public void v_shows_the_pane_h_had_hidden()
    {
        var panes = new ReaderPanes();
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        using var dialog = Open(Task, Reviewed(), panes);
        app.Begin(dialog);

        Assert.True(dialog.NewKeyDownEvent(new Key('h')));
        Assert.False(dialog.HistoryShown);
        Assert.True(dialog.NewKeyDownEvent(new Key('v')));

        Assert.True(dialog.HistoryShown);
        Assert.True(panes.HistoryShown);
        Assert.True(dialog.ReviewLog.HasFocus);
    }

    [Fact]
    public void Tab_moves_between_the_body_and_the_tab_in_front()
    {
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        using var dialog = Open(Task, Reviewed());
        app.Begin(dialog);
        dialog.NewKeyDownEvent(new Key('v'));

        Assert.True(dialog.NewKeyDownEvent(Key.Tab));
        Assert.True(dialog.Body.HasFocus);
        Assert.True(dialog.NewKeyDownEvent(Key.Tab));
        Assert.True(dialog.ReviewLog.HasFocus);
    }

    [Fact]
    public void The_tab_last_in_front_is_the_one_the_next_reviewed_card_opens_with()
    {
        var panes = new ReaderPanes();
        using (var first = Open(Task, Reviewed(), panes))
            first.NewKeyDownEvent(new Key('v'));

        using (var unreviewed = Open(Task with { Reviewed = false }, Body(), panes))
            Assert.Null(unreviewed.SideTabs);

        using (var next = Open(Task with { Number = 840, Pr = 830 }, Reviewed(), panes))
        {
            Assert.True(next.ReviewInFront);
            next.SideTabs!.Value = next.SideTabs.SubViews.Single(tab => tab.Title == "History");
        }

        using var last = Open(Task, Reviewed(), panes);
        Assert.False(last.ReviewInFront);
    }

    [Fact]
    public void The_window_s_key_for_Show_review_is_the_one_the_reader_answers_and_Commands_can_run_it()
    {
        var review = new ReaderReview(new Key('w'));
        using var dialog = Open(Task, Reviewed(), comment: Commenting(review: review));

        Assert.False(dialog.NewKeyDownEvent(new Key('v')));
        Assert.Contains("w Review", dialog.ButtonLabels);
        Assert.True(dialog.NewKeyDownEvent(new Key('w')));
        Assert.True(dialog.ReviewInFront);

        dialog.SideTabs!.Value = dialog.SideTabs.SubViews.Single(tab => tab.Title == "History");
        review.Show!();
        Assert.True(dialog.ReviewInFront);
    }

    [Fact]
    public void Commands_can_t_show_a_review_on_a_card_without_one()
    {
        var review = new ReaderReview(new Key('v'));
        using var dialog = Open(Task with { Reviewed = false }, Body(), comment: Commenting(review: review));

        Assert.Null(review.Show);
    }

    [Fact]
    public void F1_lists_v_and_f_asks_the_Dev_to_fix_from_Review()
    {
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        var asked = 0;
        using var dialog = Open(Task, Reviewed(), comment: Commenting(new ReaderCommand(new Key('F'), "ask the Dev to fix", () =>
        {
            asked++;
            return false;
        })));
        app.Begin(dialog);
        dialog.NewKeyDownEvent(new Key('v'));

        Assert.Contains(DialogTests.Rows(DialogTests.F1(dialog)), row => row is { Keys: "v", Label: "Show review" });
        Assert.True(dialog.NewKeyDownEvent(new Key('f')));
        Assert.Equal(1, asked);
    }

    [Fact]
    public void A_selection_in_Review_copies_and_quotes_into_the_comment()
    {
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        var clipboard = new InstalledTerminal.Remembering();
        var dialog = new ReaderDialog(Task, Reviewed(), () => null, comment: Commenting(), width: 140, clipboard: clipboard);
        using var _ = dialog;
        app.Begin(dialog);
        dialog.Layout(new Size(140, 30));
        dialog.NewKeyDownEvent(new Key('v'));
        dialog.NewKeyDownEvent(Key.CursorDown.WithShift);
        dialog.NewKeyDownEvent(Key.CursorDown.WithShift);
        Assert.True(dialog.ReviewLog.Marked > 0);

        Assert.True(dialog.NewKeyDownEvent(Key.C.WithCtrl));
        Assert.Contains("reviewer", clipboard.Text);
        Assert.True(dialog.NewKeyDownEvent(new Key('q')));
        Assert.True(dialog.CommentShown);
        Assert.StartsWith("> ", dialog.Field.Text);
    }

    [Fact]
    public void The_pane_keeps_its_width_when_switching_tabs()
    {
        using var dialog = Open(Task, Reviewed());
        dialog.NewKeyDownEvent(new Key('v'));
        Assert.True(dialog.Sides.CanResize);
        dialog.Sides.Widen();
        dialog.Layout(new Size(140, 30));
        var widened = dialog.SideTabs!.SuperView!.Frame.Width;
        Assert.Equal(ReaderPanes.HistoryWidth + SidePanes.Step, widened);

        dialog.SideTabs.Value = dialog.SideTabs.SubViews.Single(tab => tab.Title == "History");
        dialog.Layout(new Size(140, 30));

        Assert.Equal(widened, dialog.SideTabs.SuperView!.Frame.Width);
    }
}
