using System.Text.Json.Nodes;
using Xunit;

namespace ATeam.Core.Tests;

public class SetupTests
{
    [Theory]
    [InlineData("jq: parse error: Expected another key-value pair at line 3, column 1", "can't read demo.json: Expected another key-value pair at line 3, column 1")]
    [InlineData("jq: error (at demo.json:1): boom", "can't read demo.json: error (at demo.json:1): boom")]
    [InlineData("", "demo.json isn't a JSON object")]
    public void A_config_that_cant_be_read_is_said_as_board_sh_said_it(string jq, string words)
    {
        Assert.Equal(words, Commands.UnreadableWords("demo", jq));
    }

    [Fact]
    public void Every_step_is_for_a_format_this_a_team_can_reach()
    {
        Assert.All(Board.FormatSteps.Keys, format => Assert.InRange(format, 2, Board.Format));
    }
}
