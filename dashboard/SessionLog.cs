using System.Globalization;
using System.Text.Json;

namespace ATeam.Dashboard;

/// <summary>How a session's last run ended, as its final result event reported it.</summary>
public enum RunVerdict
{
    None,
    Ok,
    Error,
}

/// <summary>Follows a session's stream-json log and turns its events into readable lines.</summary>
public sealed class SessionLog
{
    private const int MaxLines = 500;
    internal const int MaxCopied = 64 * 1024;
    private readonly List<LogLine> _lines = [];
    private string? _path;
    private long _offset;
    private string _partial = "";

    public IReadOnlyList<LogLine> Lines => _lines;

    /// <summary>The last result event's verdict, kept even once that line has been trimmed out of <see cref="Lines"/>.</summary>
    public RunVerdict Verdict { get; private set; }

    /// <summary>The session id from the run's init event, which <c>claude --resume</c> picks the conversation up from.</summary>
    public string? SessionId { get; private set; }

    /// <summary>Reads anything new. Returns true if the lines changed.</summary>
    public bool Refresh(string? path)
    {
        if (path != _path)
        {
            _path = path;
            _offset = 0;
            _partial = "";
            _lines.Clear();
            Verdict = RunVerdict.None;
            SessionId = null;
            if (path is null)
                return true;
        }
        if (path is null)
            return false;

        string chunk;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length <= _offset)
                return false;
            stream.Seek(_offset, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            chunk = reader.ReadToEnd();
            _offset = stream.Length;
        }
        catch (IOException) { return false; }

        var text = _partial + chunk;
        var end = text.LastIndexOf('\n');
        _partial = end < 0 ? text : text[(end + 1)..];
        if (end < 0)
            return false;

        var events = text[..end].Split('\n', StringSplitOptions.RemoveEmptyEntries);
        SessionId ??= events.Select(SessionIdOf).FirstOrDefault(id => id is not null);
        foreach (var rendered in events.SelectMany(Render))
        {
            Verdict = rendered.Kind switch
            {
                LogLineKind.ResultOk => RunVerdict.Ok,
                LogLineKind.ResultError => RunVerdict.Error,
                _ => Verdict,
            };
            _lines.Add(rendered);
        }
        if (_lines.Count > MaxLines)
            _lines.RemoveRange(0, _lines.Count - MaxLines);
        return true;
    }

    /// <summary>Every line of the session at <paramref name="path"/>, none trimmed, each as <see cref="LogLine.Written"/>.</summary>
    public static IEnumerable<string> Whole(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();
        var end = text.LastIndexOf('\n');
        return end < 0
            ? []
            : [.. text[..end].Split('\n', StringSplitOptions.RemoveEmptyEntries).SelectMany(Render).Select(line => line.Written)];
    }

    private static string? SessionIdOf(string json)
    {
        if (!json.Contains("\"init\""))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return Str(root, "type") == "system" && Str(root, "subtype") == "init" && Str(root, "session_id") is { Length: > 0 } id
                ? id
                : null;
        }
        catch (JsonException) { return null; }
    }

    internal static IEnumerable<LogLine> Render(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { return [new LogLine(json, LogLineKind.Prose)]; }

        using (doc)
        {
            var root = doc.RootElement;
            return Str(root, "type") switch
            {
                "system" when Str(root, "subtype") == "init" =>
                    [new LogLine($"── session started ({Str(root, "model")}) ──", LogLineKind.SessionBoundary)],
                "assistant" => Assistant(root),
                "user" => ToolErrors(root).ToList(),
                "result" => [Result(root)],
                _ => [],
            };
        }
    }

    private static List<LogLine> Assistant(JsonElement root)
    {
        var lines = new List<LogLine>();
        foreach (var part in Content(root))
        {
            switch (Str(part, "type"))
            {
                case "text":
                    lines.AddRange(Str(part, "text").Trim().Split('\n')
                        .Select(text => new LogLine(text, LogLineKind.Prose)));
                    lines.Add(new LogLine("", LogLineKind.Prose));
                    break;
                case "tool_use":
                    var field = ToolField(part);
                    var (full, capped) = Capped(field);
                    lines.Add(new LogLine($"{Str(part, "name")} {Clip(FirstLine(field), 160)}".TrimEnd(), LogLineKind.ToolCall,
                        field.Length > 0 ? full : null, capped));
                    break;
            }
        }
        return lines;
    }

    private static IEnumerable<LogLine> ToolErrors(JsonElement root)
    {
        foreach (var part in Content(root))
        {
            if (Str(part, "type") != "tool_result" || !part.TryGetProperty("is_error", out var error) ||
                error.ValueKind != JsonValueKind.True)
                continue;
            var content = part.GetProperty("content");
            var text = content.ValueKind == JsonValueKind.String
                ? content.GetString() ?? ""
                : string.Join(" ", content.EnumerateArray().Select(c => Str(c, "text")));
            var shown = Clip(FirstLine(text), 200);
            var (full, capped) = Capped(text);
            yield return new LogLine(shown, LogLineKind.ToolError, full == shown ? null : full, capped);
        }
    }

    private static LogLine Result(JsonElement root)
    {
        var failed = root.TryGetProperty("is_error", out var e) && e.ValueKind == JsonValueKind.True;
        var turns = root.TryGetProperty("num_turns", out var t) ? t.GetInt32() : 0;
        var cost = root.TryGetProperty("total_cost_usd", out var c) ? c.GetDouble() : 0;
        var text = $"finished: {(failed ? "error" : "ok")}, {turns} turns, ${cost.ToString("0.00", CultureInfo.InvariantCulture)}";
        return new LogLine(text, failed ? LogLineKind.ResultError : LogLineKind.ResultOk);
    }

    private static string ToolField(JsonElement tool)
    {
        if (!tool.TryGetProperty("input", out var input) || input.ValueKind != JsonValueKind.Object)
            return "";
        foreach (var key in new[] { "command", "file_path", "skill", "query", "url", "pattern", "description" })
        {
            var value = Str(input, key);
            if (value.Length > 0)
                return value;
        }
        return "";
    }

    private static (string Text, bool Capped) Capped(string text) =>
        text.Length > MaxCopied ? (text[..MaxCopied], true) : (text, false);

    private static IEnumerable<JsonElement> Content(JsonElement root) =>
        root.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content) &&
        content.ValueKind == JsonValueKind.Array
            ? content.EnumerateArray()
            : [];

    private static string Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static string FirstLine(string text) => text.Split('\n', 2)[0];

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
