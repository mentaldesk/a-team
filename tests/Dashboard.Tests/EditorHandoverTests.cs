using System.Runtime.Versioning;

namespace ATeam.Dashboard.Tests;

public class EditorHandoverTests : IDisposable
{
    public static bool HasAShell => !OperatingSystem.IsWindows();

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"a-team-{Guid.NewGuid():n}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData("code --wait", "vim", new[] { "code", "--wait" })]
    [InlineData(null, "vim", new[] { "vim" })]
    [InlineData("", "nano -w", new[] { "nano", "-w" })]
    [InlineData(null, null, new[] { "less" })]
    public void VISUAL_wins_then_EDITOR_then_less(string? visual, string? editor, string[] expected)
    {
        var environment = new Dictionary<string, string?> { ["VISUAL"] = visual, ["EDITOR"] = editor };

        Assert.Equal(expected, EditorHandover.Command(name => environment[name]));
    }

    [UnsupportedOSPlatform("windows")]
    [Fact(Skip = "the editor here is a shell script", SkipUnless = nameof(HasAShell))]
    public void The_editor_gets_its_own_arguments_then_the_file_and_the_file_goes_once_it_quits()
    {
        var file = Log();
        var said = Path.Combine(_root, "said");
        var handover = Handover(file, Script($"echo \"$@\" > {said}"), "--wait");

        Assert.Null(handover.Open());

        Assert.Equal($"--wait {file}", File.ReadAllText(said).Trim());
        Assert.False(File.Exists(file));
    }

    [UnsupportedOSPlatform("windows")]
    [Fact(Skip = "the editor here is a shell script", SkipUnless = nameof(HasAShell))]
    public void An_editor_that_exits_with_an_error_says_which_and_how()
    {
        var file = Log();
        Console.SetIn(new StringReader("\n"));

        Assert.Equal("editor exited 2", Handover(file, Script("exit 2")).Open());
        Assert.False(File.Exists(file));
    }

    [UnsupportedOSPlatform("windows")]
    [Fact(Skip = "the editor here is a shell script", SkipUnless = nameof(HasAShell))]
    public void An_editor_that_isn_t_there_says_it_couldn_t_start()
    {
        var file = Log();
        Console.SetIn(new StringReader("\n"));

        Assert.Equal("couldn't start nothing-here", Handover(file, Path.Combine(_root, "nothing-here")).Open());
        Assert.False(File.Exists(file));
    }

    private static EditorHandover Handover(string file, params string[] editor) =>
        new("a-team", "dev", new PanePlace(null, new LogPlace([], 0, true, 0, 0, false)), file, editor);

    private string Log()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "session.log");
        File.WriteAllText(path, "one\n");
        return path;
    }

    [UnsupportedOSPlatform("windows")]
    private string Script(string body)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "editor");
        File.WriteAllText(path, $"#!/bin/sh\n{body}\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }
}
