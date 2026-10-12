using System.Text;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.Text;

namespace ATeam.Dashboard;

/// <summary>The last row of a window or a dialog: what the area is showing, the keys you can press, each one
/// clickable, then what's going on, and at its right end how to get help. Terminal.Gui's own StatusBar lays out Shortcuts with a border between each, so the hints are laid
/// out here instead.</summary>
public sealed class StatusBar : View
{
    internal const string Scheme = "StatusBar";
    private const string Separator = " · ";
    private const int HelpGap = 3;

    private readonly View _hints;
    private readonly MessageBar _message = new() { Y = 0 };
    private readonly Label _divider = new() { Text = Separator, Y = 0, CanFocus = false };
    private readonly Label _state = new() { X = 0, Y = 0, CanFocus = false };
    private readonly Label _stateDivider = new() { Text = Separator, Y = 0, CanFocus = false };
    private readonly Button _help = Clickable("");
    private Action? _helpRun;

    public StatusBar()
    {
        X = 0;
        Width = Dim.Fill();
        Height = 1;
        CanFocus = false;
        SchemeName = Scheme;
        _hints = new View
        {
            X = Pos.Func(_ => StateRoom(), this),
            Y = 0,
            Width = Dim.Func(_ => Math.Min(Says.GetColumns(), Math.Max(0, Viewport.Width - Room() - StateRoom())), this),
            Height = 1,
            CanFocus = false,
        };
        _stateDivider.X = Pos.Func(_ => _state.Text.GetColumns(), this);
        _stateDivider.Width = Dim.Func(_ => _state.Text.Length > 0 && (Says.Length > 0 || _message.Lines > 0) ? Separator.Length : 0, this);
        _divider.X = Pos.Func(_ => StateRoom() + Says.GetColumns(), this);
        _divider.Width = Dim.Func(_ => Says.Length > 0 && _message.Lines > 0 ? Separator.Length : 0, this);
        _message.X = Pos.Func(_ => Lead(), this);
        _message.Width = Dim.Func(_ => Math.Max(0, Viewport.Width - Room() - Lead()), this);
        _help.X = Pos.AnchorEnd();
        _help.Width = Dim.Func(_ => _help.Text.GetColumns());
        _help.Accepting += (_, args) =>
        {
            args.Handled = true;
            _helpRun?.Invoke();
        };
        Add(_state, _stateDivider, _hints, _divider, _message, _help);
    }

    /// <summary>Each hint, as it's drawn from the left.</summary>
    internal string Says => string.Concat(_hints.SubViews.Select(view => view.Text));

    internal Label State => _state;

    internal Button HelpHint => _help;

    internal MessageBar Message => _message;

    internal IReadOnlyList<Button> Hints => [.. _hints.SubViews.OfType<Button>()];

    /// <summary>The keys for the mode the window is in now. Clicking a hint runs the command it names; where several
    /// commands share a hint, like the scroll keys, it runs the first of them.</summary>
    public void Show(IReadOnlyList<HintedCommand> hints, Func<string, bool> run)
    {
        foreach (var view in _hints.SubViews.ToArray())
        {
            _hints.Remove(view);
            view.Dispose();
        }
        Pos x = 0;
        foreach (var hint in hints)
        {
            if (_hints.SubViews.Count > 0)
            {
                var separator = new Label { Text = Separator, X = x, Y = 0, CanFocus = false };
                _hints.Add(separator);
                x = Pos.Right(separator);
            }
            var button = Clickable(hint.Text);
            button.X = x;
            button.Enabled = hint.Enabled;
            button.Accepting += (_, args) => args.Handled = run(hint.Id);
            _hints.Add(button);
            x = Pos.Right(button);
        }
        SetNeedsLayout();
        SetNeedsDraw();
    }

    /// <summary>The far right: how to get help, which runs <paramref name="run"/> when clicked; empty hides it.</summary>
    public void Help(string text, Action run)
    {
        _helpRun = run;
        if (_help.Text == text)
            return;
        _help.Text = text;
        SetNeedsLayout();
        SetNeedsDraw();
    }

    private static Button Clickable(string text) => new()
    {
        Text = text,
        Y = 0,
        NoDecorations = true,
        NoPadding = true,
        ShadowStyle = ShadowStyles.None,
        HotKeySpecifier = (Rune)0xffff,
        CanFocus = false,
    };

    /// <summary>After the hints: what's selected, what's running or what just happened. Only an error takes a colour
    /// of its own.</summary>
    public void Say(string message, Schemes scheme)
    {
        _message.Show(message, scheme);
        if (scheme != Schemes.Error)
            _message.SchemeName = Scheme;
    }

    /// <summary>The left end: each of <paramref name="parts"/> that says something, such as what the area is filtered
    /// to and when it was last read.</summary>
    public void ShowState(params string[] parts)
    {
        var state = string.Join(Separator, parts.Where(part => part.Length > 0));
        if (_state.Text == state)
            return;
        _state.Text = state;
        SetNeedsLayout();
        SetNeedsDraw();
    }

    private int StateRoom() => _state.Text.Length == 0 ? 0 : _state.Text.GetColumns() + Separator.Length;

    private int Lead() => StateRoom() + (Says.Length == 0 ? 0 : Says.GetColumns() + Separator.Length);

    /// <summary>What the right end leaves the left, so a long hint bar or message is cut off rather than drawn over it.</summary>
    private int Room() => _help.Text.Length == 0 ? 0 : _help.Text.GetColumns() + HelpGap;
}
