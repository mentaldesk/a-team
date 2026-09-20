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

    [Fact]
    public void A_run_of_tool_calls_becomes_one_row_showing_the_latest()
    {
        LogLine[] lines = [.. Enumerable.Range(1, 5).Select(n => new LogLine($"▸ Bash step {n}", LogLineKind.ToolCall))];

        var row = Assert.Single(LogView.Collapse(lines, 38));

        Assert.Equal("▸ Bash step 5 (+4)", row.Text);
        Assert.Equal(LogLineKind.ToolCall, row.Kind);
    }

    [Fact]
    public void A_run_of_one_has_no_count()
    {
        LogLine[] lines =
        [
            new("before", LogLineKind.Prose),
            new("▸ Bash ls", LogLineKind.ToolCall),
            new("after", LogLineKind.Prose),
        ];

        var rows = LogView.Collapse(lines, 38);

        Assert.Equal(["before", "▸ Bash ls", "after"], rows.Select(r => r.Text));
    }

    [Fact]
    public void A_long_command_costs_one_clipped_row_and_keeps_its_count()
    {
        LogLine[] lines =
        [
            new("▸ Bash " + new string('x', 160), LogLineKind.ToolCall),
            new("▸ Bash " + new string('y', 160), LogLineKind.ToolCall),
        ];

        var rows = LogView.Wrap(LogView.Collapse(lines, 38), 38);

        var row = Assert.Single(rows);
        Assert.Equal(38, row.Text.Length);
        Assert.Equal("▸ Bash " + new string('y', 25) + "… (+1)", row.Text);
    }

    [Fact]
    public void An_error_splits_a_run_into_two_rows_and_stays_on_its_own()
    {
        LogLine[] lines =
        [
            new("▸ Bash one", LogLineKind.ToolCall),
            new("▸ Bash two", LogLineKind.ToolCall),
            new("  ✗ no such file", LogLineKind.ToolError),
            new("▸ Bash three", LogLineKind.ToolCall),
            new("▸ Bash four", LogLineKind.ToolCall),
            new("▸ Bash five", LogLineKind.ToolCall),
        ];

        var rows = LogView.Wrap(LogView.Collapse(lines, 38), 38);

        Assert.Equal(["▸ Bash two (+1)", "  ✗ no such file", "▸ Bash five (+2)"], rows.Select(r => r.Text));
        Assert.Equal(
            [LogLineKind.ToolCall, LogLineKind.ToolError, LogLineKind.ToolCall],
            rows.Select(r => r.Kind));
    }

    [Fact]
    public void Everything_that_is_not_a_tool_call_is_left_alone()
    {
        LogLine[] lines =
        [
            new("── session started (opus) ──", LogLineKind.SessionBoundary),
            new(string.Join(' ', Enumerable.Repeat("narration", 8)), LogLineKind.Prose),
            new("  ✗ boom", LogLineKind.ToolError),
            new("■ finished: ok, 34 turns, $1.42", LogLineKind.ResultOk),
            new("■ finished: error, 2 turns, $0.10", LogLineKind.ResultError),
        ];

        Assert.Equal(LogView.Wrap(lines, 38), LogView.Wrap(LogView.Collapse(lines, 38), 38));
    }

    [Fact]
    public void Collapsing_a_real_pane_more_than_halves_its_rows()
    {
        var lines = File.ReadLines(Path.Combine(AppContext.BaseDirectory, "fixtures", "pane.jsonl"))
            .SelectMany(SessionLog.Render)
            .ToList();

        var expanded = LogView.Wrap(lines, 38).Count;
        var collapsed = LogView.Wrap(LogView.Collapse(lines, 38), 38).Count;

        Assert.True(collapsed * 2 < expanded, $"{collapsed} collapsed rows vs {expanded} expanded");
    }
}
