using System.Text;
using System.Text.Json;

namespace ATeam.Dashboard;

/// <summary>Changes one value in a team config in place, so the file's formatting and every other key survive.</summary>
internal static class ConfigEdit
{
    /// <summary>The config with <c>dispatch.enabled</c> set, adding it, or <c>dispatch</c> itself, where missing.</summary>
    internal static byte[] SetEnabled(byte[] config, bool enabled)
    {
        JsonDocument.Parse(config).Dispose();
        var value = enabled ? "true" : "false";
        var reader = new Utf8JsonReader(config);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("it isn't a JSON object");
        var root = (int)reader.TokenStartIndex;

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var isDispatch = reader.ValueTextEquals("dispatch");
            reader.Read();
            if (!isDispatch)
            {
                reader.Skip();
                continue;
            }
            if (reader.TokenType != JsonTokenType.StartObject)
                return Replace(config, reader, $"{{\"enabled\": {value}}}");
            var dispatch = (int)reader.TokenStartIndex;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var isEnabled = reader.ValueTextEquals("enabled");
                reader.Read();
                if (isEnabled)
                    return Replace(config, reader, value);
                reader.Skip();
            }
            return Insert(config, dispatch, $"\"enabled\": {value}");
        }
        return Insert(config, root, $"\"dispatch\": {{\"enabled\": {value}}}");
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
