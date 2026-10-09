using System.Globalization;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

// Terminal.Gui 2.5 obsoletes TextView in favour of Terminal.Gui.Editor; moving to it is a change of its own.
#pragma warning disable CS0618

namespace ATeam.Dashboard;

/// <summary>A command the reader offers on its own key. <paramref name="Run"/> says whether the reader is done; one
/// that isn't <paramref name="Enabled"/> still runs, to say why not, and its hint is greyed.</summary>
public sealed record ReaderCommand(Key Key, string Hint, Func<bool> Run, bool Enabled = true);

/// <summary>Commenting from the reader: Post posts the comment as you, with null once it's posted or else why it
/// wasn't.</summary>
public sealed record ReaderComment(Key Key, string Hint, Func<string, Task<string?>> Post, TimeProvider? Clock = null);

/// <summary>Trying from the reader: Run hands over what it shows and the row it's scrolled to. Reopened after the
/// try, it opens at <paramref name="Top"/>, saying <paramref name="Failure"/> if it failed.</summary>
public sealed record ReaderTry(Key Key, Action<IssueBody, int> Run, int Top = 0, string? Failure = null);

/// <summary>An item's body as it was written, and its History beside it, to read without leaving the board.</summary>
public sealed class ReaderDialog : Dialog
{
    private const string ScrollHint = "scroll";
    private const string SwitchHint = "switch";
    private const string HistoryHint = "history";
    private const string SelectHint = "select";
    private const string CopyHint = "copy";
    private const string QuoteHint = "quote";
    private const string TryHint = "try";
    private const string ApproveHint = "approve";
    private const string AcceptHint = "accept";
    private const string CommentHint = "comment";
    private const string PostHint = "post";
    private const string RankHint = "rank";
    private const string SetHint = "set";
    private const string GitHubHint = "github";
    private const string CloseHint = "close";
    private const int BandLines = 3;
    private const string Marker = "\uE000";
    private static readonly TimeSpan PollEvery = TimeSpan.FromMilliseconds(100);

    private readonly Action _onGitHub;
    private readonly Action? _onApprove;
    private readonly ReaderCommand? _accept;
    private readonly ReaderComment? _comment;
    private readonly ReaderTry? _try;
    private readonly Func<bool> _confirmDiscard;
    private readonly IClipboard? _clipboard;
    private readonly int _number;
    private readonly int _width;
    private IssueBody _text;
    private readonly LogView _body;
    private readonly FrameView _bodyFrame;
    private readonly LogView _history;
    private readonly FrameView _historyFrame;
    private readonly FrameView _commentFrame;
    private readonly TextView _field;
    private readonly ReaderPanes _panes;
    private readonly View? _band;
    private readonly OptionSelector<Rank>? _ranks;
    private LogView _scrolled;
    private Task<string?>? _posting;
    private readonly StatusBar _hints = new();
    private readonly MessageBar _message = new();

