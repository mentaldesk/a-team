using System.Text.Json;

namespace ATeam.Dashboard;

/// <summary>One acceptance criterion, as <c>a-team board &lt;team&gt; criteria &lt;n&gt;</c> reports it: its number on
/// the issue, and <paramref name="How"/> the team checked it, <c>screen</c> or <c>test</c>, or nothing where it's left
/// for you.</summary>
public sealed record Criterion(int Index, int Depth, bool Ticked, string Text, string How = "", ImageLink? Picture = null)
{
    public bool ByTheTeam => How.Length > 0;

    public string Line => $"{(Depth > 0 ? "  " : "")}[{(Ticked ? 'x' : ' ')}] {Text}{(ByTheTeam ? $"  {How}" : "")}";
}

/// <summary>A task's acceptance criteria as the reader lists them: what's left for you, then what the team checked.</summary>
public sealed record Checklist(IReadOnlyList<Criterion> Criteria)
{
    private const string Heading = "## Criteria";

    public IReadOnlyList<Criterion> Left => [.. Criteria.Where(each => !each.ByTheTeam)];

    public IReadOnlyList<Criterion> Team => [.. Criteria.Where(each => each.ByTheTeam)];

    public int Unticked => Criteria.Count(each => !each.Ticked);

    /// <summary>The criterion you'd check next: the first unticked of yours, else the first unticked, else the first.</summary>
    public Criterion First => Left.FirstOrDefault(each => !each.Ticked) ?? Criteria.FirstOrDefault(each => !each.Ticked) ?? Criteria[0];

    public string Count => string.Join(" · ", new[]
    {
        Left.Count == 0 ? "" : $"{Left.Count(each => each.Ticked)} of {Left.Count} left for you ticked",
        Team.Count == 0 ? "" : $"{Team.Count} checked by the team",
    }.Where(part => part.Length > 0));

    /// <summary>What History says of a tick, counting every criterion.</summary>
    public string Said(bool ticked) => ticked
        ? $"ticked {Criteria.Count(each => each.Ticked)} of {Criteria.Count}"
        : $"unticked, {Criteria.Count(each => each.Ticked)} of {Criteria.Count} ticked";

    public Checklist With(int index, bool ticked) =>
        new([.. Criteria.Select(each => each.Index == index ? each with { Ticked = ticked } : each)]);

    /// <summary>The list as the body shows it, ending in a rule above the issue's own text.</summary>
    public IReadOnlyList<string> Lines
    {
        get
        {
            List<string> lines = [$"{Heading} · {Count}", ""];
            if (Left.Count > 0)
                lines.AddRange(["### Left for you", .. Left.Select(each => each.Line), ""]);
            if (Team.Count > 0)
                lines.AddRange(["### Checked by the team", .. Team.Select(each => each.Line), ""]);
            lines.AddRange(["---", ""]);
            return lines;
        }
    }

    /// <summary>The criterion on <paramref name="line"/> of <see cref="Lines"/>, or null where it's on anything else.</summary>
    public Criterion? At(int line)
    {
        var left = Left;
        var team = Team;
        var first = left.Count > 0 ? 3 : 0;
        if (left.Count > 0 && line >= first && line < first + left.Count)
            return left[line - first];
        var firstOfTeam = (left.Count > 0 ? first + left.Count + 2 : 3);
        return team.Count > 0 && line >= firstOfTeam && line < firstOfTeam + team.Count ? team[line - firstOfTeam] : null;
    }

    /// <summary>Where <paramref name="criterion"/> is in <see cref="Lines"/>.</summary>
    public int LineOf(Criterion criterion) => Enumerable.Range(0, Lines.Count).First(line => At(line)?.Index == criterion.Index);

    /// <summary>What that command printed, or null where it has no criteria to list or isn't what it should be.</summary>
    public static Checklist? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return null;
            List<Criterion> criteria = [.. document.RootElement.EnumerateArray().Select(each => new Criterion(
                each.GetProperty("index").GetInt32(),
                each.GetProperty("depth").GetInt32(),
                each.GetProperty("ticked").GetBoolean(),
                each.GetProperty("text").GetString() ?? "",
                each.TryGetProperty("how", out var how) && how.ValueKind == JsonValueKind.String ? how.GetString() ?? "" : "",
                each.TryGetProperty("picture", out var picture) && picture.ValueKind == JsonValueKind.Object
                    ? new ImageLink(picture.GetProperty("alt").GetString() ?? "", picture.GetProperty("url").GetString() ?? "")
                    : null))];
            return criteria.Count == 0 ? null : new Checklist(criteria);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }
}
