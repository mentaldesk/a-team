using System.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace ATeam.Dashboard.Tests;

public class RemoveTeamDialogTests
{
    [Fact]
    public void It_names_the_team_and_says_what_is_and_is_not_affected()
    {
        using var dialog = new RemoveTeamDialog("goose", "aaif-goose/goose", "goose.json.removed");

        Assert.Equal("Remove goose?", dialog.Title);
        Assert.Equal(
            "a-team forgets this team. aaif-goose/goose, its board and everything the team has built are untouched, " +
            "and the config is kept as goose.json.removed if you want it back.",
            dialog.Body.Text);
    }

    [Fact]
    public void It_says_when_an_older_removed_file_is_kept_as_it_is() =>
        Assert.EndsWith(
            "kept as goose.json.removed.2 if you want it back. An older goose.json.removed is left as it is.",
            RemoveTeamDialog.Says("goose", "aaif-goose/goose", "goose.json.removed.2"));

    [Fact]
    public void Esc_cancels()
    {
        using var dialog = new RemoveTeamDialog("goose", "o/r", "goose.json.removed");

        Assert.True(dialog.NewKeyDownEvent(Key.Esc));

        Assert.False(dialog.Confirmed);
    }

    [Fact]
    public void Enter_removes()
    {
        using var dialog = new RemoveTeamDialog("goose", "o/r", "goose.json.removed");

        dialog.InvokeCommand(Command.Accept);

        Assert.True(dialog.Confirmed);
    }

    [Fact]
    public void The_text_wraps_to_fit_and_the_hints_sit_under_it()
    {
        using var host = new View { Width = 40, Height = 20 };
        using var dialog = new RemoveTeamDialog("goose", "aaif-goose/goose", "goose.json.removed");
        host.Add(dialog);

        host.Layout(new Size(40, 20));

        Assert.True(host.Viewport.Contains(dialog.Frame), $"{dialog.Frame} overhangs {host.Viewport}");
        var lines = dialog.Body.Text.Split('\n');
        Assert.All(lines, line => Assert.True(line.Length <= dialog.Body.Viewport.Width, line));
        Assert.Equal(lines.Length, dialog.Body.Frame.Height);
    }

    [Fact]
    public void Wrapping_breaks_at_spaces() =>
        Assert.Equal(["one two", "three"], RemoveTeamDialog.Wrap("one two three", 7));
}
