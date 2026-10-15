using System.Text;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.Text;

namespace ATeam.Dashboard;

/// <summary>The window's last row: what the area is showing, then what's going on, and at its right end how to get
/// help.</summary>
public sealed class StatusBar : View
{
    internal const string Scheme = "StatusBar";
    private const string Separator = " · ";
    private const int HelpGap = 3;

    private readonly MessageBar _message = new() { Y = 0 };
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
        _stateDivider.X = Pos.Func(_ => _state.Text.GetColumns(), this);
        _stateDivider.Width = Dim.Func(_ => _state.Text.Length > 0 && _message.Lines > 0 ? Separator.Length : 0, this);
        _message.X = Pos.Func(_ => StateRoom(), this);
        _message.Width = Dim.Func(_ => Math.Max(0, Viewport.Width - Room() - StateRoom()), this);
        _help.X = Pos.AnchorEnd();
        _help.Width = Dim.Func(_ => _help.Text.GetColumns());
        _help.Accepting += (_, args) =>
        {
            args.Handled = true;
            _helpRun?.Invoke();
        };
        Add(_state, _stateDivider, _message, _help);
    }

    internal Label State => _state;

    internal Button HelpHint => _help;

    internal MessageBar Message => _message;

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

    /// <summary>After the state: what's selected, what's running or what just happened. Only an error takes a colour
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

    /// <summary>What the right end leaves the left, so a long hint bar or message is cut off rather than drawn over it.</summary>
    private int Room() => _help.Text.Length == 0 ? 0 : _help.Text.GetColumns() + HelpGap;
}
