using System.Runtime.Versioning;

namespace ATeam.Dashboard.Tests;

/// <summary>a-team's commands are shell scripts, so these run the real thing on the platforms that have one.</summary>
[UnsupportedOSPlatform("windows")]
public class TeamCommandTests : IDisposable
{
    public static bool HasAShell => !OperatingSystem.IsWindows();

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"a-team-{Guid.NewGuid():n}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact(Skip = "a-team is a shell script", SkipUnless = nameof(HasAShell))]
    public async Task A_command_that_worked_reports_nothing()
    {
        var command = new TeamCommand(Script("echo paused $2"));

        Assert.Null(await command.Run("pause", "demo"));
    }

    [Fact(Skip = "a-team is a shell script", SkipUnless = nameof(HasAShell))]
    public async Task A_command_that_failed_reports_the_first_line_of_its_stderr()
    {
        var command = new TeamCommand(Script("""
            echo "a-team pause: no config for team '$2'" >&2
            echo "and a second line nobody needs" >&2
            exit 1
            """));

        Assert.Equal("a-team pause: no config for team 'demo'", await command.Run("pause", "demo"));
    }

    [Fact(Skip = "a-team is a shell script", SkipUnless = nameof(HasAShell))]
    public async Task A_command_that_failed_silently_still_reports_something()
    {
        var command = new TeamCommand(Script("exit 3"));

        Assert.Equal("pause demo exited 3", await command.Run("pause", "demo"));
    }

    [Fact(Skip = "a-team is a shell script", SkipUnless = nameof(HasAShell))]
    public async Task An_executable_that_isn_t_there_reports_why_instead_of_throwing()
    {
        var command = new TeamCommand(Path.Combine(_root, "nothing-here"));

        Assert.NotNull(await command.Run("pause", "demo"));
    }

    [Fact(Skip = "a-team is a shell script", SkipUnless = nameof(HasAShell))]
    public async Task Streaming_passes_on_every_line_from_either_stream_and_reports_the_last_error()
    {
        var command = new TeamCommand(Script("""
            echo "Cloning into 'main'..."
            echo "remote: nope" >&2
            echo "fatal: repository not found" >&2
            exit 128
            """));
        List<string> lines = [];

        var failure = await command.Stream(line => { lock (lines) lines.Add(line); }, "repo", "clone");

        Assert.Equal("fatal: repository not found", failure);
        Assert.Equal(["Cloning into 'main'...", "fatal: repository not found", "remote: nope"], lines.Order());
    }

    private string Script(string body)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "a-team");
        File.WriteAllText(path, $"#!/bin/sh\n{body}\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }
}
