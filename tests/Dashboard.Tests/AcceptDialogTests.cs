namespace ATeam.Dashboard.Tests;

public class AcceptDialogTests
{
    private static readonly WaitingItem Task =
        new(49, "I can't change any of the keys", "In review", "https://github.com/x/49", "a-team", Pr: 122, Base: "main");

    [Fact]
    public void It_names_the_task_and_says_what_merging_does()
    {
        using var dialog = AcceptDialog.Create(Task);

        Assert.Equal("Accept #49?", dialog.Title);
        Assert.Equal(
            ["Merges PR #122 into main, squashed, and closes #49.", "", "Merged work reaches the teams when you next release."],
            dialog.Lines);
    }

    [Theory]
    [InlineData(3, "Closes #174 as done. Its 3 tasks are already merged.")]
    [InlineData(1, "Closes #174 as done. Its 1 task is already merged.")]
    public void It_names_a_pitch_and_counts_its_tasks(int tasks, string says)
    {
        using var dialog = AcceptDialog.Create(
            new(174, "Accepting finished work", "In review", "https://github.com/x/174", "a-team", Pitch: true, Tasks: tasks));

        Assert.Equal("Accept #174?", dialog.Title);
        Assert.Equal([says], dialog.Lines);
    }

    [Fact]
    public void It_says_accepting_the_docs_PR_merges_it_and_the_next_pitch_starts_another() =>
        Assert.Equal(
            "Merges the Customer lead's docs PR #352 into main, squashed. The next pitch that's done starts a new one.",
            AcceptDialog.Says(new(352, "Docs: what's changed since 28 Sep", "In review",
                "https://github.com/x/pull/352", "a-team", Pr: 352, Base: "main", Role: "customer")));

    [Fact]
    public void It_says_accepting_a_Dependabot_PR_merges_it_and_closes_nothing() =>
        Assert.Equal(
            "Merges Dependabot's PR #535 into main, squashed.\n\nMerged work reaches the teams when you next release.",
            AcceptDialog.Says(new(535, "Bump MentalDesk.Tui from 0.3.8 to 0.3.9", "In review",
                "https://github.com/x/pull/535", "a-team", Pr: 535, Base: "main", Dependency: true)));

    [Fact]
    public void It_says_accepting_a_PR_that_is_the_item_merges_it_and_closes_nothing() =>
        Assert.Equal(
            "Merges PR #86 into main, squashed.\n\nMerged work reaches the teams when you next release.",
            AcceptDialog.Says(new(86, "Let an app without AppMenu pick menu letters", "In review",
                "https://github.com/x/pull/86", "tui", Pr: 86, Base: "main")));
}
