using Terminal.Gui.Drawing;

namespace ATeam.Dashboard;

/// <summary>A row in a Work column: an item, or the PR that closes it hanging under it.</summary>
public sealed record Card(WaitingItem Item, bool IsPr)
{
    /// <summary>A card per item, each of them a root of its column's tree.</summary>
    internal static IReadOnlyList<Card> Roots(IEnumerable<WaitingItem> items) =>
        [.. items.Select(item => new Card(item, false))];

    /// <summary>Those roots and their PRs, in the order the tree draws them.</summary>
    internal static IReadOnlyList<Card> Nodes(IEnumerable<Card> roots) =>
        [.. roots.SelectMany(root => root.Children.Prepend(root))];

    /// <summary>The one PR under a card, where its item has one.</summary>
    internal IReadOnlyList<Card> Children => IsPr || Item.Pr == 0 ? [] : [this with { IsPr = true }];

    /// <summary>The page Enter opens on this row, or nothing where it has none.</summary>
    internal string Url => IsPr ? Item.PrUrl : Item.Url;

    /// <summary>What the row wears in front of its text: whose move the item is, or the line its PR hangs from.</summary>
    internal TurnIcon Lead(IconStyle style) => IsPr
        ? new TurnIcon(Glyphs.LLCorner.ToString(), LogSchemes.Dimmed)
        : Icons.For(Item, style);

    /// <summary>The Priority colour the row's number wears, which a PR's row hasn't got.</summary>
    internal PriorityMark Mark(int width) => IsPr ? default : Priorities.Mark(Item, Text(width));

    /// <summary>A card reads as the issue's number, whose move it is when it isn't the reviewer's, what its PR is
    /// in trouble over where that's why, then as much of its title as the column has room for. Its PR's row reads
    /// as the PR's own number and the same title.</summary>
    internal string Text(int width)
    {
        if (IsPr)
            return Elide($"PR #{Item.Pr}  {Item.Title}", width);
        var said = new[] { Item.Mine ? "" : Item.Turn, Item.Trouble, Item.Title }.Where(part => part.Length > 0);
        return Elide($"#{Item.Number}  {string.Join(" · ", said)}", width);
    }

    /// <summary>Cut a char earlier again rather than through a surrogate pair, which would leave half a rune.</summary>
    private static string Elide(string text, int width)
    {
        if (width <= 0 || text.Length <= width)
            return text;
        if (width == 1)
            return "…";
        var cut = width - 1;
        return string.Concat(text.AsSpan(0, char.IsHighSurrogate(text[cut - 1]) ? cut - 1 : cut), "…");
    }
}
