using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;

namespace ATeam.Dashboard;

/// <summary>Where a log line's colour comes from: a named scheme, or the pane's own when null.</summary>
public readonly record struct LogStyle(string? Scheme, VisualRole Role)
{
    public static LogStyle For(LogLineKind kind) => kind switch
    {
        LogLineKind.SessionBoundary => new LogStyle(null, VisualRole.HotNormal),
        LogLineKind.Prose => new LogStyle(null, VisualRole.Normal),
        LogLineKind.ToolCall => new LogStyle(null, VisualRole.Disabled),
        LogLineKind.ToolError or LogLineKind.ResultError => new LogStyle(SchemeManager.SchemesToSchemeName(Schemes.Error), VisualRole.Normal),
        LogLineKind.ResultOk => new LogStyle(LogSchemes.Success, VisualRole.Normal),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "No colour for this log line kind."),
    };
}
