using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ATeam.Core;

/// <summary>The state's format and what moves it, and what `check` and `setup` ask of the machine, the Project and the repo.</summary>
internal sealed partial class Board
{
    private const int DefaultFormat = 1;
    private const string KeychainService = "a-team-app";

    /// <summary>What moves a team's state up to each format, safe to repeat. A format with nothing to move has no step.</summary>
    internal static readonly IReadOnlyDictionary<int, Action<Board>> FormatSteps = new Dictionary<int, Action<Board>>();

    /// <summary>The labels setup creates, and what goes wrong without each.</summary>
    private static readonly (string Name, string Color, string Description, string Without)[] TeamLabels =
    [
        ("pitch", "5319e7", "An a-team pitch: Lead shapes it, reviewer approves it", "the team can't tell its pitches from tasks"),
    ];

    /// <summary>The Status options setup adds, for a status the Project has no option for.</summary>
    private static readonly (string Status, string Color, string Description)[] StatusOptionsWanted =
    [
        ("Idea", "GRAY", "A seed worth a look"),
        ("Exploring", "PURPLE", "Lead is researching and writing a pitch"),
        ("Pitched", "PINK", "Waiting on the reviewer: approve or comment"),
        ("Approved", "GREEN", "Reviewer approved; Lead to break it down"),
        ("Building", "BLUE", "Broken into tasks; tasks in flight"),
        ("Ready", "BLUE", "A task Dev can pick up"),
        ("In progress", "YELLOW", "Dev is working on it"),
        ("In review", "ORANGE", "Waiting on the reviewer: merge, accept or comment"),
        ("Done", "GREEN", "Merged or accepted"),
    ];

#if DEBUG
    /// <summary>tests/scripts/review-count.sh's copies of a-team at another format, which a checkout's build alone can be.</summary>
    internal static int Format =>
        int.TryParse(Environment.GetEnvironmentVariable("A_TEAM_BOARD_FORMAT"), out var format) ? format : DefaultFormat;

    private static Action<Board>? StepTo(int format) =>
        FormatSteps.GetValueOrDefault(format)
        ?? (format > DefaultFormat ? board => File.AppendAllText(Path.Combine(State, $"format-{format}-ran"), $"{board.Team}\n") : null);
#else
    internal static int Format => DefaultFormat;

    private static Action<Board>? StepTo(int format) => FormatSteps.GetValueOrDefault(format);
#endif

    /// <summary>Whether the record was there before this command opened it.</summary>
    internal bool RecordFound { get; init; }

    private static string ThisVersion() => Proc.Say(Path.Combine(Root, "bin", "a-team"), "version");

    private int? OwnFormat() =>
        _record.Scalar($"SELECT format FROM formats WHERE team = {TeamQ};") is { } own ? int.Parse(own, CultureInfo.InvariantCulture) : null;

    /// <summary>Why this a-team can't act on the state, if the state is in a newer format than it knows.</summary>
    internal string FormatBehind()
    {
        if (!RecordFound) return "";
        var top = _record.Query("SELECT format, version FROM formats ORDER BY format DESC LIMIT 1;").FirstOrDefault();
        if (top is null || int.Parse(top["format"]!, CultureInfo.InvariantCulture) <= Format) return "";
        var state = $"the board is in format {top["format"]}, from a-team {top["version"]}";
        if (!File.Exists(Path.Combine(Root, "VERSION")))
            return $"{state}, and this a-team ({Root}) is on {ThisVersion()}: merge the default branch";
        return string.IsNullOrEmpty(Environment.GetEnvironmentVariable("A_TEAM_RUN_TEAM"))
            ? $"{state}, and this is {ThisVersion()}: brew upgrade a-team"
            : $"{state}, and this run is on {ThisVersion()}: stopping";
    }

