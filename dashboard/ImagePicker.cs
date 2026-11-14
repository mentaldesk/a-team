using MentalDesk.Tui.Paths;

namespace ATeam.Dashboard;

/// <summary>MentalDesk.Tui's file <see cref="PathPicker"/> for <c>Ctrl+U</c>, opened on your latest screenshot, else where
/// you last picked one, until the dashboard closes.</summary>
public sealed class ImagePicker(string? home = null, TimeProvider? clock = null)
{
    internal const string Title = "Attach image file";

    private readonly string _home = home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private string? _last;

    /// <summary>The folder to start in: the last one picked, else the one set for screenshots, else the Desktop, else
    /// home, skipping any that aren't there.</summary>
    internal static string Start(string? last, string? setting, string home) =>
        new[] { last, Screenshot.SetFolder(setting, home), Path.Combine(home, "Desktop") }
            .FirstOrDefault(Directory.Exists) ?? home;

    internal PathPicker Create(string? setting)
    {
        var shot = Screenshot.Find(Screenshot.Folder(setting, _home), _clock.GetUtcNow());
        var start = shot is null ? Start(_last, setting, _home) : Path.GetDirectoryName(shot.Path)!;
        var picker = new PathPicker(Title, PathKind.File, start, _home).Routed();
        if (shot is not null && picker.Shown.ToList().FindIndex(row => row.FullPath == shot.Path) is >= 0 and var at)
            picker.SelectedIndex = at;
        return picker;
    }

    /// <summary>The file picked, with <c>~</c> for home, or null where it was cancelled; its folder is where the next
    /// one starts.</summary>
    internal string? Picked(PathPicker picker)
    {
        if (picker.Chosen is { } row)
            _last = Path.GetDirectoryName(row.FullPath);
        return picker.PickedPath;
    }

    public string? Pick(IApplication app)
    {
        using var picker = Create(Screenshot.Setting());
        DialogHost.Run(app, picker);
        return Picked(picker);
    }
}
