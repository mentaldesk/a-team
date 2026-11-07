using System.Globalization;
using System.Text.Json.Nodes;

namespace ATeam.Core;

/// <summary>
/// a-team board, for what's been ported: everything else runs in board.sh, which comes back here for the board's state.
/// </summary>
internal static class Program
{
    /// <summary>What board.sh asks of the core, on the record it has open.</summary>
    private static readonly HashSet<string> Internal = ["__sync", "__items", "__mirror-push", "__format"];

    /// <summary>The commands that speak as their first argument, a role's App or you.</summary>
    private static readonly HashSet<string> Spoken =
    [
        "accept", "add", "approve", "attach", "audited", "claim", "comment", "covered", "decide", "decision", "decline", "depends", "edit",
        "feedback", "finish", "fix", "gave", "hold", "mine", "move", "new", "pictures", "priority", "recommend", "reconfirm", "release",
        "replace", "review", "secret", "seeded", "shelve", "skip", "spent", "task", "theme", "triggers", "unblock", "undepend", "unlink",
        "vision-review",
    ];

    private static int Main(string[] args)
    {
        if (args is ["route"])
        {
            foreach (var (role, from, to) in StatusRoute.All)
                Console.WriteLine($"{role}:{from}>{to}");
            return 0;
        }
        if (args is ["check"])
        {
            using var memory = Record.Open(":memory:");
            Console.WriteLine($"sqlite {memory.Scalar("SELECT sqlite_version();")}, json {memory.Scalar("SELECT json_extract('{\"a\": 1}', '$.a');")}");
            return 0;
        }
        var what = args.FirstOrDefault(arg => arg != "--dry-run" && arg != args.FirstOrDefault(first => first != "--dry-run")) ?? "";
        string? scratch = null;
        try
        {
            var code = Run(args, made => scratch = made);
            Trace($"handled {what}");
            return code;
        }
        catch (Fallback reason)
        {
            return Hand(args, $"{what}: {reason.Message}");
        }
        catch (Die refusal)
        {
            Trace($"handled {what}");
            Console.Error.WriteLine($"board.sh: {refusal.Message}");
            return 1;
        }
        catch (Stop stopped)
        {
            Trace($"handled {what}");
            Console.Error.Write(stopped.Message);
            return stopped.Code;
        }
        catch (Exception problem)
        {
            Console.Error.WriteLine($"board.sh: {problem.Message}");
            return 1;
        }
        finally
        {
            if (scratch is not null) File.Delete(scratch);
        }
    }

    private static string RootDir => Environment.GetEnvironmentVariable("A_TEAM_ROOT") ?? "";

    private static string Shell => Path.Combine(RootDir, "scripts", "board.sh");

    private static int Hand(string[] args, string why)
    {
        Trace($"fallback {why}");
        var code = Libc.Exec("bash", [Shell, .. args]);
        Console.Error.WriteLine($"a-team-core: couldn't run board.sh (errno {code})");
        return 127;
    }

    private static void Trace(string line)
    {
        if (Environment.GetEnvironmentVariable("A_TEAM_CORE_TRACE") is { Length: > 0 } file)
            File.AppendAllText(file, line + "\n");
    }

