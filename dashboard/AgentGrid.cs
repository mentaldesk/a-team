using System.Drawing;

namespace ATeam.Dashboard;

/// <summary>Where each agent sits in the dashboard's grid: one row per team, one column per role it runs. A row with
/// fewer roles than the widest stretches its last pane to the edge.</summary>
internal static class AgentGrid
{
    internal static int Columns(IReadOnlyList<(string Team, string Role)> agents) =>
        Math.Max(1, agents.CountBy(agent => agent.Team).Select(team => team.Value).DefaultIfEmpty().Max());

    internal static int Rows(IReadOnlyList<(string Team, string Role)> agents) =>
        agents.Select(agent => agent.Team).Distinct().Count();

    /// <summary>The column and row of the agent at <paramref name="index"/>.</summary>
    internal static Point Slot(IReadOnlyList<(string Team, string Role)> agents, int index)
    {
        var team = agents[index].Team;
        return new Point(
            agents.Take(index).Count(agent => agent.Team == team),
            agents.Select(agent => agent.Team).Distinct().TakeWhile(other => other != team).Count());
    }

    /// <summary>The agent nearest <paramref name="column"/> in <paramref name="row"/>.</summary>
    internal static int At(IReadOnlyList<(string Team, string Role)> agents, int row, int column)
    {
        var team = agents.Select(agent => agent.Team).Distinct().ElementAt(row);
        var inRow = Enumerable.Range(0, agents.Count).Where(i => agents[i].Team == team).ToList();
        return inRow[Math.Clamp(column, 0, inRow.Count - 1)];
    }

    internal static Rectangle Cell(IReadOnlyList<(string Team, string Role)> agents, int index, Size area)
    {
        var columns = Columns(agents);
        var rows = Rows(agents);
        var slot = Slot(agents, index);
        var last = slot.X == agents.Count(agent => agent.Team == agents[index].Team) - 1;
        var left = area.Width * slot.X / columns;
        var right = last ? area.Width : area.Width * (slot.X + 1) / columns;
        var top = area.Height * slot.Y / rows;
        var bottom = area.Height * (slot.Y + 1) / rows;
        return new Rectangle(left, top, right - left, bottom - top);
    }
}
