using System.Drawing;

namespace ATeam.Dashboard.Tests;

/// <summary>The van that drives while the team is working: the animation it's read from, and what it does
/// once it's up.</summary>
public class LoadingViewTests
{
    [Fact]
    public void The_shipped_animation_is_several_frames_of_the_same_shape()
    {
        Assert.True(LoadingView.Frames.Count > 1, $"{LoadingView.Frames.Count} frames is not an animation");
        Assert.All(LoadingView.Frames, frame => Assert.Equal(LoadingView.Rows, frame.Count));
        Assert.All(LoadingView.Frames.SelectMany(frame => frame),
            row => Assert.True(row.Sum(span => span.Text.Length) <= LoadingView.Cells));
    }

    [Fact]
    public void Every_colour_the_animation_names_is_one_the_view_can_draw()
    {
        var named = LoadingView.Frames.SelectMany(frame => frame).SelectMany(row => row)
            .Select(span => span.Colour).Distinct();

        Assert.All(named, colour => Assert.True(LoadingView.Draws(colour), $"nothing draws [{colour}]"));
    }

    [Fact]
    public void A_row_splits_into_the_text_between_its_tags_and_the_colour_each_run_is_in()
    {
        Assert.Equal(
            [new ArtSpan("  ", "gray"), new ArtSpan("A-TEAM", "red"), new ArtSpan(" |", "gray")],
            LoadingView.Spans("  [red]A-TEAM[/red] |"));
    }

    [Fact]
    public void A_frame_break_ends_one_frame_and_starts_the_next()
    {
        var frames = LoadingView.Parse("[red]one[/red]\nrow\n---FRAME---\n[red]two[/red]\nrow\n");

        Assert.Equal(2, frames.Count);
        Assert.All(frames, frame => Assert.Equal(2, frame.Count));
        Assert.Equal("one", frames[0][0][0].Text);
        Assert.Equal("two", frames[1][0][0].Text);
    }

    [Fact]
    public void A_bracket_that_closes_nothing_is_text_like_any_other()
    {
        Assert.Equal([new ArtSpan("[ . ]", "gray")], LoadingView.Spans("[ . ]"));
    }

    [Fact]
    public void Starting_it_shows_it_from_the_first_frame_and_stopping_it_takes_it_away()
    {
        using var view = new LoadingView();

        Assert.False(view.Visible);

        view.Advance();
        view.Start();

        Assert.True(view.Visible);
        Assert.Equal(0, view.Showing);

        view.Stop();

        Assert.False(view.Visible);
    }

    [Fact]
    public void One_already_driving_keeps_rolling_rather_than_starting_over()
    {
        using var view = new LoadingView();
        view.Start();
        view.Advance();

        view.Start();

        Assert.Equal(1, view.Showing);
    }

    [Fact]
    public void Each_beat_moves_to_the_next_frame_and_the_last_goes_back_to_the_first()
    {
        using var view = new LoadingView();
        view.Start();

        for (var beat = 1; beat < LoadingView.Frames.Count; beat++)
        {
            view.Advance();
            Assert.Equal(beat, view.Showing);
        }

        view.Advance();

        Assert.Equal(0, view.Showing);
    }

    [Fact]
    public void It_sits_in_the_middle_of_the_room_it_is_given()
    {
        var box = LoadingView.Box(new Size(LoadingView.Cells + 10, LoadingView.Rows + 4));

        Assert.Equal(new Rectangle(5, 2, LoadingView.Cells, LoadingView.Rows), box);
    }

    [Fact]
    public void A_view_too_small_for_it_cuts_it_down_rather_than_being_spilled_out_of()
    {
        var box = LoadingView.Box(new Size(12, 3));

        Assert.Equal(new Rectangle(0, 0, 12, 3), box);

        var row = LoadingView.Clip(LoadingView.Spans("[red]A-TEAM[/red] van driving"), 12);

        Assert.Equal([new ArtSpan("A-TEAM", "red"), new ArtSpan(" van d", "gray")], row);
    }
}
