namespace ATeam.Dashboard.Tests;

public class ConsideredTests
{
    public static TheoryData<string> Fixtures()
    {
        var all = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures", "reviews"), "*.md"))
            all.Add(Path.GetFileNameWithoutExtension(file));
        return all;
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void The_points_are_as_many_as_the_board_counts(string name)
    {
        var review = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "reviews", $"{name}.md"));

        Assert.Equal(int.Parse(name[(name.LastIndexOf('-') + 1)..]), Considered.Points(review).Count);
    }

    [Fact]
    public void No_section_no_points() => Assert.Empty(Considered.Points("Nothing needs changing."));

    [Fact]
    public void Each_point_loses_its_bullet_and_keeps_its_lines()
    {
        var points = Considered.Points(
            "## Worth considering\r\n- One\r\n  over two lines.\r\n\r\n  And a paragraph.\r\n1. Two\r\n\r\nNot in the list.\r\n\r\n<!-- a-team:reviewer -->");

        Assert.Equal(["One\nover two lines.\n\nAnd a paragraph.", "Two"], points);
    }

    [Fact]
    public void A_point_read_with_the_marker_left_off_stops_at_the_end_of_the_section()
    {
        Assert.Equal(["One"], Considered.Points("## Worth considering\n- One\n\n## After\n- Two"));
    }

    [Fact]
    public void They_come_from_the_Reviewer_s_review_of_this_PR()
    {
        var at = DateTimeOffset.UnixEpoch;
        Remark[] remarks =
        [
            new("reviewer", at, "## Worth considering\n- Old PR.", 1),
            new("reviewer", at, "## Worth considering\n- This PR.", 2),
            new("dev", at, "## Worth considering\n- Not the Reviewer.", 2),
            new("you", at, "## Worth considering\n- Nor you.", 2),
        ];

        Assert.Equal(["This PR."], Considered.Points(remarks, 2));
        Assert.Empty(Considered.Points(remarks, 3));
    }

    [Fact]
    public void The_comment_quotes_one_point() =>
        Assert.Equal("> Rename it.\n\nPlease fix these.", Considered.Request(["Rename it."], "  "));

    [Fact]
    public void The_comment_quotes_each_point_apart_and_ends_with_the_note() =>
        Assert.Equal("> Rename it.\n\n> Split it.\n\nPlease fix these. keep the old name as an alias",
            Considered.Request(["Rename it.", "Split it."], " keep the old name as an alias "));

    [Fact]
    public void A_quote_keeps_a_point_over_several_lines_intact() =>
        Assert.Equal("> One\n> over two lines.\n>\n> And a paragraph.\n\nPlease fix these.",
            Considered.Request(["One\nover two lines.\n\nAnd a paragraph."], ""));
}
