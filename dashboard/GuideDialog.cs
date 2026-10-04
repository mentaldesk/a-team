using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using MarkdownView = Terminal.Gui.Views.Markdown;

namespace ATeam.Dashboard;

/// <summary>A-Team's own guide, the pages in <c>docs/guide</c>, read a page at a time and followed link to link.</summary>
public sealed class GuideDialog : Dialog
{
    internal const string Contents = "index.md";
    internal const string Work = "work.md";
    internal const string Dashboard = "dashboard.md";
    internal const string Teams = "teams.md";

    private const string NextHint = "next";
    private const string FollowHint = "follow";
    private const string BackHint = "back";
    private const string CloseHint = "close";
    private const int Inset = 1;

    private readonly string _folder;
    private readonly Action<string> _openUrl;
    private readonly MarkdownView _page;
    private readonly StatusBar _hints = new();
    private readonly MessageBar _message = new();
    private readonly Stack<(string Page, int Line)> _back = [];
    private string _current = "";
    private int _lineBefore;
    private Action? _afterLayout;

    /// <param name="folder">Where the guide's pages are.</param>
    /// <param name="openUrl">Where a link to a web page goes.</param>
    public GuideDialog(string folder, Action<string> openUrl, string page = Contents)
    {
        _folder = folder;
        _openUrl = openUrl;
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();

        int HintRow() => Math.Max(0, Viewport.Height - 1 - _message.Lines);

        _page = new MarkdownView
        {
            X = Inset,
            Y = 0,
            Width = Dim.Fill(Inset),
            Height = Dim.Func(_ => HintRow(), this),
        };
        // The view has already scrolled to an anchor on its own page by the time it says a link was followed.
        _page.ViewportChanged += (_, change) => _lineBefore = change.OldViewport.Y;
        _page.LinkClicked += (_, link) =>
        {
            link.Handled = true;
            Follow(link.Url);
        };
        _page.SubViewsLaidOut += (_, _) =>
        {
            var then = _afterLayout;
            _afterLayout = null;
            then?.Invoke();
        };
        _hints.Y = Pos.Func(_ => HintRow(), this);
        _hints.Show("", [
            new HintedCommand(NextHint, "Tab next link"),
            new HintedCommand(FollowHint, "Enter follow"),
            new HintedCommand(BackHint, "Backspace back"),
            new HintedCommand(CloseHint, "Esc close"),
        ], Run);
        _message.Y = Pos.Func(_ => Math.Max(0, Viewport.Height - _message.Lines), this);

        Add(_page, _hints, _message);
        if (!Open(page))
            Title = "Guide";
        else if (page != Contents)
            _back.Push((Contents, 0));
        _page.SetFocus();
    }

    /// <summary>The page showing, relative to the guide's folder.</summary>
    internal string Page => _current;

    internal MarkdownView View => _page;

    internal StatusBar Hints => _hints;

    internal MessageBar Message => _message;

    internal bool Closed { get; private set; }

    /// <summary>Enter reaches a Dialog as Accept and would close it; the guide has nothing to confirm.</summary>
    protected override bool OnAccepting(CommandEventArgs args) => true;

    protected override bool OnKeyDown(Key key) =>
        key == Key.Esc ? Close()
        : key == Key.Backspace ? Back()
        : base.OnKeyDown(key);

    /// <summary>A web page opens in the browser; anything else is a page of the guide, a heading on one, or both.</summary>
    internal void Follow(string url)
    {
        if (IsWeb(url))
        {
            _openUrl(url);
            return;
        }
        var (path, anchor) = Split(url);
        var page = path.Length == 0 ? _current : Resolve(path);
        var from = (_current, page == _current && anchor.Length > 0 ? _lineBefore : _page.Viewport.Y);
        if (page != _current && !Open(page))
            return;
        _back.Push(from);
        if (anchor.Length > 0)
            AfterLayout(() => _page.ScrollToAnchor(anchor));
    }

    /// <summary>Back to the page and the line a link was followed from.</summary>
    internal bool Back()
    {
        if (!_back.TryPop(out var to))
            return true;
        if (to.Page != _current)
            Open(to.Page);
        AfterLayout(() => _page.Viewport = _page.Viewport with { Y = to.Line });
        return true;
    }

    public static void Show(IApplication app, string folder, Action<string> openUrl, string page = Contents)
    {
        using var dialog = new GuideDialog(folder, openUrl, page);
        app.Run(dialog);
    }

    internal static bool IsWeb(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto";

    internal static (string Path, string Anchor) Split(string url) =>
        url.IndexOf('#') is var hash and >= 0 ? (url[..hash], url[(hash + 1)..]) : (url, "");

    /// <summary>The first heading names the page.</summary>
    internal static string Named(string markdown) =>
        markdown.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.StartsWith("# ", StringComparison.Ordinal)) is { } heading
            ? heading[2..].Trim()
            : "";

    private string Resolve(string path)
    {
        var full = Path.GetFullPath(Path.Combine(_folder, Path.GetDirectoryName(_current) ?? "", Uri.UnescapeDataString(path)));
        return Path.GetRelativePath(_folder, full);
    }

    private bool Open(string page)
    {
        string text;
        try
        {
            text = File.ReadAllText(Path.Combine(_folder, page));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _message.Show($"There's no {page} in the guide", Schemes.Error);
            SetNeedsLayout();
            return false;
        }
        _current = page;
        _page.Text = text;
        Title = Named(text) is { Length: > 0 } name ? $"Guide · {name}" : "Guide";
        _message.Clear();
        SetNeedsLayout();
        return true;
    }

    /// <summary>Headings and lines exist once the page is laid out, which a page just opened isn't yet.</summary>
    private void AfterLayout(Action then)
    {
        _afterLayout = then;
        then();
        _page.SetNeedsLayout();
        SetNeedsDraw();
    }

    private bool Close()
    {
        Closed = true;
        RequestStop();
        return true;
    }

    private bool Run(string hint) => hint switch
    {
        NextHint => _page.AdvanceFocus(NavigationDirection.Forward, TabBehavior.TabStop),
        FollowHint => _page.InvokeCommand(Command.Accept) == true,
        BackHint => Back(),
        _ => Close(),
    };
}
