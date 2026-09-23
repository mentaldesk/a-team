using System.ComponentModel;
using System.Diagnostics;

namespace ATeam.Dashboard;

/// <summary>What a command printed, and the first line of its stderr if it failed.</summary>
public sealed record Reading(string Output, string? Failure);

/// <summary>Runs one of a-team's own commands for the dashboard, and reports what went wrong in a line.</summary>
public sealed class TeamCommand(string executable)
{
    /// <summary>Null once it has run; otherwise the first line of its stderr.</summary>
    public Task<string?> Run(string verb, string team) => Task.Run(() => Invoke(verb, team).Failure);

    /// <summary>What a read-only command printed, for the caller to parse.</summary>
    public Task<Reading> Read(params string[] arguments) => Task.Run(() => Invoke(arguments));

    private Reading Invoke(params string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        var said = string.Join(' ', arguments);
        try
        {
            using var process = Process.Start(start);
            if (process is null)
                return new Reading("", $"couldn't run {Path.GetFileName(executable)} {said}");
            var error = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEndAsync();
            process.WaitForExit();
            if (process.ExitCode == 0)
                return new Reading(output.GetAwaiter().GetResult(), null);
            return new Reading(
                "",
                FirstLine(error.GetAwaiter().GetResult()) ?? $"{said} exited {process.ExitCode}");
        }
        catch (Exception e) when (e is IOException or Win32Exception or InvalidOperationException)
        {
            return new Reading("", e.Message);
        }
    }

    private static string? FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
}
