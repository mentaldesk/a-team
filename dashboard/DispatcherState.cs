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
        var output = new FileInfo(Record(stateRoot)?.Log is { Length: > 0 } log
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
        var record = Record(stateRoot);
        var pass = NextPass(stateRoot, record);
        if (record is null)
            return pass is { } unrecorded && (now - unrecorded.Due <= Grace || unrecorded.Running) ? Unrecorded : NotInstalled;

        var (due, running) = pass!;
        if (now - due > Grace && !running)
            return new(null, $"stopped {Ages.Ago(now - (due - TimeSpan.FromSeconds(record.Interval)))} ago", true);

        var next = $"next pass {Ages.Clock(Max(due - now, TimeSpan.Zero))}";
        return record.DryRun
            ? new(null, $"dry run: nothing will actually start · {next}", true)
            : new($"{Home(record.Bin, home)} {record.Version}", next, false);
    }

    internal sealed record Pass(DateTimeOffset Due, bool Running);

    /// <summary>When the next pass is due and whether one is going, or null when the dispatcher has never passed.</summary>
    internal static Pass? NextPass(string stateRoot) => NextPass(stateRoot, Record(stateRoot));

    private static Pass? NextPass(string stateRoot, Installed? record)
    {
        if (record is null)
            return Time(Path.Combine(stateRoot, "next-pass")) is { } due ? new(due, Passing(stateRoot)) : null;

        var interval = TimeSpan.FromSeconds(record.Interval);
        var passed = Time(Path.Combine(stateRoot, record.DryRun ? "dry-next-pass" : "next-pass")) - interval;
        var last = passed is { } pass && pass > record.InstalledAt ? pass : record.InstalledAt;
        return new(last + interval, Passing(stateRoot, record.DryRun));
    }

    /// <summary>When the dispatcher next checks every team, for Overseer's status bar.</summary>
    internal static string NextCheck(Pass? pass, DateTimeOffset now) => pass switch
    {
        null => "dispatcher hasn't run",
        var (due, _) when due - now >= TimeSpan.Zero => $"next check {Ages.Clock(due - now)}",
        var (due, running) when running || now - due < Grace => "checking now",
        _ => "dispatcher not running",
    };

    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(1);

    /// <summary>A pass is still going, however overdue its next-pass: see scripts/dispatch.sh.</summary>
    internal static bool Passing(string stateRoot, bool dryRun = false) =>
        AgentState.IsAlive(Path.Combine(stateRoot, dryRun ? "dry-pass" : "pass"));

    internal sealed record Installed(string Bin, string Version, bool DryRun, int Interval, DateTimeOffset InstalledAt, string Log);

    /// <summary>What the installer recorded in dispatcher.json, or null when there's no record it can read.</summary>
    internal static Installed? Record(string stateRoot)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(stateRoot, "dispatcher.json")));
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
