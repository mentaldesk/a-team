namespace ATeam.Dashboard.Tests;

public class MessageBarTests
{
    [Fact]
    public void The_status_sits_against_the_right_edge_with_the_message_at_the_left()
    {
        var line = MessageBar.Line("#118 · lead · answering your feedback since 08:14", "My items", 60);

        Assert.Equal(60, line.Length);
        Assert.StartsWith("#118 · lead · answering your feedback since 08:14", line, StringComparison.Ordinal);
        Assert.EndsWith("My items", line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_message_with_no_room_left_for_it_is_elided_rather_than_the_status()
    {
        var line = MessageBar.Line("#118 · lead · answering your feedback since 08:14", "All items", 30);

        Assert.Equal(30, line.Length);
        Assert.Equal("#118 · lead · answe… All items", line);
    }

    [Fact]
    public void Too_narrow_for_both_the_status_is_what_is_left()
    {
        Assert.Equal("All items", MessageBar.Line("Reading…", "All items", 8));
        Assert.Equal("All items", MessageBar.Line("Reading…", "All items", 0));
    }

    [Fact]
    public void With_no_status_the_line_is_the_message_as_it_was()
    {
        Assert.Equal("Reading…", MessageBar.Line("Reading…", "", 60));
        Assert.Equal("", MessageBar.Line("", "", 60));
    }

    [Fact]
    public void A_bar_with_a_status_and_nothing_to_say_still_takes_its_row()
    {
        var bar = new MessageBar();

        bar.ShowStatus("All items");
        Assert.Equal(1, bar.Lines);

        bar.Clear();
        Assert.Equal(1, bar.Lines);

        bar.ShowStatus("");
        Assert.Equal(0, bar.Lines);
    }
}
