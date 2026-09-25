using System.Drawing;

namespace ATeam.Dashboard.Tests;

public class WorkViewTests
{
    private static readonly WaitingItem[] Waiting =
    [
        new(107, "When the dashboard goes quiet, I can't tell why", "Pitched", "https://github.com/x/1", "a-team",
            "you", "awaiting your approval since 08:14", Priority: "Urgent"),
        new(108, "A misconfigured team looks like a working one", "Pitched", "https://github.com/x/2", "a-team",
            "lead", "answering your feedback since 09:30", Priority: "High"),
        new(49, "I can't change any of the dashboard's keys", "In review", "https://github.com/x/3", "a-team",
            "dev", "answering your feedback since 10:15", Pr: 122, PrUrl: "https://github.com/x/pull/122",
            Priority: "Medium"),
        new(133, "Notice when open files change on disk", "Pitched", "https://github.com/x/4", "tuicode",
            "you", "awaiting your approval since 21:37", Priority: "Low"),
        new(6, "The agents can't say what they'd change", "Idea", "https://github.com/x/5", "a-team",
            "you", "waiting to be ranked"),
    ];

    [Fact]
    public void Every_team_gets_a_swimlane_and_every_lane_a_column_per_gate()
    {
        using var view = Open(["a-team", "tuicode"]);

        Assert.Equal(["a-team", "tuicode"], view.Lanes.Select(lane => lane.Team));
        Assert.All(view.Lanes, lane => Assert.Equal(["Triage", "Pitches", "Review"], lane.Columns.Select(column => column.Gate)));
    }

    [Fact]
    public void A_column_is_titled_with_its_count_and_an_empty_one_still_draws()
    {
        using var view = Open(["a-team", "tuicode"]);

        view.Show(Waiting);
        LayOut(view, 120, 20);

        Assert.Equal(["Triage · 1", "Pitches · 2", "Review · 1", "Triage · 0", "Pitches · 1", "Review · 0"], Titles(view));
        Assert.All(Cells(view), cell => Assert.True(cell.Width > 0 && cell.Height > 0));
    }

    [Fact]
    public void The_columns_of_a_lane_divide_its_width_between_them()
    {
        using var view = Open(["a-team", "tuicode"]);

        view.Show(Waiting);
        var cells = Cells(view);

        Assert.Equal([0, 40, 80, 0, 40, 80], cells.Select(cell => cell.X));
        Assert.Equal([40, 40, 40, 40, 40, 40], cells.Select(cell => cell.Width));
    }

    [Fact]
    public void A_lane_is_as_tall_as_its_fullest_column_and_never_shorter_than_one_card()
    {
        using var view = Open(["a-team", "tuicode"]);

        view.Show(Waiting);
        LayOut(view, 120, 20);

        Assert.Equal([2, 1], view.Lanes.Select(lane => lane.Rows));
        Assert.Equal(view.Lanes[0].Frame.Bottom, view.Lanes[1].Frame.Y);
    }

    [Fact]
    public void A_lane_is_headed_by_its_team_and_a_rule_to_the_right_edge()
    {
        using var view = Open(["a-team"]);

        LayOut(view, 40, 20);

        Assert.Equal("a-team " + new string('─', 33), view.Lanes[0].Header);
    }

    [Fact]
    public void Focus_starts_on_the_first_card_of_the_first_column_that_has_one()
    {
        using var view = Open(["a-team", "tuicode"]);
        view.Show([.. Waiting.Where(item => item.Status == "In review")]);
        LayOut(view, 120, 20);

        view.FocusFirstCard();

        Assert.Equal("Review · a-team", view.Region);
        Assert.Equal(49, view.Selected?.Number);
        Assert.Equal([false, false, true, false, false, false],
            view.Lanes.SelectMany(lane => lane.Columns).Select(column => column.Shown));
    }

    [Fact]
    public void With_nothing_waiting_focus_still_lands_somewhere()
    {
        using var view = Open(["a-team"]);
        view.Show([]);
        LayOut(view, 120, 20);

        view.FocusFirstCard();

        Assert.Equal("Triage · a-team", view.Region);
        Assert.Null(view.Selected);
    }

    [Fact]
    public void Right_and_left_step_between_the_columns_of_a_lane()
    {
        using var view = Open(["a-team", "tuicode"]);
        view.Show(Waiting);
        LayOut(view, 120, 20);
        view.FocusFirstCard();

        view.MoveColumn(+1);

        Assert.Equal("Pitches · a-team", view.Region);
        Assert.Equal(107, view.Selected?.Number);

        view.MoveColumn(+1);

        Assert.Equal("Review · a-team", view.Region);
        Assert.Equal(49, view.Selected?.Number);

        view.MoveColumn(-1);

        Assert.Equal("Pitches · a-team", view.Region);
        Assert.Equal(107, view.Selected?.Number);
    }

