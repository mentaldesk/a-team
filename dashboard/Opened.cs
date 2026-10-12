using System.Text.Json;

namespace ATeam.Dashboard;

/// <summary>When you last opened each card on this machine, kept in a-team's state directory across restarts.</summary>
public sealed class Opened(string stateRoot)
{
    private readonly string _path = Path.Combine(stateRoot, "opened.json");

    /// <summary>When you last opened it, or null where you never have.</summary>
    public DateTimeOffset? Last(string team, int number) =>
        Read().TryGetValue(Key(team, number), out var at) ? at : null;

    public void Open(string team, int number, DateTimeOffset at)
    {
        var all = Read();
        all[Key(team, number)] = at;
        try
        {
            Directory.CreateDirectory(stateRoot);
            using var stream = File.Create(_path);
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
            writer.WriteStartObject();
            foreach (var (card, when) in all)
                writer.WriteString(card, when);
            writer.WriteEndObject();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string Key(string team, int number) => $"{team}#{number}";

    private Dictionary<string, DateTimeOffset> Read()
    {
        try
        {
            using var file = JsonDocument.Parse(File.ReadAllText(_path));
            return file.RootElement.ValueKind != JsonValueKind.Object ? []
                : file.RootElement.EnumerateObject()
                    .Where(card => card.Value.ValueKind == JsonValueKind.String && card.Value.TryGetDateTimeOffset(out _))
                    .ToDictionary(card => card.Name, card => card.Value.GetDateTimeOffset());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }
}
