using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ATeam.Core;

/// <summary>JSON as jq prints it, which is what a board command's readers have always been given.</summary>
internal static class Json
{
    internal static JsonNode? Parse(string text) => JsonNode.Parse(text);

    /// <summary>`gh api --paginate` prints one document after another.</summary>
    internal static List<JsonNode?> ParseMany(string text)
    {
        var documents = new List<JsonNode?>();
        var bytes = Encoding.UTF8.GetBytes(text);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { AllowMultipleValues = true });
        while (reader.Read())
        {
            using var document = JsonDocument.ParseValue(ref reader);
            documents.Add(JsonNode.Parse(document.RootElement.GetRawText()));
        }
        return documents;
    }

    internal static string? Text(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;

    internal static bool Truthy(JsonNode? node) => node is not null && node.GetValueKind() != JsonValueKind.False && node.GetValueKind() != JsonValueKind.Null;

    /// <summary>jq's `*`: <paramref name="over"/> on top of <paramref name="under"/>, objects merged all the way down.</summary>
    internal static JsonObject Merge(JsonObject under, JsonObject over)
    {
        var merged = under.DeepClone().AsObject();
        foreach (var (key, value) in over)
            merged[key] = value is JsonObject top && merged[key] is JsonObject below ? Merge(below, top) : value?.DeepClone();
        return merged;
    }

    internal static string Pretty(JsonNode? node)
    {
        var text = new StringBuilder();
        Write(text, node, 0, pretty: true);
        return text.ToString();
    }

    internal static string Compact(JsonNode? node)
    {
        var text = new StringBuilder();
        Write(text, node, 0, pretty: false);
        return text.ToString();
    }

    private static void Write(StringBuilder text, JsonNode? node, int depth, bool pretty)
    {
        switch (node)
        {
            case null:
                text.Append("null");
                break;
            case JsonObject map:
                if (map.Count == 0)
                {
                    text.Append("{}");
                    break;
                }
                text.Append('{');
                var firstKey = true;
                foreach (var (key, value) in map)
                {
                    if (!firstKey) text.Append(',');
                    firstKey = false;
                    if (pretty) NewLine(text, depth + 1);
                    Quote(text, key);
                    text.Append(pretty ? ": " : ":");
                    Write(text, value, depth + 1, pretty);
                }
                if (pretty) NewLine(text, depth);
                text.Append('}');
                break;
            case JsonArray list:
                if (list.Count == 0)
                {
                    text.Append("[]");
                    break;
                }
                text.Append('[');
                for (var i = 0; i < list.Count; i++)
                {
                    if (i > 0) text.Append(',');
                    if (pretty) NewLine(text, depth + 1);
                    Write(text, list[i], depth + 1, pretty);
                }
                if (pretty) NewLine(text, depth);
                text.Append(']');
                break;
            default:
                switch (node.GetValueKind())
                {
                    case JsonValueKind.String: Quote(text, node.GetValue<string>()); break;
                    case JsonValueKind.True: text.Append("true"); break;
                    case JsonValueKind.False: text.Append("false"); break;
                    case JsonValueKind.Null: text.Append("null"); break;
                    default: text.Append(node.ToJsonString()); break;
                }
                break;
        }
    }

    private static void NewLine(StringBuilder text, int depth) => text.Append('\n').Append(' ', depth * 2);

    private static void Quote(StringBuilder text, string value)
    {
        text.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': text.Append("\\\""); break;
                case '\\': text.Append("\\\\"); break;
                case '\n': text.Append("\\n"); break;
                case '\t': text.Append("\\t"); break;
                case '\r': text.Append("\\r"); break;
                case '\b': text.Append("\\b"); break;
                case '\f': text.Append("\\f"); break;
                default:
                    if (c < 0x20 || c == 0x7f)
                        text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        text.Append(c);
                    break;
            }
        }
        text.Append('"');
    }
}
