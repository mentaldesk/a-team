using System.Collections.ObjectModel;
using MentalDesk.Tui.Filtering;
using MentalDesk.Tui.Dialogs;
using MentalDesk.Tui.Links;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using MarkdownView = Terminal.Gui.Views.Markdown;

namespace ATeam.Dashboard;

/// <summary>A-Team's own guide, the pages of <c>docs/guide</c> built into the app, read a page at a time and followed link to link, beside
/// a Contents tree of its pages and their sections and an Index that filters the sections by name.</summary>
public sealed class GuideDialog : DashboardDialog
{
    internal const string Contents = "index.md";
    internal const string Work = "work.md";
    internal const string App = "app.md";
    internal const string Teams = "teams.md";
    internal const string Overseer = "overseer.md";

    private const string BackId = "guide.back";
    private const int Inset = 1;
    private static readonly Key ContentsKey = Key.C.WithCtrl.WithShift;
    private static readonly Key IndexKey = Key.I.WithCtrl.WithShift;

    private readonly IReadOnlyDictionary<string, string> _guide;
    private readonly Func<string, LinkOutcome> _openUrl;
    private readonly MarkdownView _page;
    private readonly MessageBar _message = new();
    private readonly Stack<(string Page, int Line, GuideTopic? At)> _back = [];
    private readonly IReadOnlyList<GuideTopic> _pages;
    private readonly IReadOnlyList<GuideTopic> _sections;
    private readonly List<GuideTopic> _matches = [];
    private readonly TeamTabs _sidebar = new();
    private readonly View _contentsTab = new() { Title = "Contents", CanFocus = true, Width = Dim.Fill(), Height = Dim.Fill() };
    private readonly View _indexTab = new() { Title = "Index", CanFocus = true, Width = Dim.Fill(), Height = Dim.Fill() };
    private readonly TreeView<GuideTopic> _contents;
    private readonly TextField _filter = new() { X = 0, Y = 0, Width = Dim.Fill() };
    private readonly ListView _index = new() { X = 0, Y = 1, Width = Dim.Fill(), Height = Dim.Fill(), CanFocus = false };
    private readonly Label _noMatches = new() { X = 0, Y = 1, Text = "(no matches)", Enabled = false, Visible = false };
    private string _current = "";
    private GuideTopic? _at;
    private int _lineBefore;
    private Action? _afterLayout;

    /// <param name="guide">Each page's Markdown, by its path in the guide.</param>
    /// <param name="openUrl">Where a link to a web page goes.</param>
    public GuideDialog(IReadOnlyDictionary<string, string> guide, Func<string, LinkOutcome> openUrl, string page = Contents)
        : base("Guide")
    {
        _guide = guide;
        _openUrl = openUrl;
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();

        _pages = GuideTopics.Read(guide);
        _sections = [.. _pages.SelectMany(topic => topic.Sections)];
        _contents = new TreeView<GuideTopic>(new DelegateTreeBuilder<GuideTopic>(topic => topic.Sections, topic => topic.Sections.Count > 0))
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            MultiSelect = false,
            AspectGetter = topic => topic.Name,
        };
        _contents.AddObjects(_pages);
        _contents.Accepting += (_, args) =>
        {
            args.Handled = true;
            if (_contents.SelectedObject is { } topic)
                Go(topic);
        };
        _contentsTab.Add(_contents);
        _filter.ValueChanged += (_, _) => Narrow();
        _filter.KeyBindings.Remove(Key.Home);
        _filter.KeyBindings.Remove(Key.End);
        _filter.Accepting += (_, args) =>
        {
            args.Handled = true;
            GoToMatch();
        };
        _index.Accepting += (_, args) =>
        {
            args.Handled = true;
            GoToMatch();
        };
        // Each match takes two rows, its section and its page under it, and only the section's can be selected.
        _index.ValueChanged += (_, change) =>
        {
            if (change.NewValue is { } row && row % 2 == 1)
                _index.Value = row - 1;
        };
        _indexTab.Add(_filter, _index, _noMatches);
        _sidebar.X = 0;
        _sidebar.Y = 0;
        _sidebar.Width = Dim.Func(_ => SidebarWidth(), this);
        _sidebar.Height = Dim.Func(_ => HintRow(), this);
        _sidebar.CanFocus = true;
        _sidebar.Add(_contentsTab, _indexTab);
        _sidebar.Value = _contentsTab;
        Narrow();

        _page = new MarkdownView
        {
            X = Pos.Right(_sidebar) + Inset,
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
        _message.Y = Pos.Func(_ => Math.Max(0, Viewport.Height - _message.Lines), this);

        Bind(BackId, Key.Backspace, "Back", () => Back(), () => !_indexTab.HasFocus);
        Bind("guide.contents", ContentsKey, "Contents", () => ShowContents());
        Bind("guide.index", IndexKey, "Index", () => ShowIndex());
        Bind("guide.previousMatch", Key.CursorUp, "Previous match", () => MoveMatch(-1), () => _indexTab.HasFocus);
        Bind("guide.nextMatch", Key.CursorDown, "Next match", () => MoveMatch(+1), () => _indexTab.HasFocus);
        Also("Tab", "Next link", () => _page.HasFocus);
        Also("Enter", "Follow the link", () => _page.HasFocus);
        Also("Enter", "Go to the section", () => !_page.HasFocus);
        Register(CancelId, "Close", () => Close());
        AddButton(ButtonKind.Secondary, BackId);
        AddButton(ButtonKind.Secondary, CancelId);
        Foot.Y = Pos.Func(_ => HintRow(), this);
        Add(_sidebar, _page, Foot, _message);
        if (!Open(page))
            Title = "Guide";
        else if (page != Contents)
            _back.Push((Contents, 0, null));
        Track(page, "");
        _page.SetFocus();
    }

