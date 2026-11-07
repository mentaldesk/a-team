using MentalDesk.Tui.Shell;

namespace ATeam.Dashboard;

/// <summary>The title of the terminal tab the dashboard runs in: the team it was opened on, if just one, and the app's name.</summary>
public static class TerminalTab
{
    public const string App = "a-team";

    public static TerminalTitle Names(this TerminalTitle title, IReadOnlyList<string> opened)
    {
        title.App = App;
        title.Place = opened is [var team] ? team : null;
        return title;
    }

    /// <summary>Shows the app and hands the terminal over until there's nothing to hand over, with the terminal's own
    /// title back for each handover and at the end. A new window starts with no unseen errors.</summary>
    public static void Run(TerminalTitle title, Func<Handover?, Handover?> show, Func<Handover, Handover> hand)
    {
        title.Save();
        try
        {
            title.Show();
            Handover? back = null;
            while (show(back) is { } handover)
            {
                title.Restore();
                // Saved again now, not on return, so a title the handed-over program leaves can't outlast the app.
                title.Save();
                title.UnseenErrors = 0;
                back = hand(handover);
                title.Show();
            }
        }
        finally
        {
            title.Restore();
        }
    }
}
