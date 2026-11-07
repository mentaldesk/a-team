using Xunit;

namespace ATeam.Core.Tests;

public class NeedsAnswerTests
{
    [Theory]
    [InlineData("## Opportunity\n\nSomething.")]
    [InlineData("## Needs your answer\n\n")]
    [InlineData("## Needs your answer\n\nNone.\n")]
    [InlineData("## needs your answer\n\nnone\n\n## Next\n\nMore.")]
    [InlineData("```\n## Needs your answer\n\nIn a code block.\n```\n")]
    public void Nothing_to_answer(string body) => Assert.Equal("", NeedsAnswer.In(body));

    [Theory]
    [InlineData("## Needs your answer\n\nWhich one?\n", "## Needs your answer\n\nWhich one?")]
    [InlineData("## Needs your answer\r\n\r\nWhich one?\r\n\r\n## Next\r\n\r\nMore.", "## Needs your answer\n\nWhich one?")]
    [InlineData("## Needs your answer\n\nWhich one?\n\n---\n\n## Original idea", "## Needs your answer\n\nWhich one?")]
    [InlineData("## Needs your answer\n\nWhich one?\n\n<!-- a-team:lead -->", "## Needs your answer\n\nWhich one?")]
    [InlineData("## Needs your answer\n\n```\n## Not a heading\n```\n", "## Needs your answer\n\n```\n## Not a heading\n```")]
    [InlineData("## Needs your answer\n\nWhich?\n\n### A smaller heading\n\nMore.", "## Needs your answer\n\nWhich?\n\n### A smaller heading\n\nMore.")]
    public void The_section_up_to_where_it_ends(string body, string section) => Assert.Equal(section, NeedsAnswer.In(body));
}
