using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace ATeam.Dashboard;

/// <summary>A command the reader offers on its own key. <paramref name="Run"/> says whether the reader is done; one
/// that isn't <paramref name="Enabled"/> still runs, to say why not, and its hint is greyed.</summary>
public sealed record ReaderCommand(Key Key, string Hint, Func<bool> Run, bool Enabled = true);

/// <summary>Commenting from the reader: Run is the remark it posted, or null if nothing was.</summary>
public sealed record ReaderComment(Key Key, string Hint, Func<Remark?> Run);

/// <summary>Trying from the reader: Run hands over what it shows and the row it's scrolled to. Reopened after the
/// try, it opens at <paramref name="Top"/>, saying <paramref name="Failure"/> if it failed.</summary>
public sealed record ReaderTry(Key Key, Action<IssueBody, int> Run, int Top = 0, string? Failure = null);

/// <summary>An item's body as it was written, and its History beside it, to read without leaving the board.</summary>
public sealed class ReaderDialog : Dialog
{
    private const string ScrollHint = "scroll";
    private const string SwitchHint = "switch";
    private const string HistoryHint = "history";
    private const string TryHint = "try";
    private const string ApproveHint = "approve";
    private const string AcceptHint = "accept";
    private const string CommentHint = "comment";
    private const string RankHint = "rank";
    private const string SetHint = "set";
    private const string GitHubHint = "github";
    private const string CloseHint = "close";
    private const int BandLines = 3;

    private readonly Action _onGitHub;
    private readonly Action? _onApprove;
    private readonly ReaderCommand? _accept;
    private readonly ReaderComment? _comment;
    private readonly ReaderTry? _try;
    private readonly int _number;
    private IssueBody _text;
    private readonly LogView _body;
    private readonly FrameView _bodyFrame;
    private readonly LogView _history;
    private readonly FrameView _historyFrame;
    private readonly ReaderPanes _panes;
    private readonly View? _band;
    private readonly OptionSelector<Rank>? _ranks;
    private LogView _scrolled;
    private readonly IReadOnlyList<HintedCommand> _commands;
    private readonly StatusBar _hints = new();
    private readonly MessageBar _message = new();

    /// <param name="onApprove">What <c>a</c> does, or null where there's nothing to approve.</param>
    /// <param name="accept">Merging the task's PR, or null where there's no task to accept.</param>
    /// <param name="comment">Commenting on the item; a posted comment joins the end of the conversation and the top
    /// of History, and the reader stays open either way.</param>
    /// <param name="tryIt">Trying the item's PR or, for a validated pitch, the default branch; null where neither.</param>
    /// <param name="rank">The rank the row of ranks starts on, or null for a reader with no row of ranks.</param>
    /// <param name="panes">Whether History was last shown; a terminal narrower than <paramref name="width"/> needs for
    /// both opens without it.</param>
    public ReaderDialog(
        WaitingItem item, IssueBody body, Action onGitHub, Action? onApprove = null, ReaderCommand? accept = null,
        ReaderComment? comment = null, ReaderTry? tryIt = null, ReaderPanes? panes = null, int width = 0,
        Rank? rank = null)
    {
        _panes = panes ?? new ReaderPanes();
        _onGitHub = onGitHub;
        _onApprove = onApprove;
        _accept = accept;
        _comment = comment;
        _try = tryIt;
        _number = item.Number;
        _text = body;
        Title = $"#{item.Number}  {item.Title}{(item.Question.Length == 0 ? "" : item.Pitch ? " · the Lead's question" : " · the Dev's question")}";
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();

        int HintRow() => Math.Max(0, Viewport.Height - 1 - _message.Lines);
        int PaneRows() => Math.Max(0, HintRow() - (rank is null ? 0 : BandLines));

        _bodyFrame = new FrameView { Title = "Body", X = 0, Y = 0, Height = Dim.Func(_ => PaneRows(), this) };
        _body = new LogView
        {
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanFocus = true,
            Following = false,
            Scrolls = true,
            ReadsMarkdown = true,
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
            Lines = (body.History ?? new History([])).Lines,
        };
        _historyFrame.Add(_history);
        _scrolled = _body;
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
        if (tryIt is not null)
            _body.Top = tryIt.Top;
        _hints.Y = Pos.Func(_ => HintRow(), this);
        _commands = [
            new HintedCommand(ScrollHint, "Up/Down/PgUp/PgDn scroll"),
            .. rank is null ? Array.Empty<HintedCommand>()
                : [new HintedCommand(RankHint, "←/→ rank"), new HintedCommand(SetHint, "Enter set")],
            .. tryIt is null ? Array.Empty<HintedCommand>() : [new HintedCommand(TryHint, $"{KeyNames.Short(tryIt.Key)} try")],
            .. onApprove is null ? Array.Empty<HintedCommand>() : [new HintedCommand(ApproveHint, "a approve")],
            .. accept is null ? Array.Empty<HintedCommand>()
                : [new HintedCommand(AcceptHint, $"{KeyNames.Short(accept.Key)} {accept.Hint}", accept.Enabled)],
            .. comment is null ? Array.Empty<HintedCommand>()
                : [new HintedCommand(CommentHint, $"{KeyNames.Short(comment.Key)} {comment.Hint}")],
            new HintedCommand(GitHubHint, "g on GitHub"),
            new HintedCommand(CloseHint, "Esc close"),
        ];

        _message.Y = Pos.Func(_ => Math.Max(0, Viewport.Height - _message.Lines), this);

        Add(_bodyFrame, _historyFrame, _hints, _message);
        if (_band is not null)
            Add(_band);
        ShowHistory(_panes.OpensWithHistory(width));
        if ((tryIt?.Failure ?? body.Failure) is { Length: > 0 } failure)
            _message.Show(failure, Schemes.Error);
        if (_ranks is null)
            _body.SetFocus();
        else
        {
            // SetFocus lands the keyboard on the first option, so the starting rank is put under it after.
            _ranks.SetFocus();
            _ranks.FocusedItem = (int)rank!.Value;
        }
    }

