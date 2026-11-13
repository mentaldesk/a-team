using MentalDesk.Tui.Paths;

namespace ATeam.Dashboard;

/// <summary>MentalDesk.Tui's file <see cref="PathPicker"/> for <c>Ctrl+U</c>: it opens on your latest screenshot, else
/// where you last picked a file, else where screenshots go, else home.</summary>
public sealed class AttachPicker(string home, Func<string?> setting, Func<string?, Screenshot?> latest)
{
    internal const string Title = "Attach image file";

    private string? _last;

    internal string Start(string? screenshots) =>
        new[] { _last, Screenshots(screenshots), Path.Combine(home, "Desktop") }
            .FirstOrDefault(folder => folder is not null && Directory.Exists(folder)) ?? home;

    internal PathPicker Create()
    {
        var folder = setting();
        var shot = latest(folder);
        var picker = new PathPicker(Title, PathKind.File, shot is null ? Start(folder) : Path.GetDirectoryName(shot.Path)!, home).Routed();
        if (shot is not null)
        {
            var name = Path.GetFileName(shot.Path);
            var at = picker.Shown.ToList().FindIndex(row => row.Kind == PathRowKind.File && Path.GetFileName(row.FullPath) == name);
            if (at >= 0)
                picker.SelectedIndex = at;
        }
        return picker;
    }

    /// <summary>The file picked, with <c>~</c> for home, or null where it was cancelled.</summary>
    public string? Browse(IApplication app)
    {
        using var picker = Create();
        DialogHost.Run(app, picker);
        return Picked(picker);
    }

    internal string? Picked(PathPicker picker)
    {
        if (picker.Chosen is not { } row)
            return null;
        _last = Path.GetDirectoryName(row.FullPath);
        return picker.PickedPath;
    }

    private string? Screenshots(string? screenshots) =>
        screenshots is { Length: > 0 } ? TeamSettings.Expand(screenshots.Trim(), home) : null;
}
