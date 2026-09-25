using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace ATeam.Dashboard;

/// <summary>The rank to give an item: the Priority field's own options, and None to clear it.</summary>
public sealed class PriorityDialog : Dialog
{
    private const string SetHint = "Enter set";
    private const string CancelHint = "Esc cancel";
    private const string Separator = " · ";
    private const int Inset = 1;
    private const int OptionAndSpace = 4;
    private const int RanksRow = 2;

    private readonly OptionSelector<Rank> _ranks;

    public PriorityDialog(WaitingItem item)
    {
        var number = $"#{item.Number}";
        var carried = Priorities.Of(item);
        var wide = Math.Max(
            Math.Max(number.Length, Enum.GetNames<Rank>().Max(name => name.Length) + OptionAndSpace),
            SetHint.Length + Separator.Length + CancelHint.Length) + (Inset * 2);
        var tall = RanksRow + Enum.GetValues<Rank>().Length + 2;

        Title = "Priority";
        Width = Dim.Func(_ => Fits(wide + GetAdornmentsThickness().Horizontal, SuperView?.Viewport.Width), this);
        Height = Dim.Func(_ => Fits(tall + GetAdornmentsThickness().Vertical, SuperView?.Viewport.Height), this);

        _ranks = new OptionSelector<Rank>
        {
            X = Inset,
            Y = RanksRow,
            Orientation = Orientation.Vertical,
            Value = carried,
        };
        foreach (var (row, rank) in _ranks.SubViews.Zip(Enum.GetValues<Rank>()))
            if (Priorities.Scheme(rank.ToString()) is { Length: > 0 } scheme)
                row.SchemeName = scheme;

        Add(new Label { X = Inset, Y = 0, Text = number, CanFocus = false }, _ranks);
        Pos x = Inset;
        foreach (var hint in Hints())
        {
            hint.X = x;
            x = Pos.Right(hint);
            Add(hint);
        }
        // SetFocus lands the keyboard on the first option, so the item's own rank is put under it after.
        _ranks.SetFocus();
        _ranks.FocusedItem = (int)carried;
    }

    /// <summary>The rank the dialog was accepted on, or null where it was cancelled.</summary>
    internal Rank? Chosen { get; private set; }

    internal OptionSelector<Rank> Ranks => _ranks;

    /// <summary>Enter reaches a Dialog as Accept, from the options themselves, and never as a key.</summary>
    protected override bool OnAccepting(CommandEventArgs args) => Close(_ranks.Value);

    protected override bool OnKeyDown(Key key) => key == Key.Esc ? Close(null) : base.OnKeyDown(key);

    /// <summary>Asks for a rank, starting on the one the item has, so opening it and pressing Esc changes
    /// nothing.</summary>
    public static Rank? Show(IApplication app, WaitingItem item)
    {
        using var dialog = new PriorityDialog(item);
        app.Run(dialog);
        return dialog.Chosen;
    }

    private static int Fits(int wanted, int? available) => available is { } room ? Math.Min(wanted, room) : wanted;

    private bool Close(Rank? chosen)
    {
        Chosen = chosen;
        RequestStop();
        return true;
    }

    /// <summary>The hint row, each hint clickable and the separator between them not.</summary>
    private IEnumerable<View> Hints()
    {
        yield return Hint(SetHint, () => Close(_ranks.Value));
        yield return new Label { Text = Separator, Y = Pos.AnchorEnd(1), CanFocus = false };
        yield return Hint(CancelHint, () => Close(null));
    }

    private static Button Hint(string text, Func<bool> run)
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
        hint.Accepting += (_, args) => args.Handled = run();
        return hint;
    }
}