    [Fact]
    public void A_lane_has_no_column_past_its_edges()
    {
        using var view = Open(["a-team", "tuicode"]);
        view.Show(Waiting);
        LayOut(view, 120, 20);
        view.FocusFirstCard();

        view.MoveColumn(-1);
        Assert.Equal("Triage · a-team", view.Region);

        view.MoveColumn(+1);
        view.MoveColumn(+1);
        view.MoveColumn(+1);
        Assert.Equal("Review · a-team", view.Region);
    }

    [Fact]
    public void An_empty_column_can_still_be_reached()
    {
        using var view = Open(["a-team", "tuicode"]);
        view.Show(Waiting);
        LayOut(view, 120, 20);
        view.FocusFirstCard();
        view.MoveColumn(+1);
        view.MoveCard(+1);
        view.MoveCard(+1);

        view.MoveColumn(+1);

        Assert.Equal("Review · tuicode", view.Region);
        Assert.Null(view.Selected);
    }

    [Fact]
    public void Down_walks_a_columns_cards_and_then_the_next_lanes()
    {
        using var view = Open(["a-team", "tuicode"]);
        view.Show(Waiting);
        LayOut(view, 120, 20);
        view.FocusFirstCard();
        view.MoveColumn(+1);

        view.MoveCard(+1);
        Assert.Equal(108, view.Selected?.Number);

        view.MoveCard(+1);
        Assert.Equal("Pitches · tuicode", view.Region);
        Assert.Equal(133, view.Selected?.Number);

        view.MoveCard(+1);
        Assert.Equal(133, view.Selected?.Number);
    }

    [Fact]
    public void Up_from_the_first_card_lands_on_the_last_of_the_lane_above()
    {
        using var view = Open(["a-team", "tuicode"]);
        view.Show(Waiting);
        LayOut(view, 120, 20);
        view.FocusFirstCard();
        view.MoveColumn(+1);
        view.MoveCard(+1);
        view.MoveCard(+1);

        view.MoveCard(-1);

        Assert.Equal("Pitches · a-team", view.Region);
        Assert.Equal(108, view.Selected?.Number);
    }

    [Fact]
    public void A_card_below_the_window_is_scrolled_into_view()
    {
        using var view = Open(["a-team", "tuicode"]);
        view.Show(Waiting);
        LayOut(view, 120, 8);
        view.FocusFirstCard();
        view.MoveColumn(+1);

        view.MoveCard(+1);
        view.MoveCard(+1);

        Assert.Equal("Pitches · tuicode", view.Region);
        Assert.True(view.Viewport.Y > 0);
    }

    [Theory]
    [InlineData(41, "#107  When the dashboard goes quiet, I c…")]
    [InlineData(53, "#107  When the dashboard goes quiet, I can't tell why")]
    public void A_card_reads_as_the_number_then_as_much_of_the_title_as_fits(int width, string expected)
    {
        Assert.Equal(expected, new Card(Waiting[0], false).Text(width));
        Assert.True(new Card(Waiting[0], false).Text(width).Length <= width);
    }

    [Fact]
    public void A_card_puts_the_role_first_only_where_the_move_isn_t_yours()
    {
        Assert.Equal("#107  When the dashboard goes quiet, I can't tell why", new Card(Waiting[0], false).Text(0));
        Assert.Equal("#108  lead · A misconfigured team looks like a working one", new Card(Waiting[1], false).Text(0));
        Assert.Equal("#49  dev · I can't change any of the dashboard's keys", new Card(Waiting[2], false).Text(0));
    }

    [Fact]
    public void A_card_whose_PR_is_in_trouble_says_so_between_the_role_and_the_title()
    {
        var failing = new WaitingItem(116, "I can change a key from the dashboard", "In review",
            "https://github.com/x/6", "a-team", "dev", "CI failing since 09:02",
            Pr: 131, PrUrl: "https://github.com/x/pull/131", Trouble: "CI failing");

        Assert.Equal("#116  dev · CI failing · I can change a key from the dashboard", new Card(failing, false).Text(0));
    }

    [Fact]
    public void A_card_the_board_said_nothing_about_is_read_as_yours()
    {
        var unsaid = new WaitingItem(12, "Whatever this is", "Pitched", "https://github.com/x/5", "a-team");

        Assert.True(unsaid.Mine);
        Assert.Equal("#12  Whatever this is", new Card(unsaid, false).Text(0));
    }

