using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ATeam.Dashboard;

/// <summary>Changes one value in a team config in place, so the file's formatting and every other key survive.</summary>
internal static class ConfigEdit
{
    private static readonly JsonWriterOptions Relaxed = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The config with <c>dispatch.enabled</c> set, adding it, or <c>dispatch</c> itself, where missing.</summary>
    internal static byte[] SetEnabled(byte[] config, bool enabled) => Set(config, ["dispatch", "enabled"], enabled);

    internal static byte[] Set(byte[] config, IReadOnlyList<string> path, bool value) =>
        SetLiteral(config, path, Literal(writer => writer.WriteBooleanValue(value)));

    internal static byte[] Set(byte[] config, IReadOnlyList<string> path, string value) =>
        SetLiteral(config, path, Literal(writer => writer.WriteStringValue(value)));

    internal static byte[] Set(byte[] config, IReadOnlyList<string> path, int? value) =>
        SetLiteral(config, path, Literal(writer =>
        {
            if (value is { } number)
                writer.WriteNumberValue(number);
            else
                writer.WriteNullValue();
        }));

    /// <summary>The config with the value at <paramref name="path"/> set, adding it, or the objects on the way to
    /// it, where missing.</summary>
    private static byte[] SetLiteral(byte[] config, IReadOnlyList<string> path, string json)
    {
        JsonDocument.Parse(config).Dispose();
        var reader = new Utf8JsonReader(config);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("it isn't a JSON object");

        for (var depth = 0; depth < path.Count; depth++)
        {
            var brace = (int)reader.TokenStartIndex;
            var found = false;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var matches = reader.ValueTextEquals(path[depth]);
                reader.Read();
                if (!matches)
                {
                    reader.Skip();
                    continue;
                }
                found = true;
                break;
            }
            if (!found)
                return Insert(config, brace, Nested(path, depth, json));
            if (depth == path.Count - 1)
                return Replace(config, reader, json);
            if (reader.TokenType != JsonTokenType.StartObject)
                return Replace(config, reader, $"{{{Nested(path, depth + 1, json)}}}");
        }
        return config;
    }

    /// <summary><c>"a": {"b": value}</c> for the keys from <paramref name="from"/> on.</summary>
    private static string Nested(IReadOnlyList<string> path, int from, string json)
    {
        var property = $"{Key(path[^1])}: {json}";
        for (var at = path.Count - 2; at >= from; at--)
            property = $"{Key(path[at])}: {{{property}}}";
        return property;
    }

    private static string Key(string name) => Literal(writer => writer.WriteStringValue(name));

    private static string Literal(Action<Utf8JsonWriter> write)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, Relaxed))
            write(writer);
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static byte[] Replace(byte[] config, Utf8JsonReader reader, string with)
    {
        var start = (int)reader.TokenStartIndex;
        reader.Skip();
        return Splice(config, start, (int)reader.BytesConsumed, with);
    }

    /// <summary>Adds a property first in the object whose brace is at <paramref name="brace"/>, laid out like the
    /// property that already comes first there.</summary>
    private static byte[] Insert(byte[] config, int brace, string property)
    {
        var after = brace + 1;
        var next = after;
        while (next < config.Length && config[next] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
            next++;
        if (next < config.Length && config[next] == (byte)'}')
            return Splice(config, after, after, property);
        var lead = Encoding.UTF8.GetString(config, after, next - after);
        return Splice(config, after, after, $"{lead}{property},");
    }

    private static byte[] Splice(byte[] config, int start, int end, string with) =>
        [.. config.AsSpan(0, start), .. Encoding.UTF8.GetBytes(with), .. config.AsSpan(end)];
}