    /// <summary>The rank Enter set, or null where the reader closed any other way.</summary>
    internal Rank? Chosen { get; private set; }

    internal OptionSelector<Rank>? Ranks => _ranks;

    internal View? Band => _band;

    internal LogView Body => _body;

    internal LogView HistoryLog => _history;

    internal bool HistoryShown => _historyFrame.Visible;

    internal StatusBar Hints => _hints;

    internal MessageBar Message => _message;

    /// <summary>Enter reaches a Dialog as Accept, from the ranks themselves, and never as a key.</summary>
    protected override bool OnAccepting(CommandEventArgs args) =>
        _ranks is null ? base.OnAccepting(args) : Set();

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Esc)
            return Close();
        if (_ranks is not null && key == Key.Enter)
            return Set();
        if (key == new Key('h') && _ranks is null)
            return ToggleHistory();
        if (key == Key.Tab || key == Key.Tab.WithShift)
            return SwitchPane();
        if (key == new Key('g'))
            return OnGitHub();
        if (_try is not null && key == _try.Key)
            return Try();
        if (key == new Key('a') && _onApprove is not null)
            return Approve();
        if (_accept is not null && key == _accept.Key)
            return Accept();
        if (_comment is not null && key == _comment.Key)
            return Comment();
        return Scroll(key) is { } scroll ? Scrolled(scroll) : base.OnKeyDown(key);
    }

    /// <returns>The rank Enter set on the row of ranks, or null.</returns>
    public static Rank? Show(
        IApplication app, WaitingItem item, IssueBody body, Action onGitHub, Action? onApprove, ReaderCommand? accept,
        ReaderComment? comment, ReaderTry? tryIt, Rank? rank, ReaderPanes panes)
    {
        using var dialog = new ReaderDialog(item, body, onGitHub, onApprove, accept, comment, tryIt, panes,
            app.Screen.Width, rank);
        app.Run(dialog);
        return dialog.Chosen;
    }

    private void ShowHistory(bool shown)
    {
        _historyFrame.Visible = shown;
        _bodyFrame.Width = shown ? Dim.Fill(ReaderPanes.HistoryWidth) : Dim.Fill();
        if (!shown && _scrolled == _history)
            Scroll(_body);
        var at = _commands.ToList();
        at.InsertRange(1, [
            .. shown ? [new HintedCommand(SwitchHint, "Tab switch pane")] : Array.Empty<HintedCommand>(),
            .. _ranks is not null ? Array.Empty<HintedCommand>()
                : [new HintedCommand(HistoryHint, shown ? "h hide history" : "h show history")],
        ]);
        _hints.Show("", at, Run);
        SetNeedsLayout();
        SetNeedsDraw();
    }

    private bool ToggleHistory()
    {
        _panes.HistoryShown = !_historyFrame.Visible;
        ShowHistory(_panes.HistoryShown);
        return true;
    }

    private bool SwitchPane()
    {
        if (_historyFrame.Visible)
            Scroll(_scrolled == _history ? _body : _history);
        return true;
    }

    /// <summary>With a row of ranks the keyboard stays on the ranks, so the pane that scrolls is only remembered.</summary>
    private void Scroll(LogView pane)
    {
        _scrolled = pane;
        if (_ranks is null)
            pane.SetFocus();
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

    private bool Step(int by)
    {
        var count = Enum.GetValues<Rank>().Length;
        _ranks!.Value = (Rank)(((int)(_ranks.Value ?? Rank.None) + by + count) % count);
        _ranks.FocusedItem = (int)_ranks.Value;
        return true;
    }

    private bool OnGitHub()
    {
        _onGitHub();
        return true;
    }

    private bool Try()
    {
        _try!.Run(_text, _body.Top);
        return Close();
    }

    private bool Approve()
    {
        _onApprove!();
        return Close();
    }

    private bool Accept() => !_accept!.Run() || Close();

    private bool Comment()
    {
        if (_comment!.Run() is { } remark)
        {
            _text = _text.With(new Conversation([remark]));
            _body.Lines = _text.Lines;
            _body.End();
            if (_text.History is { Failure: null } history)
            {
                _text = _text with { History = history.With(new HistoryEvent(remark.At, remark.Who, "commented")) };
                _history.Lines = _text.History.Lines;
                _history.Home();
            }
            _message.Show($"commented on #{_number}", Schemes.Accent);
            SetNeedsLayout();
            SetNeedsDraw();
        }
        return true;
    }

    private bool Set()
    {
        Chosen = _ranks!.Value;
        return Close();
    }

    private bool Close()
    {
        RequestStop();
        return true;
    }

    private bool Run(string hint) => hint switch
    {
        ScrollHint => Scrolled(() => FocusedPane().Page(+1)),
        SwitchHint => SwitchPane(),
        HistoryHint => ToggleHistory(),
        TryHint => Try(),
        ApproveHint => Approve(),
        AcceptHint => Accept(),
        CommentHint => Comment(),
        RankHint => Step(+1),
        SetHint => Set(),
        GitHubHint => OnGitHub(),
        _ => Close(),
    };
}
