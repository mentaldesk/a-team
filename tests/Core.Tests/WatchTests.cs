using Xunit;

namespace ATeam.Core.Tests;

public class WatchTests
{
    [Theory]
    [InlineData("gh", new[] { "api", "graphql", "-F", "owner=o", "-f", "query=query BoardRefresh { nodes }" }, "gh api graphql BoardRefresh")]
    [InlineData("gh", new[] { "api", "-X", "POST", "repos/o/r/issues/8/comments", "-F", "body=@-" }, "gh api repos/o/r/issues/8/comments")]
    [InlineData("gh", new[] { "api", "--paginate", "repos/o/r/check-runs?per_page=100", "--jq", ".x" }, "gh api repos/o/r/check-runs")]
    [InlineData("gh", new[] { "pr", "list", "-R", "o/r", "--json", "number" }, "gh pr list")]
    [InlineData("git", new[] { "-C", "/tmp/x", "worktree", "list", "--porcelain" }, "git worktree")]
    [InlineData("a-team", new[] { "token", "demo" }, "a-team token demo")]
    public void A_call_is_named_by_its_program_and_what_finds_it(string program, string[] args, string label) =>
        Assert.Equal(label, Watch.Label(program, args));

    [Theory]
    [InlineData(180, "3 minutes")]
    [InlineData(600, "10 minutes")]
    [InlineData(60, "1 minute")]
    [InlineData(1, "1 second")]
    [InlineData(90, "90 seconds")]
    public void A_limit_reads_in_whole_units(int seconds, string said) =>
        Assert.Equal(said, Watch.Span(TimeSpan.FromSeconds(seconds)));
}
