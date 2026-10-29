using MentalDesk.Tui.Loading;

namespace ATeam.Dashboard;

/// <summary>A repo's Markdown files to pick one from, loaded in the list, where typing a path none of them match picks
/// it as a new one.</summary>
public sealed class RepoFilePicker : PathPicker
{
    internal const string Loading = "Loading the Markdown files in {0}…";
    internal const string NoFiles = "No Markdown files yet. Type a path to use it.";

    public RepoFilePicker(string title, string repo, int width = 74, int contentRows = 16)
        : base(title, width, contentRows, string.Format(Loading, repo), $"Markdown files in {repo}", "Pick")
    {
        Commands.Register(PickId, "Pick", Pick);
    }

    public static string? Show(IApplication app, RepoFilePicker picker)
    {
        DialogHost.Run(app, picker);
        return picker.Picked;
    }

    /// <summary>Lists the files read, or says why they couldn't be, with <paramref name="retry"/> on R.</summary>
    internal void Show(RepoFiles files, Action retry)
    {
        if (files.Failure is { } failure)
        {
            ShowFailed(failure, retry);
            return;
        }
        ShowWhere(files.Where);
        ShowRows(files.Paths, NoFiles);
    }

    internal void Pick()
    {
        if (State is not (LoadState.Loaded or LoadState.Empty))
            return;
        if (Selected is { } path)
            Take(path);
        else if (Typed.Length > 0)
            Take(Typed);
    }
}
