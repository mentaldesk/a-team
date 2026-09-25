using System.Globalization;
using System.Reflection;
using System.Text;

namespace ATeam.Dashboard;

/// <summary>What a crash leaves to read. The run happens inside <see cref="Guard"/> so that the
/// <c>using</c>s around <c>Application</c> have already lowered the alternate screen buffer by the time
/// anything is written: on the buffer, the terminal throws the report away as it restores.</summary>
public static class CrashReport
{
    /// <summary>Runs the dashboard, and turns anything it throws into one line on the restored screen and a
    /// file to paste into an issue. Returns what the run returned, or 1 if it crashed.</summary>
    public static int Guard(Func<int> run, string stateRoot, IReadOnlyList<string> arguments, TextWriter error)
    {
        try
        {
            return run();
        }
        catch (Exception crash)
        {
            return Report(crash, stateRoot, arguments, error, DateTimeOffset.UtcNow, Version());
        }
    }

    internal static int Report(
        Exception crash,
        string stateRoot,
        IReadOnlyList<string> arguments,
        TextWriter error,
        DateTimeOffset at,
        string version)
    {
        error.WriteLine($"a-team-dashboard: {Summary(crash)}");
        var path = Path.Combine(stateRoot, $"crash-{Stamp(at)}.log");
        try
        {
            Directory.CreateDirectory(stateRoot);
            File.WriteAllText(path, Details(crash, arguments, at, version));
            error.WriteLine($"Full details: {Shortened(path)}");
        }
        catch (Exception)
        {
            // Whatever stopped the file being written must not also stop the crash being readable.
            error.WriteLine(crash.ToString());
        }
        return 1;
    }

    internal static string Summary(Exception crash) => $"{crash.GetType().Name}: {crash.Message}";

    internal static string Details(Exception crash, IReadOnlyList<string> arguments, DateTimeOffset at, string version)
    {
        var details = new StringBuilder();
        details.AppendLine($"a-team-dashboard {version}");
        details.AppendLine($"crashed {at.ToUniversalTime().ToString("u", CultureInfo.InvariantCulture)}");
        details.AppendLine($"arguments: {(arguments.Count == 0 ? "(none)" : string.Join(' ', arguments))}");
        details.AppendLine();
        details.AppendLine(crash.ToString());
        return details.ToString();
    }

    private static string Stamp(DateTimeOffset at) =>
        at.ToUniversalTime().ToString("yyyy-MM-dd'T'HH-mm-ss'Z'", CultureInfo.InvariantCulture);

    private static string Version() =>
        typeof(CrashReport).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0] ?? "";

    internal static string Shortened(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return home.Length > 0 && path.StartsWith(home + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? $"~{path[home.Length..]}"
            : path;
    }
}
