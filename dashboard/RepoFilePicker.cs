using MentalDesk.Tui.Dialogs;
using MentalDesk.Tui.Loading;

namespace ATeam.Dashboard;

/// <summary>A repo's Markdown files to pick one from, loaded in the list, where typing a path none of them match picks
/// it as a new one.</summary>
public sealed class RepoFilePicker : PickerDialog<string>
{
    internal const string Loading = "Loading the Markdown files in {0}…";
    internal const string NoFiles = "No Markdown files yet. Type a path to use it.";

    private const int ScreenMargin = 4;
    private const int Widest = 90;

    private readonly Label _where;

    public RepoFilePicker(string title, string repo, int width = 74, int contentRows = 16)
        : base(title, width, contentRows, string.Format(Loading, repo), row => row, "Pick")
    {
        _where = new Label { Text = $"Markdown files in {repo}", X = 1, Y = 0, Width = Dim.Fill(1) };
        FilterField.Y = 1;
        Add(_where);
        this.Routed();
        FilterField.SetFocus();
        Commands.Register(PickId, "Pick", Pick);
    }

    internal string Where => _where.Text;

    /// <summary>The path picked, or null where it was cancelled.</summary>
    internal string? Picked { get; private set; }

    internal string Typed => FilterField.Text.Trim();

    /// <summary>The widest a picker can be on a screen <paramref name="columns"/> wide, and how many rows it can show
    /// on one <paramref name="lines"/> high.</summary>
    internal static (int Width, int Rows) Fit(int columns, int lines) =>
        (Math.Clamp(columns - ScreenMargin, 20, Widest), Math.Clamp(lines - ScreenMargin - 2, 6, 24));

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
        _where.Text = files.Where;
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

    private void Take(string path)
    {
        Picked = path;
        Accept();
    }
}
