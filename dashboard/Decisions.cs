using System.Globalization;
using System.Text.Json;

namespace ATeam.Dashboard;

/// <summary>One entry in the decision register, as <c>a-team board &lt;team&gt; decisions --replaced</c> reports it.</summary>
public sealed record Decision(
    int Id, string Team, bool Everyone, string Text, string Why, string Who, string Kind, int Item, string Link,
    DateTimeOffset Said, DateTimeOffset Confirmed, int? ConfirmedOn = null, int? ReplacedBy = null,
    DateTimeOffset? Retired = null, DateTimeOffset? Yours = null)
{
    public const string AllTeams = "all teams";

    public bool Live => ReplacedBy is null && Retired is null;

    public string Speaker => Who == "you" ? "you" : "Lead";

    public string Scope => Everyone ? AllTeams : Team;

    /// <summary>The Decision column, saying why an entry no longer stands.</summary>
    public string Shown => ReplacedBy is not null ? $"(replaced) {Text}" : Retired is not null ? $"(retired) {Text}" : Text;

    internal static string Day(DateTimeOffset at) => at.ToLocalTime().ToString("dd MMM", CultureInfo.InvariantCulture);
}

/// <summary>The register as the Decisions dialog shows it: the team's entries and all teams', replaced ones included,
/// or the one line saying why it couldn't be read.</summary>
public sealed record DecisionRegister(IReadOnlyList<Decision> Entries, string? Failure = null)
{
    public static readonly string[] Columns = ["Confirmed", "Who", "Kind", "Scope", "Decision", "From"];

    public const string Empty = "Nothing in the register yet. The Lead adds decisions as you make them.";

    public static DecisionRegister Of(Reading reading) =>
        reading.Failure is { Length: > 0 } failure ? new DecisionRegister([], failure)
        : Parse(reading.Output) ?? new DecisionRegister([], "couldn't read the register");

    /// <summary>Newest confirmed first, narrowed to the entries holding every word of <paramref name="search"/>.</summary>
    public IReadOnlyList<Decision> Shown(string search, bool allTeams, bool replaced)
    {
        var words = search.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return [.. Entries
            .Where(entry => (allTeams || !entry.Everyone) && (replaced || entry.Live))
            .Where(entry => words.All(word => Searched(entry).Contains(word, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(entry => entry.Confirmed)
            .ThenByDescending(entry => entry.Id)];
    }

    public static string[] Row(Decision entry) =>
        [Decision.Day(entry.Confirmed), entry.Speaker, entry.Kind, entry.Scope, entry.Shown, $"#{entry.Item}"];

    /// <summary>The selected entry in full: the decision, why, where and when it was said, and what it replaced or
    /// was replaced by.</summary>
    public IReadOnlyList<string> Detail(Decision entry)
    {
        var lines = new List<string> { entry.Text };
        if (entry.Why.Length > 0)
            lines.AddRange(entry.Why.Split('\n'));
        var said = $"Said on #{entry.Item} · {Decision.Day(entry.Said)}";
        if (entry.ConfirmedOn is { } on)
            said += $" · reconfirmed on #{on} · {Decision.Day(entry.Confirmed)}";
        lines.Add($"{said} · recorded by the Lead");
        lines.AddRange(Entries.Where(older => older.ReplacedBy == entry.Id).Select(older => $"Replaces: {Quoted(older)}"));
        if (entry.ReplacedBy is { } newer)
            lines.Add(Entries.FirstOrDefault(other => other.Id == newer) is { } by ? $"Replaced by: {Quoted(by)}" : "Replaced");
        if (entry.Retired is { } retired)
            lines.Add($"Retired by you · {Decision.Day(retired)}");
        else if (entry.Yours is { } yours)
            lines.Add($"Corrected by you · {Decision.Day(yours)}");
        return lines;
    }

    private static string Quoted(Decision entry) => $"\"{entry.Text}\" (#{entry.Item} · {Decision.Day(entry.Said)})";

    private static string Searched(Decision entry) => $"{entry.Text}\n{entry.Why}";

    private static DecisionRegister? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return null;
            return new DecisionRegister([.. document.RootElement.EnumerateArray().Select(e => new Decision(
                e.GetProperty("id").GetInt32(),
                e.GetProperty("team").GetString() ?? "",
                e.GetProperty("scope").GetString() == "all",
                e.GetProperty("decision").GetString() ?? "",
                e.GetProperty("why").GetString() ?? "",
                e.GetProperty("who").GetString() ?? "",
                e.GetProperty("kind").GetString() ?? "",
                e.GetProperty("item").GetInt32(),
                e.GetProperty("link").GetString() ?? "",
                e.GetProperty("said").GetDateTimeOffset(),
                e.GetProperty("confirmed").GetDateTimeOffset(),
                Number(e, "confirmedOn"),
                Number(e, "replacedBy"),
                Time(e, "retired"),
                Time(e, "yours")))]);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static int? Number(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;

    private static DateTimeOffset? Time(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetDateTimeOffset() : null;
}

/// <summary>A correction you make in the Decisions dialog, as the arguments after
/// <c>a-team board &lt;team&gt; decision you &lt;id&gt;</c>; an edit names the file holding the new wording.</summary>
public sealed record DecisionChange(int Id, string Change, string? Wording = null)
{
    public static DecisionChange Edit(int id, string wording) => new(id, "edit", wording.Trim());

    public static DecisionChange Kind(int id) => new(id, "kind");

    public static DecisionChange Widen(int id) => new(id, "widen");

    public static DecisionChange Retire(int id) => new(id, "retire");

    public string[] Arguments(string team, string? file = null) =>
        ["board", team, "decision", "you", Id.ToString(CultureInfo.InvariantCulture), Change, .. file is null ? Array.Empty<string>() : [file]];

    /// <summary>Makes the change with <paramref name="read"/>, and returns null once it's made, or else why not.</summary>
    public async Task<string?> Run(string team, Func<string[], Task<Reading>> read)
    {
        var file = Wording is null ? null : Path.GetTempFileName();
        try
        {
            if (file is not null)
                await File.WriteAllTextAsync(file, Wording + "\n");
            var reading = await read(Arguments(team, file));
            return reading.Failure is { Length: > 0 } failure ? failure.Split('\n')[0] : null;
        }
        finally
        {
            if (file is not null)
                File.Delete(file);
        }
    }
}
