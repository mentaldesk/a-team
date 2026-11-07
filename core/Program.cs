using System.Globalization;
using System.Text.Json.Nodes;

namespace ATeam.Core;

/// <summary>
/// a-team board, for what's been ported: everything else, and anything a ported command can't finish here, runs in
/// board.sh, which is always the same command to its caller.
/// </summary>
internal static class Program
{
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
        var what = args.Length > 1 ? args[1] : "";
        Board? board = null;
        try
        {
            var code = Run(args, made => board = made);
            Trace($"handled {what}");
            return code;
        }
        catch (Die refusal)
        {
            Trace($"handled {what}");
            Console.Error.WriteLine($"board.sh: {refusal.Message}");
            return 1;
        }
        catch (Fallback reason)
        {
            return Hand(args, $"{what}: {reason.Message}");
        }
        catch (Exception problem) when (board is not { Committed: true })
        {
            return Hand(args, $"{what}: {problem.GetType().Name}: {problem.Message}");
        }
        catch (Exception problem)
        {
            Console.Error.WriteLine($"board.sh: {problem.Message}");
            return 1;
        }
    }

    private static int Hand(string[] args, string why)
    {
        Trace($"fallback {why}");
        var root = Environment.GetEnvironmentVariable("A_TEAM_ROOT") ?? "";
        var code = Libc.Exec("bash", [Path.Combine(root, "scripts", "board.sh"), .. args]);
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

    private static int Run(string[] args, Action<Board> made)
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("A_TEAM_DRY_RUN")))
            throw new Fallback("a dry run");
        if (args.Length < 2 || args[0] == "--dry-run")
            throw new Fallback("a dry run or a usage error");
        var (team, command) = (args[0], args[1]);
        var rest = args[2..];
        if (command is not ("list" or "move" or "add"))
            throw new Fallback("not ported yet");

        var configFile = Path.Combine(Dir("A_TEAM_CONFIG", "XDG_CONFIG_HOME", ".config"), "teams", team + ".json");
        if (!File.Exists(configFile))
            throw new Fallback("no config");
        if (Json.Parse(File.ReadAllText(configFile)) is not JsonObject config)
            throw new Fallback("a config that isn't an object");

        var state = Dir("A_TEAM_STATE", "XDG_STATE_HOME", ".local/state");
        Board.State = state;
        var token = command == "list" ? null : Speaker(rest, state, team, config);
        var history = Path.Combine(state, "history.db");
        if (!File.Exists(history))
            throw new Fallback("no record yet");

        using var record = Record.Open(history);
        var board = new Board(team, config, record, new Gh(token));
        made(board);
        board.Gate();
        return command switch
        {
            "list" => Commands.List(board, rest),
            "move" => Commands.Move(board, rest),
            _ => Commands.Add(board, rest, config),
        };
    }

    /// <summary>The token a role's command acts with, while the cached one is good: minting one is token.sh's.</summary>
    private static string? Speaker(string[] rest, string state, string team, JsonObject config)
    {
        var speaker = rest.Length > 0 ? rest[0] : "";
        if (speaker == "you")
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("A_TEAM_RUN_TEAM")))
                throw new Fallback("you, in a team's run");
            return null;
        }
        if (!StatusRoute.Roles.Contains(speaker))
            return null;
        if (!Json.Truthy(config["app"]?["id"]))
            throw new Fallback("a role with no App");
        var cache = Path.Combine(state, team, "token.json");
        if (!File.Exists(cache))
            throw new Fallback("no cached token");
        var cached = Json.Parse(File.ReadAllText(cache));
        if (Json.Text(cached?["token"]) is not { Length: > 0 } token
            || Json.Text(cached?["expires_at"]) is not { } expires
            || !DateTimeOffset.TryParse(expires, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)
            || at - DateTimeOffset.UtcNow < TimeSpan.FromSeconds(300))
            throw new Fallback("the cached token is ending");
        return token;
    }
}
