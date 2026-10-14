using System.Globalization;

namespace ATeam.Dashboard;

/// <summary>What a rendered log line came from, which decides how it's drawn.</summary>
public enum LogLineKind
{
    Prose,
    SessionBoundary,
    ToolCall,
    ToolError,
    ResultOk,
    ResultError,
    DispatchSkipped,
    DispatchFailed,
    Heading,
    Code,
    Quote,
    InlineCode,
    Strong,
    ListMarker,
}

/// <summary>One rendered line of a session log, and what it came from. <paramref name="Full"/> is the untruncated
/// text, kept only where it differs from <paramref name="Text"/>.</summary>
public readonly record struct LogLine(string Text, LogLineKind Kind, string? Full = null, bool Capped = false)
{
    public string Copied => Full ?? Text;

    /// <summary>The line as the whole-session file has it: a tool call keeps its tool's name in front.</summary>
    public string Written => Kind == LogLineKind.ToolCall && Full is not null ? $"{Text.Split(' ', 2)[0]} {Full}" : Copied;
}


public sealed record LogCopy(string Text, int Lines, bool Capped)
{
    public static LogCopy Of(IReadOnlyList<LogLine> lines) =>
        new(string.Join("\n", lines.Select(line => line.Copied)), lines.Count, lines.Any(line => line.Capped));

    public string Said
    {
        get
        {
            var characters = Text.Length == 1 ? "1 character" : string.Create(CultureInfo.InvariantCulture, $"{Text.Length:N0} characters");
            return (Lines == 1 ? $"copied {characters}" : $"copied {Lines} lines, {characters}") + (Capped ? " (line truncated)" : "");
        }
    }
}
