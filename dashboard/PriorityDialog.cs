using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace ATeam.Dashboard;

/// <summary>The rank to give an item: the Priority field's own options, and None to clear it. It asks about
/// one item at a time and never closes itself, so whoever opened it can walk a queue through it.</summary>
public sealed class PriorityDialog : Dialog
{
    private const string SetHint = "Enter set";
    private const string DoneHint = "Esc done";
    private const string CancelHint = "Esc cancel";
    private const string Separator = " · ";
    private const int Inset = 1;
    private const int OptionAndSpace = 4;
    private const int TitleChrome = 4;
    private const int RanksRow = 2;

    private readonly OptionSelector<Rank> _ranks;
    private readonly Label _number;
    private readonly Button _stop;

    public PriorityDialog()
    {
        Title = "Priority";
        Width = Dim.Func(_ => Fits(Wide() + GetAdornmentsThickness().Horizontal, SuperView?.Viewport.Width), this);
        Height = Dim.Func(_ => Fits(Tall() + GetAdornmentsThickness().Vertical, SuperView?.Viewport.Height), this);

        _ranks = new OptionSelector<Rank>
        {
            X = Inset,
            Y = RanksRow,
            Orientation = Orientation.Vertical,
        };
        foreach (var (row, rank) in _ranks.SubViews.Zip(Enum.GetValues<Rank>()))
            if (Priorities.Scheme(rank.ToString()) is { Length: > 0 } scheme)
                row.SchemeName = scheme;

        _number = new Label { X = Inset, Y = 0, CanFocus = false };
        _stop = Hint(CancelHint, () => Dismissed?.Invoke());
        Add(_number, _ranks);
        Pos x = Inset;
        var gap = new Label { Text = Separator, Y = Pos.AnchorEnd(1), CanFocus = false };
        foreach (View hint in new View[] { Hint(SetHint, Chose), gap, _stop })
        {
            hint.X = x;
            x = Pos.Right(hint);
            Add(hint);
        }
    }

    /// <summary>Raised on Enter, with the rank the keyboard is on.</summary>
    internal event Action<Rank>? Set;

    /// <summary>Raised on Esc, and by the hint beside it.</summary>
    internal event Action? Dismissed;

    internal OptionSelector<Rank> Ranks => _ranks;

    internal string Number => _number.Text;

    /// <summary>The hint row as it reads now.</summary>
    internal string Hints => $"{SetHint}{Separator}{_stop.Text}";

    /// <summary>Puts <paramref name="item"/> in front of the reviewer, with <paramref name="left"/> still to
    /// rank, this one among them. The keyboard lands on the rank the item carries, so Esc changes nothing.
    /// </summary>
    internal void Ask(WaitingItem item, int left)
    {
        var carried = Priorities.Of(item);
        Title = left > 1 ? $"Priority · {left} left" : "Priority";
        _number.Text = $"#{item.Number}";
        _stop.Text = left > 1 ? DoneHint : CancelHint;
        _ranks.Value = carried;
        // SetFocus lands the keyboard on the first option, so the item's own rank is put under it after.
        _ranks.SetFocus();
        _ranks.FocusedItem = (int)carried;
        SetNeedsLayout();
        SetNeedsDraw();
    }

    /// <summary>Takes it away again.</summary>
    internal void Finish() => RequestStop();

    /// <summary>Enter reaches a Dialog as Accept, from the options themselves, and never as a key.</summary>
    protected override bool OnAccepting(CommandEventArgs args)
    {
        Chose();
        return true;
    }

    protected override bool OnKeyDown(Key key)
    {
        if (key != Key.Esc)
            return base.OnKeyDown(key);
        Dismissed?.Invoke();
        return true;
    }

    private void Chose() => Set?.Invoke(_ranks.Value ?? Rank.None);

    private static int Fits(int wanted, int? available) => available is { } room ? Math.Min(wanted, room) : wanted;

    private static int Tall() => RanksRow + Enum.GetValues<Rank>().Length + 2;

    private int Wide() =>
        Math.Max(
            Math.Max(Title.Length + TitleChrome, _number.Text.Length),
            Math.Max(
                Enum.GetNames<Rank>().Max(name => name.Length) + OptionAndSpace,
                Hints.Length)) + (Inset * 2);

    /// <summary>One clickable hint on the bottom row.</summary>
    private static Button Hint(string text, Action run)
    {
        var hint = new Button
        {
            Text = text,
            Y = Pos.AnchorEnd(1),
            NoDecorations = true,
            NoPadding = true,
            ShadowStyle = ShadowStyles.None,
            HotKeySpecifier = (Rune)0xffff,
            CanFocus = false,
        };
        hint.Accepting += (_, args) =>
        {
            run();
            args.Handled = true;
        };
        return hint;
    }
}