    [Fact]
    public void Only_mine_hides_the_rest_and_every_count_follows_it()
    {
        using var view = Open(["a-team", "tuicode"]);
        view.Show(Waiting);
        LayOut(view, 120, 20);

        view.ShowOnlyMine(true);
        LayOut(view, 120, 20);

        Assert.True(view.OnlyMine);
        Assert.Equal(["Triage · 1", "Pitches · 1", "Review · 0", "Triage · 0", "Pitches · 1", "Review · 0"], Titles(view));
        Assert.All(Cells(view), cell => Assert.True(cell.Width > 0 && cell.Height > 0));

        view.ShowOnlyMine(false);
        LayOut(view, 120, 20);

        Assert.False(view.OnlyMine);
        Assert.Equal(["Triage · 1", "Pitches · 2", "Review · 1", "Triage · 0", "Pitches · 1", "Review · 0"],
            Titles(view));
    }

    [Fact]
    public void Filtering_stays_on_the_selected_card_where_it_survives_the_filter()
    {
        using var view = Open(["a-team", "tuicode"]);
        view.Show(Waiting);
        LayOut(view, 120, 20);
        view.FocusFirstCard();
        view.MoveColumn(+1);

        view.ShowOnlyMine(true);
        LayOut(view, 120, 20);

        Assert.Equal(107, view.Selected?.Number);
        Assert.Equal("Pitches · a-team", view.Region);
    }

    [Fact]
    public void Filtering_the_selected_card_away_lands_on_the_first_one_left()
    {
        using var view = Open(["a-team", "tuicode"]);
        view.Show(Waiting);
        LayOut(view, 120, 20);
        view.FocusFirstCard();
        view.MoveColumn(+1);
        view.MoveCard(+1);
        Assert.Equal(108, view.Selected?.Number);

        view.ShowOnlyMine(true);
        LayOut(view, 120, 20);

        Assert.Equal(6, view.Selected?.Number);
    }

    [Fact]
    public void A_narrow_column_still_shows_something()
    {
        Assert.Equal("…", new Card(Waiting[0], false).Text(1));
        Assert.Equal("#107  When the dashboard goes quiet, I can't tell why", new Card(Waiting[0], false).Text(0));
    }

    [Fact]
    public void A_cards_text_is_elided_to_leave_the_room_its_icon_and_its_indent_need()
    {
        using var view = Open(["a-team"]);
        LayOut(view, 90, 20);

        view.Show(Waiting);

        Assert.Equal(
            ["#107  When the dashboa…", "#108  lead · A misconf…"],
            view.Lanes[0].Columns[1].CardText);
    }

    [Fact]
    public void Every_card_wears_the_icon_for_whose_move_it_is_in_the_style_the_view_is_showing()
    {
        using var view = Open(["a-team"]);
        view.Show(Waiting);
        LayOut(view, 120, 20);

        view.ShowIcons(IconStyle.NerdFont);
        Assert.Equal(IconStyle.NerdFont, view.Icons);
        Assert.Equal(
            [Icons.For(Waiting[0], IconStyle.NerdFont), Icons.For(Waiting[1], IconStyle.NerdFont)],
            view.Lanes[0].Columns[1].CardIcons);

        view.ShowIcons(IconStyle.Unicode);
        Assert.Equal(IconStyle.Unicode, view.Icons);
        Assert.Equal("✓", view.Lanes[0].Columns[1].CardIcons[0].Glyph);
    }

    [Fact]
    public void Every_cards_number_is_coloured_by_its_Priority_and_an_unranked_Idea_has_none()
    {
        using var view = Open(["a-team"]);
        view.Show(Waiting);
        LayOut(view, 120, 20);

        Assert.Equal([default], view.Lanes[0].Columns[0].Marks);
        Assert.Equal([Priorities.Scheme("Urgent"), Priorities.Scheme("High")],
            view.Lanes[0].Columns[1].Marks.Select(mark => mark.Scheme));
        Assert.Equal([Priorities.Scheme("Medium"), null], view.Lanes[0].Columns[2].Marks.Select(mark => mark.Scheme));
    }

    [Fact]
    public void At_three_columns_a_lane_still_fits_an_80_column_terminal()
    {
        using var view = Open(["a-team", "tuicode"]);
        LayOut(view, 80, 20);

        view.Show(Waiting);

        Assert.All(view.Lanes, lane =>
        {
            var columns = lane.Columns.Select(column => column.Frame).ToList();
            Assert.All(columns, cell => Assert.True(cell.Width > 0));
            Assert.Equal(0, columns[0].X);
            Assert.Equal(lane.Viewport.Width, columns[^1].Right);
        });
        Assert.All(view.Lanes.SelectMany(lane => lane.Columns), column =>
            Assert.All(column.CardText, text => Assert.True(text.Length <= column.Frame.Width)));
    }

