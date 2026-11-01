using MentalDesk.Tui.Paths;

namespace ATeam.Dashboard;

/// <summary>MentalDesk.Tui's folder <see cref="PathPicker"/>, opened where the Workdir field points.</summary>
public static class WorkdirPicker
{
    /// <summary>The folder to start in: the one typed, else its nearest parent that's there, else home.</summary>
    internal static string Start(string typed, string home)
    {
        var path = typed.Trim().Length == 0 ? home : Path.GetFullPath(TeamSettings.Expand(typed.Trim(), home));
        for (var at = path; at is not null && (at == path || at != Path.GetPathRoot(at)); at = Path.GetDirectoryName(at))
            if (Directory.Exists(at))
                return Path.TrimEndingDirectorySeparator(at);
        return home;
    }

    internal static PathPicker Create(string title, string typed, string home) =>
        new PathPicker(title, PathKind.Folder, Start(typed, home), home).Routed();

    /// <summary>The folder used, with <c>~</c> for home, or null where it was cancelled.</summary>
    public static string? Browse(IApplication app, string title, string typed, string home)
    {
        using var picker = Create(title, typed, home);
        DialogHost.Run(app, picker);
        return picker.PickedPath;
    }
}