    /// <summary>Refuses a state this a-team is too old for, stamps one with no stamp, and moves an older one forward.</summary>
    internal void FormatGate()
    {
        if (FormatBehind() is { Length: > 0 } why) throw new Die(why);
        if (!RecordFound) return;
        var own = OwnFormat();
        if ((own ?? 1) < Format)
        {
            if (!MayMoveState()) RefuseMove($"move the board from format {own ?? 1} to {Format}");
            FormatMove();
        }
        else if (own is null && MayMoveState())
        {
            _record.Exec($"INSERT OR IGNORE INTO formats (team, format, version) VALUES ({TeamQ}, {Format}, {Reconciler.Q(ThisVersion())});");
        }
    }

    /// <summary>Moves this team's state up to the format, one a-team at a time. One that died leaves its steps to repeat.</summary>
    private void FormatMove()
    {
        TakeFormatTurn();
        var own = OwnFormat() ?? 1;
        if (own < Format)
        {
            for (var step = own + 1; step <= Format; step++)
                StepTo(step)?.Invoke(this);
            _record.Exec($"INSERT OR REPLACE INTO formats (team, format, version, moved) VALUES ({TeamQ}, {Format}, {Reconciler.Q(ThisVersion())}, {Reconciler.Q(Now())});");
        }
        _record.Exec($"DELETE FROM format_moves WHERE team = {TeamQ};");
    }

    private void TakeFormatTurn()
    {
        var waited = 0;
        while (true)
        {
            _record.Exec($"INSERT OR IGNORE INTO format_moves (team, pid) VALUES ({TeamQ}, {Environment.ProcessId});");
            if (_record.Scalar("SELECT changes();") == "1") return;
            var holder = _record.Scalar($"SELECT pid FROM format_moves WHERE team = {TeamQ};");
            if (holder is not null && !Libc.Alive(int.Parse(holder, CultureInfo.InvariantCulture)))
            {
                _record.Exec($"DELETE FROM format_moves WHERE team = {TeamQ} AND pid = {holder};");
                continue;
            }
            if (waited++ >= 300) throw new Die($"another a-team (pid {holder}) has been moving the board forward for 5 minutes");
            Thread.Sleep(1000);
        }
    }

    private static string Unprefixed(string said, string prefix) =>
        said.StartsWith(prefix, StringComparison.Ordinal) ? said[prefix.Length..] : said;

    /// <summary>What `$(... 2>&amp;1)` would have made of it.</summary>
    private static string Both(GhResult read) => (read.Out + read.Err).TrimEnd('\n');

    private static string AtHome(string path) =>
        path.StartsWith('~') ? (Environment.GetEnvironmentVariable("HOME") ?? "") + path[1..] : path;

    private string FieldClash(string name) =>
        $"{_owner} project {_number} has a {name} field that isn't text, so a-team can't copy to it: rename that field";

    private static string TextFieldName(string key) => key == "rank" ? RankField : ThemeField;

    private GhResult AddTextField(string project, string name) =>
        _gh.Run("api", "graphql", "-F", $"project={project}", "-f", $"name={name}", "-f", """
            query=mutation($project: ID!, $name: String!) {
                  createProjectV2Field(input: {projectId: $project, dataType: TEXT, name: $name}) {
                    projectV2Field { ... on ProjectV2Field { id } } } }
            """, "--jq", ".data.createProjectV2Field.projectV2Field.id");

    private static void NotThere(Findings found, string topic, int there, string file, string missing) =>
        found.Note(topic, there == 1 ? missing : $"GitHub didn't answer for {file}: checking again next pass");

    internal void CheckFormat(Findings found)
    {
        try
        {
            if (FormatBehind() is { Length: > 0 } behind) found.Problem("board", behind);
        }
        catch (Exception problem) when (problem is InvalidOperationException or Die)
        {
            Console.Error.WriteLine($"Error: {problem.Message}");
        }
    }

