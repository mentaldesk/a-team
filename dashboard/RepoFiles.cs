namespace ATeam.Dashboard;

/// <summary>The Markdown files on a repo's default branch, or why they couldn't be read.</summary>
public sealed record RepoFiles(string Repo, string Branch, IReadOnlyList<string> Paths, string? Failure = null)
{
    public static RepoFiles Unread(string repo, string failure) => new(repo, "", [], failure);

    /// <summary>The Markdown paths among the lines <c>gh api</c> printed for a tree, in order.</summary>
    public static RepoFiles Parse(string repo, string branch, string tree) =>
        new(repo, branch, [.. tree.Split('\n')
            .Select(line => line.Trim())
            .Where(path => path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)]);

    /// <summary>Asks GitHub for <paramref name="repo"/>'s default branch, then every Markdown file on it.</summary>
    public static async Task<RepoFiles> Read(string repo)
    {
        if (repo.Trim().Length == 0)
            return Unread(repo, "No Repo yet, so there are no files to pick from.");
        var gh = new TeamCommand("gh");
        var branch = await gh.Read("api", $"repos/{repo}", "--jq", ".default_branch");
        if (branch.Failure is { } failure)
            return Unread(repo, $"Couldn't read {repo}: {Plain(failure)}");
        var name = branch.Output.Trim();
        var tree = await gh.Read("api", $"repos/{repo}/git/trees/{name}?recursive=1", "--jq", ".tree[] | select(.type == \"blob\") | .path");
        return tree.Failure is { } treeFailure
            ? Unread(repo, $"Couldn't read {repo}'s files: {Plain(treeFailure)}")
            : Parse(repo, name, tree.Output);
    }

    private static string Plain(string failure) => failure.StartsWith("gh: ", StringComparison.Ordinal) ? failure[4..] : failure;

    public bool Has(string path) => Paths.Contains(path.Trim());

    /// <summary>The line above the list: where the files are from, or why there are none.</summary>
    public string Where => Failure ?? $"{Paths.Count} Markdown files in {Repo} on {Branch}";
}
