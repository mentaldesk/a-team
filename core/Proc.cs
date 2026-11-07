using System.Diagnostics;

namespace ATeam.Core;

/// <summary>Other programs a command needs an answer from, or leaves the terminal to.</summary>
internal static class Proc
{
    private static ProcessStartInfo Start(string file, IEnumerable<string> args)
    {
        var start = new ProcessStartInfo(file) { UseShellExecute = false };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        return start;
    }

    /// <summary>Its exit code, and stdout and stderr as one.</summary>
    internal static (int Code, string Said) Capture(string file, params string[] args)
    {
        var start = Start(file, args);
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        try
        {
            using var process = Process.Start(start)!;
            var err = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, (output + err.Result).TrimEnd('\n'));
        }
        catch (System.ComponentModel.Win32Exception problem)
        {
            return (127, problem.Message);
        }
    }

    internal static string Say(string file, params string[] args) => Capture(file, args).Said;

    /// <summary>Runs it on this terminal, and gives back its exit code.</summary>
    internal static int Run(string file, params string[] args)
    {
        using var process = Process.Start(Start(file, args))!;
        process.WaitForExit();
        return process.ExitCode;
    }
}
