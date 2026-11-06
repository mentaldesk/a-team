using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace ATeam.Dashboard;

/// <summary>The one task a Dev run was started for.</summary>
public sealed record RunTask(int Number, string Title);

/// <summary>A Dev run, and the directory under runs/ its state is kept in.</summary>
public sealed record DevRun(string Dir, RunTask? Task, DateTimeOffset? Started)
{
    /// <summary>When it was stopped on its own, by task; no run starts on that task until it's let go.</summary>
    public DateTimeOffset? Held { get; init; }
}

/// <summary>A live run the dispatcher has found stuck, and what it says about it.</summary>
public sealed record StuckRun(int Pid, string Says);

/// <summary>What the dispatcher has recorded about one role: see scripts/dispatch.sh.</summary>
public sealed record AgentState(
    bool Running, DateTimeOffset? LastStart, IReadOnlyList<string> Reasons, string? LogPath, DateTimeOffset? Stopped = null,
    RunTask? Task = null)
{
    /// <summary>The role's runs under runs/ that are live or stopped by task, in the order they started.</summary>
    public IReadOnlyList<DevRun> Runs { get; init; } = [];

    /// <summary>The run under runs/ the role's own files follow, live or not.</summary>
    public DevRun? Latest { get; init; }

    /// <summary>The one card a Lead or Customer lead run was started for, or has taken up since.</summary>
    public int? Card { get; init; }

    /// <summary>The role's runs the dispatcher found stuck and that are still going, oldest first.</summary>
    public IReadOnlyList<StuckRun> Stuck { get; init; } = [];

    public static AgentState Read(string dir)
    {
        var running = IsAlive(Path.Combine(dir, "pid"));
        var task = ReadTask(Path.Combine(dir, "task"));
        var lastStart = ReadTime(Path.Combine(dir, "last-start"));
        var reasons = (ReadText(Path.Combine(dir, "last-reasons")) ?? "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var log = Path.Combine(dir, "latest.jsonl");
        var logPath = File.Exists(log) ? new FileInfo(log).ResolveLinkTarget(true)?.FullName ?? log : null;
        return new AgentState(
            running, lastStart, reasons, logPath, ReadTime(Path.Combine(dir, "stopped")), task)
        {
            Runs = LiveRuns(Path.Combine(dir, "runs")),
            Latest = task is null ? null : RunOf(Path.Combine(dir, "runs", task.Number.ToString(CultureInfo.InvariantCulture))),
            Card = ReadTask(Path.Combine(dir, "card"))?.Number,
            Stuck = ReadStuck(dir),
        };
    }

    /// <summary>A run counts only while its pid file still holds its pid and that process is the one it was written for.</summary>
    private static List<StuckRun> ReadStuck(string dir)
    {
        if (ReadText(Path.Combine(dir, "stuck")) is not { Length: > 0 } text)
            return [];
        try
        {
            using var document = JsonDocument.Parse(text);
            List<StuckRun> stuck = [];
            foreach (var run in document.RootElement.EnumerateArray())
            {
                var pid = run.GetProperty("pid").GetInt32();
                var file = Path.Combine(dir, run.GetProperty("file").GetString() ?? "");
                if (ReadText(file) == pid.ToString(CultureInfo.InvariantCulture) && IsAlive(file))
                    stuck.Add(new StuckRun(pid, run.GetProperty("says").GetString() ?? ""));
            }
            return stuck;
        }
        catch (JsonException) { return []; }
        catch (InvalidOperationException) { return []; }
        catch (KeyNotFoundException) { return []; }
        catch (FormatException) { return []; }
    }

    private static List<DevRun> LiveRuns(string runs)
    {
        if (!Directory.Exists(runs))
            return [];
        return [.. Directory.EnumerateDirectories(runs)
            .Select(DevRun)
            .Where(run => run.Held is not null || IsAlive(Path.Combine(run.Dir, "pid")))
            .OrderBy(run => run.Started)
            .ThenBy(run => run.Dir, StringComparer.Ordinal)];
    }

    private static DevRun? RunOf(string run) => Directory.Exists(run) ? DevRun(run) : null;

    private static DevRun DevRun(string run) =>
        new(run, ReadTask(Path.Combine(run, "task")), ReadTime(Path.Combine(run, "last-start")))
        {
            Held = ReadTime(Path.Combine(run, "held")),
        };

    internal static RunTask? ReadTask(string path)
    {
        if (ReadText(path) is not { Length: > 0 } text)
            return null;
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            return root.TryGetProperty("number", out var number) && number.TryGetInt32(out var n)
                ? new RunTask(n, root.TryGetProperty("title", out var title) ? title.GetString() ?? "" : "")
                : null;
        }
        catch (JsonException) { return null; }
    }

    private static DateTimeOffset? ReadTime(string path) =>
        long.TryParse(ReadText(path), out var seconds) ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;

    private static string? ReadText(string path)
    {
        try { return File.ReadAllText(path).Trim(); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>Whether the pid in <paramref name="file"/> is running and started before the file was last written,
    /// give or take a minute. A process that started later has only been given the same pid since.</summary>
    internal static bool IsAlive(string file)
    {
        if (!int.TryParse(ReadText(file), out var pid))
            return false;
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited
                && process.StartTime.ToUniversalTime() <= File.GetLastWriteTimeUtc(file) + TimeSpan.FromMinutes(1);
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (Win32Exception) { return false; }
        catch (NotSupportedException) { return false; }
    }
}
