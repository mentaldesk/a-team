namespace ATeam.Dashboard.Tests;

public class MarkdownTests
{
    [Fact]
    public void Headings_quotes_and_fenced_code_are_told_apart_line_by_line()
    {
        var lines = Markdown.Lines("## Opportunity\r\nplain\n> quoted\n```bash\n# not a heading\n```\n#hashtag");

        Assert.Equal(
            [LogLineKind.Heading, LogLineKind.Prose, LogLineKind.Quote, LogLineKind.Code, LogLineKind.Code, LogLineKind.Code, LogLineKind.Prose],
            lines.Select(line => line.Kind));
        Assert.Equal("## Opportunity", lines[0].Text);
    }

    [Fact]
    public void A_prose_row_marks_its_list_marker_inline_code_and_bold()
    {
        var spans = Markdown.Spans(new LogRow("- run `a-team board` **first**", LogLineKind.Prose)).ToList();

        Assert.Equal(
            [new MarkdownSpan(0, 1, LogLineKind.ListMarker), new MarkdownSpan(6, 14, LogLineKind.InlineCode), new MarkdownSpan(21, 9, LogLineKind.Strong)],
            spans);
    }

    [Fact]
    public void A_wrapped_row_that_starts_like_a_list_is_not_one()
    {
        var spans = Markdown.Spans(new LogRow("1. more of the sentence", LogLineKind.Prose, Wrapped: true));

        Assert.Empty(spans);
    }

    [Fact]
    public void Code_and_headings_are_coloured_whole_and_carry_no_spans()
    {
        Assert.Empty(Markdown.Spans(new LogRow("- `x`", LogLineKind.Code)));
        Assert.Empty(Markdown.Spans(new LogRow("## `x`", LogLineKind.Heading)));
    }
}
