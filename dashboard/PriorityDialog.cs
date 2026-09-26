using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace ATeam.Dashboard;

/// <summary>What an item is about, and the rank to give it: the Priority field's own options, and None to
/// clear it.</summary>
public sealed class PriorityDialog : Dialog
{
    private const string ScrollHint = "scroll";
    private const string SetHint = "set";
    private const string CancelHint = "cancel";
    private const int BandLines = 3;
    private const int Inset = 1;

    private readonly View _band;
    private readonly OptionSelector<Rank> _ranks;
    private readonly LogView _body;
    private readonly StatusBar _hints = new();
    private readonly MessageBar _message = new();

    public PriorityDialog(WaitingItem item, IssueBody body)
    {
        var carried = Priorities.Of(item);

        Title = $"#{item.Number}  {item.Title}";
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();

        int HintRow() => Math.Max(0, Viewport.Height - 1 - _message.Lines);
        int BandRow() => Math.Max(0, HintRow() - BandLines);

        _body = new LogView
        {
            X = Inset,
            Y = 0,
            Width = Dim.Fill(Inset),
            Height = Dim.Func(_ => BandRow(), this),
            Following = false,
            Lines = body.Lines,
        };
        _ranks = new OptionSelector<Rank>
        {
            X = Pos.Center(),
            Y = 1,
            Orientation = Orientation.Horizontal,
            // NoStop is what makes the arrows move between the ranks; with the default the options are Tab stops.
            TabBehavior = TabBehavior.NoStop,
            // The ranks' initials are unique, so this is what gives each option the key its name starts with.
            AssignHotKeys = true,
            Value = carried,
        };
        foreach (var (row, rank) in _ranks.SubViews.Zip(Enum.GetValues<Rank>()))
            if (Priorities.FormScheme(rank.ToString()) is { Length: > 0 } scheme)
                row.SchemeName = scheme;
        _band = new View
        {
            X = 0,
            Y = Pos.Func(_ => BandRow(), this),
            Width = Dim.Fill(),
            Height = BandLines,
            // Without this the ranks can't take focus, whatever they say.
            CanFocus = true,
            // A Dialog only takes Accept from a child of its own, so the band has to pass Enter on.
            CommandsToBubbleUp = [Command.Accept],
            SchemeName = LogSchemes.Form,
        };
        _band.Add(_ranks);
        _hints.Y = Pos.Func(_ => HintRow(), this);
        _hints.Show("", [
            new HintedCommand(ScrollHint, "PgUp/PgDn scroll"),
            new HintedCommand(SetHint, "Enter set"),
            new HintedCommand(CancelHint, "Esc cancel"),
        ], Run);
        _message.Y = Pos.Func(_ => Math.Max(0, Viewport.Height - _message.Lines), this);

        Add(_body, _band, _hints, _message);
        if (body.Failure is { Length: > 0 } failure)
            _message.Show(failure, Schemes.Error);
        // SetFocus lands the keyboard on the first option, so the item's own rank is put under it after.
        _ranks.SetFocus();
        _ranks.FocusedItem = (int)carried;
    }

    /// <summary>The rank the dialog was accepted on, or null where it was cancelled.</summary>
    internal Rank? Chosen { get; private set; }

    internal View Band => _band;

    internal OptionSelector<Rank> Ranks => _ranks;

    internal LogView Body => _body;

    internal StatusBar Hints => _hints;

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

    private bool Run(string hint) => hint switch
    {
        ScrollHint => Scrolled(() => _body.Page(+1)),
        SetHint => Close(_ranks.Value),
        _ => Close(null),
    };
}
