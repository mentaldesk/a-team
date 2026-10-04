using System.ComponentModel;
using System.Diagnostics;

namespace ATeam.Dashboard;

/// <summary>What a command printed, and the first line of its stderr if it failed.</summary>
public sealed record Reading(string Output, string? Failure);

/// <summary>Runs one of a-team's own commands for the dashboard, and reports what went wrong in a line.</summary>
public sealed class TeamCommand(string executable, IReadOnlyDictionary<string, string>? environment = null)
{
    /// <summary>Null once it has run; otherwise the first line of its stderr.</summary>
    public Task<string?> Run(params string[] arguments) => Task.Run(() => Invoke(arguments).Failure);

    /// <summary>What a read-only command printed, for the caller to parse.</summary>
    public Task<Reading> Read(params string[] arguments) => Task.Run(() => Invoke(arguments));

    /// <summary>What a command printed whether or not it failed, for one that reports problems and exits non-zero.</summary>
    public Task<Reading> Report(params string[] arguments) => Task.Run(() => Invoke(arguments, keepOutput: true));

    /// <summary>Runs a command, passing on each line it prints to either stream as it prints it. Null once it has
    /// run; otherwise its last line of stderr.</summary>
    public Task<string?> Stream(Action<string> line, params string[] arguments) => Task.Run(async () =>
    {
        var start = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        var said = string.Join(' ', arguments);
        string? lastError = null;
        try
        {
            using var process = Process.Start(start);
            if (process is null)
                return $"couldn't run {Path.GetFileName(executable)} {said}";
            async Task Pass(StreamReader reader, bool error)
            {
                while (await reader.ReadLineAsync() is { } text)
                {
                    if (text.Trim().Length == 0)
                        continue;
                    if (error)
                        lastError = text.Trim();
                    line(text);
                }
            }
            await Task.WhenAll(Pass(process.StandardOutput, false), Pass(process.StandardError, true));
            await process.WaitForExitAsync();
            return process.ExitCode == 0 ? null : lastError ?? $"{said} exited {process.ExitCode}";
        }
        catch (Exception e) when (e is IOException or Win32Exception or InvalidOperationException)
        {
            return e.Message;
        }
    });

    /// <summary>Runs a command that owns the terminal until it quits, Ctrl+C included. Null once it has run;
    /// otherwise how it failed, once the user has read what it printed.</summary>
    public string? Hand(string[] arguments, string? said = null)
    {
        said ??= string.Join(' ', arguments);
        var start = new ProcessStartInfo(executable);
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        ConsoleCancelEventHandler stay = (_, e) => e.Cancel = true;
        Console.CancelKeyPress += stay;
        string? failure;
        try
        {
            using var process = Process.Start(start);
            process?.WaitForExit();
            failure = process is null ? $"couldn't run {said}"
                : process.ExitCode == 0 ? null
                : $"{said} exited {process.ExitCode}";
        }
        catch (Win32Exception)
        {
            failure = $"couldn't start {Path.GetFileName(executable)}";
        }
        catch (Exception e) when (e is IOException or InvalidOperationException)
        {
            failure = e.Message;
        }
        finally
        {
            Console.CancelKeyPress -= stay;
        }
        if (failure is null)
            return null;
        Console.Error.WriteLine($"\n{failure}. Press Enter to go back to the dashboard.");
        Console.ReadLine();
        return failure;
    }

    private Reading Invoke(string[] arguments, bool keepOutput = false)
    {
        var start = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        foreach (var (name, value) in environment ?? new Dictionary<string, string>())
            start.Environment[name] = value;
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
                keepOutput ? output.GetAwaiter().GetResult() : "",
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
