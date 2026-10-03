using System.Diagnostics;
using System.Text.Json;

namespace ATeam.Dashboard;

/// <summary>The one task a Dev run was started for.</summary>
public sealed record RunTask(int Number, string Title);

/// <summary>A Dev run still going besides the one the pane shows.</summary>
public sealed record OtherRun(RunTask? Task, DateTimeOffset? Started);

/// <summary>What the dispatcher has recorded about one role: see scripts/dispatch.sh.</summary>
public sealed record AgentState(
    bool Running, DateTimeOffset? LastStart, IReadOnlyList<string> Reasons, string? LogPath, DateTimeOffset? Stopped = null,
    RunTask? Task = null)
{
    /// <summary>The role's other live runs, in the order they started.</summary>
    public IReadOnlyList<OtherRun> Others { get; init; } = [];

    public static AgentState Read(string dir)
    {
        var latest = Pid(dir);
        var running = latest is { } id && IsAlive(id);
        var lastStart = ReadTime(Path.Combine(dir, "last-start"));
        var reasons = (ReadText(Path.Combine(dir, "last-reasons")) ?? "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var log = Path.Combine(dir, "latest.jsonl");
        var logPath = File.Exists(log) ? new FileInfo(log).ResolveLinkTarget(true)?.FullName ?? log : null;
        return new AgentState(
            running, lastStart, reasons, logPath, ReadTime(Path.Combine(dir, "stopped")), ReadTask(Path.Combine(dir, "task")))
        {
            Others = LiveOthers(Path.Combine(dir, "runs"), latest),
        };
    }

    private static List<OtherRun> LiveOthers(string runs, int? shown)
    {
        if (!Directory.Exists(runs))
            return [];
        return [.. Directory.EnumerateDirectories(runs)
            .Where(run => Pid(run) is { } pid && pid != shown && IsAlive(pid))
            .Select(run => new OtherRun(ReadTask(Path.Combine(run, "task")), ReadTime(Path.Combine(run, "last-start"))))
            .OrderBy(run => run.Started)];
    }

    private static int? Pid(string dir) => int.TryParse(ReadText(Path.Combine(dir, "pid")), out var pid) ? pid : null;

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

    private static bool IsAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
}
