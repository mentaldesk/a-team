using System.Globalization;
using System.Text.Json;

namespace ATeam.Dashboard;

/// <summary>When a team's cards were last synced from GitHub, and when a sync last failed since then, as
/// <c>a-team board &lt;team&gt; waiting</c> reports it alongside the items.</summary>
public sealed record Synced(DateTimeOffset? At, DateTimeOffset? Failed)
{
    internal const string Unreachable = "GitHub unreachable, retrying";

    /// <summary>What the status bar says of it.</summary>
    public string Stamp(DateTimeOffset now)
    {
        var synced = At is not { } at ? ""
            : now - at < TimeSpan.FromMinutes(1) ? "synced just now"
            : $"synced {Ages.Ago(now - at)} ago";
        return string.Join(" · ", new[] { synced, Failed is null ? "" : Unreachable }.Where(part => part.Length > 0));
    }

    /// <summary>The sync stamp in what that command printed, or null where it has none.</summary>
    public static Synced? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return null;
            foreach (var entry in document.RootElement.EnumerateArray())
                if (entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("synced", out _))
                    return new Synced(Time(entry, "synced"), Time(entry, "failed"));
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static DateTimeOffset? Time(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)
            ? at
            : null;
}
