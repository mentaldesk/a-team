namespace ATeam.Dashboard;

/// <summary>Asks before a team is removed, saying what goes and what doesn't.</summary>
public sealed class RemoveTeamDialog(string team, string repo, string kept)
    : ConfirmDialog($"Remove {team}?", Says(team, repo, kept), "remove")
{
    internal static string Says(string team, string repo, string kept)
    {
        var older = kept == $"{team}.json.removed" ? "" : $" An older {team}.json.removed is left as it is.";
        var what = repo.Length > 0 ? repo : "Its repo";
        return $"a-team forgets this team. {what}, its board and everything the team has built are untouched, " +
               $"and the config is kept as {kept} if you want it back.{older}";
    }

    public static bool Show(IApplication app, string team, string repo, string kept)
    {
        using var dialog = new RemoveTeamDialog(team, repo, kept);
        return Ask(app, dialog);
    }
}
