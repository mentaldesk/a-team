using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace ATeam.Dashboard;

/// <summary>What an item is about, and the rank to give it: the Priority field's own options, and None to
/// clear it. It asks about one item at a time and never closes itself, so whoever opened it can walk a queue
/// through it.</summary>
public sealed class PriorityDialog : Dialog
{
    private const string ScrollHint = "scroll";
    private const string SetHint = "set";
    private const string StopHint = "stop";
    private const string DoneText = "Esc done";
    private const string CancelText = "Esc cancel";
    private const int BandLines = 3;
    private const int Inset = 1;

    private readonly View _band;
    private readonly OptionSelector<Rank> _ranks;
    private readonly LogView _body;
    private readonly LoadingView _loading;
    private readonly StatusBar _hints = new();
    private readonly MessageBar _message = new();
    private string _stop = CancelText;
    private bool _busy;

    public PriorityDialog()
    {
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
            Scrolls = true,
        };
        _loading = new LoadingView
        {
            X = Inset,
            Y = 0,
            Width = Dim.Fill(Inset),
            Height = Dim.Func(_ => BandRow(), this),
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
        _message.Y = Pos.Func(_ => Math.Max(0, Viewport.Height - _message.Lines), this);

        Add(_body, _loading, _band, _hints, _message);
    }

    /// <summary>Raised on Enter, with the rank the keyboard is on.</summary>
    internal event Action<Rank>? Set;

    /// <summary>Raised on Esc, and by the hint beside it.</summary>
    internal event Action? Dismissed;

    internal View Band => _band;

    internal OptionSelector<Rank> Ranks => _ranks;

    internal LogView Body => _body;

    internal LoadingView Loading => _loading;

    internal StatusBar Hints => _hints;

    internal MessageBar Message => _message;

    /// <summary>Puts <paramref name="item"/> in front of the reviewer, with what it's about and
    /// <paramref name="left"/> still to rank, this one among them. The keyboard lands on the rank the item
    /// carries, so Esc changes nothing.</summary>
    internal void Ask(WaitingItem item, IssueBody body, int left)
    {
        var carried = Priorities.Of(item);
        var named = $"#{item.Number}  {item.Title}";
        Title = left > 1 ? $"{named} · {left} left" : named;
        _loading.Stop();
        _body.Visible = true;
        _body.Show(body.Lines);
        if (body.Failure is { Length: > 0 } failure)
            _message.Show(failure, Schemes.Error);
        else
            _message.Clear();
        _busy = false;
        _stop = left > 1 ? DoneText : CancelText;
        Say();
        _ranks.Enabled = true;
        _ranks.Value = carried;
        // SetFocus lands the keyboard on the first option, so the item's own rank is put under it after.
        _ranks.SetFocus();
        _ranks.FocusedItem = (int)carried;
        SetNeedsLayout();
        SetNeedsDraw();
    }

    /// <summary>Says what's running and refuses a second Enter until it's done: what the reviewer chose is
    /// already on its way to the board. The body it replaces belongs to the item just ranked, so the van takes
    /// its place until the next one is read. Esc still stops it.</summary>
    internal void Busy(string what)
    {
        _busy = true;
        _ranks.Enabled = false;
        _body.Visible = false;
        _loading.Start();
        _message.Show(what, Schemes.Accent);
        Say();
        SetNeedsLayout();
        SetNeedsDraw();
    }

    /// <summary>Takes it away again.</summary>
    internal void Finish()
    {
        _loading.Stop();
        RequestStop();
    }

    /// <summary>Enter reaches a Dialog as Accept, from the options themselves, and never as a key.</summary>
    protected override bool OnAccepting(CommandEventArgs args) => Chose();

    /// <summary>The pane never takes focus, so the keys that scroll it are the dialog's own.</summary>
    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Esc)
            return Stopped();
        return !_busy && Scroll(key) is { } scroll ? Scrolled(scroll) : base.OnKeyDown(key);
    }

    private void Say()
    {
        List<HintedCommand> hints = [];
        if (!_busy)
        {
            hints.Add(new(ScrollHint, "PgUp/PgDn scroll"));
            hints.Add(new(SetHint, "Enter set"));
        }
        hints.Add(new(StopHint, _stop));
        _hints.Show("", hints, Run);
    }

    private bool Chose()
    {
        if (!_busy)
            Set?.Invoke(_ranks.Value ?? Rank.None);
        return true;
    }

    private bool Stopped()
    {
        Dismissed?.Invoke();
        return true;
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

    private bool Run(string hint) => hint switch
    {
        ScrollHint => Scrolled(() => _body.Page(+1)),
        SetHint => Chose(),
        _ => Stopped(),
    };
}
