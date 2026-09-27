using System.ComponentModel;
using System.Diagnostics;

namespace ATeam.Dashboard;

/// <summary>The terminal's settings as the dashboard found them. An app gives back the settings it started on,
/// and after a handover those are .NET's raw mode, so they're put back by hand before the next one.</summary>
public sealed class TerminalMode
{
    private readonly string? _saved;

    private TerminalMode(string? saved) => _saved = saved;

    public static TerminalMode Save() => new(OperatingSystem.IsWindows() ? null : Stty("-g"));

    public void Restore()
    {
        if (_saved is not null)
            Stty(_saved);
    }

    private static string? Stty(string argument)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("stty", argument) { RedirectStandardOutput = true });
            if (process is null)
                return null;
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return process.ExitCode == 0 && output.Length > 0 ? output : null;
        }
        catch (Exception e) when (e is IOException or Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }
}
