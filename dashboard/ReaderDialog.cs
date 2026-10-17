using System.Globalization;
using System.Text.RegularExpressions;
using MentalDesk.Tui.Dialogs;
using MentalDesk.Tui.Fields;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace ATeam.Dashboard;

/// <summary>A command the reader offers on its own key. <paramref name="Run"/> says whether the reader is done; one
/// that isn't <paramref name="Enabled"/> still runs, to say why not, and its hint is greyed.</summary>
public sealed record ReaderCommand(Key Key, string Hint, Func<bool> Run, bool Enabled = true);

/// <summary>Commenting from the reader: Post posts the comment as you, with null once it's posted or else why it
/// wasn't.</summary>
public sealed record ReaderComment(
    Key Key, string Hint, Func<string, Task<string?>> Post, TimeProvider? Clock = null, ReaderDecline? Decline = null,
    ReaderShelve? Shelve = null, Attaching? Attach = null, ReaderImage? Image = null);

/// <summary>Viewing the image line at the comment's cursor, for Commands to run. The reader sets both.</summary>
public sealed class ReaderImage
{
    public Func<bool> Viewable { get; set; } = () => false;

    public Action View { get; set; } = () => { };
}

/// <summary>Declining from the Comment pane: Run closes the item as not planned with the reason, with null once it's
/// done or else why not. <paramref name="Open"/> opens the reader already declining.</summary>
public sealed record ReaderDecline(Key Key, Func<string, Task<string?>> Run, bool Open = false);

/// <summary>Saying Not now from the Comment pane: Run shelves the pitch for a number of weeks, with the reason if
/// there is one, with null once it's done or else why not. <paramref name="Open"/> opens the reader already shelving.</summary>
public sealed record ReaderShelve(Key Key, Func<int, string, Task<string?>> Run, bool Open = false)
{
    public const int Weeks = 2;

    /// <summary>The day it comes back, as the board dates it.</summary>
    public static DateOnly Until(DateTimeOffset now, int weeks, TimeZoneInfo? zone = null) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone ?? TimeZoneInfo.Local).DateTime.AddDays(7 * weeks));

    public static string BackOn(DateTimeOffset now, int weeks, TimeZoneInfo? zone = null) =>
        Until(now, weeks, zone).ToString("d MMM", CultureInfo.InvariantCulture);
}

/// <summary>Trying from the reader: Run hands over what it shows and the row it's scrolled to. Reopened after the
/// try, it opens at <paramref name="Top"/>, saying <paramref name="Failure"/> if it failed.</summary>
public sealed record ReaderTry(Key Key, Action<IssueBody, int> Run, int? Top = null, string? Failure = null);

/// <summary>Editing a document pitch's file from the reader: Run hands over what it shows and the row it's scrolled to,
/// unless <paramref name="Uneditable"/> says why it can't. Reopened after the edit, it opens at <paramref name="Top"/>,
/// saying <paramref name="Said"/> or <paramref name="Failure"/>.</summary>
public sealed record ReaderEdit(
    Key Key, Action<IssueBody, int> Run, string? Uneditable = null, int? Top = null, string? Said = null, string? Failure = null);

/// <summary>Filing a new idea from what you're reading: File opens New idea with the Description given, and says what
/// it added, or null where nothing was. The reader sets <see cref="Start"/> to its own, for Commands to run.</summary>
public sealed record ReaderIdea(Key Key, Func<string, string?> File)
{
    public Action? Start { get; set; }
}

/// <summary>An item's body as it was written, and its History beside it, to read without leaving the board.</summary>
public sealed partial class ReaderDialog : DashboardDialog
{
    private const string SetId = "reader.set";
    private const string ApproveId = "reader.approve";
    private const string AcceptId = "reader.accept";
    private const string CommentId = "reader.comment";
    private const string PostId = "reader.post";
    private const string AttachId = "reader.attach";
    private const string DeclineId = "reader.decline";
    private const string NotNowId = "reader.notNow";
    private const string IdeaId = "reader.idea";
    private const string TryId = "reader.try";
    private const string EditId = "reader.edit";
    private const string GitHubId = "reader.github";
    private const string HistoryId = "reader.history";
    private const string CopyId = "reader.copy";
    private const string QuoteId = "reader.quote";
    private const string BranchId = "reader.branch";
    private const int BandLines = 3;
    private static readonly string[] WaitForPost =
        [SetId, ApproveId, AcceptId, CommentId, DeclineId, NotNowId, TryId, EditId, CancelId];
    private static readonly TimeSpan SameComment = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PollEvery = TimeSpan.FromMilliseconds(100);

    private readonly Action _onGitHub;
    private readonly Action? _onApprove;
    private readonly ReaderCommand? _accept;
    private readonly ReaderComment? _comment;
    private readonly ReaderTry? _try;
    private readonly ReaderEdit? _edit;
    private readonly ReaderIdea? _idea;
    private readonly Func<Key, bool> _window;
    private readonly string _url;
    private readonly Func<bool> _confirmDiscard;
    private readonly IClipboard? _clipboard;
    private readonly Key _copyBranch;
    private readonly string _branch;
    private readonly int _number;
    private readonly int _width;
    private IssueBody _text;
    private readonly LogView _body;
    private readonly FrameView _bodyFrame;
    private readonly LogView _history;
    private readonly FrameView _historyFrame;
    private readonly FrameView _commentFrame;
    private readonly MultiLineField _field;
    private readonly View _weeksRow;
    private readonly NumericUpDown<int> _weeks;
    private readonly ReaderPanes _panes;
    private readonly View? _band;
    private readonly OptionSelector<Rank>? _ranks;
    private LogView _scrolled;
    private Task<string?>? _posting;
    private Func<bool>? _afterPost;
    private bool _declining;
    private bool _shelving;
    private readonly MessageBar _message = new();
    private readonly Attaching? _attaching;
    private readonly ImageAttach? _attach;
    private readonly Pictures? _pictures;
    private readonly Action<string> _openUrl;
    private readonly Action<Picture, string> _viewImage;

