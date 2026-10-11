namespace ATeam.Dashboard;

/// <summary>A repo's Markdown files to pick one from, where typing a path none of them match picks it as a new one.</summary>
public sealed class RepoFilePicker : PathPicker
{
    internal const string Unreadable = "Type a path to use it.";

    public RepoFilePicker(string title, RepoFiles files, int width = 74, int contentRows = 16)
        : base(title, width, contentRows, files.Paths, files.Where, "pick")
    {
        Commands.Register(PickId, "Pick", Pick);
        if (files.Failure is not null)
            Say(Unreadable);
    }

    public static string? Show(IApplication app, string title, RepoFiles files)
    {
        var (width, rows) = Fit(app.Screen.Width, app.Screen.Height);
        using var picker = new RepoFilePicker(title, files, width, rows);
        app.Run(picker);
        return picker.Picked;
    }

    internal void Pick()
    {
        if (Selected is { } path)
            Take(path);
        else if (Typed.Length > 0)
            Take(Typed);
    }
}
