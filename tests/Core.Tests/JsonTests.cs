using System.Text.Json.Nodes;
using Xunit;

namespace ATeam.Core.Tests;

public class JsonTests
{
    [Fact]
    public void It_prints_as_jq_does()
    {
        var node = JsonNode.Parse("""{"a": [1, {"b": null}], "c": {}, "d": [], "e": "ü\"\n\u0001"}""");
        Assert.Equal("""
            {
              "a": [
                1,
                {
                  "b": null
                }
              ],
              "c": {},
              "d": [],
              "e": "ü\"\n\u0001"
            }
            """, Json.Pretty(node));
    }

    [Fact]
    public void Compact_has_no_spaces() =>
        Assert.Equal("""{"a":[1,true],"b":"x y"}""", Json.Compact(JsonNode.Parse("""{ "a": [1, true], "b": "x y" }""")));

    [Fact]
    public void Paginated_output_is_one_document_after_another() =>
        Assert.Equal(2, Json.ParseMany("""{"a":1}{"a":2}""").Count);

    [Theory]
    [InlineData("a-team:dev", "dev")]
    [InlineData("a-team:idea", "idea")]
    public void An_old_label_stands_for_a_mark(string label, string mark)
    {
        var marks = Board.MarksOf(new JsonArray((JsonNode?)label));
        Assert.True(marks.ContainsKey(mark));
    }

    [Fact]
    public void A_rank_label_is_a_capitalised_rank() =>
        Assert.Equal("Medium", Json.Text(Board.MarksOf(new JsonArray((JsonNode?)"rank:medium"))["rank"]));
}
