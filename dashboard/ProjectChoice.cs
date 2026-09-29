using System.Text.Json;

namespace ATeam.Dashboard;

/// <summary>A GitHub Project a team can move its work across, as the form's Project list shows it.</summary>
public sealed record ProjectChoice(string Owner, int Number, string Title)
{
    public override string ToString() => Title.Length == 0 ? $"{Owner} #{Number}" : $"{Title} ({Owner} #{Number})";

    /// <summary>The projects in <c>gh project list --format json</c>'s output, or null where it can't be read.</summary>
    public static IReadOnlyList<ProjectChoice>? Parse(Reading reading, string owner)
    {
        if (reading.Failure is not null)
            return null;
        try
        {
            using var document = JsonDocument.Parse(reading.Output);
            if (!document.RootElement.TryGetProperty("projects", out var projects) ||
                projects.ValueKind != JsonValueKind.Array)
                return null;
            return
            [
                .. projects.EnumerateArray()
                    .Where(project => project.TryGetProperty("number", out var number) && number.ValueKind == JsonValueKind.Number)
                    .Select(project => new ProjectChoice(
                        project.TryGetProperty("owner", out var by) && by.ValueKind == JsonValueKind.Object &&
                        by.TryGetProperty("login", out var login) && login.ValueKind == JsonValueKind.String
                            ? login.GetString() ?? owner
                            : owner,
                        project.GetProperty("number").GetInt32(),
                        project.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String
                            ? title.GetString() ?? ""
                            : ""))
            ];
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}
