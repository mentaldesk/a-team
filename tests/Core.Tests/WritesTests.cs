using System.Text.Json.Nodes;
using Xunit;

namespace ATeam.Core.Tests;

public class WritesTests
{
    private const string Shots = "## What it looks like\n\nCap\n\n![Cap](u)";

    [Theory]
    [InlineData("", Shots)]
    [InlineData("\n\n", Shots)]
    [InlineData("Words", "Words\n\n" + Shots)]
    [InlineData("Closes #12\n\n<!-- a-team:dev -->", "Closes #12\n\n" + Shots + "\n\n<!-- a-team:dev -->")]
    [InlineData("A\n\na-team try demo 9\n\n<!-- a-team:dev -->", "A\n\n" + Shots + "\n\na-team try demo 9\n\n<!-- a-team:dev -->")]
    [InlineData("a\n\n\nb\n\n\n\n<!-- a-team:dev -->", "a\n\n\nb\n\n" + Shots + "\n\n<!-- a-team:dev -->")]
    [InlineData("<!-- a-team:lead -->\n\nend", "<!-- a-team:lead -->\n\nend\n\n" + Shots)]
    public void A_section_goes_above_the_try_line_and_the_Devs_marker(string body, string with) =>
        Assert.Equal(with, Board.WithSection(body, Shots));

    [Theory]
    [InlineData("A\n\n## What it looks like\n\nOld\n\n![Old](v)\n\na-team try x\n\n<!-- a-team:dev -->", "A\n\n" + Shots + "\n\na-team try x\n\n<!-- a-team:dev -->")]
    [InlineData("A\n\n## What it looks like  \n\nOld\n\n<!-- a-team:dev -->", "A\n\n" + Shots + "\n\n<!-- a-team:dev -->")]
    [InlineData("A\r\n## What it looks like\r\nOld\r\n## Next\r\nz", "A\r\n\n## Next\r\nz\n\n" + Shots)]
    [InlineData("## What it looks like\n### A smaller heading stays in it\n# Next", "# Next\n\n" + Shots)]
    public void A_section_replaces_the_one_already_there(string body, string with) =>
        Assert.Equal(with, Board.WithSection(body, Shots));

    private const string Pics = "## What it'll look like\n\nCap\n\n![Cap](u)";

    [Theory]
    [InlineData("A\n\n## Mockup\n\nm\n\n## Scope", "A\n\n" + Pics + "\n\n## Mockup\n\nm\n\n## Scope")]
    [InlineData("A\n## Mockup\nm", "A\n\n" + Pics + "\n\n## Mockup\nm")]
    [InlineData("## Mockups\nm", Pics + "\n\n## Mockups\nm")]
    public void A_pitchs_pictures_go_above_its_text_mockup(string body, string with) =>
        Assert.Equal(with, Board.WithPitchPictures(body, Pics));

    [Theory]
    [InlineData("A\n\n## What it'll look like\n\nNo picture: x.\n\n## Mockup\n\nm", "A\n\n" + Pics + "\n\n## Mockup\n\nm")]
    [InlineData("A\n\n## What it'll look like\n\nOld\n\n![Old](v)\n\n## Mockup\n\nm", "A\n\n" + Pics + "\n\n## Mockup\n\nm")]
    [InlineData("## Mockup\n\nm\n\n## What it'll look like\n\nOld\n\n---\n\nz", Pics + "\n\n## Mockup\n\nm\n\n---\n\nz")]
    public void A_later_revision_replaces_the_pictures_rather_than_adding_a_second(string body, string with) =>
        Assert.Equal(with, Board.WithPitchPictures(body, Pics));

    [Fact]
    public void A_heading_drawn_in_a_mockups_code_block_is_left_alone()
    {
        const string body = "## Mockup\n\n```\n## What it'll look like\n\nNo picture.\n```\n\n## Scope";

        Assert.Equal(Pics + "\n\n" + body, Board.WithPitchPictures(body, Pics));
    }

    [Theory]
    [InlineData("A\n\n## Scope")]
    [InlineData("```\n## Mockup\n```\n")]
    public void A_pitch_with_no_mockup_has_nowhere_for_its_pictures(string body) =>
        Assert.Null(Board.WithPitchPictures(body, Pics));

    [Fact]
    public void Criteria_are_the_list_under_the_heading_up_to_the_next_one()
    {
        var items = Checklist.Items("Intro\n- not this\n\n## Acceptance criteria\r\n\r\n- one\n- [ ] two\n  wrapped\n* [x] three\n  + [X]four\n\n## Next\n- nor this\n");
        Assert.Equal(
        [
            (5L, false, "one"),
            (6L, false, "two wrapped"),
            (8L, true, "three"),
            (9L, true, "four"),
        ], items.Select(item => (Json.Long(item["line"]), Json.Truthy(item["ticked"]), Json.Text(item["text"]))));
    }

