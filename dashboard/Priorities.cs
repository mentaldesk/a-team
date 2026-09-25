using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace ATeam.Dashboard;

/// <summary>A Priority an item can carry, and <see cref="None"/> for carrying none.</summary>
public enum Rank
{
    Urgent,
    High,
    Medium,
    Low,
    None,
}

/// <summary>How much of a card is its issue number, and the scheme to draw that number in.</summary>
public readonly record struct PriorityMark(int Width, string Scheme);

/// <summary>A card's issue number wears its item's Priority, in the colours GitHub gives that field's
/// own options.</summary>
public static class Priorities
{
    private static readonly (Rank Rank, string Colour)[] Levels =
    [
        (Rank.Urgent, "#c1408d"),
        (Rank.High, "#d4323c"),
        (Rank.Medium, "#9a6700"),
        (Rank.Low, "#21833d"),
    ];

    internal static void Register(Scheme baseScheme)
    {
        foreach (var (rank, colour) in Levels)
            SchemeManager.AddScheme(Scheme(rank.ToString()), baseScheme with
            {
                Normal = new Attribute(new Color(colour), baseScheme.Normal.Background),
            });
    }

    /// <summary>The scheme a Priority is drawn in, or none for an item that has no Priority set.</summary>
    public static string Scheme(string priority) =>
        Array.Exists(Levels, level => level.Rank.ToString() == priority) ? $"Priority.{priority}" : "";

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
