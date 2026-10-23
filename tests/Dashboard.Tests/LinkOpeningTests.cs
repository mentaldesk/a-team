namespace ATeam.Dashboard.Tests;

public sealed class LinkOpeningTests
{
    [Fact]
    public void Nothing_in_the_dashboard_opens_a_link_except_through_the_opener()
    {
        var dashboard = Path.Combine(RepoRoot(), "dashboard");

        var direct = Directory.EnumerateFiles(dashboard, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(file => File.ReadAllText(file).Contains("Link.OpenUrl", StringComparison.Ordinal))
            .Select(Path.GetFileName);

        Assert.Empty(direct);
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "dashboard", "Dashboard.csproj")))
                return dir.FullName;
        throw new DirectoryNotFoundException("no dashboard/Dashboard.csproj above the tests");
    }
}
