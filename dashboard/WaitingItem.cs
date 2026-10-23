using System.Text.Json;

namespace ATeam.Dashboard;

/// <summary>One item at a gate, as <c>a-team board &lt;team&gt; waiting</c> reports it.</summary>
public sealed record WaitingItem(
    int Number, string Title, string Status, string Url, string Team, string Turn = "", string Reason = "",
    int Pr = 0, string PrUrl = "", string Trouble = "", string Priority = "", bool Pitch = false,
    string Question = "", string Unready = "", string Base = "", int Tasks = 0, int OpenTasks = 0, string Role = "",
    string Recommendation = "", bool Reviewed = false, int Consider = 0, bool Document = false, string Branch = "",
    bool Dependency = false, string Upstream = "", IReadOnlyList<IReadOnlyList<ChainLink>>? Chains = null,
    IReadOnlyList<int>? Frees = null, string HeldBy = "")
{
    /// <summary>The tasks this card holds up, found across every team's cards by <see cref="Holds.Tag"/>.</summary>
    public IReadOnlyList<Hold> Holding { get; init; } = [];

    /// <summary>On a task waiting on another repo, the link of its chain holding it now, as its card says it.</summary>
    public string WaitsOn { get; init; } = "";

    /// <summary>Whether it's a task waiting on a change in another repo, which frees itself and asks nobody anything.</summary>
    public bool WaitsUpstream => Upstream.Length > 0;

    /// <summary>Whether it's a Ready task someone labelled blocked, which waits on you whoever held it.</summary>
    public bool Held => Status == "Ready" && !WaitsUpstream;

    /// <summary>Who held it and why, as the status bar says it: <c>held by the Lead · &lt;its ask&gt;</c>.</summary>
    public string Hold => HeldBy switch
    {
        "dev" => $"held by the Dev · {Asked}",
        "lead" => $"held by the Lead · {Asked}",
        _ => "held, no reason given",
    };

    private string Asked => string.Join(' ', Question.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The PR its branch is on: the one that closes it, or a document pitch itself; 0 where there's none.</summary>
    public int BranchPr => Branch.Length == 0 ? 0 : Pr > 0 ? Pr : Number;

    /// <summary>The PR and its branch, as the status bar ends with them, or nothing where there's no branch.</summary>
    public string PrBranch => BranchPr == 0 ? "" : $"PR #{BranchPr} · {Branch}";

    /// <summary>The PR, its branch and the branch it merges into, as the reader heads its body with them.</summary>
    public string PrHeading => PrBranch.Length == 0 ? "" : Base.Length == 0 ? PrBranch : $"{PrBranch} → {Base}";

    /// <summary>Whether this is the Customer lead's docs PR, which is its own PR to merge.</summary>
    public bool Docs => Role == "customer";

    /// <summary>What the card says about the Reviewer's review of its PR, or nothing where it has none.</summary>
    public string Review => !Reviewed ? "" : Consider == 0 ? "reviewed" : $"reviewed · {Consider} to consider";

    /// <summary>Whether this is a pitch the reviewer can approve now.</summary>
    public bool Approvable => Pitch && Status == "Pitched";

    /// <summary>Whether this is an Idea or a pitch the reviewer could turn down: never a task, a PR or a question.</summary>
    public bool Declinable => Question.Length == 0 && (Status == "Idea" || Pitch && Status == "Pitched");

    /// <summary>Whether this is a pitch the reviewer could say Not now to, holding it back for a while.</summary>
    public bool Shelvable => Pitch && !Document && Status == "Pitched";

    /// <summary>Whether this is a task whose PR the reviewer could merge, a validated pitch they could close, or a
    /// document pitch they could merge, trouble or not.</summary>
    public bool Acceptable => Status == "In review" || Document && Status == "Pitched";

    /// <summary>Whether there's something to try: a task's PR, or the default branch for a validated pitch.</summary>
    public bool Triable => Pr > 0 || Pitch && Acceptable;

    /// <summary>Why it can't be accepted yet, for the message bar, or nothing when it can.</summary>
    public string Unacceptable =>
        Holdup.Length == 0 ? "" : Pitch ? $"#{Number} {Holdup}" : $"#{Number} · {Holdup}";

    /// <summary>What stands in the way of accepting it, without its number, or nothing when it can be.</summary>
    public string Holdup =>
        Document ? ""
        : Pitch ? OpenTasks == 0 ? "" : $"has {OpenTasks} open task{(OpenTasks == 1 ? "" : "s")}"
        : Pr == 0 ? "no PR to merge"
        : Unready;

    /// <summary>The rank the Lead recommends, as the card wears it while no Priority is set, so it never reads
    /// like one: <c>High?</c>.</summary>
    public string Suggested => Priority.Length > 0 ? "" : Recommendation switch
    {
        "Urgent" => "Urgent?",
        "High" => "High?",
        "Medium" => "Med?",
        "Low" => "Low?",
        _ => "",
    };

    /// <summary>Whether the next move is the reviewer's. An item the board said nothing about is theirs.</summary>
    public bool Mine => Turn.Length == 0 || Turn == "you";

    /// <summary>The whole of it for the message bar, with the role in front when the move isn't the reviewer's,
    /// and the PR it hands over to at the end; who held it, for a held task that's yours; or the chain, for a card holding up
    /// a task or a task held up.</summary>
    public string Line => Held && Mine ? $"#{Number} · {Hold}" : Holding.Count > 0 ? Holds.Chain(this) : WaitsOn.Length > 0 ? Holds.Waiting(this) : string.Join(" · ",
        new[] { $"#{Number}", Mine ? "" : Turn, Suggested, Reason, PrBranch.Length > 0 ? PrBranch : Pr == 0 ? "" : $"PR #{Pr}" }.Where(part => part.Length > 0));

    /// <summary>What that command printed. Anything that isn't an item with a number is left out.</summary>
    public static IReadOnlyList<WaitingItem> Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return [];
            return
            [
                .. document.RootElement.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.Object && Numbered(item) is not null)
                    .Select(item => new WaitingItem(
                        Numbered(item)!.Value, Text(item, "title"), Text(item, "status"), Text(item, "url"),
                        Text(item, "team"), Text(item, "turn"), Text(item, "reason"),
                        Numbered(item, "pr") ?? 0, Text(item, "prUrl"), Text(item, "trouble"),
                        Text(item, "priority"), Flag(item, "pitch"), Text(item, "question"),
                        Text(item, "unready"), Text(item, "base"),
                        Numbered(item, "tasks") ?? 0, Numbered(item, "openTasks") ?? 0, Text(item, "role"),
                        Text(item, "recommendation"), Flag(item, "reviewed"), Numbered(item, "consider") ?? 0,
                        Flag(item, "document"), Text(item, "branch"), Flag(item, "dependency"), Text(item, "upstream"),
                        Linked(item) is { Count: > 0 } chains ? chains : null,
                        Numbers(item, "frees") is { Count: > 0 } frees ? frees : null, Text(item, "heldBy")))
            ];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static int? Numbered(JsonElement item) => Numbered(item, "number");

    private static IReadOnlyList<IReadOnlyList<ChainLink>> Linked(JsonElement item) =>
        item.TryGetProperty("chains", out var chains) && chains.ValueKind == JsonValueKind.Array
            ? [.. chains.EnumerateArray().Where(chain => chain.ValueKind == JsonValueKind.Array)
                .Select(chain => (IReadOnlyList<ChainLink>)[.. chain.EnumerateArray().Where(link => link.ValueKind == JsonValueKind.Object)
                    .Select(link => new ChainLink(Text(link, "team"), Numbers(link, "refs"), Text(link, "what"), Text(link, "shown")))])
                .Where(chain => chain.Count > 0)]
            : [];

    private static IReadOnlyList<int> Numbers(JsonElement item, string name) =>
        item.TryGetProperty(name, out var numbers) && numbers.ValueKind == JsonValueKind.Array
            ? [.. numbers.EnumerateArray().Where(number => number.ValueKind == JsonValueKind.Number && number.TryGetInt32(out _))
                .Select(number => number.GetInt32())]
            : [];

    private static int? Numbered(JsonElement item, string name) =>
        item.TryGetProperty(name, out var number) && number.TryGetInt32(out var value) ? value : null;

    private static bool Flag(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
}
