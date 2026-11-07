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

    public static TheoryData<string, string, string, bool> EveryGate()
    {
        var data = new TheoryData<string, string, string, bool>();
        foreach (var who in new[] { "lead", "dev", "customer", "reviewer", "you", StatusRoute.Dispatcher })
            foreach (var from in From)
                foreach (var to in StatusRoute.Statuses)
                    data.Add(who, from, to, Expected.Contains($"{who}:{from}>{to}") || Gates.Contains($"{who}:{from}>{to}")
                        || (who == StatusRoute.Dispatcher && to is "Done" or "In progress" or "Ready"));
        return data;
    }

    private static readonly HashSet<string> Gates =
    [
        "you:Pitched>Approved", "lead:Pitched>Approved", "you:Idea>Done", "you:Pitched>Done", "you:In review>Done",
        "you:Pitched>Idea", "lead:Pitched>Idea", "dev:None>In progress", "dev:None>In review",
    ];

    [Theory]
    [MemberData(nameof(EveryGate))]
    public void Every_status_write_a_command_can_ask_for_is_permitted_only_where_the_route_says(string who, string from, string to, bool permitted) =>
        Assert.Equal(permitted, StatusRoute.Permits(who, from, to));

    [Theory]
    [MemberData(nameof(EveryStatus))]
    public void Every_status_has_a_way_out(string status) =>
        Assert.True(StatusRoute.Everything().Any(move => move.From == status && move.To != status && move.Who != StatusRoute.Dispatcher)
            || StatusRoute.Follows().Any(move => move.From == status && move.To != status));

    [Theory]
    [MemberData(nameof(EveryStatus))]
    public void Every_status_can_be_reached(string status)
    {
        if (status != StatusRoute.None)
            Assert.True(StatusRoute.Everything().Any(move => move.To == status && move.Who != StatusRoute.Dispatcher)
                || StatusRoute.Follows().Any(move => move.To == status));
    }

    public static TheoryData<string> EveryStatus()
    {
        var data = new TheoryData<string>();
        foreach (var status in From)
            data.Add(status);
        return data;
    }

    [Fact]
    public void Nobody_moves_a_card_to_Done_or_out_of_it_here() =>
        Assert.DoesNotContain(StatusRoute.All, move => move.To == "Done" || move.From == "Done");

    [Fact]
    public void The_stakeholders_only_move_here_is_adding_an_idea() =>
        Assert.Equal([("you", StatusRoute.None, "Idea")], StatusRoute.All.Where(move => move.Role == "you"));
}