    /// <summary>Whether GitHub will show this team its repo.</summary>
    internal bool CheckRepo(Findings found)
    {
        if (_repo.Length == 0)
        {
            found.Problem("repo", $"{_team}.json names no repo");
            return false;
        }
        var read = _gh.Run("api", $"repos/{_repo}", "--silent");
        if (read.Ok) return true;
        found.Problem("repo", $"can't reach {_repo}: {Unprefixed(Both(read), "gh: ")}");
        return false;
    }

    internal void CheckWorkdir(Findings found)
    {
        var workdir = Text("workdir");
        var checkout = OrElse(Text("checkout"), $"{(workdir.EndsWith('/') ? workdir[..^1] : workdir)}/main");
        if (workdir.Length == 0)
            found.Problem("workdir", $"{_team}.json names no workdir");
        else if (!Directory.Exists(AtHome(workdir)))
            found.Problem("workdir", $"{workdir} isn't there, so the agents would have nothing to work in");
        else if (!Directory.Exists(AtHome(checkout)))
            found.Problem("checkout", $"{checkout} isn't there: gh repo clone {_repo} {checkout}");
    }

    internal void CheckVision(Findings found)
    {
        var vision = Text("vision");
        if (vision.Length == 0) return;
        var (there, said) = FileThere("-H", "Accept: application/vnd.github.raw", $"repos/{_repo}/contents/{vision}");
        if (there != 0)
            NotThere(found, "vision", there, vision, $"{vision} isn't in {_repo} yet: the Lead will draft one and open it as a draft PR");
        else if (said.Contains("<!-- a-team:lead -->", StringComparison.Ordinal))
            found.Note("vision", "drafted by the Lead: Write the vision with me replaces it");
    }

    internal void CheckDocs(Findings found, bool reached)
    {
        if (_config["roles"] is not JsonObject || !CustomerOn) return;
        var docs = Text("docs");
        if (docs.Length == 0)
        {
            found.Note("docs", $"{_team}.json names no docs page, so the Customer lead has nothing to keep right");
            return;
        }
        if (!reached) return;
        var (there, _) = FileThere($"repos/{_repo}/contents/{docs}", "--silent");
        if (there != 0)
            NotThere(found, "docs", there, docs, $"{docs} isn't in {_repo} yet: the Customer lead will propose the docs as a draft PR");
    }

    internal void CheckRelease(Findings found, bool reached)
    {
        var release = OrElse(Text("release"), "never");
        if (release == "never") return;
        if (release is not ("daily" or "continuous"))
        {
            found.Note("release", $"release is '{release}': expected never, daily or continuous");
            return;
        }
        if (!reached) return;
        var (there, _) = FileThere($"repos/{_repo}/actions/workflows/release.yml", "--silent");
        if (there != 0)
            NotThere(found, "release", there, "release.yml", $"release is {release}, but {_repo} has no release.yml workflow for it to run");
    }

    /// <summary>What the mirror's Project lacks, and whether nobody can read it.</summary>
    internal bool CheckProject(Findings found)
    {
        if (_config["mirror"]?.GetValueKind() == JsonValueKind.False)
        {
            Console.Out.Write("mirror: none, so the board is only on this machine\n");
            return false;
        }
        if (_number.Length == 0)
        {
            found.Note("project", $"{_team}.json names no GitHub Project to copy the board to: pick one, or None, under Mirror in its settings");
            return false;
        }
        var (meta, err) = ProjectMeta();
        if (meta is null)
        {
            found.Problem("project", $"can't read {_owner} project {_number}: {Unprefixed(err.TrimEnd('\n').Split('\n')[^1], "gh: ")}");
            return true;
        }
        if (!Json.Truthy(meta["field"]?["id"]))
        {
            found.Problem("project", $"no single-select field '{_field}' on {_owner} project {_number}");
            return false;
        }
        foreach (var name in meta["clash"]!.AsArray().Select(Json.Text).OfType<string>())
            found.Note("fields", FieldClash(name));
        CheckStatuses(found, meta["field"]!["options"]!.AsArray());
        return false;
    }

