namespace ATeam.Core;

/// <summary>Who may move a card from where to where. board.sh's `allowed` until the commands that use it have all moved here.</summary>
internal static class StatusRoute
{
    internal static readonly string[] Statuses = ["Idea", "Exploring", "Pitched", "Approved", "Building", "Ready", "In progress", "In review", "Done"];
    internal static readonly string[] Roles = ["lead", "dev", "customer", "reviewer"];
    internal const string None = "None";

    private static readonly HashSet<(string Role, string From, string To)> Moves =
    [
        ("lead", "Idea", "Exploring"),
        ("lead", "Exploring", "Pitched"),
        ("lead", "Exploring", "Idea"),
        ("lead", "Pitched", "Exploring"),
        ("lead", "Approved", "Building"),
        ("lead", None, "Idea"),
        ("lead", None, "Exploring"),
        ("lead", None, "Pitched"),
        ("lead", None, "Ready"),
        ("dev", None, "Idea"),
        ("dev", "Ready", "In progress"),
        ("dev", "In progress", "In review"),
        ("dev", "In progress", "Ready"),
        ("customer", None, "Idea"),
        ("customer", None, "Pitched"),
        ("customer", None, "In review"),
        ("you", None, "Idea"),
    ];

    internal static bool Allowed(string role, string from, string to) => Moves.Contains((role, from, to));

    internal static IEnumerable<(string Role, string From, string To)> All => Moves;
}
