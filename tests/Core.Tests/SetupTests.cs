using System.Text.Json.Nodes;
using Xunit;

namespace ATeam.Core.Tests;

public class SetupTests
{
    private static string[] Ours => StatusRoute.Statuses;

    private static JsonObject Field(params string[] names)
    {
        var options = new JsonArray();
        foreach (var name in names)
            options.Add((JsonNode?)new JsonObject { ["id"] = $"O_{name}", ["name"] = name });
        return new JsonObject { ["id"] = "F_status", ["options"] = options };
    }

    private static string Named(JsonNode? option) => Json.Text(option?["name"])!;

    private static string[] Options(JsonObject input) => input["singleSelectOptions"]!.AsArray().Select(Named).ToArray();

    private static string[] Dropped(JsonObject input) => input["dropped"]!.AsArray().Select(Named).ToArray();

    [Fact]
    public void Setup_adds_each_status_the_project_lacks_in_the_teams_order()
    {
        var input = Board.StatusOptions(Field("Done", "Idea"), [], status => status);
        Assert.Equal("F_status", Json.Text(input["fieldId"]));
        Assert.Equal(["fieldId", "singleSelectOptions", "dropped"], input.Select(pair => pair.Key));
        Assert.Equal(Ours, Options(input));
        var options = input["singleSelectOptions"]!.AsArray();
        Assert.Equal("O_Idea", Json.Text(options[0]!["id"]));
        Assert.Equal("O_Done", Json.Text(options[8]!["id"]));
        Assert.Equal("""{"name":"Exploring","color":"PURPLE","description":"Lead is researching and writing a pitch"}""", Json.Compact(options[1]));
    }

    [Fact]
    public void Setup_keeps_a_projects_other_options_after_the_teams()
    {
        var input = Board.StatusOptions(Field("Blocked", "Idea"), [], status => status);
        Assert.Equal([.. Ours, "Blocked"], Options(input));
        Assert.Empty(Dropped(input));
    }

    [Fact]
    public void Setup_drops_the_defaults_GitHub_made_unless_an_item_has_one()
    {
        var input = Board.StatusOptions(Field("Todo", "In Progress", "Idea"), ["In Progress"], status => status);
        Assert.Equal([.. Ours, "In Progress"], Options(input));
        Assert.Equal(["Todo"], Dropped(input));
    }

    [Fact]
    public void Setup_asks_for_a_mapped_status_by_the_name_it_has_on_the_project()
    {
        var input = Board.StatusOptions(Field("Todo", "Triage"), [], status => status switch { "Idea" => "Triage", "Ready" => "Todo", _ => status });
        Assert.Equal("Triage", Options(input)[0]);
        Assert.Equal("Todo", Options(input)[5]);
        Assert.Equal("O_Todo", Json.Text(input["singleSelectOptions"]![5]!["id"]));
        Assert.Empty(Dropped(input));
    }

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