    private void CheckStatuses(Findings found, JsonArray options)
    {
        var names = options.Select(option => Json.Text(option?["name"])).ToHashSet();
        var missing = StatusRoute.Statuses.Select(StatusOnMirror).Where(option => !names.Contains(option)).ToList();
        if (missing.Count > 0)
        {
            found.Problem("status", $"{missing.Count} of {StatusRoute.Statuses.Length} options missing from '{_field}': {string.Join(", ", missing)}");
            return;
        }
        var unmapped = Unmapped();
        if (unmapped.Length > 0)
            found.Problem("items", $"outside the team's states: {unmapped}");
        else
            Console.Out.Write($"ok: {_owner} project {_number}, field '{_field}'\n");
    }

    /// <summary>The items on the mirror in a status the team doesn't have; nothing if it can't be read whole.</summary>
    private string Unmapped()
    {
        JsonArray? nodes;
        try
        {
            nodes = MirrorNodes(shortEnds: false);
        }
        catch (JsonException)
        {
            return "";
        }
        var known = StatusRoute.Statuses.Concat(_reverseMap.Keys).ToHashSet();
        var outside = (nodes ?? []).Where(node => Json.Text(node?["project"]?["status"]) is { } status && !known.Contains(status))
            .Select(node => $"#{node!["content"]?["number"]?.ToJsonString() ?? "null"} {Json.Text(node["project"]?["status"])}");
        return string.Join(", ", outside);
    }

    internal void CheckLabels(Findings found)
    {
        var read = _gh.Run("label", "list", "-R", _repo, "--limit", "500", "--json", "name", "--jq", ".[].name");
        if (!read.Ok)
        {
            found.Note("labels", $"can't list {_repo}'s labels: {Unprefixed(Both(read), "gh: ")}");
            return;
        }
        var have = Both(read).Split('\n');
        foreach (var (name, _, _, without) in TeamLabels)
            if (!have.Contains(name))
                found.Note("labels", $"no '{name}' label, so {without}");
    }

    private bool HasAppKey(string account) =>
        Gh.Which("security") is { } security && Proc.Capture(security, "find-generic-password", "-s", KeychainService, "-a", account).Code == 0;

    private static string KeyAdvice(string account) =>
        $"generate a new private key on the App's settings page on GitHub, then run: security add-generic-password -U -s {KeychainService} -a {account} -w \"$(openssl base64 -A -in <the .pem you downloaded>)\"";

