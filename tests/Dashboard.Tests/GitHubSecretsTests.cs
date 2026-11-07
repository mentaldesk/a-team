using System.Runtime.Versioning;

namespace ATeam.Dashboard.Tests;

/// <summary>Runs a stand-in for gh, to see what the real one would be given.</summary>
[UnsupportedOSPlatform("windows")]
public sealed class GitHubSecretsTests : IDisposable
{
    public static bool HasAShell => !OperatingSystem.IsWindows();

    private const string Value = "distinctive-value-5f3a";
    private static readonly SecretAsk Repo = new("WINGET_TOKEN", "", "mentaldesk/TuiCode", "why", "lead");
    private static readonly SecretAsk Environment = Repo with { Environment = "release" };

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"a-team-{Guid.NewGuid():n}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string Gh(string then = "")
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "gh");
        File.WriteAllText(path, $$"""
            #!/bin/sh
            printf '%s\n' "$*" >"{{_root}}/args"
            [ "$1 $2" != "secret set" ] || cat >"{{_root}}/stdin"
            {{then}}
            """);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    private string Said(string file) => File.ReadAllText(Path.Combine(_root, file)).TrimEnd('\n');

    [Fact(Skip = "gh here is a shell script", SkipUnless = nameof(HasAShell))]
    public async Task A_repo_secret_goes_to_gh_on_stdin_and_never_in_its_arguments()
    {
        Assert.Null(await new GitHubSecrets(new TeamCommand(Gh())).Store(Repo, Value));

        Assert.Equal("secret set WINGET_TOKEN --repo mentaldesk/TuiCode", Said("args"));
        Assert.Equal(Value, Said("stdin"));
    }

    [Fact(Skip = "gh here is a shell script", SkipUnless = nameof(HasAShell))]
    public async Task An_environment_secret_names_the_environment()
    {
        Assert.Null(await new GitHubSecrets(new TeamCommand(Gh())).Store(Environment, Value));

        Assert.Equal("secret set WINGET_TOKEN --repo mentaldesk/TuiCode --env release", Said("args"));
    }

    [Fact(Skip = "gh here is a shell script", SkipUnless = nameof(HasAShell))]
    public async Task A_dry_run_stores_nothing()
    {
        Assert.Null(await new GitHubSecrets(new TeamCommand(Gh()), dryRun: true).Store(Repo, Value));

        Assert.False(File.Exists(Path.Combine(_root, "args")));
    }

    [Theory(Skip = "gh here is a shell script", SkipUnless = nameof(HasAShell))]
    [InlineData("failed to fetch public key: HTTP 403: Must have admin rights", "GitHub refused: you aren't an admin of mentaldesk/TuiCode")]
    [InlineData("failed to fetch public key: HTTP 404: Not Found", "GitHub can't find the release environment of mentaldesk/TuiCode, or you aren't an admin of it")]
    [InlineData("something else", "something else")]
    public async Task A_refusal_says_why_without_the_value(string said, string why)
    {
        var refused = await new GitHubSecrets(new TeamCommand(Gh($"echo '{said}' >&2; exit 1"))).Store(Environment, Value);

        Assert.Equal(why, refused);
    }

    [Theory(Skip = "gh here is a shell script", SkipUnless = nameof(HasAShell))]
    [InlineData("""[{"name":"WINGET_TOKEN"}]""", true)]
    [InlineData("""[{"name":"winget_token"}]""", true)]
    [InlineData("""[{"name":"OTHER"}]""", false)]
    [InlineData("""not json""", false)]
    public async Task It_reads_whether_it_s_already_set(string names, bool set)
    {
        Assert.Equal(set, await new GitHubSecrets(new TeamCommand(Gh($"echo '{names}'"))).IsSet(Environment));
        Assert.Equal("secret list --repo mentaldesk/TuiCode --env release --json name", Said("args"));
    }

    [Fact(Skip = "gh here is a shell script", SkipUnless = nameof(HasAShell))]
    public async Task A_read_that_fails_says_it_isn_t_set()
    {
        Assert.False(await new GitHubSecrets(new TeamCommand(Gh("exit 1"))).IsSet(Repo));
    }

    [Fact(Skip = "gh here is a shell script", SkipUnless = nameof(HasAShell))]
    public async Task It_runs_as_whatever_gh_is_logged_in_as_with_no_token_of_its_own()
    {
        var saved = System.Environment.GetEnvironmentVariable("GH_TOKEN");
        System.Environment.SetEnvironmentVariable("GH_TOKEN", null);
        try
        {
            await new GitHubSecrets(new TeamCommand(Gh("""printf '%s' "${GH_TOKEN:-none}" >"$(dirname "$0")/token" """))).Store(Repo, Value);

            Assert.Equal("none", Said("token"));
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("GH_TOKEN", saved);
        }
    }
}
