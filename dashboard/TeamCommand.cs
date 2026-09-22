using System.ComponentModel;
using System.Diagnostics;

namespace ATeam.Dashboard;

/// <summary>Runs one of a-team's own commands for the dashboard, and reports what went wrong in a line.</summary>
public sealed class TeamCommand(string executable)
{
    /// <summary>Null once it has run; otherwise the first line of its stderr.</summary>
    public Task<string?> Run(string verb, string team) => Task.Run(() => Invoke(verb, team));

    private string? Invoke(string verb, string team)
    {
        var start = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(verb);
        start.ArgumentList.Add(team);
        try
        {
            using var process = Process.Start(start);
            if (process is null)
                return $"couldn't run {Path.GetFileName(executable)} {verb}";
            var error = process.StandardError.ReadToEndAsync();
            _ = process.StandardOutput.ReadToEndAsync();
            process.WaitForExit();
            if (process.ExitCode == 0)
                return null;
            return FirstLine(error.GetAwaiter().GetResult()) ?? $"{verb} {team} exited {process.ExitCode}";
        }
        catch (Exception e) when (e is IOException or Win32Exception or InvalidOperationException)
        {
            return e.Message;
        }
    }

    private static string? FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
}
