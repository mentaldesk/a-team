namespace ATeam.Dashboard;

/// <summary>The repos a team could contribute to: the ones its GitHub App's installation can reach, its own
/// aside, or why they couldn't be read.</summary>
public sealed record ReachableRepos(IReadOnlyList<string> Names, string? Failure = null)
{
    public static ReachableRepos Parse(Reading repos, string own) =>
        repos.Failure is { } failure
            ? new([], failure)
            : new([.. repos.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(repo => !repo.Equals(own, StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .Order(StringComparer.OrdinalIgnoreCase)]);

    /// <summary>Asks GitHub, as <paramref name="team"/>'s App, for every repo its installation covers.</summary>
    public static async Task<ReachableRepos> Read(TeamCommand aTeam, string team, string own)
    {
        var token = await aTeam.Read("token", team);
        if (token.Failure is { } failure)
            return new([], Plain(failure));
        var gh = new TeamCommand("gh", new Dictionary<string, string> { ["GH_TOKEN"] = token.Output.Trim() });
        return Parse(await gh.Read("api", "--paginate", "installation/repositories?per_page=100", "--jq", ".repositories[].full_name"), own);
    }

    /// <summary>What the picker says once the list has come back, or not.</summary>
    public string Message => Failure is { } failure
        ? $"Couldn't read the repos the team's App can reach: {Plain(failure)}"
        : $"{Names.Count} other repos the team's App can reach.";

    private static string Plain(string failure) =>
        failure.StartsWith("a-team token: ", StringComparison.Ordinal) ? failure["a-team token: ".Length..]
        : failure.StartsWith("gh: ", StringComparison.Ordinal) ? failure[4..]
        : failure;
}