    /// <param name="onApprove">What <c>a</c> does, or null where there's nothing to approve.</param>
    /// <param name="accept">Merging the task's PR, or null where there's no task to accept.</param>
    /// <param name="comment">Commenting on the item, in a pane beside the body; a posted comment joins the end of the
    /// conversation and the top of History, and the reader stays open either way.</param>
    /// <param name="tryIt">Trying the item's PR or, for a validated pitch, the default branch; null where neither.</param>
    /// <param name="edit">Editing a document pitch's file in your editor; null for anything else.</param>
    /// <param name="rank">The rank the row of ranks starts on, or null for a reader with no row of ranks.</param>
    /// <param name="panes">Whether History was last shown; a terminal narrower than <paramref name="width"/> needs for
    /// both opens without it.</param>
    /// <param name="confirmDiscard">Asked before closing on a comment that hasn't been posted.</param>
    /// <param name="copyBranch">The key that copies the PR's branch name, where the item has one.</param>
    /// <param name="idea">Filing a new idea that links back here; null where there's nowhere to file one.</param>
    /// <param name="window">The window's own keys that work here too, run where the reader leaves a key alone.</param>
    /// <param name="pictures">Draws image lines as pictures, where the terminal can.</param>
    /// <param name="openUrl">Opens an image on GitHub where it can't be drawn.</param>
    /// <param name="viewImage">Shows a picture full-window, under its alt text.</param>
    public ReaderDialog(
        WaitingItem item, IssueBody body, Action onGitHub, Action? onApprove = null, ReaderCommand? accept = null,
        ReaderComment? comment = null, ReaderTry? tryIt = null, ReaderPanes? panes = null, int width = 0,
        Rank? rank = null, Func<bool>? confirmDiscard = null, IClipboard? clipboard = null, ReaderEdit? edit = null,
        Key? copyBranch = null, ReaderIdea? idea = null, Func<Key, bool>? window = null, Pictures? pictures = null,
        Action<string>? openUrl = null, Action<Picture, string>? viewImage = null)
        : base($"#{item.Number}  {item.Title}{(item.Question.Length == 0 ? "" : item.Pitch ? " · the Lead's question" : " · the Dev's question")}")
    {
        _pictures = pictures;
        _openUrl = openUrl ?? (_ => { });
        _viewImage = viewImage ?? ((_, _) => { });
        _idea = idea;
        if (idea is not null)
            idea.Start = () =>
            {
                NewIdea();
                Offer();
            };
        _window = window ?? (_ => false);
        _url = item.Url;
        _clipboard = clipboard;
        _branch = item.Branch;
        _copyBranch = item.Branch.Length > 0 ? copyBranch ?? Key.Empty : Key.Empty;
        _panes = panes ?? new ReaderPanes();
        _onGitHub = onGitHub;
        _onApprove = onApprove;
        _accept = accept;
        _comment = comment;
        _try = tryIt;
        _edit = edit;
        _confirmDiscard = confirmDiscard ?? (() => true);
        _number = item.Number;
        _width = width;
        _text = body;
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();

        int PaneRows() => Math.Max(0, FootRow() - (rank is null ? 0 : BandLines));

        _commentFrame = new FrameView
        {
            Title = "Comment",
            X = 0,
            Y = 0,
            Width = ReaderPanes.CommentWidth,
            Height = Dim.Func(_ => PaneRows(), this),
            Visible = false,
        };
        _weeks = new NumericUpDown<int> { Value = ReaderShelve.Weeks, X = 8, Width = 5, CanEdit = true };
        _weeks.ValueChanging += (_, e) => e.Handled = e.NewValue < 1;
        _weeks.KeyDown += (_, key) =>
        {
            if (key.Handled = Commenting(key))
                Offer();
        };
        _weeksRow = new View { Width = Dim.Fill(), Height = 1, CanFocus = true, Visible = false };
        _weeksRow.Add(new Label { Text = "Back in" }, _weeks, new Label { Text = "weeks", X = Pos.Right(_weeks) + 1 });
        _field = new MultiLineField { Width = Dim.Fill(), Height = Dim.Fill(), BorderStyle = LineStyle.None };
        // The field binds Enter itself, so the keys that leave it are caught before it sees them.
        _field.KeyDown += (_, key) =>
        {
            if (key.Handled = Commenting(key))
                Offer();
        };
        _commentFrame.Add(_weeksRow, _field);
        if (comment?.Image is { } image)
        {
            image.Viewable = () => _field.HasFocus && FieldImage() is not null;
            image.View = () => ViewImage(FieldImage());
        }
        if (comment?.Attach is { } attaching)
        {
            _attaching = attaching;
            _attach = new ImageAttach(_field, file => attaching.Upload(item.Team, file), attaching.Clipboard, Say, text: _clipboard);
        }
        _bodyFrame = new FrameView
        {
            Title = "Body",
            X = Pos.Func(_ => _commentFrame.Visible ? ReaderPanes.CommentWidth : 0, this),
            Y = 0,
            Height = Dim.Func(_ => PaneRows(), this),
        };
        _body = new LogView
        {
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanFocus = true,
            Following = false,
            Scrolls = true,
            ReadsMarkdown = true,
            SelectsText = true,
            Pictures = pictures,
            SchemeName = LogSchemes.Reader,
            Lines = body.Lines,
        };
        _bodyFrame.Add(_body);
        _historyFrame = new FrameView
        {
            Title = "History",
            X = Pos.AnchorEnd(ReaderPanes.HistoryWidth),
            Y = 0,
            Width = ReaderPanes.HistoryWidth,
            Height = Dim.Func(_ => PaneRows(), this),
        };
        _history = new LogView
        {
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanFocus = true,
            Following = false,
            Scrolls = true,
            SelectsText = true,
            Lines = (body.History ?? new History([])).Lines,
        };
        _historyFrame.Add(_history);
        _scrolled = _body;
        foreach (var pane in new[] { _body, _history })
        {
            pane.HasFocusChanged += (_, _) =>
            {
                if (pane.HasFocus && pane != _scrolled)
                    Scroll(pane);
                Offer();
            };
            pane.SelectionChanged += (_, _) => Offer();
        }
        _history.MouseEvent += (_, mouse) =>
        {
            if (mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked) && mouse.Position is { } at
                && _history.CaretAt(_history.Rows(Math.Max(1, _history.Viewport.Width)), at) is { } clicked)
                mouse.Handled = ShowRemark(clicked.Line);
        };
        if (rank is { } start)
        {
            _ranks = new OptionSelector<Rank>
            {
                X = Pos.Center(),
                Y = 1,
                Orientation = Orientation.Horizontal,
                // NoStop is what makes the arrows move between the ranks; with the default the options are Tab stops.
                TabBehavior = TabBehavior.NoStop,
                // The ranks' initials are unique, so this is what gives each option the key its name starts with.
                AssignHotKeys = true,
                Value = start,
            };
            foreach (var (row, each) in _ranks.SubViews.Zip(Enum.GetValues<Rank>()))
                if (Priorities.FormScheme(each.ToString()) is { Length: > 0 } scheme)
                    row.SchemeName = scheme;
            _ranks.KeyDown += (_, key) =>
                key.Handled = key == Key.CursorRight ? Step(+1) : key == Key.CursorLeft && Step(-1);
            _band = new View
            {
                X = 0,
                Y = Pos.Func(_ => PaneRows(), this),
                Width = Dim.Fill(),
                Height = BandLines,
                // Without this the ranks can't take focus, whatever they say.
                CanFocus = true,
                // A Dialog only takes Accept from a child of its own, so the band has to pass Enter on.
                CommandsToBubbleUp = [Command.Accept],
                SchemeName = LogSchemes.Form,
            };
            _band.Add(_ranks);
        }
        if ((tryIt?.Top ?? edit?.Top) is { } top)
            _body.Top = top;
        else if (OpensAt(item, body) is > 0 and var line)
            _body.ShowLine(line);
        Foot.Y = Pos.Func(_ => FootRow(), this);
        _message.Y = Pos.Func(_ => Math.Max(0, Viewport.Height - _message.Lines), this);
        AddKeys();

