using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ATeam.Core;

/// <summary>Which comments a run has read and which role said each of the team's, as the record keeps them.</summary>
internal sealed partial class Board
{
    private static readonly string[] Speakers = [.. StatusRoute.Roles, StatusRoute.Dispatcher];

    private Dictionary<string, string>? _said;
    private HashSet<string>? _read;

    /// <summary>What names a comment across GitHub's APIs: its URL's fragment, like `issuecomment-123`.</summary>
    internal static string? CommentKey(string? url) =>
        url is not null && Regex.Match(url, @"#((?:issuecomment|pullrequestreview|discussion_r)-?[0-9]+)\b") is { Success: true } found
            ? found.Groups[1].Value : null;

    private static string? KeyOf(JsonNode comment) => CommentKey(Json.Text(comment["url"]));

    private Dictionary<string, string> SaidRows => _said ??= _record
        .Query($"SELECT comment, role FROM comments_said WHERE team = {TeamQ};").ToDictionary(row => row["comment"]!, row => row["role"]!);

    private HashSet<string> ReadRows => _read ??= _record
        .Query($"SELECT comment FROM comments_read WHERE team = {TeamQ};").Select(row => row.First!).ToHashSet();

    /// <summary>The role the record says wrote that comment, or null for anyone else's.</summary>
    private string? Speaker(JsonNode comment) => KeyOf(comment) is { } key ? SaidRows.GetValueOrDefault(key) : null;

    private bool SaidBy(JsonNode comment, string role) => Speaker(comment) == role;

    private bool TeamSpoke(JsonNode comment) => Speaker(comment) is not null;

    /// <summary>Whether the record says the Lead wrote any of those comments.</summary>
    internal bool LeadSaid(JsonArray? comments) => (comments ?? []).OfType<JsonNode>().Any(comment => SaidBy(comment, "lead"));

    /// <summary>Whether the record says a run has read that comment.</summary>
    private bool WasRead(JsonNode comment) => KeyOf(comment) is { } key && ReadRows.Contains(key);

    /// <summary>The role whose marker ends a body the team is about to post, or null.</summary>
    private static string? SpeakerOf(string body)
    {
        var last = body.Replace("\r", "").Split('\n').Select(line => line.Trim()).LastOrDefault(line => line.Length > 0) ?? "";
        var match = Regex.Match(last, "^<!-- a-team:(lead|dev|customer|reviewer|dispatch) -->$");
        return !match.Success ? null : match.Groups[1].Value == "dispatch" ? StatusRoute.Dispatcher : match.Groups[1].Value;
    }

    /// <summary>The item a `gh issue comment`, `gh pr comment` or comments POST is on, or null for any other call.</summary>
    private static string? CommentedOn(string[] args)
    {
        if (args.Length > 2 && args[0] is "issue" or "pr" && args[1] == "comment") return args[2];
        return args.Select(arg => Regex.Match(arg, "^repos/[^/]+/[^/]+/issues/([0-9]+)/comments$")).FirstOrDefault(match => match.Success)?.Groups[1].Value;
    }

    /// <summary>Records which role said the comment just posted, as gh gave its URL.</summary>
    private void NoteSaid(string body, string[] args, GhResult posted)
    {
        if (!posted.Ok || CommentedOn(args) is not { } n || SpeakerOf(body) is not { } role || CommentKey(posted.Out) is not { } key) return;
        Atomically(SaidRow(n, key, role, Now(), "OR REPLACE"));
        SaidRows[key] = role;
    }

    private string SaidRow(string n, string key, string role, string at, string conflict) =>
        $"INSERT {conflict} INTO comments_said (team, item, comment, role, at) VALUES ({TeamQ}, {n}, {Reconciler.Q(key)}, {Reconciler.Q(role)}, {Reconciler.Q(at)});";

    private string ReadRow(string n, string key, string at) =>
        $"INSERT OR IGNORE INTO comments_read (team, item, comment, at) VALUES ({TeamQ}, {n}, {Reconciler.Q(key)}, {Reconciler.Q(at)});";

    /// <summary>Records that a run has read that comment, once its 👀 is on it.</summary>
    private void NoteRead(string n, string? url)
    {
        if (DryRun || CommentKey(url) is not { } key) return;
        Atomically(ReadRow(n, key, Now()));
        ReadRows.Add(key);
    }

    /// <summary>The team wrote this, as its marker says: read only to move a team into the record, and to label a conversation.</summary>
    private bool MarkedBy(JsonNode comment, string marker)
    {
        var bot = Bot;
        var body = Json.Text(comment["body"]) ?? "";
        if (bot.Length > 0 && Json.Text(comment["author"]) == bot)
            return body.Contains(marker, StringComparison.Ordinal);
        if (string.CompareOrdinal(Json.Text(comment["at"]), AppFrom) >= 0)
            return false;
        var last = body.Replace("\r", "").Split('\n').Select(line => line.TrimEnd(' ', '\t')).LastOrDefault(line => line.Length > 0) ?? "";
        return last.StartsWith(marker, StringComparison.Ordinal);
    }

    private static string MarkerOf(string role) => role == StatusRoute.Dispatcher ? DispatchMarker : $"<!-- a-team:{role} -->";

    /// <summary>Format 4: on each open card, its PR and each open Dependabot PR, every stakeholder comment with a 👀 counts as read, and every team comment's marker says who said it.</summary>
    private void MoveTalkIntoRecord()
    {
        var open = _record.Query($"SELECT item FROM board WHERE team = {TeamQ} AND status != 'Done';").Select(row => row.First!).ToList();
        var (talk, _, err) = GatedTalk(open.Select(n => long.Parse(n, CultureInfo.InvariantCulture)), aloud: false);
        if (talk is null) throw new Die($"can't read the board's conversations to move who read and said what into the record ({FirstLine(err)}): try again");
        var prs = GatedPrs(talk, null, new HashSet<long>()).Select(pr => pr["pr"]!.ToJsonString());
        var bumps = OpenBumps().Select(pr => pr!["number"]!.ToJsonString());
        foreach (var n in open.Concat(prs).Concat(bumps).Distinct())
            CopyTalk(n, Comments(n, eyes: true) ?? throw new Die($"can't read #{n}'s comments to move who read and said what into the record: try again"));
    }

    /// <summary>Copies who said each of those comments on #n, by its marker, and which of the stakeholders' a run has read, by its 👀.</summary>
    internal void CopyTalk(string n, IEnumerable<JsonObject> comments)
    {
        var rows = new List<string>();
        foreach (var comment in comments.Where(c => Json.Text(c["kind"]) != "body"))
        {
            if (KeyOf(comment) is not { } key) continue;
            var at = Json.Text(comment["at"]) ?? Now();
            var body = Json.Text(comment["body"]) ?? "";
            var role = Speakers.FirstOrDefault(role => MarkedBy(comment, MarkerOf(role)))
                ?? (string.CompareOrdinal(at, AckFrom) < 0 ? Speakers.FirstOrDefault(role => body.Contains(MarkerOf(role), StringComparison.Ordinal)) : null);
            if (role is not null) rows.Add(SaidRow(n, key, role, at, "OR IGNORE"));
            else if (FromStakeholder(comment) && Json.Long(comment["eyes"]) > 0) rows.Add(ReadRow(n, key, at));
        }
        if (rows.Count > 0) Atomically(string.Concat(rows));
        _said = null;
        _read = null;
    }
}
