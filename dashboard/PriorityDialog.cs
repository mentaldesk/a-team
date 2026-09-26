using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace ATeam.Dashboard;

/// <summary>What an item is about, and the rank to give it: the Priority field's own options, and None to
/// clear it.</summary>
public sealed class PriorityDialog : Dialog
{
    private const string ScrollHint = "PgUp/PgDn scroll";
    private const string SetHint = "Enter set";
    private const string CancelHint = "Esc cancel";
    private const string Separator = " · ";
    private const int Inset = 1;

    private readonly OptionSelector<Rank> _ranks;
    private readonly LogView _body;
    private readonly MessageBar _message = new();

    public PriorityDialog(WaitingItem item, IssueBody body)
    {
        var carried = Priorities.Of(item);
        var ranks = Enum.GetValues<Rank>().Length;

        Title = $"#{item.Number}  {item.Title}";
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();

        int HintRow() => Math.Max(0, Viewport.Height - 1 - _message.Lines);
        int RanksRow() => Math.Max(0, HintRow() - ranks);

        _body = new LogView
        {
            X = Inset,
            Y = 0,
            Width = Dim.Fill(Inset),
            Height = Dim.Func(_ => RanksRow(), this),
            Following = false,
            Lines = body.Lines,
        };
        _ranks = new OptionSelector<Rank>
        {
            X = Inset,
            Y = Pos.Func(_ => RanksRow(), this),
            Orientation = Orientation.Vertical,
            // NoStop is what makes Up/Down move between the ranks; with the default the options are Tab stops.
            TabBehavior = TabBehavior.NoStop,
            Value = carried,
        };
        foreach (var (row, rank) in _ranks.SubViews.Zip(Enum.GetValues<Rank>()))
            if (Priorities.Scheme(rank.ToString()) is { Length: > 0 } scheme)
                row.SchemeName = scheme;
        _message.Y = Pos.Func(_ => Math.Max(0, Viewport.Height - _message.Lines), this);

        Add(_body, _ranks);
        Pos x = Inset;
        foreach (var hint in Hints(Pos.Func(_ => HintRow(), this)))
        {
            hint.X = x;
            x = Pos.Right(hint);
            Add(hint);
        }
        Add(_message);
        if (body.Failure is { Length: > 0 } failure)
            _message.Show(failure, Schemes.Error);
        // SetFocus lands the keyboard on the first option, so the item's own rank is put under it after.
        _ranks.SetFocus();
        _ranks.FocusedItem = (int)carried;
    }

    /// <summary>The rank the dialog was accepted on, or null where it was cancelled.</summary>
    internal Rank? Chosen { get; private set; }

    internal OptionSelector<Rank> Ranks => _ranks;

    internal LogView Body => _body;

    internal MessageBar Message => _message;

    /// <summary>Enter reaches a Dialog as Accept, from the options themselves, and never as a key.</summary>
    protected override bool OnAccepting(CommandEventArgs args) => Close(_ranks.Value);

    /// <summary>The pane never takes focus, so the keys that scroll it are the dialog's own.</summary>
    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Esc)
            return Close(null);
        return Scroll(key) is { } scroll ? Scrolled(scroll) : base.OnKeyDown(key);
    }

    /// <summary>Asks for a rank, starting on the one the item has, so opening it and pressing Esc changes
    /// nothing.</summary>
    public static Rank? Show(IApplication app, WaitingItem item, IssueBody body)
    {
        using var dialog = new PriorityDialog(item, body);
        app.Run(dialog);
        return dialog.Chosen;
    }

    private bool Scrolled(Action scroll)
    {
        scroll();
        SetNeedsDraw();
        return true;
    }

    private Action? Scroll(Key key) =>
        key == Key.PageUp ? () => _body.Page(-1)
        : key == Key.PageDown ? () => _body.Page(+1)
        : key == Key.Home ? _body.Home
        : key == Key.End ? _body.End
        : null;

    private bool Close(Rank? chosen)
    {
        Chosen = chosen;
        RequestStop();
        return true;
    }

    /// <summary>The hint row, each hint clickable and the separator between them not.</summary>
    private IEnumerable<View> Hints(Pos y)
    {
        yield return Hint(ScrollHint, y, () => Scrolled(() => _body.Page(+1)));
        yield return new Label { Text = Separator, Y = y, CanFocus = false };
        yield return Hint(SetHint, y, () => Close(_ranks.Value));
        yield return new Label { Text = Separator, Y = y, CanFocus = false };
        yield return Hint(CancelHint, y, () => Close(null));
    }

    private static Button Hint(string text, Pos y, Func<bool> run)
    {
        var hint = new Button
        {
            Text = text,
            Y = y,
            NoDecorations = true,
            NoPadding = true,
            ShadowStyle = ShadowStyles.None,
            HotKeySpecifier = (Rune)0xffff,
            CanFocus = false,
        };
        hint.Accepting += (_, args) => args.Handled = run();
        return hint;
    }
}
