using Terminal.Gui.Input;

namespace ATeam.Dashboard.Tests;

public class AcceptDialogTests
{
    private static readonly WaitingItem Task =
        new(49, "I can't change any of the keys", "In review", "https://github.com/x/49", "a-team", Pr: 122, Base: "main");

    [Fact]
    public void It_names_the_task_and_says_what_merging_does()
    {
        using var dialog = new AcceptDialog(Task);

        Assert.Equal("Accept #49?", dialog.Title);
        Assert.Equal(
            "Merges PR #122 into main, squashed, and closes #49.\n\nMerged work reaches the teams when you next release.",
            dialog.Body.Text);
        Assert.Equal(["Enter accept", "Esc cancel"], dialog.Hints.Select(hint => hint.Text));
    }

    [Fact]
    public void Enter_accepts()
    {
        using var dialog = new AcceptDialog(Task);

        dialog.InvokeCommand(Command.Accept);

        Assert.True(dialog.Confirmed);
    }

    [Fact]
    public void Esc_cancels()
    {
        using var dialog = new AcceptDialog(Task);

        Assert.True(dialog.NewKeyDownEvent(Key.Esc));

        Assert.False(dialog.Confirmed);
    }
}
