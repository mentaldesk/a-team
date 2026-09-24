using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace ATeam.Dashboard;

/// <summary>How much of a card is its issue number, and the scheme to draw that number in.</summary>
public readonly record struct PriorityMark(int Width, string Scheme);

/// <summary>A card's issue number wears its item's Priority, in the colours GitHub gives that field's
/// own options.</summary>
public static class Priorities
{
    private static readonly (string Name, string Scheme, string Colour)[] Levels =
    [
        ("Urgent", "Priority.Urgent", "#c1408d"),
        ("High", "Priority.High", "#d4323c"),
        ("Medium", "Priority.Medium", "#9a6700"),
        ("Low", "Priority.Low", "#21833d"),
    ];

    internal static void Register(Scheme baseScheme)
    {
        foreach (var (_, scheme, colour) in Levels)
            SchemeManager.AddScheme(scheme, baseScheme with
            {
                Normal = new Attribute(new Color(colour), baseScheme.Normal.Background),
            });
    }

    /// <summary>The scheme a Priority is drawn in, or none for an item that has no Priority set.</summary>
    public static string Scheme(string priority) =>
        Array.Find(Levels, level => level.Name == priority).Scheme ?? "";

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
