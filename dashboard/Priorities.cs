using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;

namespace ATeam.Dashboard;

/// <summary>A Priority an item can carry, and <see cref="None"/> for carrying none.</summary>
public enum Rank
{
    None,
    Low,
    Medium,
    High,
    Urgent,
}

/// <summary>How much of a card is its issue number, and the scheme to draw that number in.</summary>
public readonly record struct PriorityMark(int Width, string Scheme);

/// <summary>A card's issue number wears its item's Priority, in the colours GitHub gives that field's
/// own options.</summary>
public static class Priorities
{
    private static readonly (Rank Rank, string Colour)[] Levels =
    [
        (Rank.Low, "#21833d"),
        (Rank.Medium, "#9a6700"),
        (Rank.High, "#d4323c"),
        (Rank.Urgent, "#c1408d"),
    ];

    internal static void Register(Scheme cards, Scheme form)
    {
        foreach (var (rank, colour) in Levels)
        {
            var ink = new Color(colour);
            SchemeManager.AddScheme(Scheme(rank.ToString()), cards with
            {
                Normal = cards.Normal with { Foreground = ink },
            });
            SchemeManager.AddScheme(FormScheme(rank.ToString()), form with
            {
                Normal = form.Normal with { Foreground = ink },
                HotNormal = form.HotNormal with { Foreground = ink },
            });
        }
    }

    /// <summary>The scheme a Priority is drawn in, or none for an item that has no Priority set.</summary>
    public static string Scheme(string priority) =>
        Array.Exists(Levels, level => level.Rank.ToString() == priority) ? $"Priority.{priority}" : "";

    /// <summary>The same colour on a form band, whose background isn't the cards'.</summary>
    public static string FormScheme(string priority) =>
        Scheme(priority) is { Length: > 0 } scheme ? $"{scheme}.{LogSchemes.Form}" : "";

    /// <summary>What the board calls <paramref name="rank"/>, which for <see cref="Rank.None"/> is what
    /// clears the field.</summary>
    public static string Value(Rank rank) => rank == Rank.None ? "none" : rank.ToString();

    /// <summary>The Priority <paramref name="item"/> carries, or <see cref="Rank.None"/> where it carries
    /// one we don't colour.</summary>
    public static Rank Of(WaitingItem item) =>
        Scheme(item.Priority).Length > 0 ? Enum.Parse<Rank>(item.Priority) : Rank.None;

    /// <summary>The part of <paramref name="card"/> that is the item's number, which a column too narrow
    /// to draw the whole of it cuts short.</summary>
    public static PriorityMark Mark(WaitingItem item, string card)
    {
        var scheme = Scheme(item.Priority);
        if (scheme.Length == 0)
            return default;
        var number = $"#{item.Number}";
        var width = 0;
        while (width < number.Length && width < card.Length && number[width] == card[width])
            width++;
        return new PriorityMark(width, scheme);
    }
}
