using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace ATeam.Dashboard;

/// <summary>A command the reader offers on its own key. <paramref name="Run"/> says whether the reader is done; one
/// that isn't <paramref name="Enabled"/> still runs, to say why not, and its hint is greyed.</summary>
public sealed record ReaderCommand(Key Key, string Hint, Func<bool> Run, bool Enabled = true);

/// <summary>Commenting from the reader: Run is the remark it posted, or null if nothing was.</summary>
public sealed record ReaderComment(Key Key, string Hint, Func<Remark?> Run);

/// <summary>An item's body as it was written, to read without leaving the board.</summary>
public sealed class ReaderDialog : Dialog
{
    private const string ScrollHint = "scroll";
    private const string ApproveHint = "approve";
    private const string AcceptHint = "accept";
    private const string CommentHint = "comment";
    private const string GitHubHint = "github";
    private const string CloseHint = "close";
    private const int Inset = 1;

    private readonly Action _onGitHub;
    private readonly Action? _onApprove;
    private readonly ReaderCommand? _accept;
    private readonly ReaderComment? _comment;
    private readonly int _number;
    private IssueBody _text;
    private readonly LogView _body;
    private readonly StatusBar _hints = new();
    private readonly MessageBar _message = new();

    /// <param name="onApprove">What <c>a</c> does, or null where there's nothing to approve.</param>
    /// <param name="accept">Merging the task's PR, or null where there's no task to accept.</param>
    /// <param name="comment">Commenting on the item; a posted comment joins the end of the conversation, and the
    /// reader stays open either way.</param>
    public ReaderDialog(
        WaitingItem item, IssueBody body, Action onGitHub, Action? onApprove = null, ReaderCommand? accept = null,
        ReaderComment? comment = null)
    {
        _onGitHub = onGitHub;
        _onApprove = onApprove;
        _accept = accept;
        _comment = comment;
        _number = item.Number;
        _text = body;
        Title = $"#{item.Number}  {item.Title}{(item.Question.Length == 0 ? "" : item.Pitch ? " · the Lead's question" : " · the Dev's question")}";
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();

        int HintRow() => Math.Max(0, Viewport.Height - 1 - _message.Lines);

        _body = new LogView
        {
            X = Inset,
            Y = 0,
            Width = Dim.Fill(Inset),
            Height = Dim.Func(_ => HintRow(), this),
            CanFocus = true,
            Following = false,
            Scrolls = true,
            ReadsMarkdown = true,
            SchemeName = LogSchemes.Reader,
            Lines = body.Lines,
        };
        _hints.Y = Pos.Func(_ => HintRow(), this);
        _hints.Show("", [
            new HintedCommand(ScrollHint, "Up/Down/PgUp/PgDn scroll"),
            .. onApprove is null ? Array.Empty<HintedCommand>() : [new HintedCommand(ApproveHint, "a approve")],
            .. accept is null ? Array.Empty<HintedCommand>()
                : [new HintedCommand(AcceptHint, $"{KeyNames.Short(accept.Key)} {accept.Hint}", accept.Enabled)],
            .. comment is null ? Array.Empty<HintedCommand>()
                : [new HintedCommand(CommentHint, $"{KeyNames.Short(comment.Key)} {comment.Hint}")],
            new HintedCommand(GitHubHint, "g on GitHub"),
            new HintedCommand(CloseHint, "Esc close"),
        ], Run);

        _message.Y = Pos.Func(_ => Math.Max(0, Viewport.Height - _message.Lines), this);

        Add(_body, _hints, _message);
        _body.SetFocus();
    }

    internal LogView Body => _body;

    internal StatusBar Hints => _hints;

    internal MessageBar Message => _message;

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Esc)
            return Close();
        if (key == new Key('g'))
            return OnGitHub();
        if (key == new Key('a') && _onApprove is not null)
            return Approve();
        if (_accept is not null && key == _accept.Key)
            return Accept();
        if (_comment is not null && key == _comment.Key)
            return Comment();
        return Scroll(key) is { } scroll ? Scrolled(scroll) : base.OnKeyDown(key);
    }

    public static void Show(
        IApplication app, WaitingItem item, IssueBody body, Action onGitHub, Action? onApprove, ReaderCommand? accept,
        ReaderComment? comment)
    {
        using var dialog = new ReaderDialog(item, body, onGitHub, onApprove, accept, comment);
        app.Run(dialog);
    }

    private bool Scrolled(Action scroll)
    {
        scroll();
        SetNeedsDraw();
        return true;
    }

    private Action? Scroll(Key key) =>
        key == Key.CursorUp ? () => _body.Step(-1)
        : key == Key.CursorDown ? () => _body.Step(+1)
        : key == Key.PageUp ? () => _body.Page(-1)
        : key == Key.PageDown ? () => _body.Page(+1)
        : key == Key.Home ? _body.Home
        : key == Key.End ? _body.End
        : null;

    private bool OnGitHub()
    {
        _onGitHub();
        return true;
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
            _message.Show($"commented on #{_number}", Schemes.Accent);
            SetNeedsLayout();
            SetNeedsDraw();
        }
        return true;
    }

    private bool Close()
    {
        RequestStop();
        return true;
    }

    private bool Run(string hint) => hint switch
    {
        ScrollHint => Scrolled(() => _body.Page(+1)),
        ApproveHint => Approve(),
        AcceptHint => Accept(),
        CommentHint => Comment(),
        GitHubHint => OnGitHub(),
        _ => Close(),
    };
}
