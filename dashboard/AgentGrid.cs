using System.Drawing;

namespace ATeam.Dashboard;

/// <summary>Where each agent sits in the dashboard's grid: one row per team, one column per role.</summary>
internal static class AgentGrid
{
    internal static int Columns(IReadOnlyList<(string Team, string Role)> agents) =>
        Math.Max(1, agents.Select(agent => agent.Role).Distinct().Count());

    internal static Rectangle Cell(int index, int count, int columns, Size area)
    {
        var rows = (count + columns - 1) / columns;
        var row = index / columns;
        var column = index % columns;
        var left = area.Width * column / columns;
        var right = index == count - 1 ? area.Width : area.Width * (column + 1) / columns;
        var top = area.Height * row / rows;
        var bottom = area.Height * (row + 1) / rows;
        return new Rectangle(left, top, right - left, bottom - top);
    }
}
