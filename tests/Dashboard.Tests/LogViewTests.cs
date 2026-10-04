using Terminal.Gui.Text;

namespace ATeam.Dashboard.Tests;

public class LogViewTests
{
    public static TheoryData<IconStyle> Styles => [IconStyle.Auto, IconStyle.NerdFont, IconStyle.Unicode];

    [Fact]
    public void Wrapping_keeps_the_kind_on_every_continuation_row()
    {
        var line = new LogLine(string.Join(' ', Enumerable.Repeat("error", 12)), LogLineKind.ToolError);

        var rows = LogView.Wrap([line], 20);

        Assert.True(rows.Count > 1);
        Assert.All(rows, row => Assert.Equal(LogLineKind.ToolError, row.Kind));
        Assert.Equal(line.Text.Replace(" ", ""), string.Concat(rows.Select(r => r.Text)).Replace(" ", ""));
    }

    [Fact]
    public void A_line_that_fits_is_left_alone()
    {
        var line = new LogLine("finished: ok, 34 turns, $1.42", LogLineKind.ResultOk);

        Assert.Equal([new LogRow(line.Text, line.Kind)], LogView.Wrap([line], 80));
    }

    [Fact]
    public void An_unbroken_run_longer_than_the_width_is_split_at_what_is_left_of_it()
    {
        var line = new LogLine(new string('x', 25), LogLineKind.ToolCall);

        var rows = LogView.Wrap([line], 10);

        Assert.Equal(["xxxxxxxx", "xxxxxxxx", "xxxxxxxx", "x"], rows.Select(r => r.Text));
        Assert.All(rows, row => Assert.Equal(LogLineKind.ToolCall, row.Kind));
    }

    [Fact]
    public void Only_the_first_row_of_a_wrapped_line_wears_the_icon()
    {
        var line = new LogLine(string.Join(' ', Enumerable.Repeat("error", 12)), LogLineKind.ToolError);

        var rows = LogView.Wrap([line], 20);

        Assert.False(rows[0].Wrapped);
        Assert.All(rows.Skip(1), row => Assert.True(row.Wrapped));
    }

    [Fact]
    public void The_icon_comes_out_of_the_rows_width_budget()
    {
        var text = string.Join(' ', Enumerable.Repeat("narration", 8));

        var call = LogView.Wrap([new LogLine(text, LogLineKind.ToolCall)], 40);

        Assert.Equal(
            LogView.Wrap([new LogLine(text, LogLineKind.Prose)], 40 - Icons.Width).Select(r => r.Text),
            call.Select(r => r.Text));
    }

    [Theory]
    [MemberData(nameof(Styles))]
    public void A_row_wears_its_icon_in_a_field_the_text_starts_at_the_same_column_after(IconStyle style)
    {
        var view = new LogView();
        view.ShowIcons(style);

        var drawn = view.Drawn(new LogRow("Bash ls", LogLineKind.ToolCall), 20);

        Assert.StartsWith(Icons.Field(Icon.ToolCall, style) + "Bash ls", drawn, StringComparison.Ordinal);
        Assert.Equal(20, drawn.GetColumns());
    }

    [Fact]
    public void A_continuation_row_is_drawn_under_its_text_rather_than_under_its_icon()
    {
        var view = new LogView();

        var drawn = view.Drawn(new LogRow("ls", LogLineKind.ToolCall, Wrapped: true), 20);

        Assert.StartsWith(new string(' ', Icons.Width) + "ls", drawn, StringComparison.Ordinal);
    }

    [Fact]
    public void The_Unicode_style_draws_the_lines_the_log_drew_before_the_icons_left_the_text()
    {
        var view = new LogView();
        view.ShowIcons(IconStyle.Unicode);

        Assert.Equal(
            ["▸ Bash ls", "  ✗ boom", "■ finished: ok, 34 turns, $1.42", "narration"],
            new[]
            {
                new LogRow("Bash ls", LogLineKind.ToolCall),
                new LogRow("boom", LogLineKind.ToolError),
                new LogRow("finished: ok, 34 turns, $1.42", LogLineKind.ResultOk),
                new LogRow("narration", LogLineKind.Prose),
            }.Select(row => view.Drawn(row, 40).TrimEnd()));
    }

