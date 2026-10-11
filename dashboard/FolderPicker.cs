using Terminal.Gui.Input;

namespace ATeam.Dashboard;

/// <summary>One folder's subfolders, to go into, go up from, or use: the Workdir picker, a level at a time.</summary>
public sealed class FolderPicker : PathPicker
{
    internal const string UseText = "Use this folder (Ctrl+Enter)";
    private const string UseId = "folder.use";
    private const string UpId = "folder.up";

    private readonly string _folder;

    public FolderPicker(string title, string folder, string home, int width = 74, int contentRows = 16)
        : base(title, width, contentRows, Subfolders(folder), Tilde(folder, home), "open")
    {
        _folder = folder;
        Commands.Register(PickId, "Open", Open).Register(UseId, "Use this folder", Use).Register(UpId, "Up", Up);
        Keys.Bind([Key.Enter.WithCtrl], UseId);
        var list = SubViews.OfType<ListView>().Single();
        list.Height = contentRows - 4;
        UseButton = new Button { Text = UseText, X = Pos.Center(), Y = contentRows - 1 };
        UseButton.Accepting += (_, args) =>
        {
            Use();
            args.Handled = true;
        };
        Add(UseButton);
        FilterField.SetFocus();
    }

    internal Button UseButton { get; }

    /// <summary>Where the picker goes next: into a folder, up one, or nowhere, once it's used or cancelled.</summary>
    internal string? Next { get; private set; }

    /// <summary>The folder to start in: the one typed, else its nearest parent that's there, else home.</summary>
    internal static string Start(string typed, string home)
    {
        var path = typed.Trim().Length == 0 ? home : Path.GetFullPath(TeamSettings.Expand(typed.Trim(), home));
        for (var at = path; at is not null && (at == path || at != Path.GetPathRoot(at)); at = Path.GetDirectoryName(at))
            if (Directory.Exists(at))
                return Path.TrimEndingDirectorySeparator(at);
        return home;
    }

    /// <summary>The folders in <paramref name="folder"/>, hidden ones left out, or none where it can't be read.</summary>
    internal static IReadOnlyList<string> Subfolders(string folder)
    {
        try
        {
            return [.. new DirectoryInfo(folder).EnumerateDirectories()
                .Where(directory => !directory.Name.StartsWith('.') && !directory.Attributes.HasFlag(FileAttributes.Hidden))
                .Select(directory => directory.Name)
                .Order(StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>The path as the form writes it, with <c>~</c> for <paramref name="home"/>.</summary>
    internal static string Tilde(string path, string home)
    {
        path = Path.TrimEndingDirectorySeparator(path);
        home = Path.TrimEndingDirectorySeparator(home);
        return path == home ? "~"
            : path.StartsWith(home + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "~/" + path[(home.Length + 1)..].Replace('\\', '/')
            : path;
    }

    /// <summary>Browses from the folder <paramref name="typed"/> names, a dialog per folder, and returns the one used,
    /// with <c>~</c> for home, or null where it was cancelled.</summary>
    public static string? Browse(IApplication app, string title, string typed, string home)
    {
        var (width, rows) = Fit(app.Screen.Width, app.Screen.Height);
        var folder = Start(typed, home);
        while (true)
        {
            using var picker = new FolderPicker(title, folder, home, width, rows);
            app.Run(picker);
            if (picker.Picked is { } used)
                return used;
            if (picker.Next is not { } next)
                return null;
            folder = next;
        }
    }

    internal override bool Route(Key key) =>
        key == Key.Backspace && Typed.Length == 0 ? Run(Up) : base.Route(key);

    internal void Open()
    {
        if (Selected is { } name)
            Go(Path.Combine(_folder, name));
    }

    internal void Up()
    {
        if (Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(_folder)) is { } parent)
            Go(parent);
    }

    internal void Use() => Take(Where);

    private void Go(string folder)
    {
        Next = folder;
        RequestStop();
    }

    private static bool Run(Action action)
    {
        action();
        return true;
    }
}
