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
}

/// <summary>One rendered line of a session log, and what it came from.</summary>
public readonly record struct LogLine(string Text, LogLineKind Kind);
