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
        Assert.All(view.Lanes, lane => Assert.Equal(["Triage", "Pitches", "Questions", "Review"], lane.Columns.Select(column => column.Gate)));
    }

    [Fact]
    public void A_column_is_titled_with_its_count_and_an_empty_one_is_hidden()
    {
        using var view = Open(["a-team", "tuicode"]);

        view.Show(Waiting);
        LayOut(view, 120, 20);

        Assert.Equal(["\U0001F4A1Triage · 1", "◇ Pitches · 2", "Questions · 0", "Review · 1", "\U0001F4A1Triage · 0", "◇ Pitches · 1", "Questions · 0", "Review · 0"], Titles(view));
        Assert.Equal([true, true, false, true, false, true, false, false], Visible(view));
    }

    [Fact]
    public void Questions_holds_the_tasks_the_Dev_handed_back_and_nothing_else_moves()
    {
        using var view = Open(["a-team", "tuicode"]);

        view.Show([.. Waiting, new(192, "I can reply to a pitch", "Ready", "https://github.com/x/192", "a-team",
            "dev", "reading your answer since 10:50", Question: "Which marker?")]);
        LayOut(view, 120, 20);

        Assert.Equal(["\U0001F4A1Triage · 1", "◇ Pitches · 2", "Questions · 1", "Review · 1", "\U0001F4A1Triage · 0", "◇ Pitches · 1", "Questions · 0", "Review · 0"], Titles(view));
    }

    [Fact]
    public void A_pitch_the_Lead_needs_an_answer_on_waits_in_Questions_whatever_its_rank()
    {
        using var view = Open(["a-team", "tuicode"]);
        WaitingItem Asking(int number, string priority) =>
            new(number, "I wait on the team", "Pitched", $"https://github.com/x/{number}", "a-team", "you",
                "asked you since 08:00", Priority: priority, Pitch: true, Question: "## Needs your answer\n\n1. Which?");

        view.Show([.. Waiting, Asking(257, "High"), Asking(258, "")]);
        LayOut(view, 120, 20);

        Assert.Equal(["\U0001F4A1Triage · 1", "◇ Pitches · 2", "Questions · 2", "Review · 1", "\U0001F4A1Triage · 0", "◇ Pitches · 1", "Questions · 0", "Review · 0"], Titles(view));
    }

    [Fact]
    public void The_columns_a_lane_shows_divide_its_width_between_them()
    {
        using var view = Open(["a-team", "tuicode"]);

        view.Show(Waiting);
        LayOut(view, 120, 20);

        Assert.Equal([40, 40, 0, 40, 0, 120, 0, 0], Cells(view).Select(cell => cell.Width));
        Assert.Equal([0, 40, 80], view.Lanes[0].Columns.Where(column => column.Visible).Select(column => column.Frame.X));
    }

    [Fact]
    public void The_selected_column_takes_half_its_lane_and_the_others_share_the_rest()
    {
        using var view = Open(["a-team", "tuicode"]);
        view.Show(Waiting);
        LayOut(view, 120, 20);
        view.FocusFirstCard();
        view.MoveColumn(+1);

        LayOut(view, 120, 20);

        Assert.Equal([30, 60, 0, 30], view.Lanes[0].Columns.Select(column => column.Frame.Width));
        Assert.Equal([0, 30, 90], view.Lanes[0].Columns.Where(column => column.Visible).Select(column => column.Frame.X));
        Assert.Equal(["#107  When the dashboard goes quiet, I can't tell …", "#108  lead · A misconfigured team looks like a wor…"],
            view.Lanes[0].Columns[1].CardText);
    }

    [Fact]
    public void A_lane_with_nothing_in_it_is_just_its_name()
    {
        using var view = Open(["a-team", "tuicode"]);

        view.Show([.. Waiting.Where(item => item.Team == "a-team")]);
        LayOut(view, 120, 20);

        Assert.Equal(2, view.Lanes[1].Lines);
        Assert.All(view.Lanes[1].Columns, column => Assert.False(column.Visible));
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
        Assert.Equal([false, false, false, true, false, false, false, false],
            view.Lanes.SelectMany(lane => lane.Columns).Select(column => column.Shown));
    }

    [Fact]
    public void With_nothing_waiting_there_is_no_column_to_focus()
    {
        using var view = Open(["a-team"]);
        view.Show([]);
        LayOut(view, 120, 20);

        view.FocusFirstCard();

        Assert.Null(view.Region);
        Assert.Null(view.Selected);
    }

    [Fact]
    public void Right_and_left_step_between_the_columns_a_lane_shows()
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
        view.MoveColumn(+1);
        Assert.Equal("Review · a-team", view.Region);
    }

    [Fact]
    public void Down_into_a_lane_without_the_column_lands_on_the_nearest_one_it_shows()
    {
        using var view = Open(["a-team", "tuicode"]);
        view.Show(Waiting);
        LayOut(view, 120, 20);
        view.FocusFirstCard();

        view.MoveCard(+1);

        Assert.Equal("Pitches · tuicode", view.Region);
        Assert.Equal(133, view.Selected?.Number);
    }

    [Fact]
    public void Down_passes_over_a_lane_with_nothing_in_it()
    {
        using var view = Open(["a-team", "tuicode", "triagent"]);
        view.Show([.. Waiting.Where(item => item.Team == "a-team"),
            Waiting[3] with { Team = "triagent" }]);
        LayOut(view, 120, 20);
        view.FocusFirstCard();

        view.MoveCard(+1);

        Assert.Equal("Pitches · triagent", view.Region);
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
        Assert.Equal(["\U0001F4A1Triage · 1", "◇ Pitches · 1", "Questions · 0", "Review · 0", "\U0001F4A1Triage · 0", "◇ Pitches · 1", "Questions · 0", "Review · 0"], Titles(view));
        Assert.Equal([true, true, false, false, false, true, false, false], Visible(view));

        view.ShowOnlyMine(false);
        LayOut(view, 120, 20);

        Assert.False(view.OnlyMine);
        Assert.Equal(["\U0001F4A1Triage · 1", "◇ Pitches · 2", "Questions · 0", "Review · 1", "\U0001F4A1Triage · 0", "◇ Pitches · 1", "Questions · 0", "Review · 0"],
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
    public void A_cards_text_is_elided_to_leave_the_room_its_icons_and_its_indent_need()
    {
        using var view = Open(["a-team"]);
        LayOut(view, 90, 20);

        view.Show(Waiting);

        Assert.Equal(
            ["#107  When th…", "#108  lead · …"],
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
        Assert.Equal([Priorities.Scheme("Medium"), null], view.Lanes[0].Columns[3].Marks.Select(mark => mark.Scheme));
    }

    [Fact]
    public void At_four_columns_a_lane_still_fits_an_80_column_terminal()
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
    public void Switching_the_icon_style_redraws_every_cards_kind_and_its_PRs_glyph()
    {
        using var view = Open(["a-team"]);
        view.Show(Waiting);
        LayOut(view, 120, 20);
        var review = view.Lanes[0].Columns[3];

        view.ShowIcons(IconStyle.NerdFont);
        Assert.Equal("\uEC37", review.CardLeads[0][1].Glyph);
        Assert.StartsWith("\uE726 #122  ", review.CardText[1]);

        view.ShowIcons(IconStyle.Unicode);
        Assert.Equal("‹›", review.CardLeads[0][1].Glyph);
        Assert.StartsWith("PR #122  ", review.CardText[1]);
    }

    [Fact]
    public void Triage_and_Pitches_wear_their_kind_in_the_heading_in_the_icon_style_in_effect()
    {
        using var view = Open(["a-team"]);
        view.Show([.. Waiting.Select(item => item with { Pitch = item.Status == "Pitched" })]);
        LayOut(view, 120, 20);
        var (triage, pitches) = (view.Lanes[0].Columns[0], view.Lanes[0].Columns[1]);

        view.ShowIcons(IconStyle.NerdFont);
        Assert.Equal("\uF400 Triage · 1", triage.Title);
        Assert.Equal("\U000F0428 Pitches · 2", pitches.Title);
        Assert.All(triage.CardLeads.Concat(pitches.CardLeads), leads => Assert.Single(leads));

        view.ShowIcons(IconStyle.Unicode);
        Assert.Equal("\U0001F4A1Triage · 1", triage.Title);
        Assert.Equal("◇ Pitches · 2", pitches.Title);
    }

    [Fact]
    public void A_card_with_a_PR_draws_a_row_under_it_and_the_title_still_counts_items()
    {
        using var view = Open(["a-team"]);
        view.Show(Waiting);
        LayOut(view, 120, 20);

        var review = view.Lanes[0].Columns[3];

        Assert.Equal(1, review.Count);
        Assert.Equal("Review · 1", review.Title);
        Assert.Equal(2, review.Nodes);
        Assert.Equal(["#49  dev · I can't change any …", "PR #122  I can't change any of …"], review.CardText);
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
    public void Down_walks_a_card_then_the_row_under_it_then_the_next_lane()
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

        view.MoveCard(-1);
        Assert.Equal("https://github.com/x/3", view.SelectedUrl);

        view.MoveCard(+1);
        view.MoveCard(+1);
        Assert.Equal("Pitches · tuicode", view.Region);
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

        Assert.Equal("Pitches · tuicode", view.Region);
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

        var review = view.Lanes[0].Columns[3];

        Assert.All(review.CardText, text => Assert.Contains("\U0001F440", text));
        Assert.All(review.CardText, text => Assert.DoesNotContain(CardCells.LaidOut(text), char.IsSurrogate));
    }

    [Fact]
    public void Forgetting_a_team_drops_its_lane_and_its_cards_and_closes_the_gap()
    {
        using var view = Open(["a-team", "tuicode", "goose"]);
        view.Show(Waiting);

        view.Forget(["a-team"]);
        LayOut(view, 120, 20);

        Assert.Equal(["tuicode", "goose"], view.Lanes.Select(lane => lane.Team));
        Assert.All(view.Items, item => Assert.Equal("tuicode", item.Team));
        Assert.Equal(0, view.Lanes[0].Frame.Y);
        Assert.Equal(view.Lanes[0].Frame.Bottom, view.Lanes[1].Frame.Y);
    }

    [Theory]
    [InlineData("conflicts with main", 122, false)]
    [InlineData("CI failing", 122, false)]
    [InlineData("CI running", 122, false)]
    [InlineData("still a draft", 122, false)]
    [InlineData("", 0, false)]
    [InlineData("", 122, true)]
    public void Review_holds_a_task_only_when_its_PR_can_be_merged(string unready, int pr, bool held)
    {
        using var view = Open(["a-team"]);

        view.Show([Reviewing(244, unready, pr)]);
        LayOut(view, 120, 20);

        var review = view.Lanes[0].Columns[3];
        Assert.Equal(held ? 1 : 0, review.Count);
        Assert.Equal(held, review.Summary.Length == 0);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(2, false)]
    public void Review_holds_a_validated_pitch_only_once_its_tasks_are_closed(int open, bool held)
    {
        using var view = Open(["a-team"]);

        view.Show([new WaitingItem(174, "Accepting finished work", "In review", "https://github.com/x/174", "a-team",
            "you", "awaiting your acceptance", Pitch: true, Tasks: 3, OpenTasks: open)]);
        LayOut(view, 120, 20);

        Assert.Equal(held ? 1 : 0, view.Lanes[0].Columns[3].Count);
        Assert.Equal(held ? "" : "1 with the Dev: #174 has 2 open tasks", view.Lanes[0].Columns[3].Summary);
    }

    [Fact]
    public void A_clean_card_the_Dev_owes_a_reply_on_stays_in_Review()
    {
        using var view = Open(["a-team"]);

        view.Show([Waiting[2]]);
        LayOut(view, 120, 20);

        Assert.Equal("Review · 1", view.Lanes[0].Columns[3].Title);
        Assert.Equal("", view.Lanes[0].Columns[3].Summary);
    }

    [Fact]
    public void Review_sums_up_what_the_Dev_is_still_fixing_under_its_cards_and_counts_only_the_rest()
    {
        using var view = Open(["a-team"]);

        view.Show([Waiting[2], Reviewing(244, "conflicts with main"), Reviewing(246, "CI running")]);
        LayOut(view, 120, 20);

        var review = view.Lanes[0].Columns[3];
        Assert.Equal("Review · 1", review.Title);
        Assert.Equal("2 with the Dev: #244 conflicts with main · #246 CI running", review.Summary);
        Assert.Equal(review.Nodes + 1, review.Lines);
    }

    [Theory]
    [InlineData(1, 100, "1 with the Dev: #244 conflicts with main")]
    [InlineData(2, 100, "2 with the Dev: #244 conflicts with main · #246 CI running")]
    [InlineData(2, 30, "2 with the Dev: #244 conflict…")]
    public void The_summary_names_each_one_and_is_cut_to_fit(int count, int width, string says)
    {
        WaitingItem[] aside = [Reviewing(244, "conflicts with main"), Reviewing(246, "CI running")];

        Assert.Equal(says, WorkColumn.Summarise([.. aside.Take(count)], width));
    }

    [Fact]
    public void The_summary_is_cut_to_the_column_it_is_drawn_in()
    {
        using var view = Open(["a-team"]);

        view.Show([Waiting[2], Reviewing(244, "conflicts with main"), Reviewing(246, "CI running"), Reviewing(247, "CI failing")]);
        LayOut(view, 40, 20);

        var summary = view.Lanes[0].Columns[3].Summary;
        Assert.EndsWith("…", summary);
        Assert.True(summary.Length <= 40);
    }

    [Fact]
    public void A_team_with_only_tasks_the_Dev_is_fixing_still_shows_Review_holding_just_the_summary()
    {
        using var view = Open(["a-team"]);

        view.Show([Reviewing(244, "conflicts with main")]);
        LayOut(view, 120, 20);

        var review = view.Lanes[0].Columns[3];
        Assert.True(review.Visible);
        Assert.Equal("Review · 0", review.Title);
        Assert.Equal(0, review.Nodes);
        Assert.Equal(1, review.Lines);
    }

    [Fact]
    public void The_keyboard_never_lands_on_the_summary()
    {
        using var view = Open(["a-team", "tuicode"]);
        view.Show([Waiting[2], Reviewing(244, "conflicts with main"),
            Reviewing(246, "CI running") with { Team = "tuicode" }, Waiting[3]]);
        LayOut(view, 120, 20);
        view.FocusFirstCard();
        view.MoveColumn(1);
        Assert.Equal(49, view.Selected?.Number);

        view.MoveCard(1);
        Assert.True(view.SelectedUrl?.EndsWith("/pull/122"));
        view.MoveCard(1);
        Assert.Equal(133, view.Selected?.Number);
        Assert.Equal("Pitches · tuicode", view.Region);

        view.MoveColumn(1);
        Assert.Equal("Pitches · tuicode", view.Region);
    }

    [Fact]
    public void Focus_skips_a_Review_column_holding_just_the_summary()
    {
        using var view = Open(["a-team", "tuicode"]);
        view.Show([Reviewing(244, "conflicts with main"), Waiting[3]]);
        LayOut(view, 120, 20);

        view.FocusFirstCard();

        Assert.Equal("Pitches · tuicode", view.Region);
    }

    [Fact]
    public void Once_the_Dev_has_fixed_it_a_re_read_brings_the_card_back_and_keeps_the_selection()
    {
        using var view = Open(["a-team"]);
        view.Show([Reviewing(244, "conflicts with main"), Reviewing(246, "")]);
        LayOut(view, 120, 20);
        view.FocusFirstCard();
        Assert.Equal(246, view.Selected?.Number);

        view.Show([Reviewing(244, ""), Reviewing(246, "")]);
        LayOut(view, 120, 20);

        var review = view.Lanes[0].Columns[3];
        Assert.Equal("Review · 2", review.Title);
        Assert.Equal("", review.Summary);
        Assert.Equal(review.Nodes, review.Lines);
        Assert.Equal(246, view.Selected?.Number);
    }

    private static WaitingItem Reviewing(int number, string unready, int pr = 122) =>
        new(number, $"Task {number}", "In review", $"https://github.com/x/{number}", "a-team",
            unready.Length > 0 ? "dev" : "you", unready.Length > 0 ? unready : "awaiting your acceptance",
            Pr: pr, PrUrl: pr == 0 ? "" : $"https://github.com/x/pull/{pr + number}", Trouble: unready,
            Unready: unready);

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

    private static IEnumerable<bool> Visible(WorkView view) =>
        view.Lanes.SelectMany(lane => lane.Columns).Select(column => column.Visible);

    private static IReadOnlyList<Rectangle> Cells(WorkView view) =>
        [.. view.Lanes.SelectMany(lane => lane.Columns).Select(column => column.Frame)];
}
