using System.Globalization;
using System.Text.Json;

namespace ATeam.Dashboard;

/// <summary>How far a team's GitHub Project is behind its board, as <c>a-team board &lt;team&gt; overview</c> reports
/// it alongside the cards: the moves it hasn't shown yet, why, and when it next tries.</summary>
public sealed record MirrorLag(int Behind, string Failed = "", DateTimeOffset? Retry = null)
{
    /// <summary>What the team's lane heading says about it.</summary>
    public string Note
    {
        get
        {
            var note = $"mirror {Behind} move{(Behind == 1 ? "" : "s")} behind";
            var why = string.Join(", ", new[]
            {
                Failed,
                Retry is { } retry ? $"retry {retry.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)}" : "",
            }.Where(part => part.Length > 0));
            return why.Length > 0 ? $"{note} ({why})" : note;
        }
    }

    /// <summary>The lag in what that command printed, or null where the mirror is caught up or there's none.</summary>
    public static MirrorLag? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return null;
            foreach (var entry in document.RootElement.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("mirror", out var mirror) ||
                    mirror.ValueKind != JsonValueKind.Object || !mirror.TryGetProperty("behind", out var behind) ||
                    !behind.TryGetInt32(out var count) || count <= 0)
                    continue;
                return new MirrorLag(count,
                    mirror.TryGetProperty("failed", out var failed) && failed.ValueKind == JsonValueKind.String ? failed.GetString() ?? "" : "",
                    mirror.TryGetProperty("retry", out var retry) && retry.ValueKind == JsonValueKind.String &&
                    DateTimeOffset.TryParse(retry.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)
                        ? at
                        : null);
            }
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