    private static string Dir(string variable, string xdg, string under)
    {
        if (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } set) return set;
        if (Environment.GetEnvironmentVariable(xdg) is { Length: > 0 } home) return Path.Combine(home, "a-team");
        return Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? "", under, "a-team");
    }

    /// <summary>A team's config, or null if the shell couldn't read its settings either, with a parser's words if it stopped one.</summary>
    private static JsonObject? ReadConfig(string file, out string? trouble)
    {
        trouble = null;
        try
        {
            var text = File.ReadAllText(file);
            return !string.IsNullOrWhiteSpace(text) && Json.Parse(text) is JsonObject config
                && config["project"] is null or JsonObject && config["app"] is null or JsonObject ? config : null;
        }
        catch (Exception problem) when (problem is System.Text.Json.JsonException or ArgumentException)
        {
            trouble = problem.Message;
            return null;
        }
    }

    private static int Run(string[] args, Action<string> scratch)
    {
        var dry = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("A_TEAM_DRY_RUN"));
        var words = args;
        if (words.FirstOrDefault() == "--dry-run")
        {
            dry = true;
            words = words[1..];
        }
        if (words.Length < 2)
            throw new Fallback("a usage error");
        var (team, command) = (words[0], words[1]);
        var rest = words[2..];
        dry |= command == "setup" && rest.FirstOrDefault() == "--dry-run";
        var commands = Commands.All();
        if (!Internal.Contains(command) && !commands.ContainsKey(command))
            throw new Fallback("not ported yet");

        var configDir = Dir("A_TEAM_CONFIG", "XDG_CONFIG_HOME", ".config");
        var configFile = Path.Combine(configDir, "teams", team + ".json");
        if (!File.Exists(configFile))
            throw new Fallback("no config");
        if (ReadConfig(configFile, out var trouble) is not { } config)
        {
            if (command == "check") return Commands.CheckUnreadable(team, configFile, trouble);
            if (Internal.Contains(command)) throw new Die(Commands.Unreadable(team, configFile, trouble));
            throw new Fallback("a config it can't read");
        }

        var state = Dir("A_TEAM_STATE", "XDG_STATE_HOME", ".local/state");
        Board.State = state;
        Board.Root = RootDir;
        Board.ConfigDir = configDir;
        var token = command == "lead-next" ? Speaker(team, command, "lead", state, config)
            : Spoken.Contains(command) ? Speaker(team, command, SpeakerOf(command, rest), state, config) : null;

        var history = Environment.GetEnvironmentVariable("A_TEAM_HISTORY");
        if (string.IsNullOrEmpty(history))
            history = Scratch(dry, state, scratch) ?? Path.Combine(state, "history.db");
        var existed = File.Exists(history);
        Record Open()
        {
            Directory.CreateDirectory(state);
            return Record.Open(history);
        }
        var opened = new Lazy<Record>(Open);
        try
        {
            var board = new Board(team, config, () => opened.Value, new Gh(token), dry) { RecordFound = existed };
            if (Internal.Contains(command))
            {
                board.SkipSync = command == "__items";
                return command switch
                {
                    "__format" => InternalFormat(board),
                    "__sync" => InternalSync(board, rest),
                    "__items" => InternalItems(board, rest),
                    _ => InternalPush(board, rest),
                };
            }
            if (command != "check") board.FormatGate();
            return commands[command](board, rest);
        }
        finally
        {
            if (opened.IsValueCreated) opened.Value.Dispose();
        }
    }

    private static int InternalFormat(Board board)
    {
        board.FormatGate();
        return 0;
    }

    private static int InternalSync(Board board, string[] rest)
    {
        board.Sync(cached: rest.Contains("--cached"));
        return 0;
    }

    private static int InternalItems(Board board, string[] rest)
    {
        Console.Out.Write(Json.Pretty(board.Items(open: rest.Contains("open"))) + "\n");
        return 0;
    }

    private static int InternalPush(Board board, string[] rest)
    {
        board.MirrorPush(rest.FirstOrDefault());
        return 0;
    }

    /// <summary>A dry run works on a copy of the record, so it reads as a real run would and changes nothing. With no record
    /// yet it keeps its own between commands, so the board is moved over once and not on every command.</summary>
    private static string? Scratch(bool dry, string state, Action<string> scratch)
    {
        if (!dry) return null;
        var real = Path.Combine(state, "history.db");
        if (!File.Exists(real))
        {
            Directory.CreateDirectory(state);
            return Path.Combine(state, "dry-run.db");
        }
        var temp = Environment.GetEnvironmentVariable("TMPDIR");
        var copy = Path.Combine(string.IsNullOrEmpty(temp) ? "/tmp" : temp, $"a-team-history.{Path.GetRandomFileName()}");
        File.Copy(real, copy);
        scratch(copy);
        return copy;
    }

    private static string SpeakerOf(string command, string[] rest) =>
        command == "triggers" && rest.FirstOrDefault() == "--sweep" ? rest.ElementAtOrDefault(1) ?? "" : rest.FirstOrDefault() ?? "";

    /// <summary>The token a role's command acts with. As you, whatever this shell's login is, unless this is a team's run.</summary>
    private static string? Speaker(string team, string command, string speaker, string state, JsonObject config)
    {
        if (speaker == "you")
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("A_TEAM_RUN_TEAM")))
                throw new Die($"only the stakeholder can {command} as you, and this is a team's run");
            return null;
        }
        if (!StatusRoute.Roles.Contains(speaker))
            return null;
        var slug = Json.Text(config["app"]?["slug"]);
        var acts = $"{speaker} acts as {(string.IsNullOrEmpty(slug) ? "the App" : $"{slug}[bot]")}, which can't get a token";
        if (!Json.Truthy(config["app"]?["id"]))
            throw new Die($"{acts}: no GitHub App: run a-team app create {team}, then install it");
        return CachedToken(state, team) ?? MintToken(team, acts);
    }

    private static string? CachedToken(string state, string team)
    {
        var cache = Path.Combine(state, team, "token.json");
        if (!File.Exists(cache)) return null;
        try
        {
            var cached = Json.Parse(File.ReadAllText(cache));
            return Json.Text(cached?["token"]) is { Length: > 0 } token
                && Json.Text(cached?["expires_at"]) is { } expires
                && DateTimeOffset.TryParse(expires, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)
                && at - DateTimeOffset.UtcNow >= TimeSpan.FromSeconds(300)
                ? token : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static string MintToken(string team, string acts)
    {
        var (code, said) = Proc.Capture(Path.Combine(RootDir, "bin", "a-team"), "token", team);
        if (code == 0) return said;
        const string prefix = "a-team token: ";
        throw new Die($"{acts}: {(said.StartsWith(prefix, StringComparison.Ordinal) ? said[prefix.Length..] : said)}");
    }
}