    [Fact]
    public void A_card_with_a_PR_draws_a_row_under_it_and_the_title_still_counts_items()
    {
        using var view = Open(["a-team"]);
        view.Show(Waiting);
        LayOut(view, 120, 20);

        var review = view.Lanes[0].Columns[2];

        Assert.Equal(1, review.Count);
        Assert.Equal("Review · 1", review.Title);
        Assert.Equal(2, review.Nodes);
        Assert.Equal(["#49  dev · I can't change any of…", "PR #122  I can't change any of …"], review.CardText);
    }

    [Fact]
    public void A_lane_is_tall_enough_for_the_row_a_PR_adds()
    {
        using var view = Open(["a-team"]);

        view.Show([.. Waiting.Where(item => item.Status == "In review")]);
        LayOut(view, 120, 20);

        Assert.Equal(2, view.Lanes[0].Rows);
    }

    [Fact]
    public void Down_walks_a_card_then_the_row_under_it_and_up_comes_back_to_the_last_row()
    {
        using var view = Open(["a-team", "tuicode"]);
        view.Show(Waiting);
        LayOut(view, 120, 20);
        view.FocusFirstCard();
        view.MoveColumn(+1);
        view.MoveColumn(+1);

        Assert.Equal("https://github.com/x/3", view.SelectedUrl);

        view.MoveCard(+1);
        Assert.Equal(49, view.Selected?.Number);
        Assert.Equal("https://github.com/x/pull/122", view.SelectedUrl);

        view.MoveCard(+1);
        Assert.Equal("Review · tuicode", view.Region);
        Assert.Null(view.SelectedUrl);

        view.MoveCard(-1);
        Assert.Equal("Review · a-team", view.Region);
        Assert.Equal("https://github.com/x/pull/122", view.SelectedUrl);
    }

    [Fact]
    public void The_row_under_a_card_is_scrolled_into_view_like_the_card_itself()
    {
        using var view = Open(["a-team", "tuicode"]);
        view.Show(Waiting);
        LayOut(view, 120, 8);
        view.FocusFirstCard();
        view.MoveColumn(+1);
        view.MoveColumn(+1);

        view.MoveCard(+1);
        view.MoveCard(+1);

        Assert.Equal("Review · tuicode", view.Region);
        Assert.True(view.Viewport.Y > 0);
    }

    [Fact]
    public void Filtering_puts_the_selection_back_on_the_cards_own_row()
    {
        using var view = Open(["a-team"]);
        view.Show(Waiting);
        LayOut(view, 120, 20);
        view.FocusFirstCard();
        view.MoveColumn(+1);
        view.MoveColumn(+1);
        view.MoveCard(+1);

        view.ShowOnlyMine(false);
        LayOut(view, 120, 20);

        Assert.Equal("https://github.com/x/pull/122", view.SelectedUrl);
    }

    [Fact]
    public void A_title_with_an_emoji_keeps_it_and_still_lays_out_a_cell_the_tree_can_draw()
    {
        var watched = new WaitingItem(157, "A \U0001F440 appears on my comment", "In review",
            "https://github.com/x/157", "a-team", "dev", "answering your feedback",
            Pr: 159, PrUrl: "https://github.com/x/pull/159");
        using var view = Open(["a-team"]);

        view.Show([watched]);
        LayOut(view, 120, 20);

        var review = view.Lanes[0].Columns[2];

        Assert.All(review.CardText, text => Assert.Contains("\U0001F440", text));
        Assert.All(review.CardText, text => Assert.DoesNotContain(CardCells.LaidOut(text), char.IsSurrogate));
    }

    private static WorkView Open(string[] teams)
    {
        var view = new WorkView(teams);
        LayOut(view, 120, 20);
        return view;
    }

    private static void LayOut(WorkView view, int width, int height)
    {
        view.Frame = new Rectangle(0, 0, width, height);
        view.Layout(new Size(width, height));
    }

    private static IEnumerable<string> Titles(WorkView view) =>
        view.Lanes.SelectMany(lane => lane.Columns).Select(column => column.Title);

    private static IReadOnlyList<Rectangle> Cells(WorkView view) =>
        [.. view.Lanes.SelectMany(lane => lane.Columns).Select(column => column.Frame)];
}
