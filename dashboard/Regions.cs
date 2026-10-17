using MentalDesk.Tui.Commands;

namespace ATeam.Dashboard;

/// <summary>Where the window can be, each with keys of its own: the Work area, Overseer, the agent grid, a log
/// expanded to fill the window, and the Assistant pane beside any of them.</summary>
internal static class Regions
{
    internal static readonly CommandScope Work = new("Work");
    internal static readonly CommandScope Overseer = new("Overseer");
    internal static readonly CommandScope Agents = new("Agents");
    internal static readonly CommandScope Log = new("Log");
    internal static readonly CommandScope Assistant = new("Assistant");
}
