using Terminal.Gui.Drawing;

namespace ATeam.Dashboard;

/// <summary>A row in a Work column: an item, or the PR that closes it hanging under it. <paramref name="Column"/> is
/// the kind the column's heading already wears, which a card of that kind doesn't repeat. <paramref name="New"/> marks
/// one that has become your move since the app started, and <paramref name="Writing"/> one with a board write under way
/// or queued. <paramref name="ReasonWidth"/> is what a stuck Dependabot PR's reason wraps to, and <paramref name="Line"/>
/// which of its rows this is.</summary>
public sealed record Card(WaitingItem Item, bool IsPr, Icon? Column = null, bool New = false, bool Writing = false, int ReasonWidth = 0, int Line = 0)
{
    internal const string NewMark = "● ";

    /// <summary>A card per item, each of them a root of its column's tree.</summary>
    internal static IReadOnlyList<Card> Roots(IEnumerable<WaitingItem> items, Icon? column = null, Func<WaitingItem, bool>? isNew = null,
        Func<WaitingItem, bool>? isWriting = null, int reasonWidth = 0) =>
        [.. items.Select(item => new Card(item, false, column, isNew?.Invoke(item) ?? false, isWriting?.Invoke(item) ?? false, reasonWidth))];

    /// <summary>Those roots and their PRs, in the order the tree draws them.</summary>
    internal static IReadOnlyList<Card> Nodes(IEnumerable<Card> roots) =>
        [.. roots.SelectMany(root => root.Children.Prepend(root))];

    /// <summary>The one PR under a card, where its item has one, or the rows of why the Dev can't make a Dependabot PR build.</summary>
    internal IReadOnlyList<Card> Children =>
        IsPr ? []
        : StuckBump ? [.. Reason.Select((_, line) => this with { IsPr = true, Line = line })]
        : Item.Pr != 0 && Item.Pr != Item.Number ? [this with { IsPr = true }]
        : [];

    internal bool StuckBump => Item.Dependency && Item.Held && !Item.GaveUp;

    /// <summary>The reason in rows, a word too long for one, like a URL, broken across them so none of it is lost.</summary>
    private List<string> Reason => ReasonWidth <= 0
        ? [$"dev · {Item.Asked}"]
        : [.. Confirm.Wrap($"dev · {Item.Asked}", ReasonWidth).SelectMany(row => row.Chunk(ReasonWidth).Select(cells => new string(cells)))];

    /// <summary>The page Enter opens on this row, or nothing where it has none.</summary>
    internal string Url => IsPr ? Item.PrUrl : Item.Url;

    /// <summary>What the row wears in front of its text: whose move the item is, that it's being written, or the line
    /// its PR hangs from, which a reason's later rows don't repeat.</summary>
    internal TurnIcon Lead(IconStyle style) => IsPr
        ? new TurnIcon(Line == 0 ? Glyphs.LLCorner.ToString() : " ", LogSchemes.Dimmed)
        : Writing ? new TurnIcon(Icons.Glyph(Icon.Writing, style), LogSchemes.Dimmed)
        : Icons.For(Item, style);

    /// <summary>Every icon in front of the row's text: a card's lead is followed by what kind of thing it is.</summary>
    internal IReadOnlyList<TurnIcon> Leads(IconStyle style) =>
        WearsKind ? [Lead(style), Icons.Kind(Item, style)] : [Lead(style)];

    /// <summary>The cells those icons take.</summary>
    internal int LeadWidth => Icons.Width * (WearsKind ? 2 : 1);

    private bool WearsKind => !IsPr && Icons.KindOf(Item) != Column;

    /// <summary>The Priority colour the row's number wears, which a PR's row hasn't got.</summary>
    internal PriorityMark Mark(int width) => IsPr ? default : Priorities.Mark(Item, Text(width));

    /// <summary>A card reads as the issue's number, the task it frees or the link it waits on, whose move it is when it
    /// isn't the reviewer's, the rank the Lead recommends, what its PR is in trouble over where that's why, then as much of its title as the column has room for. Its PR's row reads
    /// as the PR's own number and the same title.</summary>
    internal string Text(int width, IconStyle style = IconStyle.Unicode)
    {
        if (IsPr && StuckBump)
            return Elide(Reason[Line], width);
        if (IsPr)
            return Elide($"{Icons.Glyph(Icon.PullRequest, style)} #{Item.Pr}  {Item.Title}", width);
        var chained = Item.Holding.Count > 0 ? Holds.Frees(Item) : Item.WaitsOn;
        var number = StuckBump && chained.Length == 0 ? $"PR #{Item.Number}" : chained.Length == 0 ? $"#{Item.Number}" : $"#{Item.Number} {Icons.Glyph(Icon.Chain, style)} {chained}";
        var said = new[] { Item.Secret is { } secret ? $"{secret.By} · needs {secret.Name}" : Item.Mine || Item.WaitsUpstream ? "" : Item.Turn, Item.Suggested, Item.Trouble, Item.Docs ? "Customer lead" : "",
                Item.Fix ? "fix" : "", Item.Dependency && !StuckBump ? "Dependabot" : "", Item.Review,
                New ? NewMark + Item.Title : Item.Title }
            .Where(part => part.Length > 0);
        return Elide($"{number}  {string.Join(" · ", said)}", width);
    }

    /// <summary>Cut a char earlier again rather than through a surrogate pair, which would leave half a rune.</summary>
    internal static string Elide(string text, int width)
    {
        if (width <= 0 || text.Length <= width)
            return text;
        if (width == 1)
            return "…";
        var cut = width - 1;
        return string.Concat(text.AsSpan(0, char.IsHighSurrogate(text[cut - 1]) ? cut - 1 : cut), "…");
    }
}