    private bool Granted(string permission)
    {
        try
        {
            return Json.Text(Json.Parse(File.ReadAllText(Path.Combine(State, _team, "token.json")))?["permissions"]?[permission]) == "write";
        }
        catch (Exception e) when (e is IOException or JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    private bool PriorityReadable()
    {
        var read = PriorityRead();
        if (!read.Ok) return false;
        try
        {
            return Json.Parse(read.Out)?["data"]?["organization"]?["issueFields"]?["nodes"] is JsonArray;
        }
        catch (Exception e) when (e is InvalidOperationException or JsonException)
        {
            return false;
        }
    }

    /// <summary>What the team's App can do, each part of its identity in turn, as far as the first that's wrong.</summary>
    internal void CheckIdentity(Findings found, bool unreachable)
    {
        var bot = Bot;
        if (bot.Length == 0)
        {
            found.Problem("app", $"{_team} has no GitHub App: run a-team app create {_team}, then install it");
            return;
        }
        var account = _repo.Contains('/') ? _repo[.._repo.LastIndexOf('/')] : _repo;
        if (!HasAppKey(account))
        {
            found.Problem("app", $"{bot} has no key in the login Keychain (service '{KeychainService}', account '{account}'): {KeyAdvice(account)}");
            return;
        }
        var (code, token) = Proc.Capture(Path.Combine(Root, "bin", "a-team"), "token", _team);
        if (code != 0)
        {
            found.Problem("app", $"{bot} can't get a token: {Unprefixed(token, "a-team token: ")}");
            return;
        }
        var app = new Board(_team, _config, () => _record, new Gh(token), DryRun);
        var line = $"identity: {bot} · token ok";
        var mirrored = _mirror.Length > 0;
        if (mirrored && !unreachable)
        {
            var project = app.Gql("""
                query($owner: String!, $number: Int!) {
                          organization(login: $owner) { projectV2(number: $number) { items(first: 1) { totalCount } } } }
                """, "--jq", ".data.organization.projectV2.items.totalCount");
            if (!project.Ok)
            {
                found.Problem("app", $"{bot} can't read project {_number}: grant the organisation's \"Projects: read and write\", and install the App on {_repo}");
                return;
            }
            if (!Granted("organization_projects"))
            {
                found.Problem("app", $"{bot} can only read project {_number}: grant the organisation's \"Projects: read and write\"");
                return;
            }
            line += $" · project {_number} read+write ok";
        }
        if (mirrored)
        {
            if (!app.PriorityReadable())
            {
                found.Problem("app", $"{bot} can't read the {_priority} field: grant the organisation's \"Issue Fields: read\", or `triggers` fails with \"Resource not accessible by integration\"");
                return;
            }
            line += $" · {_priority} readable";
        }
        if (!Granted("contents"))
        {
            found.Problem("app", $"{bot} can't push to {_repo}: grant the repository's \"Contents: read and write\"");
            return;
        }
        Console.Out.Write($"{line} · push access to {_repo} ok\n");
    }

    /// <summary>What setup asks GitHub to make the Status field: the team's statuses first, each keeping an option it has, then the
    /// field's others, less `dropped`, the defaults GitHub gives a new project while no item has them.</summary>
    internal static JsonObject StatusOptions(JsonNode field, IReadOnlyCollection<string> used, Func<string, string> onMirror)
    {
        var options = field["options"]!.AsArray();
        JsonNode? Named(string name) => options.FirstOrDefault(option => Json.Text(option?["name"]) == name);
        var wanted = new JsonArray();
        foreach (var (status, color, description) in StatusOptionsWanted)
        {
            var name = onMirror(status);
            wanted.Add(Named(name)?.DeepClone() ?? new JsonObject { ["name"] = name, ["color"] = color, ["description"] = description });
        }
        var names = wanted.Select(option => Json.Text(option?["name"])).ToHashSet();
        var dropped = new JsonArray();
        foreach (var other in options.Where(option => !names.Contains(Json.Text(option?["name"]))))
        {
            if (Json.Text(other?["name"]) is "Todo" or "In Progress" && !used.Contains(Json.Text(other?["name"])!))
                dropped.Add(other?.DeepClone());
            else
                wanted.Add(other?.DeepClone());
        }
        return new JsonObject { ["fieldId"] = field["id"]?.DeepClone(), ["singleSelectOptions"] = wanted, ["dropped"] = dropped };
    }

    /// <summary>The Status names the mirror's items are in; a read short twice ends the command.</summary>
    private HashSet<string> StatusesInUse()
    {
        var pages = BoardPages($$"""
            query($owner: String!, $number: Int!, $field: String!, $endCursor: String) {
                      {{_kind}}(login: $owner) { projectV2(number: $number) {
                        items(first: 100, after: $endCursor) { totalCount pageInfo { hasNextPage endCursor }
                          nodes { fieldValueByName(name: $field) { ... on ProjectV2ItemFieldSingleSelectValue { name } } } } } } }
            """, true, "-F", $"field={_field}") ?? throw new Stop(1, _mirrorError);
        Console.Error.Write(_mirrorError);
        return pages.SelectMany(page => page?["data"]?[_kind]?["projectV2"]?["items"]?["nodes"]?.AsArray() ?? [])
            .Select(node => Json.Text(node?["fieldValueByName"]?["name"])).OfType<string>().ToHashSet();
    }

    private void ApplyStatusOptions(JsonObject input)
    {
        if (Would("set the Status options"))
        {
            foreach (var option in input["singleSelectOptions"]!.AsArray())
                Console.Out.Write($"  {(Json.Truthy(option?["id"]) ? "keep" : "add ")}  {Json.Text(option?["name"])}\n");
            foreach (var option in input["dropped"]!.AsArray())
                Console.Out.Write($"  drop  {Json.Text(option?["name"])}\n");
            return;
        }
        var kept = input.DeepClone().AsObject();
        kept.Remove("dropped");
        var body = new JsonObject
        {
            ["variables"] = new JsonObject { ["input"] = kept },
            ["query"] = "mutation($input: UpdateProjectV2FieldInput!) { updateProjectV2Field(input: $input) { clientMutationId } }",
        };
        Must(Post("set the Status options", Json.Pretty(body) + "\n", "api", "graphql", "--input", "-"));
        Console.Out.Write($"Status options set on {_owner} project {_number}\n");
    }

    /// <summary>Gives the Project the Status options and text fields the team needs, or in a dry run lists them.</summary>
    internal void SetUpProject()
    {
        if (_mirror.Length == 0)
        {
            if (_config["mirror"]?.GetValueKind() != JsonValueKind.False)
                throw new Die($"{_team}.json names no GitHub Project: pick one, or None, under Mirror in its settings");
            return;
        }
        var raw = ProjectRaw();
        if (!raw.Ok) throw new Stop(1, raw.Err);
        var meta = MetaOf(raw.Out) ?? throw new Stop(4, raw.Err);
        Console.Error.Write(raw.Err);
        var field = meta["field"];
        if (!Json.Truthy(field?["id"])) throw new Die($"no single-select field '{_field}' on {_owner} project {_number}");
        if (Json.Text(meta["clash"]!.AsArray().FirstOrDefault()) is { } clash) throw new Die(FieldClash(clash));
        ApplyStatusOptions(StatusOptions(field!, StatusesInUse(), StatusOnMirror));
        foreach (var key in new[] { "rank", "theme" })
        {
            if (Json.Truthy(meta[key]?["id"])) continue;
            var name = TextFieldName(key);
            if (Would($"add the {name} field"))
            {
                Console.Out.Write($"  add   field {name}\n");
                continue;
            }
            Must(AddTextField(Json.Text(meta["id"])!, name));
            Console.Out.Write($"Field {name} added to {_owner} project {_number}\n");
        }
    }

    /// <summary>Says how many of the repo's open issues aren't on the board yet, and so will arrive in Triage.</summary>
    internal void SayArrivals()
    {
        if (OpenIssues() is not { } open) return;
        var had = _record.Query($"SELECT item FROM board WHERE team = {TeamQ};").Select(row => row.First).ToHashSet();
        var coming = open.Count(issue => Json.Text(issue?["repository"]?["nameWithOwner"]) == _repo && !had.Contains(issue?["number"]?.ToJsonString()));
        if (coming > 0)
            Say($"{coming} open issue{(coming == 1 ? "" : "s")} on {_repo} will arrive in Triage");
    }

    /// <summary>Creates the labels the team needs, and puts right the ones that have changed.</summary>
    internal void SetUpLabels()
    {
        var existing = Json.Parse(Must(_gh.Run("label", "list", "-R", _repo, "--limit", "500", "--json", "name,color,description")).Out)!.AsArray();
        foreach (var (name, color, description, _) in TeamLabels)
        {
            var current = existing.FirstOrDefault(label => Json.Text(label?["name"]) == name);
            if (current is null)
            {
                Must(Write($"create label {name}", "label", "create", name, "-R", _repo, "--color", color, "--description", description));
                Say($"created label {name}");
                continue;
            }
            var differs = new List<string>();
            if (Json.Text(current["color"]) != color) differs.Add("colour");
            if (Json.Text(current["description"]) != description) differs.Add("description");
            if (differs.Count == 0) continue;
            Must(Write($"update label {name}", "label", "edit", name, "-R", _repo, "--color", color, "--description", description));
            Say($"updated label {name}: {string.Join(" and ", differs)}");
        }
    }
}
