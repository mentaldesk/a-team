using System.Text.Json;

namespace ATeam.Dashboard;

/// <summary>What a document pitch changes, as <c>a-team board &lt;team&gt; document &lt;n&gt;</c> reports it: its files,
/// and where it changes just one, that file as it is on the pitch's branch and the blob it was read at.</summary>
public sealed record DocumentPitch(IReadOnlyList<string> Files, string Path = "", string Ref = "", string Sha = "", string Text = "")
{
    public bool Editable => Files.Count == 1 && Path.Length > 0;

    /// <summary>Why <c>e</c> can't edit it, or null where it can.</summary>
    public string? Uneditable(int number) => Editable ? null
        : Files.Count == 0 ? $"#{number} changes no files, so there's nothing to edit"
        : $"#{number} changes {Files.Count} files, so it can't be edited here: g opens it on GitHub";

    public static DocumentPitch? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
                return null;
            return new DocumentPitch(
                [.. files.EnumerateArray().Where(file => file.ValueKind == JsonValueKind.String).Select(file => file.GetString() ?? "")],
                String(root, "path"), String(root, "ref"), String(root, "sha"), String(root, "text"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string String(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}
