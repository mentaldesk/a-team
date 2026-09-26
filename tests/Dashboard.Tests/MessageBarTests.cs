using Terminal.Gui.Drawing;

namespace ATeam.Dashboard.Tests;

public class MessageBarTests
{
    [Fact]
    public void A_bar_with_nothing_to_say_takes_no_row_at_all()
    {
        var bar = new MessageBar();

        Assert.Equal(0, bar.Lines);

        bar.Show("Reading…", Schemes.Accent);
        Assert.Equal(1, bar.Lines);
        Assert.Equal("Reading…", bar.Text);

        bar.Clear();
        Assert.Equal(0, bar.Lines);
        Assert.Equal("", bar.Text);
    }

    [Fact]
    public void A_message_with_more_to_it_is_shown_one_line_deep()
    {
        var bar = new MessageBar();

        bar.Show("a-team pause: can't write /nope/team0.json\nstack\ntrace", Schemes.Error);

        Assert.Equal(1, bar.Lines);
        Assert.Equal("a-team pause: can't write /nope/team0.json", bar.Says);
    }
}
