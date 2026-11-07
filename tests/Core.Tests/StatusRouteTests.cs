using Xunit;

namespace ATeam.Core.Tests;

public class StatusRouteTests
{
    private static readonly string[] Everyone = [.. StatusRoute.Roles, "you"];
    private static readonly string[] From = [StatusRoute.None, .. StatusRoute.Statuses];

    public static TheoryData<string, string, string, bool> EveryMove()
    {
        var data = new TheoryData<string, string, string, bool>();
        foreach (var role in Everyone)
            foreach (var from in From)
                foreach (var to in StatusRoute.Statuses)
                    data.Add(role, from, to, Expected.Contains($"{role}:{from}>{to}"));
        return data;
    }

    private static readonly HashSet<string> Expected =
    [
        "lead:Idea>Exploring", "lead:Exploring>Pitched", "lead:Exploring>Idea", "lead:Pitched>Exploring", "lead:Approved>Building",
        "lead:None>Idea", "lead:None>Exploring", "lead:None>Pitched", "lead:None>Ready",
        "dev:None>Idea", "dev:Ready>In progress", "dev:In progress>In review", "dev:In progress>Ready",
        "customer:None>Idea", "customer:None>Pitched", "customer:None>In review",
        "you:None>Idea",
    ];

    [Theory]
    [MemberData(nameof(EveryMove))]
    public void Every_role_status_and_target_is_allowed_only_where_the_table_says(string role, string from, string to, bool allowed) =>
        Assert.Equal(allowed, StatusRoute.Allowed(role, from, to));

    [Fact]
    public void Nobody_moves_a_card_to_Done_or_out_of_it_here() =>
        Assert.DoesNotContain(StatusRoute.All, move => move.To == "Done" || move.From == "Done");

    [Fact]
    public void The_stakeholders_only_move_here_is_adding_an_idea() =>
        Assert.Equal([("you", StatusRoute.None, "Idea")], StatusRoute.All.Where(move => move.Role == "you"));
}