    [Fact]
    public void An_elided_row_leaves_its_icon_room_inside_the_viewport()
    {
        var view = new LogView { Elides = true };
        view.Lines = [new LogLine(new string('x', 452), LogLineKind.ToolError)];

        var row = Assert.Single(view.Rows(80));

        Assert.Equal(80 - LogView.Lead(LogLineKind.ToolError), row.Text.Length);
        Assert.Equal(80, view.Drawn(row, 80).GetColumns());
    }

    [Fact]
    public void A_run_of_tool_calls_becomes_one_row_showing_the_latest()
    {
        LogLine[] lines = [.. Enumerable.Range(1, 5).Select(n => new LogLine($"Bash step {n}", LogLineKind.ToolCall))];

        var row = Assert.Single(LogView.Collapse(lines, 38));

        Assert.Equal("Bash step 5 (+4)", row.Text);
        Assert.Equal(LogLineKind.ToolCall, row.Kind);
    }

    [Fact]
    public void A_run_of_one_has_no_count()
    {
        LogLine[] lines =
        [
            new("before", LogLineKind.Prose),
            new("Bash ls", LogLineKind.ToolCall),
            new("after", LogLineKind.Prose),
        ];

        var rows = LogView.Collapse(lines, 38);

        Assert.Equal(["before", "Bash ls", "after"], rows.Select(r => r.Text));
    }

    [Fact]
    public void A_long_command_costs_one_clipped_row_and_keeps_its_count()
    {
        LogLine[] lines =
        [
            new("Bash " + new string('x', 160), LogLineKind.ToolCall),
            new("Bash " + new string('y', 160), LogLineKind.ToolCall),
        ];

        var rows = LogView.Wrap(LogView.Collapse(lines, 38), 38);

        var row = Assert.Single(rows);
        Assert.Equal(38 - Icons.Width, row.Text.Length);
        Assert.Equal("Bash " + new string('y', 25) + "… (+1)", row.Text);
    }

    [Fact]
    public void Every_error_survives_the_loss_of_the_call_it_came_from()
    {
        LogLine[] lines =
        [
            new("Bash one", LogLineKind.ToolCall),
            new("Bash two", LogLineKind.ToolCall),
            new("no such file", LogLineKind.ToolError),
            new("Bash three", LogLineKind.ToolCall),
            new("permission denied", LogLineKind.ToolError),
            new("Bash four", LogLineKind.ToolCall),
            new("Bash five", LogLineKind.ToolCall),
        ];

        var rows = LogView.Wrap(LogView.Collapse(lines, 38), 38);

        Assert.Equal(["no such file", "permission denied", "Bash five (+4)"], rows.Select(r => r.Text));
        Assert.Equal(
            [LogLineKind.ToolError, LogLineKind.ToolError, LogLineKind.ToolCall],
            rows.Select(r => r.Kind));
        Assert.Equal(Icons.Width + 2, LogView.Lead(LogLineKind.ToolError));
    }

    [Fact]
    public void Several_runs_of_calls_leave_one_row_and_it_is_the_newest()
    {
        LogLine[] lines =
        [
            new("Bash one", LogLineKind.ToolCall),
            new("Bash two", LogLineKind.ToolCall),
            new("narration", LogLineKind.Prose),
            new("Bash three", LogLineKind.ToolCall),
            new("more narration", LogLineKind.Prose),
            new("Bash four", LogLineKind.ToolCall),
            new("Bash five", LogLineKind.ToolCall),
        ];

        var rows = LogView.Collapse(lines, 38);

        Assert.Equal(["narration", "more narration", "Bash five (+4)"], rows.Select(r => r.Text));
    }

