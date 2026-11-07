using Xunit;

namespace ATeam.Core.Tests;

public class RecordTests
{
    [Fact]
    public void A_query_that_fails_part_way_says_so_rather_than_looking_empty()
    {
        using var record = Record.Open(":memory:");
        var problem = Assert.Throws<InvalidOperationException>(() =>
            record.Query("SELECT abs(-9223372036854775807 - 1);"));
        Assert.Contains("overflow", problem.Message);
    }

    [Fact]
    public void So_does_a_query_read_as_JSON()
    {
        using var record = Record.Open(":memory:");
        var problem = Assert.Throws<InvalidOperationException>(() =>
            record.Rows("SELECT abs(-9223372036854775807 - 1);"));
        Assert.Contains("overflow", problem.Message);
    }

    [Fact]
    public void A_query_with_no_rows_is_just_empty()
    {
        using var record = Record.Open(":memory:");
        Assert.Empty(record.Query("SELECT item FROM board;"));
    }
}