    [Theory]
    [InlineData("Nothing to see\n- here")]
    [InlineData("##Acceptance criteria\n- no space")]
    [InlineData("## Acceptance criteria\n\nWords before any item.\n")]
    public void No_criteria(string body) => Assert.Empty(Checklist.Items(body));

    [Theory]
    [InlineData("1", "a")]
    [InlineData("2", "b")]
    [InlineData("3", null)]
    [InlineData("99999999999999999999", null)]
    public void Criteria_count_from_one(string index, string? text) =>
        Assert.Equal(text, Json.Text(Checklist.Nth(Checklist.Items("# Acceptance criteria\n- a\n- b"), index)?["text"]));

    [Theory]
    [InlineData("- one\n- two", "- [x] one\n- two")]
    [InlineData("- [ ] one\n- two", "- [x] one\n- two")]
    [InlineData("- [X]one\r\n- two", "- [x] one\r\n- two")]
    [InlineData("  * [x]   one", "  * [x]   one")]
    public void Boxed_sets_only_the_lines_named(string body, string with) =>
        Assert.Equal(with, Checklist.Boxed(body, [0], "x"));

    [Fact]
    public void Boxed_clears_a_box() => Assert.Equal("- [ ] one", Checklist.Boxed("- [x] one", [0], " "));

    [Fact]
    public void A_section_ends_at_a_big_heading_the_try_line_or_a_marker()
    {
        Assert.Equal(["a", "### b", ""], Checklist.Section("x\n## H  \r\na\n### b\n\n## Next\nc", "## H"));
        Assert.Equal(["a"], Checklist.Section("## H\na\na-team try x", "## H"));
        Assert.Equal(["a"], Checklist.Section("## H\na\n<!-- a-team:dev -->", "## H"));
        Assert.Empty(Checklist.Section("nothing", "## H"));
    }

    [Theory]
    [InlineData("  a  [b]\tc ", "a b c")]
    [InlineData("plain", "plain")]
    [InlineData("", "")]
    public void Words_are_compared_without_brackets_or_extra_space(string text, string norm) =>
        Assert.Equal(norm, Checklist.Norm(text));

    [Fact]
    public void A_PR_body_says_what_was_checked_and_what_is_pictured()
    {
        const string body = "## What it looks like\n\nSave [left]\n\n![Save [left]](u)\n![  Two  words ](v)\n\n## Checked by the team\n\n- `screen` Save [left]\n- `test`  Two words\n- `other` No\n\n<!-- a-team:dev -->";
        Assert.Equal(["Save left", "Two words"], Checklist.Pictured(body).Order(StringComparer.Ordinal));
        Assert.Equal(["Save left", "Two words"], Checklist.Recorded(body).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Only_a_linked_worktree_on_a_branch_is_listed()
    {
        const string porcelain = """
            worktree /clone
            HEAD aaa
            branch refs/heads/main

            worktree /clone.w/one
            HEAD bbb
            branch refs/heads/feat/one

            worktree /clone.w/detached
            HEAD ccc
            detached

            worktree /clone.w/locked
            HEAD ddd
            branch refs/heads/feat/locked
            locked why

            worktree /clone.w/a	tab
            HEAD eee
            branch refs/heads/feat/tab
            """;
        Assert.Equal([("/clone.w/one", "bbb", "feat/one"), ("/clone.w/locked", "ddd", "feat/locked")], Board.Worktrees(porcelain));
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("[]", null)]
    [InlineData("""[{"number": 3}, {"number": 9}, {"number": 5}]""", 9L)]
    [InlineData("""[{"number": 3}, {"number": 9, "isCrossRepository": true}]""", 3L)]
    [InlineData("""[{"number": 9, "isCrossRepository": true}]""", null)]
    public void Only_a_branchs_newest_PR_from_this_repo_counts(string prs, long? number) =>
        Assert.Equal(number, Board.NewestOwnPr(prs)?["number"]?.GetValue<long>());

    [Theory]
    [InlineData("OPEN", "<!-- a-team:dev -->", "[]", false, true)]
    [InlineData("MERGED", "<!-- a-team:dev -->", "[]", false, false)]
    [InlineData("OPEN", "By hand", "[]", false, false)]
    [InlineData("OPEN", "<!-- a-team:dev -->", """[{"name": "pitch"}]""", false, false)]
    [InlineData("OPEN", "<!-- a-team:dev -->", "[]", true, false)]
    public void A_Dev_task_PR_is_open_marked_and_neither_a_pitch_nor_the_docs(string state, string body, string labels, bool customer, bool isOne)
    {
        var pr = new JsonObject
        {
            ["state"] = state,
            ["body"] = body,
            ["labels"] = new JsonObject { ["nodes"] = JsonNode.Parse(labels) },
            ["customer"] = customer,
        };
        Assert.Equal(isOne, Board.DevTaskPr(pr));
    }
}
