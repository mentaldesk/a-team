using MentalDesk.Tui.Dialogs;

namespace ATeam.Dashboard;

/// <summary>Asks before a team is removed, saying what goes and what doesn't.</summary>
public static class RemoveTeamDialog
{
    internal static readonly ConfirmAction Remove = new("Remove", ButtonKind.Danger, Confirm.Chord);

    internal static string Says(string team, string repo, string kept)
    {
        var older = kept == $"{team}.json.removed" ? "" : $" An older {team}.json.removed is left as it is.";
        var what = repo.Length > 0 ? repo : "Its repo";
        return $"a-team forgets this team. {what}, its board and everything the team has built are untouched, " +
               $"and the config is kept as {kept} if you want it back.{older}";
    }

    internal static ConfirmDialog Create(string team, string repo, string kept, int width = Confirm.Wide) =>
        Confirm.Create($"Remove {team}?", Says(team, repo, kept), Remove, width);

    public static bool Show(IApplication app, string team, string repo, string kept)
    {
        using var dialog = Create(team, repo, kept, Confirm.Fit(app.Screen.Width));
        return Confirm.Ask(app, dialog);
    }
}
