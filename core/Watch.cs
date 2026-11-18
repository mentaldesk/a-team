using System.Diagnostics;
using System.Globalization;

namespace ATeam.Core;

/// <summary>A program a command started gave no answer within its limit: the line that says so.</summary>
internal sealed class Stuck(string line) : Exception(line);

/// <summary>How long a board command waits on each program it starts, and on itself as a whole, before it stops and says
/// what it was waiting on. It stops only a child it started itself.</summary>
internal static class Watch
{
    internal const int StuckCode = 124;

    internal static readonly TimeSpan CallLimit = Limit("A_TEAM_CALL_LIMIT", 180);
    internal static readonly TimeSpan CommandLimit = Limit("A_TEAM_COMMAND_LIMIT", 600);

    private static readonly Lock Gate = new();
    private static string _team = "", _step = "", _role = "", _state = "";
    private static string _waiting = "its own work";
    private static Process? _child;
    private static Action? _leaving;
    private static Timer? _deadline;
    private static bool _stopped, _dry;

    private static TimeSpan Limit(string variable, int seconds) =>
        TimeSpan.FromSeconds(double.TryParse(Environment.GetEnvironmentVariable(variable), NumberStyles.Float, CultureInfo.InvariantCulture,
            out var set) && set > 0 ? set : seconds);

    /// <summary>Starts the command's own deadline. <paramref name="leaving"/> tidies up what a stop skips past.</summary>
    internal static void Begin(string team, string step, string role, string state, bool dry, Action leaving)
    {
        (_team, _step, _role, _state, _dry, _leaving) = (team, step, role, state, dry, leaving);
        _deadline = new Timer(_ => Overdue(), null, CommandLimit, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Names what the command waits on until the scope ends.</summary>
    internal static Waiting On(string what)
    {
        lock (Gate)
        {
            var was = _waiting;
            _waiting = what;
            return new Waiting(was);
        }
    }

    internal readonly struct Waiting(string was) : IDisposable
    {
        public void Dispose()
        {
            lock (Gate) _waiting = was;
        }
    }

    /// <summary>Waits for <paramref name="process"/> to exit and its output to be read, up to the call limit; past it, stops
    /// that child and throws <see cref="Stuck"/>.</summary>
    internal static void Await(Process process, string what, params Task[] reading)
    {
        string was;
        lock (Gate)
        {
            was = _waiting;
            _child = process;
            _waiting = what;
        }
        try
        {
            if (Task.WhenAll([process.WaitForExitAsync(), .. reading]).Wait(CallLimit))
                return;
            Kill(process);
            throw new Stuck(Line(what, $"gave no answer in {Span(CallLimit)}"));
        }
        catch (AggregateException failed) when (failed.InnerException is IOException or ObjectDisposedException)
        {
        }
        finally
        {
            bool stopped;
            lock (Gate)
            {
                _child = null;
                _waiting = was;
                stopped = _stopped;
            }
            // The deadline killed this call and is exiting: nothing may act on it meanwhile.
            if (stopped) Thread.Sleep(Timeout.Infinite);
        }
    }

    private static readonly HashSet<string> Valued = ["-C", "-F", "-f", "-H", "-L", "-q", "-R", "-X", "--author", "--input", "--jq", "--json", "--limit", "--state"];

    /// <summary>`gh api graphql BoardRefresh`, `gh pr list`, `git worktree`: the program and enough of its arguments to find the call.</summary>
    internal static string Label(string program, IReadOnlyList<string> args)
    {
        var words = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i].StartsWith('-'))
                i += Valued.Contains(args[i]) ? 1 : 0;
            else
                words.Add(args[i].Split('?')[0]);
        }
        var label = new List<string> { program };
        label.AddRange(words.Take(program switch { "git" => 1, "gh" => 2, _ => 3 }));
        if (label is [_, "api", "graphql"] && args.FirstOrDefault(arg => arg.StartsWith("query=", StringComparison.Ordinal)) is { } query
            && query["query=".Length..].Split([' ', '{', '(', '\n'], StringSplitOptions.RemoveEmptyEntries) is [("query" or "mutation"), var name, ..])
            label.Add(name);
        return string.Join(' ', label);
    }

    private static string Line(string what, string how) => $"board stuck: {what} (team {_team}, {_step}) {how}, stopping";

    internal static string Span(TimeSpan span) =>
        span.TotalSeconds >= 60 && span.TotalSeconds % 60 == 0
            ? Plural((int)span.TotalMinutes, "minute")
            : Plural((int)Math.Ceiling(span.TotalSeconds), "second");

    private static string Plural(int n, string unit) => $"{n} {unit}{(n == 1 ? "" : "s")}";

    /// <summary>Says the line on stderr, its last, and in the dispatch log unless this is a dry run.</summary>
    internal static void Report(string line)
    {
        Console.Out.Flush();
        Console.Error.WriteLine(line);
        if (_state.Length == 0 || _dry) return;
        try
        {
            var who = Environment.GetEnvironmentVariable("A_TEAM_RUN_ROLE") is { Length: > 0 } running ? running : _role;
            Directory.CreateDirectory(_state);
            File.AppendAllText(Path.Combine(_state, "dispatch.log"),
                $"{DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)} {_team}{(who.Length > 0 ? " " + who : "")}: {line}\n");
        }
        catch (IOException)
        {
            // The line on stderr still says it.
        }
    }

    private static void Overdue()
    {
        string line;
        lock (Gate)
        {
            if (_stopped) return;
            _stopped = true;
            if (_child is { } child) Kill(child);
            line = Line(_waiting, $"was still waiting after {Span(CommandLimit)}");
        }
        Report(line);
        _leaving?.Invoke();
        Environment.Exit(StuckCode);
    }

    internal static void End() => _deadline?.Dispose();

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // It ended on its own.
        }
    }
}
