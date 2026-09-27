using System.Diagnostics;

namespace ATeam.Dashboard;

/// <summary>What the dispatcher has recorded about one role: see scripts/dispatch.sh.</summary>
public sealed record AgentState(
    bool Running, DateTimeOffset? LastStart, IReadOnlyList<string> Reasons, string? LogPath, DateTimeOffset? Stopped = null)
{
    public static AgentState Read(string dir)
    {
        var running = ReadText(Path.Combine(dir, "pid")) is { } pid && int.TryParse(pid, out var id) && IsAlive(id);
        var lastStart = ReadTime(Path.Combine(dir, "last-start"));
        var reasons = (ReadText(Path.Combine(dir, "last-reasons")) ?? "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var latest = Path.Combine(dir, "latest.jsonl");
        var logPath = File.Exists(latest) ? new FileInfo(latest).ResolveLinkTarget(true)?.FullName ?? latest : null;
        return new AgentState(running, lastStart, reasons, logPath, ReadTime(Path.Combine(dir, "stopped")));
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
