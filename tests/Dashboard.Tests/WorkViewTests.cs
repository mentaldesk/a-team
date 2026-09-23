using System.Drawing;

namespace ATeam.Dashboard.Tests;

public class WorkViewTests
{
    private static readonly WaitingItem[] Gated =
    [
        new(107, "When the dashboard goes quiet, I can't tell why", "Pitched", "https://github.com/x/1", "a-team"),
        new(108, "A misconfigured team looks like a working one", "Pitched", "https://github.com/x/2", "a-team"),
        new(49, "I can't change any of the dashboard's keys", "In review", "https://github.com/x/3", "a-team"),
        new(133, "Notice when open files change on disk", "Pitched", "https://github.com/x/4", "tuicode"),
    ];

    [Fact]
    public void Every_team_gets_a_swimlane_and_every_lane_a_column_per_gate()
    {
        using var view = Open(["a-team", "tuicode"]);

        Assert.Equal(["a-team", "tuicode"], view.Lanes.Select(lane => lane.Team));
        Assert.All(view.Lanes, lane => Assert.Equal(["Pitches", "Review"], lane.Columns.Select(column => column.Gate)));
    }

    [Fact]
    public void A_column_is_titled_with_its_count_and_an_empty_one_still_draws()
    {
        using var view = Open(["a-team", "tuicode"]);

        view.Show(Gated);
        LayOut(view, 120, 20);

        Assert.Equal(["Pitches · 2", "Review · 1", "Pitches · 1", "Review · 0"], Titles(view));
        Assert.All(Cells(view), cell => Assert.True(cell.Width > 0 && cell.Height > 0));
    }

    [Fact]
    public void The_columns_of_a_lane_divide_its_width_between_them()
    {
        using var view = Open(["a-team", "tuicode"]);

        view.Show(Gated);
        var cells = Cells(view);

        Assert.Equal([0, 60, 0, 60], cells.Select(cell => cell.X));
        Assert.Equal([60, 60, 60, 60], cells.Select(cell => cell.Width));
    }

    [Fact]
    public void A_lane_is_as_tall_as_its_fullest_column_and_never_shorter_than_one_card()
    {
        using var view = Open(["a-team", "tuicode"]);

        view.Show(Gated);
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
        view.Show([.. Gated.Where(item => item.Status == "In review")]);
        LayOut(view, 120, 20);

        view.FocusFirstCard();

        Assert.Equal("Review · a-team", view.Region);
        Assert.Equal(49, view.Selected?.Number);
        Assert.Equal([false, true, false, false], view.Lanes.SelectMany(lane => lane.Columns).Select(column => column.Shown));
    }

    [Fact]
    public void With_nothing_waiting_focus_still_lands_somewhere()
    {
        using var view = Open(["a-team"]);
        view.Show([]);
        LayOut(view, 120, 20);

        view.FocusFirstCard();

        Assert.Equal("Pitches · a-team", view.Region);
        Assert.Null(view.Selected);
    }

    [Theory]
    [InlineData(41, "#107  When the dashboard goes quiet, I c…")]
    [InlineData(53, "#107  When the dashboard goes quiet, I can't tell why")]
    public void A_card_reads_as_the_number_then_as_much_of_the_title_as_fits(int width, string expected)
    {
        Assert.Equal(expected, WorkColumn.Card(Gated[0], width));
        Assert.True(WorkColumn.Card(Gated[0], width).Length <= width);
    }

    [Fact]
    public void A_narrow_column_still_shows_something()
    {
        Assert.Equal("…", WorkColumn.Card(Gated[0], 1));
        Assert.Equal("#107  When the dashboard goes quiet, I can't tell why", WorkColumn.Card(Gated[0], 0));
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
