namespace ATeam.Dashboard.Tests;

public class CrashReportTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 9, 24, 10, 41, 8, TimeSpan.Zero);

    private readonly string _stateRoot = Path.Combine(Path.GetTempPath(), $"a-team-{Guid.NewGuid():n}");
    private readonly StringWriter _error = new();

    public void Dispose()
    {
        if (Directory.Exists(_stateRoot))
            Directory.Delete(_stateRoot, recursive: true);
        else if (File.Exists(_stateRoot))
            File.Delete(_stateRoot);
    }

    [Fact]
    public void The_summary_is_the_exception_type_and_its_message()
    {
        Assert.Equal(
            "ArgumentException: The popover must have a superview.",
            CrashReport.Summary(new ArgumentException("The popover must have a superview.")));
    }

    [Fact]
    public void A_crash_says_what_happened_and_where_the_rest_of_it_is()
    {
        var code = CrashReport.Report(Thrown(), _stateRoot, [], _error, At, "0.0.9");

        var path = Path.Combine(_stateRoot, "crash-2026-09-24T10-41-08Z.log");
        Assert.Equal(1, code);
        Assert.True(File.Exists(path));
        Assert.Equal(
            ["a-team-dashboard: ArgumentException: the popover must have a superview", $"Full details: {path}"],
            Lines());
    }

    [Fact]
    public void Two_crashes_in_a_row_leave_two_files()
    {
        CrashReport.Report(Thrown(), _stateRoot, [], _error, At, "0.0.9");
        CrashReport.Report(Thrown(), _stateRoot, [], _error, At.AddSeconds(3), "0.0.9");

        Assert.Equal(
            ["crash-2026-09-24T10-41-08Z.log", "crash-2026-09-24T10-41-11Z.log"],
            Directory.GetFiles(_stateRoot).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void The_file_holds_the_trace_and_every_inner_exception()
    {
        CrashReport.Report(Thrown(new InvalidOperationException("no driver")), _stateRoot, [], _error, At, "0.0.9");

        var written = Written();
        Assert.Contains("System.ArgumentException: the popover must have a superview", written);
        Assert.Contains("System.InvalidOperationException: no driver", written);
        Assert.Contains(nameof(Thrown), written);
    }

    [Fact]
    public void The_file_holds_the_version_the_arguments_and_when_it_happened()
    {
        CrashReport.Report(Thrown(), _stateRoot, ["--area", "work"], _error, At, "0.0.9");

        var written = Written();
        Assert.Contains("a-team-dashboard 0.0.9", written);
        Assert.Contains("arguments: --area work", written);
        Assert.Contains("crashed 2026-09-24 10:41:08Z", written);
    }

    [Fact]
    public void A_state_directory_it_cant_write_to_puts_the_whole_trace_on_stderr_and_still_exits_1()
    {
        File.WriteAllText(_stateRoot, "not a directory");

        var code = CrashReport.Report(Thrown(), _stateRoot, [], _error, At, "0.0.9");

        Assert.Equal(1, code);
        Assert.Equal("a-team-dashboard: ArgumentException: the popover must have a superview", Lines()[0]);
        Assert.Contains("System.ArgumentException: the popover must have a superview", _error.ToString());
        Assert.Contains(nameof(Thrown), _error.ToString());
        Assert.DoesNotContain("Full details:", _error.ToString());
    }

    [Fact]
    public void A_clean_run_keeps_its_exit_code_and_leaves_nothing_behind()
    {
        var code = CrashReport.Guard(() => 0, _stateRoot, [], _error);

        Assert.Equal(0, code);
        Assert.Equal("", _error.ToString());
        Assert.False(Directory.Exists(_stateRoot));
    }

    [Fact]
    public void A_run_that_throws_is_reported_rather_than_left_to_the_runtime()
    {
        var code = CrashReport.Guard(() => throw Thrown(), _stateRoot, [], _error);

        Assert.Equal(1, code);
        Assert.StartsWith("a-team-dashboard: ArgumentException:", _error.ToString());
        Assert.Single(Directory.GetFiles(_stateRoot));
    }

    [Fact]
    public void A_path_in_the_home_directory_is_said_the_short_way()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.Equal(
            "~/.local/state/a-team/crash.log".Replace('/', Path.DirectorySeparatorChar),
            CrashReport.Shortened(Path.Combine(home, ".local", "state", "a-team", "crash.log")));
        Assert.Equal(
            Path.Combine("elsewhere", "crash.log"),
            CrashReport.Shortened(Path.Combine("elsewhere", "crash.log")));
    }

    private static ArgumentException Thrown(Exception? inner = null)
    {
        try
        {
            throw new ArgumentException("the popover must have a superview", inner);
        }
        catch (ArgumentException crash)
        {
            return crash;
        }
    }

    private string Written() => File.ReadAllText(Directory.GetFiles(_stateRoot).Single());

    private string[] Lines() => _error.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
}
