using System.Text.Json;

namespace ATeam.Dashboard;

/// <summary>What is driving the teams, from the installer's dispatcher.json and the pass's next-pass: see
/// scripts/install.sh and scripts/dispatch.sh.</summary>
public sealed record DispatcherState(string? Binary, string State, bool Error)
{
    private const string Name = "dispatcher";
    private const string Separator = " · ";
    private const string LastPassFailed = "last pass failed";

    /// <summary>A dispatcher installed before dispatcher.json existed, still passing: titled as it always was.</summary>
    private static readonly DispatcherState Unrecorded = new(null, "", false);

    private static readonly DispatcherState NotInstalled = new(null, "nothing installed · run: a-team install", true);

    /// <summary>The binary gives way first, from its start, so the state at the end survives a narrow window.</summary>
    public string Title(int width = int.MaxValue)
    {
        if (State.Length == 0)
            return Name;
        var tail = Separator + State;
        if (Binary is null)
            return Name + tail;
        var room = width - Name.Length - Separator.Length - tail.Length;
        return room >= Binary.Length ? Name + Separator + Binary + tail
            : room >= 2 ? Name + Separator + "…" + Binary[^(room - 1)..] + tail
            : Name + tail;
    }

    public DispatcherState Failed() =>
        this with { State = State.Length == 0 ? LastPassFailed : State + Separator + LastPassFailed, Error = true };

    /// <summary>The tail of the dispatcher's own output when it was written after dispatch.log last was: a pass that
    /// broke before it could log why.</summary>
    public static IReadOnlyList<string> Broke(string stateRoot, int count)
    {
        var output = new FileInfo(Record(Path.Combine(stateRoot, "dispatcher.json"))?.Log is { Length: > 0 } log
            ? log
            : Path.Combine(stateRoot, "launchd.log"));
        var logged = new FileInfo(Path.Combine(stateRoot, "dispatch.log"));
        if (!output.Exists || (logged.Exists && output.LastWriteTimeUtc <= logged.LastWriteTimeUtc))
            return [];
        try
        {
            return [.. File.ReadLines(output.FullName).TakeLast(count)];
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    public static DispatcherState Read(string stateRoot, DateTimeOffset now, string home)
    {
        var record = Record(Path.Combine(stateRoot, "dispatcher.json"));
        if (record is null)
            return Time(Path.Combine(stateRoot, "next-pass")) is { } due && now - due <= Grace ? Unrecorded : NotInstalled;

        var interval = TimeSpan.FromSeconds(record.Interval);
        var passed = Time(Path.Combine(stateRoot, record.DryRun ? "dry-next-pass" : "next-pass")) - interval;
        var last = passed is { } pass && pass > record.InstalledAt ? pass : record.InstalledAt;
        if (now - last > interval + Grace)
            return new(null, $"stopped {AgentPane.Ago(now - last)} ago", true);

        var next = $"next pass {AgentPane.Clock(Max(last + interval - now, TimeSpan.Zero))}";
        return record.DryRun
            ? new(null, $"dry run: nothing will actually start · {next}", true)
            : new($"{Home(record.Bin, home)} {record.Version}", next, false);
    }

    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(1);

    private sealed record Installed(string Bin, string Version, bool DryRun, int Interval, DateTimeOffset InstalledAt, string Log);

    private static Installed? Record(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            return new Installed(
                root.GetProperty("bin").GetString() ?? "",
                root.TryGetProperty("version", out var version) ? version.GetString() ?? "" : "",
                root.TryGetProperty("dryRun", out var dryRun) && dryRun.GetBoolean(),
                root.TryGetProperty("interval", out var interval) ? interval.GetInt32() : 120,
                DateTimeOffset.FromUnixTimeSeconds(root.TryGetProperty("installedAt", out var at) ? at.GetInt64() : 0),
                root.TryGetProperty("log", out var log) ? log.GetString() ?? "" : "");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException
                                      or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static DateTimeOffset? Time(string path)
    {
        try
        {
            return long.TryParse(File.ReadAllText(path).Trim(), out var seconds) ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static string Home(string path, string home) =>
        home.Length > 0 && path.StartsWith(home + "/", StringComparison.Ordinal) ? "~" + path[home.Length..] : path;

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
