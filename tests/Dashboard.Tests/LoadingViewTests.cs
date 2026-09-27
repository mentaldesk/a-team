using System.Drawing;
using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace ATeam.Dashboard.Tests;

/// <summary>The van that drives while the team is working: the animation it's read from, and what it does
/// once it's up.</summary>
public class LoadingViewTests
{
    [Fact]
    public void The_shipped_animation_is_several_frames_of_the_same_shape_on_one_body()
    {
        Assert.True(LoadingView.Frames.Count > 1, $"{LoadingView.Frames.Count} frames is not an animation");
        Assert.All(LoadingView.Frames, frame => Assert.Equal(LoadingView.Rows, frame.Count));
        Assert.Equal(LoadingView.Rows, LoadingView.Shipped.Body.Count);
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
    public void Every_letter_in_the_body_is_one_the_view_can_paint()
    {
        var letters = LoadingView.Shipped.Body.SelectMany(row => row).Distinct();

        Assert.All(letters, fill => Assert.True(LoadingView.Paints(fill), $"nothing paints '{fill}'"));
    }

    [Fact]
    public void A_row_splits_into_the_text_between_its_tags_and_the_colour_each_run_is_in()
    {
        Assert.Equal(
            [new ArtSpan("  ", "gray"), new ArtSpan("A-TEAM", "red"), new ArtSpan(" |", "gray")],
            LoadingView.Spans("  [red]A-TEAM[/red] |"));
    }

    [Fact]
    public void The_body_is_read_apart_from_the_frames_and_a_frame_break_starts_the_next()
    {
        var art = LoadingView.Parse(
            "---BODY---\nbb\ngg\n---FRAME---\n[red]one[/red]\nrow\n---FRAME---\n[red]two[/red]\nrow\n");

        Assert.Equal(["bb", "gg"], art.Body);
        Assert.Equal(2, art.Frames.Count);
        Assert.All(art.Frames, frame => Assert.Equal(2, frame.Count));
        Assert.Equal("one", art.Frames[0][0][0].Text);
        Assert.Equal("two", art.Frames[1][0][0].Text);
    }

    [Fact]
    public void Art_with_no_body_is_all_road()
    {
        var art = LoadingView.Parse("[red]one[/red]\n---FRAME---\n[red]two[/red]\n");

        Assert.Empty(art.Body);
        Assert.Equal(2, art.Frames.Count);
    }

    [Fact]
    public void A_glyph_is_found_by_its_column_across_the_runs_and_past_the_end_is_nothing()
    {
        var row = LoadingView.Spans("ab[red]cd[/red]");

        Assert.Equal(('c', "red"), LoadingView.At(row, 2));
        Assert.Equal(('b', "gray"), LoadingView.At(row, 1));
        Assert.Equal((' ', "gray"), LoadingView.At(row, 9));
    }

    [Fact]
    public void On_the_body_a_space_is_the_body_and_not_the_road()
    {
        var road = LoadingView.RoadIn(BundledThemes.Midnight);

        var (glyph, paint) = LoadingView.Cell('b', ' ', "gray", road);

        Assert.Equal(' ', glyph);
        Assert.NotEqual(road.Surface, paint.Background);
    }

    [Fact]
    public void On_the_road_a_space_is_a_dot_and_anything_drawn_there_sits_on_the_road()
    {
        var road = LoadingView.RoadIn(BundledThemes.Daylight);

        var (dot, under) = LoadingView.Cell(' ', ' ', "gray", road);
        var (smoke, exhaust) = LoadingView.Cell(' ', '@', "yellow", road);

        Assert.NotEqual(' ', dot);
        Assert.Equal(new Attribute(road.Dots, road.Surface), under);
        Assert.Equal('@', smoke);
        Assert.Equal(new Attribute(road.Smoke, road.Surface), exhaust);
    }

    [Fact]
    public void The_road_is_dark_under_a_dark_theme_and_pale_under_a_pale_one()
    {
        Assert.True(LoadingView.RoadIn(BundledThemes.Midnight).Surface.IsDarkColor());
        Assert.False(LoadingView.RoadIn(BundledThemes.Daylight).Surface.IsDarkColor());
        Assert.False(LoadingView.RoadIn(BundledThemes.TurboPascal).Surface.IsDarkColor());
        Assert.All(BundledThemes.Names, theme => Assert.NotEqual(
            LoadingView.RoadIn(theme).Dots, LoadingView.RoadIn(theme).Surface));
    }

    [Fact]
    public void A_theme_we_dont_ship_drives_on_the_default_ones_road()
    {
        Assert.Equal(LoadingView.RoadIn(BundledThemes.Default), LoadingView.RoadIn("Solarized"));
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
        Assert.Equal(new Rectangle(0, 0, 12, 3), LoadingView.Box(new Size(12, 3)));
    }
}