        Add(_commentFrame, _bodyFrame, _historyFrame, Foot, _message);
        if (_band is not null)
            Add(_band);
        ShowHistory(_panes.OpensWithHistory(width));
        if ((tryIt?.Failure ?? edit?.Failure ?? body.Failure) is { Length: > 0 } failure)
            _message.Show(failure, Schemes.Error);
        else if (edit?.Said is { Length: > 0 } said)
            _message.Show(said, Schemes.Accent);
        if (_ranks is null)
            _body.SetFocus();
        else
            FocusRanks();
        if (comment?.Decline is { Open: true })
        {
            Decline();
            Offer();
        }
        else if (comment?.Shelve is { Open: true })
        {
            Shelve();
            Offer();
        }
    }

    /// <summary>The rank Enter set, or null where the reader closed any other way.</summary>
    internal Rank? Chosen { get; private set; }

    internal OptionSelector<Rank>? Ranks => _ranks;

    internal View? Band => _band;

    internal LogView Body => _body;

    internal LogView HistoryLog => _history;

    internal bool HistoryShown => _historyFrame.Visible;

    internal bool CommentShown => _commentFrame.Visible;

    internal bool Declining => _declining;

    /// <summary>Whether the item was declined, which closed the reader.</summary>
    internal bool Declined { get; private set; }

    internal bool Shelving => _shelving;

    /// <summary>Whether the pitch was shelved, which closed the reader.</summary>
    internal bool Shelved { get; private set; }

    internal NumericUpDown<int> Weeks => _weeks;

    internal bool WeeksShown => _weeksRow.Visible;

    internal string CommentTitle => _commentFrame.Title;

    internal MultiLineField Field => _field;

    internal MessageBar Message => _message;

    /// <summary>A click on the ranks reaches the dialog as Accept.</summary>
    protected override bool OnAccepting(CommandEventArgs args) =>
        _ranks is null || Writing || Run(SetId, Set);

    /// <summary>The keyboard is in the Comment pane: its text, or its weeks while saying Not now.</summary>
    private bool Writing => _field.HasFocus || _weeks.HasFocus;

    protected override bool OnKeyDown(Key key)
    {
        var handled = Writing ? Commenting(key) : Reading(key);
        if (handled)
            Offer();
        return handled || base.OnKeyDown(key) || _window(key);
    }

    /// <returns>The rank Enter set on the row of ranks, or null.</returns>
    public static Rank? Show(
        IApplication app, WaitingItem item, IssueBody body, Action onGitHub, Action? onApprove, ReaderCommand? accept,
        ReaderComment? comment, ReaderTry? tryIt, Rank? rank, ReaderPanes panes, ReaderEdit? edit = null,
        Key? copyBranch = null, ReaderIdea? idea = null, Func<Key, bool>? window = null, Pictures? pictures = null,
        Action<string>? openUrl = null)
    {
        using var dialog = new ReaderDialog(item, body, onGitHub, onApprove, accept, comment, tryIt, panes,
            app.Screen.Width, rank, () => DiscardDialog.Show(app), edit: edit, copyBranch: copyBranch, idea: idea,
            window: window, pictures: pictures, openUrl: openUrl,
            viewImage: (picture, alt) => ImageViewer.Show(app, picture, alt));
        DialogHost.Run(app, dialog);
        return dialog.Chosen;
    }

    /// <summary>The line the body opens at: a question at the question; else, once you've opened it before, the first
    /// remark since, or the newest where nothing is new; else the top.</summary>
    internal static int OpensAt(WaitingItem item, IssueBody body)
    {
        if (item.Question.Length > 0 && Asked(item, body) is { } asked)
            return asked;
        if (body.Opened is not { } opened || body.Remarks.Count == 0)
            return 0;
        return (body.Remarks.FirstOrDefault(said => said.Remark.IsNewSince(opened)) ?? body.Remarks[^1]).Line;
    }

    /// <summary>A Lead's question is its pitch's Needs your answer heading; a Dev's is its comment.</summary>
    private static int? Asked(WaitingItem item, IssueBody body)
    {
        if (item.Status == "Pitched")
        {
            var lines = body.Lines;
            var heading = Enumerable.Range(0, lines.Count)
                .FirstOrDefault(i => lines[i].Kind == LogLineKind.Heading && NeedsAnswer().IsMatch(lines[i].Text), -1);
            return heading < 0 ? null : heading;
        }
        var dev = body.Remarks.Where(said => said.Remark.Who == "dev").ToList();
        return (dev.LastOrDefault(said => DevMarker().Replace(said.Remark.Body, "").Trim() == item.Question.Trim())
            ?? dev.LastOrDefault())?.Line;
    }

    [GeneratedRegex(@"\s*<!-- a-team:dev -->\s*$")]
    private static partial Regex DevMarker();

    [GeneratedRegex(@"^##\s+Needs your answer\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex NeedsAnswer();

    /// <summary>The lines quoted as a reply quotes them, on lines of their own with a blank line after.</summary>
    internal static string Quote(IEnumerable<string> lines, bool midLine) =>
        (midLine ? "\n" : "") + string.Join('\n', lines.Select(line => line.Length == 0 ? ">" : $"> {line}")) + "\n\n";

    /// <summary>A new idea's Description: the lines quoted, then a link to the remark the first came from, or to the
    /// item where they're from its body or nothing is selected.</summary>
    internal static string IdeaFrom(IssueBody body, string url, IReadOnlyList<string> lines, int? from)
    {
        var said = from is { } line ? body.Remarks.LastOrDefault(said => said.Line <= line) : null;
        var link = said is not null && from <= said.Line + 2 + said.Remark.Body.Count(c => c == '\n')
            ? said.Remark.Url ?? url
            : url;
        return (lines.Count == 0 ? "" : Quote(lines, midLine: false)) + $"From {link}";
    }

    private bool Commenting(Key key) =>
        key == Key.Tab ? SwitchPane(+1)
        : key == Key.Tab.WithShift ? SwitchPane(-1)
        : _attach?.Pressed(key) == true;

    /// <summary>A paste reaches the app before any view, so a dropped image or a pasted screenshot is caught here.</summary>
    protected override void OnIsRunningChanged(bool newIsRunning)
    {
        base.OnIsRunningChanged(newIsRunning);
        if (App is not { } app)
            return;
        if (newIsRunning)
            app.Paste += OnAppPaste;
        else
            app.Paste -= OnAppPaste;
    }

    internal void OnAppPaste(object? sender, PasteEventArgs paste) =>
        paste.Handled = paste.Handled || (_field.HasFocus && _attach?.Paste(paste.Text) == true);

    internal bool AttachFile()
    {
        if (_attaching?.PickFile() is { } path)
        {
            ToComment();
            _attach!.Attach(path);
        }
        return true;
    }

    private void Say(string text, Schemes scheme)
    {
        _message.Show(text, scheme);
        SetNeedsLayout();
        SetNeedsDraw();
    }

    private bool Reading(Key key)
    {
        if (key == Key.Tab)
            return SwitchPane(+1);
        if (key == Key.Tab.WithShift)
            return SwitchPane(-1);
        if (_ranks is null && _body.HasFocus && key == Key.Enter && _body.ImageAtCaret is { } image)
            return ViewImage(image);
        if (_ranks is null && _history.HasFocus && key == Key.Enter)
            return ShowRemark(_history.Caret?.Line ?? -1);
        if (_ranks is null && Caret(key) is { } move)
            return MoveCaret(move, key.IsShift);
        return _ranks is not null && Scroll(key) is { } scroll && Scrolled(scroll);
    }

    /// <summary>Each of the reader's own keys, as a button where it acts on the item, else only under F1.</summary>
    private void AddKeys()
    {
        bool Reads() => !Writing;
        void Bind(string id, Key key, string label, Func<bool> run, Func<bool> when) =>
            base.Bind(id, key, label, () => Run(id, run), when);
        if (_ranks is not null)
            Bind(SetId, Key.Enter, "Set", Set, Reads);
        if (_onApprove is not null)
            Bind(ApproveId, new Key('a'), "Approve", Approve, Reads);
        if (_accept is not null)
            Bind(AcceptId, _accept.Key, Capital(_accept.Hint), AcceptItem, Reads);
        if (_comment is not null)
        {
            Bind(CommentId, _comment.Key, Capital(_comment.Hint), Comment, Reads);
            Bind(PostId, Key.Enter.WithCtrl, "Post", Post, () => CommentShown);
        }
        if (_attaching is not null)
            Bind(AttachId, _attaching.Key, "Attach file", AttachFile, () => Writing);
        if (_comment?.Decline is { } decline)
            Bind(DeclineId, decline.Key, "Decline", Decline, Reads);
        if (_comment?.Shelve is { } shelve)
            Bind(NotNowId, shelve.Key, "Not now", Shelve, Reads);
        if (_idea is not null)
            Bind(IdeaId, _idea.Key, "Idea", NewIdea, () => Reads() && _ranks is null);
        if (_try is not null)
            Bind(TryId, _try.Key, "Try", Try, Reads);
        if (_edit is not null)
            Bind(EditId, _edit.Key, "Edit", Edit, Reads);
        Bind(GitHubId, new Key('g'), "GitHub", OnGitHub, Reads);
        Register(CancelId, "Close", () => Run(CancelId, () => Writing ? Unfocus() : Escape()));
        foreach (var (id, kind) in new[]
        {
            (SetId, ButtonKind.Primary), (ApproveId, _accept is null ? ButtonKind.Primary : ButtonKind.Secondary),
            (AcceptId, ButtonKind.Primary), (CommentId, ButtonKind.Secondary), (PostId, ButtonKind.Primary),
            (AttachId, ButtonKind.Secondary), (DeclineId, ButtonKind.Danger),
            (NotNowId, ButtonKind.Secondary), (IdeaId, ButtonKind.Secondary), (TryId, ButtonKind.Secondary),
            (EditId, ButtonKind.Secondary), (GitHubId, ButtonKind.Secondary), (CancelId, ButtonKind.Secondary),
        })
            if (Commands.Find(id) is not null)
                AddButton(kind, id);

        Bind(HistoryId, new Key('h'), "Show history", ToggleHistory, () => Reads() && _ranks is null);
        Bind("reader.previousRemark", Key.CursorUp.WithAlt, "Previous remark", () => ToRemark(-1), () => Reads() && _scrolled == _body);
        Bind("reader.nextRemark", Key.CursorDown.WithAlt, "Next remark", () => ToRemark(+1), () => Reads() && _scrolled == _body);
        Bind(CopyId, Key.C.WithCtrl, "Copy", Copy, Reads);
        if (_comment is not null)
            Bind(QuoteId, new Key('q'), "Quote", Quote, () => Reads() && FocusedPane().Marked > 0);
        if (_copyBranch != Key.Empty)
            Bind(BranchId, _copyBranch, "Copy the branch name", CopyBranch, Reads);
        Also("Tab/Shift+Tab", "Switch pane", () => CommentShown || HistoryShown);
        Also("↑/↓/PgUp/PgDn", "Scroll", () => _ranks is not null);
        Also("←/→", "Priority", () => _ranks is not null);
        Also("Arrows/PgUp/PgDn", "Move", () => Reads() && _ranks is null);
        Also("Shift+arrows", "Select", () => _ranks is null);
        Also("Enter", "View the picture", () => Reads() && _ranks is null && _scrolled == _body && _body.ImageAtCaret is not null);
        Also("Enter", "Show the comment", () => Reads() && _ranks is null && _scrolled == _history);
        Also("Ctrl+V", "Paste", () => Writing);
        Also("Ctrl+X", "Cut", () => _field.HasFocus);
        Also("Ctrl+Z", "Undo", () => _field.HasFocus);
        Also("↑/↓", "Weeks", () => _weeks.HasFocus);
    }

    /// <summary>One that would change the comment or close the reader waits for a comment that's posting.</summary>
    private bool Run(string id, Func<bool> run)
    {
        if (_posting is not null && WaitForPost.Contains(id))
            _afterPost = run;
        else
            run();
        Offer();
        return true;
    }

    private static string Capital(string label) => label.Length == 0 ? label : char.ToUpperInvariant(label[0]) + label[1..];

    private int FootRow() => Math.Max(0, Viewport.Height - Foot.Rows - _message.Lines);

    /// <summary>The buttons, and the labels that change, for where the keyboard is now.</summary>
    private void Offer()
    {
        ShowCarets();
        var marked = FocusedPane().Marked;
        if (_comment is not null)
        {
            Rename(PostId, _declining ? "Decline" : _shelving ? "Not now" : "Post");
            Restyle(PostId, _declining ? ButtonKind.Danger : ButtonKind.Primary);
        }
        if (_idea is not null)
            Rename(IdeaId, Quoted() > 0 ? $"Idea from {Lines(Quoted())}" : "Idea");
        if (_comment is not null)
            Rename(QuoteId, $"Quote {Lines(marked)}");
        Rename(HistoryId, HistoryShown ? "Hide history" : "Show history");
        Rename(CancelId, Writing ? _declining ? "Cancel" : _shelving ? "Comment instead" : "Back" : marked > 0 ? "Clear" : "Close");
        Restyle(CancelId, !Writing && marked == 0 && !string.IsNullOrWhiteSpace(_field.Text) ? ButtonKind.Danger : ButtonKind.Secondary);
        ShowButtons();
        Hide(CommentId, CommentShown);
        Hide(DeclineId, _declining);
        Hide(NotNowId, _shelving);
        Grey(AcceptId, _accept?.Enabled != false);
        Grey(EditId, _edit?.Uneditable is null);
    }

    private static string Lines(int count) => $"{count} {(count == 1 ? "line" : "lines")}";

    /// <summary>How many of the Body's lines a new idea quotes: none while History has the keyboard.</summary>
    private int Quoted() => FocusedPane() == _body ? _body.Marked : 0;

    private void ShowCarets()
    {
        var reading = _ranks is null && !Writing;
        _body.ShowsCaret = reading && _scrolled == _body;
        _history.ShowsCaret = reading && _scrolled == _history;
    }

    private void ShowHistory(bool shown)
    {
        _historyFrame.Visible = shown;
        _bodyFrame.Width = shown ? Dim.Fill(ReaderPanes.HistoryWidth) : Dim.Fill();
        if (!shown && _scrolled == _history)
            Scroll(_body);
        Offer();
        SetNeedsLayout();
        SetNeedsDraw();
    }

    private bool ToggleHistory()
    {
        _panes.HistoryShown = !_historyFrame.Visible;
        ShowHistory(_panes.HistoryShown);
        return true;
    }

    /// <summary>Weeks, Comment, Body and History in turn, leaving out whichever isn't showing.</summary>
    private bool SwitchPane(int by)
    {
        List<View> panes =
        [
            .. _shelving ? [_weeks] : Array.Empty<View>(), .. CommentShown ? [_field] : Array.Empty<View>(),
            _body, .. HistoryShown ? [_history] : Array.Empty<View>(),
        ];
        var at = panes.IndexOf(_field.HasFocus ? _field : _weeks.HasFocus ? _weeks : _scrolled);
        var next = panes[(at + by + panes.Count) % panes.Count];
        if (next == _field)
            ToComment();
        else if (next == _weeks)
            _weeks.SetFocus();
        else
            Scroll((LogView)next);
        return true;
    }

    /// <summary>With a row of ranks the keyboard stays on the ranks, so the pane that scrolls is only remembered.</summary>
    private void Scroll(LogView pane)
    {
        _scrolled = pane;
        if (_ranks is null)
            pane.SetFocus();
        else if (!_ranks.HasFocus)
            FocusRanks();
    }

    /// <summary>SetFocus lands the keyboard on the first option, so the chosen rank is put back under it after.</summary>
    private void FocusRanks()
    {
        _ranks!.SetFocus();
        _ranks.FocusedItem = (int)(_ranks.Value ?? Rank.None);
    }

    private LogView FocusedPane() => _history.HasFocus ? _history : _body.HasFocus ? _body : _scrolled;

    private bool Scrolled(Action scroll)
    {
        scroll();
        SetNeedsDraw();
        return true;
    }

    private Action? Scroll(Key key)
    {
        var pane = FocusedPane();
        return key == Key.CursorUp ? () => pane.Step(-1)
            : key == Key.CursorDown ? () => pane.Step(+1)
            : key == Key.PageUp ? () => pane.Page(-1)
            : key == Key.PageDown ? () => pane.Page(+1)
            : key == Key.Home ? pane.Home
            : key == Key.End ? pane.End
            : null;
    }

    /// <summary>The previous or next remark's heading, the top of the body counting as the first.</summary>
    private bool ToRemark(int by)
    {
        var at = _body.Caret?.Line ?? _body.TopLine;
        var stops = _text.Remarks.Select(said => said.Line).Prepend(0).Distinct().ToList();
        var to = by < 0 ? stops.LastOrDefault(line => line < at, -1) : stops.FirstOrDefault(line => line > at, -1);
        if (to >= 0)
            _body.ShowLine(to);
        SetNeedsDraw();
        return true;
    }

    /// <summary>The comment History's line <paramref name="line"/> is about, matched by who and when; a line about
    /// anything else shows nothing.</summary>
    private bool ShowRemark(int line)
    {
        if (_text.History is not { Failure: null } history || line < 0 || line >= history.Events.Count
            || history.Events[line] is not { What: "commented", Run: null } commented)
            return true;
        var said = _text.Remarks
            .Where(said => said.Remark.Who == commented.Who && (said.Remark.At - commented.At).Duration() <= SameComment)
            .MinBy(said => (said.Remark.At - commented.At).Duration());
        if (said is null)
            return true;
        _body.ShowLine(said.Line);
        Scroll(_body);
        SetNeedsDraw();
        return true;
    }

    private bool Step(int by)
    {
        var count = Enum.GetValues<Rank>().Length;
        _ranks!.Value = (Rank)(((int)(_ranks.Value ?? Rank.None) + by + count) % count);
        _ranks.FocusedItem = (int)_ranks.Value;
        return true;
    }

    private static CaretMove? Caret(Key key)
    {
        var bare = key.NoShift;
        return bare == Key.CursorLeft ? CaretMove.Left
            : bare == Key.CursorRight ? CaretMove.Right
            : bare == Key.CursorLeft.WithCtrl || bare == Key.CursorLeft.WithAlt || bare == Key.B.WithAlt ? CaretMove.WordLeft
            : bare == Key.CursorRight.WithCtrl || bare == Key.CursorRight.WithAlt || bare == Key.F.WithAlt ? CaretMove.WordRight
            : bare == Key.CursorUp ? CaretMove.Up
            : bare == Key.CursorDown ? CaretMove.Down
            : bare == Key.PageUp ? CaretMove.PageUp
            : bare == Key.PageDown ? CaretMove.PageDown
            : bare == Key.Home ? CaretMove.RowStart
            : bare == Key.End ? CaretMove.RowEnd
            : bare == Key.Home.WithCtrl ? CaretMove.Start
            : bare == Key.End.WithCtrl ? CaretMove.End
            : null;
    }

    private bool MoveCaret(CaretMove move, bool extend)
    {
        FocusedPane().MoveCaret(move, extend);
        return true;
    }

    private bool Copy()
    {
        var text = string.Join('\n', FocusedPane().MarkedText());
        return text.Length == 0 || Copy(text, Copied(text));
    }

    private bool CopyBranch() => Copy(_branch, $"Copied {_branch}");

    private bool Copy(string text, string said)
    {
        var clipboard = _clipboard ?? App?.Clipboard;
        if (clipboard is { IsSupported: true } && clipboard.TrySetClipboardData(text))
            _message.Show(said, Schemes.Accent);
        else
            _message.Show("there's no clipboard to copy to", Schemes.Error);
        SetNeedsLayout();
        return true;
    }

    internal static string Copied(string text) =>
        text.Length == 1 ? "copied 1 character" : string.Create(CultureInfo.InvariantCulture, $"copied {text.Length:N0} characters");

    /// <summary>The selected text goes in at the comment's cursor, and the keyboard follows it there to answer it.</summary>
    private bool Quote()
    {
        var pane = FocusedPane();
        if (pane.MarkedText() is not { Count: > 0 } lines)
            return true;
        pane.Unmark();
        // Taken now: the comment posting rewrites the body, and the selection with it.
        if (_posting is not null)
        {
            _afterPost = () => Quote(lines);
            return true;
        }
        return Quote(lines);
    }

    private bool Quote(IReadOnlyList<string> lines)
    {
        if (!CommentShown)
        {
            Comment();
            Layout();
        }
        var at = _field.CaretOffset;
        var quoted = Quote(lines, at > 0 && _field.Text[at - 1] != '\n');
        _field.Document!.Insert(at, quoted);
        _field.CaretOffset = at + quoted.Length;
        ToComment();
        return true;
    }

    /// <summary>The reader stays open underneath, and says what was added.</summary>
    private bool NewIdea()
    {
        var quoted = Quoted() > 0;
        var description = IdeaFrom(_text, _url, quoted ? _body.MarkedText() : [], quoted ? _body.MarkedFrom : null);
        if (_idea!.File(description) is not { } added)
            return true;
        _body.Unmark();
        _message.Show(added, Schemes.Accent);
        SetNeedsLayout();
        return true;
    }

    /// <summary>The image line the comment's cursor is on, or null.</summary>
    private ImageLink? FieldImage()
    {
        var text = _field.Text;
        var at = Math.Clamp(_field.CaretOffset, 0, text.Length);
        var start = at == 0 ? 0 : text.LastIndexOf('\n', at - 1) + 1;
        var end = text.IndexOf('\n', at);
        return Pictures.Link(text[start..(end < 0 ? text.Length : end)]);
    }

    /// <summary>Drawn full-window where the terminal can, else opened on GitHub; one still uploading only says so.</summary>
    private bool ViewImage(ImageLink? image)
    {
        if (image is null)
            return true;
        if (_pictures is { Drawn: true } && _pictures.For(image.Url) is { } picture)
            _viewImage(picture, image.Alt);
        else if (image.Url.StartsWith("https://", StringComparison.Ordinal))
            _openUrl(image.Url);
        else
            Say($"{image.Alt} hasn't finished uploading", Schemes.Error);
        return true;
    }

    private bool OnGitHub()
    {
        _onGitHub();
        return true;
    }

    private bool Try() => Leave(() =>
    {
        _try!.Run(_text, _body.Top);
        return Close();
    });

    /// <summary>One that can't be edited only says why, and stays open.</summary>
    private bool Edit()
    {
        if (_edit!.Uneditable is { } why)
        {
            _message.Show(why, Schemes.Error);
            SetNeedsLayout();
            return true;
        }
        return Leave(() =>
        {
            _edit.Run(_text, _body.Top);
            return Close();
        });
    }

    private bool Approve() => Leave(() =>
    {
        _onApprove!();
        return Close();
    });

    /// <summary>One that can't go ahead only says why, so it leaves the comment alone.</summary>
    private bool AcceptItem() => _accept!.Enabled ? Leave(AcceptNow) : AcceptNow();

    private bool AcceptNow() => !_accept!.Run() || Close();

    private bool Comment()
    {
        if (!CommentShown)
        {
            _commentFrame.Visible = true;
            if (HistoryShown && _width < ReaderPanes.NarrowestThree)
                ShowHistory(false);
            SetNeedsLayout();
        }
        ToComment();
        return true;
    }

    /// <summary>The Comment pane, retitled; what's already in it becomes the start of the reason.</summary>
    private bool Decline()
    {
        ShowWeeks(false);
        _declining = true;
        _commentFrame.Title = $"Decline #{_number}: why?";
        return Comment();
    }

    /// <summary>The Comment pane, retitled, with the weeks above it; what's already in it becomes the reason.</summary>
    private bool Shelve()
    {
        _declining = false;
        ShowWeeks(true);
        _commentFrame.Title = $"Not now #{_number}";
        return Comment();
    }

    private void ShowWeeks(bool shelving)
    {
        _shelving = shelving;
        _weeksRow.Visible = shelving;
        _field.Y = shelving ? 2 : 0;
        SetNeedsLayout();
    }

    private bool BackToComment()
    {
        var weeks = _weeks.HasFocus;
        _declining = false;
        ShowWeeks(false);
        _commentFrame.Title = "Comment";
        if (weeks)
            ToComment();
        SetNeedsDraw();
        return true;
    }

    private bool Unfocus() => _declining || _shelving ? BackToComment() : Back();

    private void ToComment()
    {
        _field.SetFocus();
        SetNeedsDraw();
    }

    private bool Back()
    {
        Scroll(_scrolled == _history && !HistoryShown ? _body : _scrolled);
        return true;
    }

    private bool Post()
    {
        if (_posting is not null)
            return true;
        if (!_shelving && string.IsNullOrWhiteSpace(_field.Text))
        {
            _message.Show(_declining ? $"A reason is needed to decline #{_number}" : "Nothing to post: the comment is empty",
                Schemes.Error);
            SetNeedsLayout();
            return true;
        }
        if (_attach?.Uploading == true)
        {
            Say("Wait for the image to finish uploading", Schemes.Error);
            return true;
        }
        _message.Show(_declining ? $"Declining #{_number}…" : _shelving ? $"Shelving #{_number}…" : "Posting…", Schemes.Accent);
        SetNeedsLayout();
        var text = _field.Text;
        var posting = _posting = _declining ? _comment!.Decline!.Run(text)
            : _shelving ? _comment!.Shelve!.Run(_weeks.Value, text)
            : _comment!.Post(text);
        if (posting.IsCompleted)
        {
            Settle(posting, text);
            return true;
        }
        // Off the field, so a key pressed while it posts isn't typed into what's being posted.
        var written = _field.HasFocus;
        if (written)
            Back();
        // Polled rather than Invoked: the reader opens inside a timer, and Invoke waits on that timer's lock.
        App?.AddTimeout(PollEvery, () =>
        {
            if (!posting.IsCompleted)
                return true;
            Settle(posting, text, written);
            return false;
        });
        return true;
    }

    /// <summary>A comment that didn't post leaves the pane open, with what you wrote still in it.</summary>
    private void Settle(Task<string?> done, string text, bool written = false)
    {
        _posting = null;
        var after = _afterPost;
        _afterPost = null;
        var failure = done.Status == TaskStatus.RanToCompletion
            ? done.Result
            : done.Exception?.GetBaseException().Message ?? "the comment didn't post";
        if (failure is { Length: > 0 })
        {
            _message.Show(failure, Schemes.Error);
            if (written)
                ToComment();
            Offer();
            SetNeedsLayout();
            return;
        }
        if (_declining || _shelving)
        {
            Declined = _declining;
            Shelved = _shelving;
            Close();
            return;
        }
        var remark = new Remark("you", (_comment!.Clock ?? TimeProvider.System).GetUtcNow(), text);
        _text = _text.With(new Conversation([remark]));
        _body.Lines = _text.Lines;
        _body.End();
        if (_text.History is { Failure: null } history)
        {
            _text = _text with { History = history.With(new HistoryEvent(remark.At, remark.Who, "commented")) };
            _history.Lines = _text.History.Lines;
            _history.Home();
        }
        _field.Text = "";
        _commentFrame.Visible = false;
        _body.DropCaret();
        _history.DropCaret();
        ShowHistory(_panes.OpensWithHistory(_width));
        Back();
        Offer();
        _message.Show($"commented on #{_number}", Schemes.Accent);
        SetNeedsLayout();
        SetNeedsDraw();
        if (after is not null)
        {
            after();
            Offer();
        }
    }

    private bool Set() => Leave(() =>
    {
        Chosen = _ranks!.Value;
        return Close();
    });

    private bool Escape()
    {
        if (FocusedPane() is { Marked: > 0 } pane)
        {
            pane.Unmark();
            return true;
        }
        return Leave(Close);
    }

    /// <summary>Closing on a comment you've written asks first, and waits for one that's posting.</summary>
    private bool Leave(Func<bool> close) =>
        _posting is not null || (!string.IsNullOrWhiteSpace(_field.Text) && !_confirmDiscard()) || close();

    private bool Close()
    {
        RequestStop();
        return true;
    }
}
