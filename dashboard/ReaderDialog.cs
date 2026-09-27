using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace ATeam.Dashboard;

/// <summary>An item's body as it was written, to read without leaving the board.</summary>
public sealed class ReaderDialog : Dialog
{
    private const string ScrollHint = "scroll";
    private const string GitHubHint = "github";
    private const string CloseHint = "close";
    private const int Inset = 1;

    private readonly Action _onGitHub;
    private readonly LogView _body;
    private readonly StatusBar _hints = new();

    public ReaderDialog(WaitingItem item, IssueBody body, Action onGitHub)
    {
        _onGitHub = onGitHub;
        Title = $"#{item.Number}  {item.Title}";
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();

        int HintRow() => Math.Max(0, Viewport.Height - 1);

        _body = new LogView
        {
            X = Inset,
            Y = 0,
            Width = Dim.Fill(Inset),
            Height = Dim.Func(_ => HintRow(), this),
            CanFocus = true,
            Following = false,
            Scrolls = true,
            Lines = body.Lines,
        };
        _hints.Y = Pos.Func(_ => HintRow(), this);
        _hints.Show("", [
            new HintedCommand(ScrollHint, "Up/Down/PgUp/PgDn scroll"),
            new HintedCommand(GitHubHint, "o on GitHub"),
            new HintedCommand(CloseHint, "Esc close"),
        ], Run);

        Add(_body, _hints);
        _body.SetFocus();
    }

    internal LogView Body => _body;

    internal StatusBar Hints => _hints;

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Esc)
            return Close();
        if (key == new Key('o'))
            return OnGitHub();
        return Scroll(key) is { } scroll ? Scrolled(scroll) : base.OnKeyDown(key);
    }

    public static void Show(IApplication app, WaitingItem item, IssueBody body, Action onGitHub)
    {
        using var dialog = new ReaderDialog(item, body, onGitHub);
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

    private bool Close()
    {
        RequestStop();
        return true;
    }

    private bool Run(string hint) => hint switch
    {
        ScrollHint => Scrolled(() => _body.Page(+1)),
        GitHubHint => OnGitHub(),
        _ => Close(),
    };
}