    [Fact]
    public void Two_sessions_in_a_pane_leave_one_row_in_the_later_one()
    {
        LogLine[] lines =
        [
            new("── session started (opus) ──", LogLineKind.SessionBoundary),
            new("Bash one", LogLineKind.ToolCall),
            new("finished: ok, 34 turns, $1.42", LogLineKind.ResultOk),
            new("── session started (opus) ──", LogLineKind.SessionBoundary),
            new("Bash two", LogLineKind.ToolCall),
        ];

        var rows = LogView.Collapse(lines, 38);

        var call = Assert.Single(rows, row => row.Kind == LogLineKind.ToolCall);
        Assert.Equal("Bash two (+1)", call.Text);
        Assert.Equal(rows[^1], call);
    }

    [Fact]
    public void The_call_the_agent_is_on_is_the_last_row()
    {
        LogLine[] lines =
        [
            new("Bash one", LogLineKind.ToolCall),
            new("narration", LogLineKind.Prose),
            new("Bash two", LogLineKind.ToolCall),
        ];

        var rows = LogView.Wrap(LogView.Collapse(lines, 38), 38);

        Assert.Equal("Bash two (+1)", rows[^1].Text);
    }

    [Fact]
    public void The_retained_row_counts_every_call_hidden_across_the_whole_log()
    {
        var lines = Fixture();
        var calls = lines.Count(line => line.Kind == LogLineKind.ToolCall);
        Assert.True(calls > 2, $"the fixture has {calls} tool calls");

        var row = Assert.Single(LogView.Collapse(lines, 200), line => line.Kind == LogLineKind.ToolCall);

        Assert.EndsWith($" (+{calls - 1})", row.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Everything_that_is_not_a_tool_call_is_left_alone()
    {
        LogLine[] lines =
        [
            new("── session started (opus) ──", LogLineKind.SessionBoundary),
            new(string.Join(' ', Enumerable.Repeat("narration", 8)), LogLineKind.Prose),
            new("boom", LogLineKind.ToolError),
            new("finished: ok, 34 turns, $1.42", LogLineKind.ResultOk),
            new("finished: error, 2 turns, $0.10", LogLineKind.ResultError),
        ];

        Assert.Equal(LogView.Wrap(lines, 38), LogView.Wrap(LogView.Collapse(lines, 38), 38));
    }

    [Fact]
    public void Collapsing_a_real_pane_more_than_halves_its_rows()
    {
        var lines = Fixture();

        var expanded = LogView.Wrap(lines, 38).Count;
        var collapsed = LogView.Wrap(LogView.Collapse(lines, 38), 38).Count;

        Assert.True(collapsed * 2 < expanded, $"{collapsed} collapsed rows vs {expanded} expanded");
    }

    [Fact]
    public void Expanding_a_pane_shows_every_tool_call_again()
    {
        var view = new LogView { Lines = Fixture() };

        var collapsed = view.Rows(38);
        view.ToggleToolCalls();
        var expanded = view.Rows(38);
        view.ToggleToolCalls();

        Assert.Equal(LogView.Wrap(Fixture(), 38), expanded);
        Assert.True(expanded.Count > collapsed.Count);
        Assert.Equal(collapsed, view.Rows(38));
    }

    [Fact]
    public void Toggling_one_pane_leaves_the_other_alone()
    {
        var toggled = new LogView { Lines = Fixture() };
        var other = new LogView { Lines = Fixture() };
        var before = other.Rows(38);

        toggled.ToggleToolCalls();

        Assert.True(toggled.Expanded);
        Assert.False(other.Expanded);
        Assert.Equal(before, other.Rows(38));
    }

    [Fact]
    public void A_pane_that_was_following_still_follows_after_a_toggle()
    {
        var view = new LogView { Lines = Fixture() };

        view.ToggleToolCalls();

        Assert.True(view.Following);
    }

    [Fact]
    public void A_scrolled_pane_lands_on_the_line_it_was_reading()
    {
        LogLine[] lines =
        [
            new("first", LogLineKind.Prose),
            .. Enumerable.Range(1, 6).Select(n => new LogLine($"Bash step {n}", LogLineKind.ToolCall)),
            new("second", LogLineKind.Prose),
            new("third", LogLineKind.Prose),
        ];
        var collapsed = LogView.Wrap(LogView.Collapse(lines, 38), 38);
        var expanded = LogView.Wrap(lines, 38);

        Assert.Equal("second", expanded[LogView.Anchor(collapsed, expanded, 2)].Text);
        Assert.Equal(2, LogView.Anchor(expanded, collapsed, 7));
    }

    [Fact]
    public void A_scrolled_pane_lands_on_the_same_line_with_runs_gone_from_between()
    {
        LogLine[] lines =
        [
            new("first", LogLineKind.Prose),
            new("Bash one", LogLineKind.ToolCall),
            new("Bash two", LogLineKind.ToolCall),
            new("second", LogLineKind.Prose),
            new("Bash three", LogLineKind.ToolCall),
            new("third", LogLineKind.Prose),
            new("Bash four", LogLineKind.ToolCall),
        ];
        var collapsed = LogView.Wrap(LogView.Collapse(lines, 38), 38);
        var expanded = LogView.Wrap(lines, 38);

        Assert.Equal("third", expanded[LogView.Anchor(collapsed, expanded, 2)].Text);
        Assert.Equal("third", collapsed[LogView.Anchor(expanded, collapsed, 5)].Text);
    }

    [Fact]
    public void A_scrolled_pane_reading_tool_calls_lands_on_that_run()
    {
        LogLine[] lines =
        [
            new("first", LogLineKind.Prose),
            .. Enumerable.Range(1, 6).Select(n => new LogLine($"Bash step {n}", LogLineKind.ToolCall)),
            new("second", LogLineKind.Prose),
        ];
        var collapsed = LogView.Wrap(LogView.Collapse(lines, 38), 38);
        var expanded = LogView.Wrap(lines, 38);

        Assert.Equal(1, LogView.Anchor(collapsed, expanded, 1));
        Assert.Equal(1, LogView.Anchor(expanded, collapsed, 4));
    }

    [Theory]
    [InlineData(80)]
    [InlineData(160)]
    public void A_line_too_long_to_fit_keeps_its_head_and_its_tail(int width)
    {
        var line = "17:01 tuicode dev: triggers failed: Get \"https://api.github.com/"
            + new string('x', 366) + "\": operation timed out";
        Assert.Equal(452, line.Length);

        var elided = LogView.Elide(line, width);

        Assert.Equal(width, elided.Length);
        Assert.StartsWith("17:01 tuicode dev: triggers failed:", elided, StringComparison.Ordinal);
        Assert.EndsWith(": operation timed out", elided, StringComparison.Ordinal);
        Assert.Contains('…', elided);
    }

    [Fact]
    public void A_line_that_already_fits_comes_back_untouched()
    {
        const string line = "17:01 a-team lead: would start: 3 Ideas to shape";

        Assert.Equal(line, LogView.Elide(line, 80));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(-3)]
    public void A_pane_too_narrow_for_even_the_head_is_still_drawn(int width) =>
        Assert.Equal(Math.Max(0, width), LogView.Elide("17:01 a-team lead: started 41234: 1 task Ready", width).Length);

    [Fact]
    public void An_eliding_view_gives_every_line_one_row_of_exactly_the_width()
    {
        var view = new LogView { Elides = true };
        view.Lines = [.. Enumerable.Range(1, 4).Select(n => new LogLine(new string((char)('a' + n), 452), LogLineKind.Prose))];

        var rows = view.Rows(80);

        Assert.Equal(4, rows.Count);
        Assert.All(rows, row => Assert.Equal(80, row.Text.Length));
    }

    [Fact]
    public void The_highlight_starts_on_the_last_line_and_moves_a_whole_wrapped_line_at_a_time()
    {
        var view = Selecting(20, 10, new("first", LogLineKind.Prose), new(string.Join(' ', Enumerable.Repeat("long", 12)), LogLineKind.Prose), new("last", LogLineKind.Prose));

        Assert.Equal((2, 2), view.Selection(view.Shown()));
        view.MoveSelection(-1, extend: false);
        Assert.Equal((1, 1), view.Selection(view.Shown()));
        view.MoveSelection(-1, extend: false);
        Assert.Equal((0, 0), view.Selection(view.Shown()));
        view.MoveSelection(-1, extend: false);
        Assert.Equal((0, 0), view.Selection(view.Shown()));
    }

    [Fact]
    public void A_folded_run_is_one_stop_and_copies_the_call_it_draws()
    {
        var view = Selecting(40, 10,
            new("first", LogLineKind.Prose),
            new("Bash one", LogLineKind.ToolCall, "one"),
            new("Bash two", LogLineKind.ToolCall, "two\nmore"),
            new("last", LogLineKind.Prose));

        view.MoveSelection(-1, extend: false);

        Assert.Equal("two\nmore", view.CopySelection().Text);
        view.MoveSelection(-1, extend: false);
        Assert.Equal("first", view.CopySelection().Text);
    }

    [Fact]
    public void Extending_grows_and_shrinks_from_where_it_started_and_a_plain_move_goes_back_to_one_line()
    {
        var view = Selecting(40, 10, [.. Enumerable.Range(0, 5).Select(n => new LogLine($"line {n}", LogLineKind.Prose))]);

        view.MoveSelection(-1, extend: false);
        view.MoveSelection(-1, extend: true);
        view.MoveSelection(-1, extend: true);
        Assert.Equal((1, 3), view.Selection(view.Shown()));
        Assert.Equal("line 1\nline 2\nline 3", view.CopySelection().Text);
        view.MoveSelection(+1, extend: true);
        Assert.Equal((2, 3), view.Selection(view.Shown()));

        view.MoveSelection(+1, extend: false);
        Assert.Equal((3, 3), view.Selection(view.Shown()));
    }

    [Fact]
    public void Moving_the_highlight_stops_following_and_End_follows_again_on_the_last_line()
    {
        var view = Selecting(40, 3, [.. Enumerable.Range(0, 10).Select(n => new LogLine($"line {n}", LogLineKind.Prose))]);

        view.MoveSelection(-1, extend: false);
        Assert.False(view.Following);
        view.Lines = [.. view.Lines, new LogLine("new", LogLineKind.Prose)];
        Assert.Equal("line 8", view.CopySelection().Text);

        view.End();

        Assert.True(view.Following);
        Assert.Equal("new", view.CopySelection().Text);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(+1)]
    public void Paging_brings_the_highlight_with_it(int direction)
    {
        var view = Selecting(40, 5, [.. Enumerable.Range(0, 40).Select(n => new LogLine($"line {n}", LogLineKind.Prose))]);
        view.MoveSelection(-20, extend: false);

        view.Page(direction);
        view.Page(direction);

        var (from, _) = view.Selection(view.Shown());
        Assert.InRange(from, view.Top, view.Top + 4);
        Assert.Equal(direction < 0 ? 11 : 27, from);
    }

    [Fact]
    public void The_highlight_stays_on_its_line_when_the_oldest_lines_are_trimmed()
    {
        var view = Selecting(40, 5, [.. Enumerable.Range(0, 10).Select(n => new LogLine($"line {n}", LogLineKind.Prose))]);
        view.MoveSelection(-3, extend: false);

        view.Lines = [.. view.Lines.Skip(2), new LogLine("line 10", LogLineKind.Prose), new LogLine("line 11", LogLineKind.Prose)];

        Assert.Equal("line 6", view.CopySelection().Text);
    }

    [Fact]
    public void Copying_the_whole_log_takes_every_line_folded_calls_included()
    {
        var view = Selecting(40, 10,
            new("first", LogLineKind.Prose),
            new("Bash one", LogLineKind.ToolCall, "one"),
            new("Bash two", LogLineKind.ToolCall, "two"));

        var copied = view.CopyAll();

        Assert.Equal("first\none\ntwo", copied.Text);
        Assert.Equal(3, copied.Lines);
    }

    private static LogView Selecting(int width, int height, params LogLine[] lines)
    {
        var view = new LogView { Lines = lines };
        view.Frame = new System.Drawing.Rectangle(0, 0, width, height);
        view.Selects = true;
        return view;
    }

    private static List<LogLine> Fixture() =>
        File.ReadLines(Path.Combine(AppContext.BaseDirectory, "fixtures", "pane.jsonl"))
            .SelectMany(SessionLog.Render)
            .ToList();
}
