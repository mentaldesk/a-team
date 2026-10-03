using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace ATeam.Dashboard;

/// <summary>A comment to post on an item as you, written without leaving the reader.</summary>
public sealed class CommentDialog : Dialog
{
    private const string PostHint = "post";
    private const string CancelHint = "cancel";
    private const int Inset = 1;
    private static readonly TimeSpan PollEvery = TimeSpan.FromMilliseconds(100);

    private readonly Func<string, Task<string?>> _post;
    private readonly TextView _field;
    private readonly StatusBar _hints = new();
    private readonly MessageBar _message = new();
    private Task<string?>? _posting;

    /// <param name="post">Posts the comment, with null once it's posted or else why it wasn't.</param>
    public CommentDialog(WaitingItem item, Func<string, Task<string?>> post)
    {
        _post = post;
        Title = $"Comment on #{item.Number}";
        X = Pos.Center();
        Y = Pos.Center();
        Width = Dim.Percent(80);
        Height = Dim.Percent(60);

        int HintRow() => Math.Max(0, Viewport.Height - 1 - _message.Lines);

        _field = new TextView
        {
            X = Inset,
            Y = 0,
            Width = Dim.Fill(Inset),
            Height = Dim.Func(_ => HintRow(), this),
            WordWrap = true,
            TabKeyAddsTab = false,
        };
        // The field binds Enter itself, so Ctrl+Enter and Esc are caught before it sees them.
        _field.KeyDown += (_, key) => key.Handled = Keyed(key);
        _hints.Y = Pos.Func(_ => HintRow(), this);
        _hints.Show("", [
            new HintedCommand(PostHint, "Ctrl+Enter post"),
            new HintedCommand(CancelHint, "Esc cancel"),
        ], Run);
        _message.Y = Pos.Func(_ => Math.Max(0, Viewport.Height - _message.Lines), this);

        Add(_field, _hints, _message);
        _field.SetFocus();
    }

    internal bool Posted { get; private set; }

    internal TextView Field => _field;

    internal StatusBar Hints => _hints;

    internal MessageBar Message => _message;

    protected override bool OnKeyDown(Key key) => Keyed(key) || base.OnKeyDown(key);

    /// <summary>Asks for a comment and posts it, and returns what was posted, or null if nothing was.</summary>
    public static string? Show(IApplication app, WaitingItem item, Func<string, Task<string?>> post)
    {
        using var dialog = new CommentDialog(item, post);
        app.Run(dialog);
        return dialog.Posted ? dialog._field.Text : null;
    }

    private bool Keyed(Key key) => key == Key.Enter.WithCtrl ? Post() : key == Key.Esc && Cancel();

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
        var posting = _posting = _post(_field.Text);
        if (posting.IsCompleted)
        {
            Settle(posting);
            return true;
        }
        // Polled rather than Invoked: the reader opens inside a timer, and Invoke waits on that timer's lock.
        App?.AddTimeout(PollEvery, () =>
        {
            if (!posting.IsCompleted)
                return true;
            Settle(posting);
            return false;
        });
        return true;
    }

    /// <summary>A comment that didn't post leaves the dialog open, with what you wrote still in it.</summary>
    private void Settle(Task<string?> done)
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
        Posted = true;
        RequestStop();
    }

    private bool Cancel()
    {
        if (_posting is null)
            RequestStop();
        return true;
    }

    private bool Run(string hint) => hint == PostHint ? Post() : Cancel();
}
