using Terminal.Gui.Drawing;

namespace ATeam.Dashboard;

/// <summary>One dispatcher pass run now rather than at the next scheduled one, by the installed dispatcher, so it
/// starts exactly what that pass would have.</summary>
public sealed class DispatchPass(string stateRoot, string fallback, Func<string, string[], Task<Reading>>? run = null)
{
    internal const string Running = "Running a pass…";
    internal const string AlreadyRunning = "A pass is already running.";
    internal const string NothingToStart = "Pass done: nothing to start.";

    private static readonly Dictionary<string, string> Unscheduled = new() { ["A_TEAM_UNSCHEDULED"] = "1" };

    private readonly Func<string, string[], Task<Reading>> _run =
        run ?? ((binary, arguments) => new TeamCommand(binary, Unscheduled).Read(arguments));

    private readonly string _log = Path.Combine(stateRoot, "dispatch.log");
    private Task<Reading>? _pass;
    private long _logFrom;
    private bool _again;

    /// <summary>What the bar says while a pass runs, or null while none is.</summary>
    public string? Progress => _pass is null ? null : _again ? AlreadyRunning : Running;

    public void Start()
    {
        if (_pass is not null)
        {
            _again = true;
            return;
        }
        var (binary, arguments) = Command();
        _logFrom = Length(_log);
        _again = false;
        _pass = _run(binary, arguments);
    }

    /// <summary>What the pass did, once, as soon as it has finished; null until then.</summary>
    public (string Text, Schemes Scheme)? Finished()
    {
        if (_pass is not { IsCompleted: true } pass)
            return null;
        _pass = null;
        var failure = pass.Status == TaskStatus.RanToCompletion
            ? pass.Result.Failure
            : pass.Exception?.GetBaseException().Message ?? "the pass didn't finish";
        if (failure is { Length: > 0 })
            return (failure, Schemes.Error);
        var started = Started();
        return started == 0 ? (NothingToStart, Schemes.Accent) : ($"Pass done: started {started} run{(started == 1 ? "" : "s")}.", Schemes.Accent);
    }

    internal (string Binary, string[] Arguments) Command() =>
        DispatcherState.Record(stateRoot) is { Bin.Length: > 0 } installed
            ? (installed.Bin, installed.DryRun ? ["dispatch", "--dry-run"] : ["dispatch"])
            : (fallback, ["dispatch"]);

    private int Started()
    {
        try
        {
            using var stream = new FileStream(_log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            stream.Seek(Math.Min(_logFrom, stream.Length), SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            var started = 0;
            while (reader.ReadLine() is { } line)
                if (DispatchLine.Starts(line))
                    started++;
            return started;
        }
        catch (IOException) { return 0; }
        catch (UnauthorizedAccessException) { return 0; }
    }

    private static long Length(string path)
    {
        try
        {
            return new FileInfo(path) is { Exists: true } file ? file.Length : 0;
        }
        catch (IOException) { return 0; }
        catch (UnauthorizedAccessException) { return 0; }
    }
}