    /// <param name="onApprove">What <c>a</c> does, or null where there's nothing to approve.</param>
    /// <param name="accept">Merging the task's PR, or null where there's no task to accept.</param>
    /// <param name="comment">Commenting on the item, in a pane beside the body; a posted comment joins the end of the
    /// conversation and the top of History, and the reader stays open either way.</param>
    /// <param name="tryIt">Trying the item's PR or, for a validated pitch, the default branch; null where neither.</param>
    /// <param name="rank">The rank the row of ranks starts on, or null for a reader with no row of ranks.</param>
    /// <param name="panes">Whether History was last shown; a terminal narrower than <paramref name="width"/> needs for
    /// both opens without it.</param>
    /// <param name="confirmDiscard">Asked before closing on a comment that hasn't been posted.</param>
    public ReaderDialog(
        WaitingItem item, IssueBody body, Action onGitHub, Action? onApprove = null, ReaderCommand? accept = null,
        ReaderComment? comment = null, ReaderTry? tryIt = null, ReaderPanes? panes = null, int width = 0,
        Rank? rank = null, Func<bool>? confirmDiscard = null, IClipboard? clipboard = null)
    {
        _clipboard = clipboard;
        _panes = panes ?? new ReaderPanes();
        _onGitHub = onGitHub;
        _onApprove = onApprove;
        _accept = accept;
        _comment = comment;
        _try = tryIt;
        _confirmDiscard = confirmDiscard ?? (() => true);
        _number = item.Number;
        _width = width;
        _text = body;
        Title = $"#{item.Number}  {item.Title}{(item.Question.Length == 0 ? "" : item.Pitch ? " · the Lead's question" : " · the Dev's question")}";
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();

        int HintRow() => Math.Max(0, Viewport.Height - 1 - _message.Lines);
        int PaneRows() => Math.Max(0, HintRow() - (rank is null ? 0 : BandLines));

        _commentFrame = new FrameView
        {
            Title = "Comment",
            X = 0,
            Y = 0,
            Width = ReaderPanes.CommentWidth,
            Height = Dim.Func(_ => PaneRows(), this),
            Visible = false,
        };
        _field = new TextView
        {
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            WordWrap = true,
            TabKeyAddsTab = false,
        };
        // The field binds Enter itself, so the keys that leave it are caught before it sees them.
        _field.KeyDown += (_, key) =>
        {
            if (key.Handled = Commenting(key))
                ShowHints();
        };
        _commentFrame.Add(_field);
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
                ShowHints();
            };
            pane.SelectionChanged += (_, _) => ShowHints();
        }
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
        _message.Y = Pos.Func(_ => Math.Max(0, Viewport.Height - _message.Lines), this);

        Add(_commentFrame, _bodyFrame, _historyFrame, _hints, _message);
        if (_band is not null)
            Add(_band);
        ShowHistory(_panes.OpensWithHistory(width));
        if ((tryIt?.Failure ?? body.Failure) is { Length: > 0 } failure)
            _message.Show(failure, Schemes.Error);
        if (_ranks is null)
            _body.SetFocus();
        else
            FocusRanks();
    }

    /// <summary>The rank Enter set, or null where the reader closed any other way.</summary>
    internal Rank? Chosen { get; private set; }

    internal OptionSelector<Rank>? Ranks => _ranks;

    internal View? Band => _band;

    internal LogView Body => _body;

    internal LogView HistoryLog => _history;

    internal bool HistoryShown => _historyFrame.Visible;

    internal bool CommentShown => _commentFrame.Visible;

    internal TextView Field => _field;

    internal StatusBar Hints => _hints;

    internal MessageBar Message => _message;

    /// <summary>Enter reaches a Dialog as Accept, from the ranks themselves, and never as a key.</summary>
    protected override bool OnAccepting(CommandEventArgs args) =>
        _ranks is null ? base.OnAccepting(args) : _field.HasFocus || Set();

    protected override bool OnKeyDown(Key key)
    {
        var handled = _field.HasFocus ? Commenting(key) : Reading(key);
        if (handled)
            ShowHints();
        return handled || base.OnKeyDown(key);
    }

    /// <returns>The rank Enter set on the row of ranks, or null.</returns>
    public static Rank? Show(
        IApplication app, WaitingItem item, IssueBody body, Action onGitHub, Action? onApprove, ReaderCommand? accept,
        ReaderComment? comment, ReaderTry? tryIt, Rank? rank, ReaderPanes panes)
    {
        using var dialog = new ReaderDialog(item, body, onGitHub, onApprove, accept, comment, tryIt, panes,
            app.Screen.Width, rank, () => DiscardDialog.Show(app));
        app.Run(dialog);
        return dialog.Chosen;
    }

    /// <summary>The lines quoted as a reply quotes them, on lines of their own with a blank line after.</summary>
    internal static string Quote(IEnumerable<string> lines, bool midLine) =>
        (midLine ? "\n" : "") + string.Join('\n', lines.Select(line => line.Length == 0 ? ">" : $"> {line}")) + "\n\n";

    private bool Commenting(Key key) =>
        key == Key.Esc ? Back()
        : key == Key.Enter.WithCtrl ? Post()
        : key == Key.Tab ? SwitchPane(+1)
        : key == Key.Tab.WithShift && SwitchPane(-1);

    private bool Reading(Key key)
    {
        if (key == Key.Esc)
            return Escape();
        if (_ranks is not null && key == Key.Enter)
            return Set();
        if (key == new Key('h') && _ranks is null)
            return ToggleHistory();
        if (key == Key.Tab)
            return SwitchPane(+1);
        if (key == Key.Tab.WithShift)
            return SwitchPane(-1);
        if (CommentShown && key == Key.Enter.WithCtrl)
            return Post();
        if (_ranks is null && Caret(key) is { } move)
            return MoveCaret(move, key.IsShift);
        if (key == Key.C.WithCtrl)
            return Copy();
        if (_comment is not null && key == new Key('q') && FocusedPane().Marked > 0)
            return Quote();
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
        return _ranks is not null && Scroll(key) is { } scroll && Scrolled(scroll);
    }

    private void ShowHints()
    {
        ShowCarets();
        var marked = FocusedPane().Marked;
        var reading = !_field.HasFocus && marked > 0;
        _hints.Show("", [
            .. _ranks is null ? Array.Empty<HintedCommand>() : [new HintedCommand(ScrollHint, "Up/Down/PgUp/PgDn scroll")],
            .. CommentShown || HistoryShown ? [new HintedCommand(SwitchHint, "Tab switch pane")] : Array.Empty<HintedCommand>(),
            .. _ranks is not null || CommentShown ? Array.Empty<HintedCommand>()
                : [new HintedCommand(HistoryHint, HistoryShown ? "h hide history" : "h show history")],
            .. _ranks is null && !reading ? [new HintedCommand(SelectHint, "Shift+arrows select")] : Array.Empty<HintedCommand>(),
            .. reading ? [new HintedCommand(CopyHint, "Ctrl+C copy")] : Array.Empty<HintedCommand>(),
            .. reading && _comment is not null
                ? [new HintedCommand(QuoteHint, $"q quote {marked} {(marked == 1 ? "line" : "lines")}")]
                : Array.Empty<HintedCommand>(),
            .. _ranks is null ? Array.Empty<HintedCommand>()
                : [new HintedCommand(RankHint, "←/→ rank"), new HintedCommand(SetHint, "Enter set")],
            .. _try is null ? Array.Empty<HintedCommand>() : [new HintedCommand(TryHint, $"{KeyNames.Short(_try.Key)} try")],
            .. _onApprove is null ? Array.Empty<HintedCommand>() : [new HintedCommand(ApproveHint, "a approve")],
            .. _accept is null ? Array.Empty<HintedCommand>()
                : [new HintedCommand(AcceptHint, $"{KeyNames.Short(_accept.Key)} {_accept.Hint}", _accept.Enabled)],
            .. _comment is null ? Array.Empty<HintedCommand>()
                : CommentShown ? [new HintedCommand(PostHint, "Ctrl+Enter post")]
                : [new HintedCommand(CommentHint, $"{KeyNames.Short(_comment.Key)} {_comment.Hint}")],
            new HintedCommand(GitHubHint, "g on GitHub"),
            new HintedCommand(CloseHint, _field.HasFocus ? "Esc back" : marked > 0 ? "Esc clear" : "Esc close"),
        ], Run);
    }

    private void ShowCarets()
    {
        var reading = _ranks is null && !_field.HasFocus;
        _body.ShowsCaret = reading && _scrolled == _body;
        _history.ShowsCaret = reading && _scrolled == _history;
    }

    private void ShowHistory(bool shown)
    {
        _historyFrame.Visible = shown;
        _bodyFrame.Width = shown ? Dim.Fill(ReaderPanes.HistoryWidth) : Dim.Fill();
        if (!shown && _scrolled == _history)
            Scroll(_body);
        ShowHints();
        SetNeedsLayout();
        SetNeedsDraw();
    }

    private bool ToggleHistory()
    {
        _panes.HistoryShown = !_historyFrame.Visible;
        ShowHistory(_panes.HistoryShown);
        return true;
    }

    /// <summary>Comment, Body and History in turn, leaving out whichever isn't showing.</summary>
    private bool SwitchPane(int by)
    {
        List<View> panes = [.. CommentShown ? [_field] : Array.Empty<View>(), _body, .. HistoryShown ? [_history] : Array.Empty<View>()];
        var at = panes.IndexOf(_field.HasFocus ? _field : _scrolled);
        var next = panes[(at + by + panes.Count) % panes.Count];
        if (next == _field)
            ToComment();
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
        if (text.Length == 0)
            return true;
        var clipboard = _clipboard ?? App?.Clipboard;
        if (clipboard is { IsSupported: true } && clipboard.TrySetClipboardData(text))
            _message.Show(Copied(text), Schemes.Accent);
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
        if (!CommentShown)
        {
            Comment();
            Layout();
        }
        // The field's cursor is in wrapped rows, so a character typed and taken back finds it in the text.
        _field.InsertText(Marker);
        var at = _field.Text.IndexOf(Marker, StringComparison.Ordinal);
        _field.DeleteCharLeft();
        Type(Quote(lines, at > 0 && _field.Text[at - 1] != '\n'));
        pane.Unmark();
        ToComment();
        return true;
    }

    /// <summary>InsertText puts a line break after the cursor's line rather than at the cursor, so each is typed.</summary>
    private void Type(string text)
    {
        foreach (var (line, i) in text.Split('\n').Select((line, i) => (line, i)))
        {
            if (i > 0)
                _field.NewKeyDownEvent(Key.Enter);
            if (line.Length > 0)
                _field.InsertText(line);
        }
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

    private bool Approve() => Leave(() =>
    {
        _onApprove!();
        return Close();
    });

    /// <summary>One that can't go ahead only says why, so it leaves the comment alone.</summary>
    private bool Accept() => _accept!.Enabled ? Leave(AcceptNow) : AcceptNow();

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
        if (string.IsNullOrWhiteSpace(_field.Text))
        {
            _message.Show("Nothing to post: the comment is empty", Schemes.Error);
            SetNeedsLayout();
            return true;
        }
        _message.Show("Posting…", Schemes.Accent);
        SetNeedsLayout();
        var text = _field.Text;
        var posting = _posting = _comment!.Post(text);
        if (posting.IsCompleted)
        {
            Settle(posting, text);
            return true;
        }
        // Polled rather than Invoked: the reader opens inside a timer, and Invoke waits on that timer's lock.
        App?.AddTimeout(PollEvery, () =>
        {
            if (!posting.IsCompleted)
                return true;
            Settle(posting, text);
            return false;
        });
        return true;
    }

    /// <summary>A comment that didn't post leaves the pane open, with what you wrote still in it.</summary>
    private void Settle(Task<string?> done, string text)
    {
        _posting = null;
        var failure = done.Status == TaskStatus.RanToCompletion
            ? done.Result
            : done.Exception?.GetBaseException().Message ?? "the comment didn't post";
        if (failure is { Length: > 0 })
        {
            _message.Show(failure, Schemes.Error);
            SetNeedsLayout();
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
        ShowHints();
        _message.Show($"commented on #{_number}", Schemes.Accent);
        SetNeedsLayout();
        SetNeedsDraw();
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

    private bool Run(string hint)
    {
        var handled = hint switch
        {
            ScrollHint => Scrolled(() => FocusedPane().Page(+1)),
            SwitchHint => SwitchPane(+1),
            HistoryHint => ToggleHistory(),
            SelectHint => MoveCaret(CaretMove.Down, extend: true),
            CopyHint => Copy(),
            QuoteHint => Quote(),
            TryHint => Try(),
            ApproveHint => Approve(),
            AcceptHint => Accept(),
            CommentHint => Comment(),
            PostHint => Post(),
            RankHint => Step(+1),
            SetHint => Set(),
            GitHubHint => OnGitHub(),
            _ => _field.HasFocus ? Back() : Escape(),
        };
        ShowHints();
        return handled;
    }
}
