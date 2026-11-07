using System.Diagnostics;
using System.Text;

namespace ATeam.Core;

internal readonly record struct GhResult(int Code, string Out, string Err)
{
    public bool Ok => Code == 0;

    /// <summary>What `$(...)` would have made of its output.</summary>
    public string Said => Out.TrimEnd('\n');
}

/// <summary>`gh`, found on PATH as a shell finds it: never beside this binary or in the working directory.</summary>
internal sealed class Gh(string? token)
{
    public GhResult Run(params string[] args) => Feed(null, args);

    /// <summary>`gh` with <paramref name="input"/> on its stdin.</summary>
    public GhResult Feed(string? input, params string[] args)
    {
        using var process = Process.Start(Starting(args)) ?? throw new Die("couldn't start gh");
        var err = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEndAsync();
        try
        {
            if (input is not null) process.StandardInput.Write(input);
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // It stopped reading, and its exit code says why.
        }
        output.Wait();
        process.WaitForExit();
        return new GhResult(process.ExitCode, output.Result, err.Result);
    }

    /// <summary>`gh`'s stdout, whatever its bytes, in a file, and whether it exited 0. What it said on stderr is dropped.</summary>
    public bool Save(string file, params string[] args)
    {
        using var process = Process.Start(Starting(args)) ?? throw new Die("couldn't start gh");
        process.StandardInput.Close();
        var err = process.StandardError.ReadToEndAsync();
        using (var saved = File.Create(file))
            process.StandardOutput.BaseStream.CopyTo(saved);
        err.Wait();
        process.WaitForExit();
        return process.ExitCode == 0;
    }

    private ProcessStartInfo Starting(string[] args)
    {
        var path = Which("gh") ?? throw new Die("no gh on PATH");
        var start = new ProcessStartInfo(path)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        if (token is not null)
            start.Environment["GH_TOKEN"] = token;
        return start;
    }

    internal static string? Which(string name)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':'))
        {
            var candidate = Path.Combine(dir.Length == 0 ? "." : dir, name);
            if (File.Exists(candidate) && !OperatingSystem.IsWindows()
                && (File.GetUnixFileMode(candidate) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0)
                return candidate;
        }
        return null;
    }
}
