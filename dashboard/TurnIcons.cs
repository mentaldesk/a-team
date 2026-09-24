using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace ATeam.Dashboard;

/// <summary>A card's glyph for whose move it is, and the scheme its colour comes from.</summary>
public readonly record struct TurnIcon(string Glyph, string Scheme);

/// <summary>The two glyphs a card wears: <c>nf-md-check_circle</c> where the move is the reviewer's,
/// <c>nf-md-face_agent</c> where it's an agent's, with plain equivalents for a terminal with no Nerd Font.</summary>
public static class TurnIcons
{
    /// <summary>The cells a card spends on its icon, the same either way so its text doesn't shift with the style.</summary>
    public const int Width = 2;

    private const string NerdMine = "\U000F05E0";
    private const string NerdTheirs = "\U000F0D70";
    private const string PlainMine = "✓";
    private const string PlainTheirs = "·";

    public static TurnIcon For(WaitingItem item, bool nerdFont) => item.Mine
        ? new TurnIcon(nerdFont ? NerdMine : PlainMine, LogSchemes.Success)
        : new TurnIcon(nerdFont ? NerdTheirs : PlainTheirs, LogSchemes.Dimmed);
}

/// <summary>Cards that wear their icon and their Priority: both drawn rather than put in the text, so a card's
/// text stays the item's own, in their own colours over the row's own background so the selection still reads.</summary>
internal sealed class CardSource(
    IReadOnlyList<string> cards, IReadOnlyList<TurnIcon> icons, IReadOnlyList<PriorityMark> priorities)
    : IListDataSource
{
    private readonly ListWrapper<string> _text = new(new ObservableCollection<string>(cards));

    public event NotifyCollectionChangedEventHandler? CollectionChanged
    {
        add => _text.CollectionChanged += value;
        remove => _text.CollectionChanged -= value;
    }

    public int Count => _text.Count;

    public int MaxItemLength => _text.MaxItemLength + TurnIcons.Width;

    public bool SuspendCollectionChangedEvent
    {
        get => _text.SuspendCollectionChangedEvent;
        set => _text.SuspendCollectionChangedEvent = value;
    }

    public void Render(ListView listView, bool selected, int item, int col, int row, int width, int viewportX = 0)
    {
        // Scrolled sideways, the icon would sit on top of the text; draw the text alone.
        if (viewportX > 0 || item < 0 || item >= icons.Count)
        {
            _text.Render(listView, selected, item, col, row, width, viewportX);
            return;
        }

        var attribute = listView.GetCurrentAttribute();
        listView.Move(col, row);
        listView.SetAttribute(Colour(icons[item].Scheme, attribute));
        listView.AddStr(icons[item].Glyph);
        listView.SetAttribute(attribute);
        listView.AddStr(" ");
        if (width <= TurnIcons.Width)
            return;
        _text.Render(listView, selected, item, col + TurnIcons.Width, row, width - TurnIcons.Width);
        Number(listView, item, col + TurnIcons.Width, row, attribute);
    }

    public bool IsMarked(int item) => _text.IsMarked(item);

    public void SetMark(int item, bool value) => _text.SetMark(item, value);

    public IList ToList() => _text.ToList();

    public void Dispose() => _text.Dispose();

    /// <summary>The scheme's own foreground over the row's background, so a selected card keeps its highlight.</summary>
    internal static Attribute Colour(string name, Attribute row) =>
        SchemeManager.TryGetScheme(name, out var scheme)
            ? new Attribute(scheme.GetAttributeForRole(VisualRole.Normal).Foreground, row.Background, row.Style)
            : row;

    /// <summary>The issue number over again in its Priority's colour, leaving the rest of the card as drawn.</summary>
    private void Number(ListView listView, int item, int col, int row, Attribute attribute)
    {
        if (item >= priorities.Count || priorities[item] is not { Width: > 0 } mark)
            return;
        listView.Move(col, row);
        listView.SetAttribute(Colour(mark.Scheme, attribute));
        listView.AddStr(cards[item][..mark.Width]);
        listView.SetAttribute(attribute);
    }
}
