namespace ATeam.Dashboard.Tests;

public class RemoveTeamDialogTests
{
    [Fact]
    public void It_names_the_team_and_says_what_is_and_is_not_affected()
    {
        using var dialog = RemoveTeamDialog.Create("goose", "aaif-goose/goose", "goose.json.removed");

        Assert.Equal("Remove goose?", dialog.Title);
        Assert.Equal(
            "a-team forgets this team. aaif-goose/goose, its board and everything the team has built are untouched, " +
            "and the config is kept as goose.json.removed if you want it back.",
            string.Join(' ', dialog.Lines));
    }

    [Fact]
    public void It_says_when_an_older_removed_file_is_kept_as_it_is() =>
        Assert.EndsWith(
            "kept as goose.json.removed.2 if you want it back. An older goose.json.removed is left as it is.",
            RemoveTeamDialog.Says("goose", "aaif-goose/goose", "goose.json.removed.2"));
}
