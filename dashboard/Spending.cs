using System.Text.Json;

namespace ATeam.Dashboard;

/// <summary>A run's cost, the role it was, and the items it was on.</summary>
public sealed record SpendingRun(DateTimeOffset Started, string Role, decimal Cost, IReadOnlyList<int> Items);

/// <summary>An item a run was on, or a pitch over one, with the vision theme its <c>theme:</c> label names.</summary>
public sealed record SpendingItem(int Number, string Title, string Url, string Status, bool Pitch, int? Parent, string? Theme);

/// <summary>One team's runs, as <c>a-team board &lt;team&gt; spending</c> reports them.</summary>
public sealed record TeamSpending(DateTimeOffset? Since, IReadOnlyList<SpendingRun> Runs, IReadOnlyList<SpendingItem> Items)
{
    public static TeamSpending? Of(Reading reading) =>
        reading.Failure is { Length: > 0 } ? null : Parse(reading.Output);

    private static TeamSpending? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return new TeamSpending(
                root.GetProperty("since").ValueKind == JsonValueKind.String ? root.GetProperty("since").GetDateTimeOffset() : null,
                [.. root.GetProperty("runs").EnumerateArray().Select(run => new SpendingRun(run.GetProperty("started").GetDateTimeOffset(),
                    run.GetProperty("role").GetString() ?? "", run.GetProperty("cost").GetDecimal(),
                    [.. run.GetProperty("items").EnumerateArray().Select(item => item.GetInt32())]))],
                [.. root.GetProperty("items").EnumerateArray().Select(item => new SpendingItem(item.GetProperty("number").GetInt32(),
                    item.GetProperty("title").GetString() ?? "", item.GetProperty("url").GetString() ?? "",
                    item.GetProperty("status").GetString() ?? "", item.GetProperty("pitch").GetBoolean(),
                    item.GetProperty("parent").ValueKind == JsonValueKind.Number ? item.GetProperty("parent").GetInt32() : null,
                    item.GetProperty("theme").ValueKind == JsonValueKind.String ? item.GetProperty("theme").GetString() : null))]);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}

public enum SpendingKind
{
    Team,
    Theme,
    NoTheme,
    Pitch,
    Discovery,
    Other,
}

/// <summary>A row of Spending: what its runs cost each role, and how many runs went into it. A run on several items
/// is split evenly between them, but counts once in each row it reaches.</summary>
public sealed class SpendingRow(SpendingKind kind, string label, string team, SpendingItem? pitch = null)
{
    public const string NoTheme = "No theme";
    public const string Discovery = "Discovery & upkeep";
    public const string Other = "Other items";

    private readonly HashSet<SpendingRun> _runs = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, decimal> _byRole = [];

    public SpendingKind Kind { get; } = kind;

    public string Label { get; } = label;

    public string Team { get; } = team;

    public SpendingItem? Pitch { get; } = pitch;

    public List<SpendingRow> Children { get; } = [];

    public int Runs => _runs.Count;

    public decimal Total => _byRole.Values.Sum();

    public decimal Spent(string role) => _byRole.GetValueOrDefault(role);

    internal void Add(SpendingRun run, decimal cost)
    {
        _runs.Add(run);
        _byRole[run.Role] = _byRole.GetValueOrDefault(run.Role) + cost;
    }

    internal void AddAll(SpendingRow from)
    {
        _runs.UnionWith(from._runs);
        foreach (var (role, cost) in from._byRole)
            _byRole[role] = _byRole.GetValueOrDefault(role) + cost;
    }

    public override string ToString() => Label;
}

public static class Spending
{
    public static readonly string[] Periods = ["7 days", "30 days", "Everything"];

    /// <summary>The start of period <paramref name="period"/> of <see cref="Periods"/>, or null for everything.</summary>
    public static DateTimeOffset? From(int period, DateTimeOffset now) => period switch
    {
        0 => now.AddDays(-7),
        1 => now.AddDays(-30),
        _ => null,
    };

    /// <summary>One row per team with runs after <paramref name="from"/>, costliest first: under each its themes, then
    /// No theme, then Discovery &amp; upkeep and Other items. A task's runs count toward its pitch, which takes its
    /// theme from its label.</summary>
    public static IReadOnlyList<SpendingRow> Roll(IReadOnlyList<(string Team, TeamSpending Spending)> teams, DateTimeOffset? from)
    {
        var rows = new List<SpendingRow>();
        foreach (var (team, spending) in teams)
        {
            var runs = spending.Runs.Where(run => from is null || run.Started > from).ToList();
            if (runs.Count == 0)
                continue;
            var items = spending.Items.GroupBy(item => item.Number).ToDictionary(group => group.Key, group => group.First());
            var pitches = new Dictionary<int, SpendingRow>();
            var discovery = new SpendingRow(SpendingKind.Discovery, SpendingRow.Discovery, team);
            var other = new SpendingRow(SpendingKind.Other, SpendingRow.Other, team);
            foreach (var run in runs)
            {
                if (run.Items.Count == 0)
                {
                    discovery.Add(run, run.Cost);
                    continue;
                }
                var share = run.Cost / run.Items.Count;
                foreach (var number in run.Items)
                {
                    if (PitchOf(number, items) is not { } pitch)
                    {
                        other.Add(run, share);
                        continue;
                    }
                    if (!pitches.TryGetValue(pitch.Number, out var row))
                        pitches[pitch.Number] = row = new SpendingRow(SpendingKind.Pitch, $"#{pitch.Number} {pitch.Title}", team, pitch);
                    row.Add(run, share);
                }
            }
            var themes = pitches.Values.GroupBy(row => row.Pitch!.Theme)
                .Select(group =>
                {
                    var theme = group.Key is { } name
                        ? new SpendingRow(SpendingKind.Theme, name, team)
                        : new SpendingRow(SpendingKind.NoTheme, SpendingRow.NoTheme, team);
                    foreach (var pitch in group.OrderByDescending(row => row.Total).ThenBy(row => row.Pitch!.Number))
                    {
                        theme.Children.Add(pitch);
                        theme.AddAll(pitch);
                    }
                    return theme;
                })
                .OrderBy(theme => theme.Kind == SpendingKind.NoTheme).ThenByDescending(theme => theme.Total).ThenBy(theme => theme.Label);
            var top = new SpendingRow(SpendingKind.Team, team, team);
            foreach (var row in themes.Append(discovery).Append(other))
            {
                top.Children.Add(row);
                top.AddAll(row);
            }
            rows.Add(top);
        }
        return [.. rows.OrderByDescending(row => row.Total).ThenBy(row => row.Team)];
    }

    /// <summary>The roles with a column: Lead and Dev always, and any other a team has on or spent on in
    /// <paramref name="rows"/>.</summary>
    public static IReadOnlyList<string> Roles(IEnumerable<string> on, IReadOnlyList<SpendingRow> rows)
    {
        string[] known = ["lead", "dev", "customer", "reviewer"];
        var set = on.ToHashSet();
        return [.. known.Where(role => role is "lead" or "dev" || set.Contains(role) || rows.Any(row => row.Spent(role) != 0))];
    }

    private static SpendingItem? PitchOf(int number, Dictionary<int, SpendingItem> items)
    {
        if (!items.TryGetValue(number, out var item))
            return null;
        if (item.Pitch)
            return item;
        return item.Parent is { } parent && items.TryGetValue(parent, out var over) && over.Pitch ? over : null;
    }
}
