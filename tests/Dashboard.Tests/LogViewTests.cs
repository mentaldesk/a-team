namespace ATeam.Dashboard.Tests;

public class LogViewTests
{
    [Fact]
    public void Wrapping_keeps_the_kind_on_every_continuation_row()
    {
        var line = new LogLine("  ✗ " + string.Join(' ', Enumerable.Repeat("error", 12)), LogLineKind.ToolError);

        var rows = LogView.Wrap([line], 20);

        Assert.True(rows.Count > 1);
        Assert.All(rows, row => Assert.Equal(LogLineKind.ToolError, row.Kind));
        Assert.Equal(line.Text.Replace(" ", ""), string.Concat(rows.Select(r => r.Text)).Replace(" ", ""));
    }

    [Fact]
    public void A_line_that_fits_is_left_alone()
    {
        var line = new LogLine("■ finished: ok, 34 turns, $1.42", LogLineKind.ResultOk);

        Assert.Equal([line], LogView.Wrap([line], 80));
    }

    [Fact]
    public void An_unbroken_run_longer_than_the_width_is_split_at_the_width()
    {
        var line = new LogLine(new string('x', 25), LogLineKind.ToolCall);

        var rows = LogView.Wrap([line], 10);

        Assert.Equal(["xxxxxxxxxx", "xxxxxxxxxx", "xxxxx"], rows.Select(r => r.Text));
        Assert.All(rows, row => Assert.Equal(LogLineKind.ToolCall, row.Kind));
    }
}
