namespace ATeam.Dashboard.Tests;

public class ApproveDialogTests
{
    [Fact]
    public void It_names_the_pitch_and_says_what_approving_starts()
    {
        using var dialog = ApproveDialog.Create(
            new(169, "Approve by mistake", "Pitched", "https://github.com/x/169", "a-team", Pitch: true));

        Assert.Equal("Approve #169?", dialog.Title);
        Assert.Equal(["Moves #169 to Approved. The Lead's next run breaks it into tasks."], dialog.Lines);
    }
}