    /// <summary>The page showing, by its path in the guide.</summary>
    internal string Page => _current;

    internal MarkdownView View => _page;

    internal MessageBar Message => _message;

    internal bool HasClosed { get; private set; }

    internal TreeView<GuideTopic> ContentsTree => _contents;

    internal TextField IndexFilter => _filter;

    internal IReadOnlyList<GuideTopic> Matches => _matches;

    internal ListView IndexList => _index;

    /// <summary>The section showing in the sidebar: the match chosen in the Index, or the topic selected in Contents.</summary>
    internal GuideTopic? Chosen => ReferenceEquals(_sidebar.Value, _indexTab) ? Match() : _contents.SelectedObject;

    internal bool ShowsIndex => ReferenceEquals(_sidebar.Value, _indexTab);

    internal bool ShowContents()
    {
        _sidebar.Value = _contentsTab;
        _contents.SetFocus();
        return true;
    }

    internal bool ShowIndex()
    {
        _sidebar.Value = _indexTab;
        _filter.SetFocus();
        return true;
    }

    /// <summary>Opens a topic's page, at its heading if it's a section, and reads on there.</summary>
    internal void Go(GuideTopic topic)
    {
        Follow($"{topic.Page}#{(topic.IsPage ? GuideTopics.Slug(topic.Name) : topic.Anchor)}");
        _page.SetFocus();
    }

    /// <summary>A web page opens in the browser; anything else is a page of the guide, a heading on one, or both.</summary>
    internal void Follow(string url)
    {
        if (IsWeb(url))
        {
            var (said, scheme) = Copying.Said(_openUrl(url));
            _message.Show(said, scheme);
            SetNeedsLayout();
            return;
        }
        var (path, anchor) = Split(url);
        var page = path.Length == 0 ? _current : GuidePages.Resolve(_current, path);
        var from = (_current, page == _current && anchor.Length > 0 ? _lineBefore : _page.Viewport.Y, _at);
        if (page != _current && !Open(page))
            return;
        _back.Push(from);
        Track(page, anchor);
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
        Track(to.At);
        AfterLayout(() => _page.Viewport = _page.Viewport with { Y = to.Line });
        return true;
    }

    public static void Show(IApplication app, IReadOnlyDictionary<string, string> guide, Func<string, LinkOutcome> openUrl, string page = Contents)
    {
        using var dialog = new GuideDialog(guide, openUrl, page);
        DialogHost.Run(app, dialog);
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

    /// <summary>Selects, in Contents, the section of <paramref name="page"/> an anchor is on, or the page itself.</summary>
    private void Track(string page, string anchor) =>
        Track(_pages.FirstOrDefault(topic => topic.Page == page) is { } at
            ? at.Sections.FirstOrDefault(section => section.Anchor == anchor) ?? at
            : null);

    private void Track(GuideTopic? at)
    {
        _at = at;
        if (at is null)
            return;
        if (_pages.FirstOrDefault(topic => topic.Page == at.Page) is { } page)
            _contents.Expand(page);
        _contents.SelectedObject = at;
        // Before the tree has a height, EnsureVisible scrolls the rows above the selection away.
        if (_contents.Viewport.Height > 0)
            _contents.EnsureVisible(at);
    }

    private int SidebarWidth() => Math.Clamp(Viewport.Width / 4, 16, 30);

    private int HintRow() => Math.Max(0, Viewport.Height - Foot.Rows - _message.Lines);

    private void Narrow()
    {
        _matches.Clear();
        _matches.AddRange(ListFilter.Apply(_filter.Text ?? "", _sections, topic => topic.Name));
        _index.SetSource(new ObservableCollection<string>(_matches.SelectMany(topic => new[] { topic.Name, $"  {topic.PageName}" })));
        _index.Value = _matches.Count == 0 ? null : 0;
        _noMatches.Visible = _matches.Count == 0;
    }

    private GuideTopic? Match() =>
        _index.Value is { } row && row / 2 < _matches.Count ? _matches[row / 2] : null;

    private bool MoveMatch(int step)
    {
        if (_matches.Count == 0)
            return true;
        _index.Value = Math.Clamp((_index.Value ?? 0) / 2 + step, 0, _matches.Count - 1) * 2;
        _index.EnsureSelectedItemVisible();
        return true;
    }

    private void GoToMatch()
    {
        if (Match() is { } topic)
            Go(topic);
    }

    private bool Open(string page)
    {
        if (!_guide.TryGetValue(page, out var text))
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
        HasClosed = true;
        RequestStop();
        return true;
    }
}
