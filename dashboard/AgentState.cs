using System.Diagnostics;

namespace ATeam.Dashboard;

/// <summary>What the dispatcher has recorded about one role: see scripts/dispatch.sh.</summary>
public sealed record AgentState(bool Running, DateTimeOffset? LastStart, IReadOnlyList<string> Reasons, string? LogPath)
{
    public static AgentState Read(string dir)
    {
        var running = ReadText(Path.Combine(dir, "pid")) is { } pid && int.TryParse(pid, out var id) && IsAlive(id);
        DateTimeOffset? lastStart = long.TryParse(ReadText(Path.Combine(dir, "last-start")), out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
        var reasons = (ReadText(Path.Combine(dir, "last-reasons")) ?? "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var latest = Path.Combine(dir, "latest.jsonl");
        var logPath = File.Exists(latest) ? new FileInfo(latest).ResolveLinkTarget(true)?.FullName ?? latest : null;
        return new AgentState(running, lastStart, reasons, logPath);
    }

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
