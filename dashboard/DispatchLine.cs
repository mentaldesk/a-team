using System.Globalization;

namespace ATeam.Dashboard;

/// <summary>A line of dispatch.log as the pane draws it: the timestamp cut to HH:MM, coloured by what it says.</summary>
public static class DispatchLine
{
    public static LogLine Read(string line) => new(Shorten(line), KindOf(line));

    internal static string Shorten(string line)
    {
        var space = line.IndexOf(' ');
        return space > 0 && DateTimeOffset.TryParseExact(
            line[..space],
            "yyyy-MM-ddTHH:mm:ss'Z'",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var stamp)
            ? stamp.ToString("HH:mm", CultureInfo.InvariantCulture) + line[space..]
            : line;
    }

    internal static LogLineKind KindOf(string line)
    {
        var said = Said(line);
        if (said.StartsWith("triggers failed:", StringComparison.Ordinal) || said.StartsWith("killed run", StringComparison.Ordinal))
            return LogLineKind.DispatchFailed;
        return said.StartsWith("would start:", StringComparison.Ordinal)
            ? LogLineKind.DispatchSkipped
            : LogLineKind.Prose;
    }

    /// <summary>What dispatch.sh wrote, past the timestamp and the "team role: " it prefixes every line with.</summary>
    private static string Said(string line)
    {
        var colon = line.IndexOf(": ", StringComparison.Ordinal);
        return colon < 0 ? line : line[(colon + 2)..];
    }
}
