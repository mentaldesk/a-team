using System.Text.RegularExpressions;
using Terminal.Gui.Drawing;

namespace ATeam.Dashboard;

/// <summary>One question on the way to a new team's first run. <paramref name="Work"/> is what answering yes runs,
/// passing on each line it prints; <paramref name="Load"/> fills <paramref name="Lines"/> in once it's read.
/// <paramref name="ShowOutput"/> keeps the dialog open on what the work printed, for you to read.</summary>
public sealed record Step(
    string Title,
    IReadOnlyList<string> Lines,
    string Yes,
    string No,
    Func<Action<string>, Task<string?>>? Work = null,
    Func<Task<(IReadOnlyList<string>? Lines, string? Failure)>>? Load = null,
    bool ShowOutput = false);

/// <summary>Everything after a new team's file is written: cloning its repo, its GitHub App, setting its board up,
/// and asking it to get to work. Each is offered, never done unasked.</summary>
public sealed partial class TeamStart(string example, TeamCommand aTeam, TeamCommand gh, string home, Func<bool> dispatcherInstalled)
{
    internal const string StatusField = "Status";

    /// <summary>The new team form's starting values, from <c>examples/team.json</c>.</summary>
    public TeamSettings Defaults() => TeamSettings.New(File.ReadAllBytes(example), "");

    /// <summary>Who's signed in to GitHub, the new team's first stakeholder; empty where it can't be read.</summary>
    public async Task<string> Me() => (await gh.Read("api", "user", "--jq", ".login")).Output.Trim();

    public Task<Reading> Projects(string owner) =>
        gh.Read("project", "list", "--owner", owner, "--format", "json", "--limit", "100");

    /// <summary>Writes the new team's file, or says why it couldn't.</summary>
    public string? Create(TeamConfigs teams, string team, TeamSettings settings)
    {
        try
        {
            teams.Create(team, File.ReadAllBytes(example), settings);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return $"Couldn't create {team}: {e.Message}";
        }
    }

    /// <summary>Offers each step the new team still needs, in order, through <paramref name="ask"/>, which answers
    /// whether it was done. Returns the line to leave on the Teams page.</summary>
    public (string Message, Schemes Scheme) Follow(TeamConfigs teams, string team, Func<Step, bool> ask)
    {
        var settings = teams.Settings(team);
        var checkout = settings.CheckoutPath;
        var clonePath = TeamSettings.Expand(checkout, home);
        if (!Directory.Exists(clonePath))
            ask(new Step(
                $"Clone {settings.Repo}?",
                [$"{checkout} isn't there, so the agents would have nothing to work in.", $"gh repo clone {settings.Repo} {checkout}"],
                "clone",
                "skip",
                line => gh.Stream(line, "repo", "clone", settings.Repo, clonePath, "--", "--progress")));

        if (!teams.HasApp(team))
            ask(AppStep(teams, team, settings));
        var hasApp = teams.HasApp(team);

        ask(new Step(
            $"Set up {team}'s board?",
            ["Reading what setting it up would change…"],
            "set it up",
            "skip",
            line => aTeam.Stream(line, "board", team, "setup"),
            async () =>
            {
                var plan = await aTeam.Read("board", team, "setup", "--dry-run");
                return plan.Failure is { } failure
                    ? (null, $"Can't set up {team}'s board: {failure}")
                    : (SetupPlan(plan.Output, settings.ProjectOwner, settings.ProjectNumber, settings.Repo), null);
            }));

        string? warning = Directory.Exists(clonePath) ? null : $"{checkout} isn't there, so the agents would have nothing to work in.";
        if (!hasApp)
            return ($"{team} is paused: it won't run until it has a GitHub App (a-team app create {team}).", Schemes.Accent);

        List<string> lines = [$"{team}'s board is ready. Its lead will start looking for opportunities and its dev will start building what you approve."];
        if (!dispatcherInstalled())
            lines.Add("No dispatcher is installed on this Mac, though, so nothing will start it until you run a-team install.");
        if (!ask(new Step("Get to work?", lines, "get to work", "not yet")))
            return warning is not null ? (warning, Schemes.Accent) : ($"{team} is paused. Press p when you want it to start.", Schemes.Base);
        teams.SetWorking(team, true);
        return warning is not null ? (warning, Schemes.Accent) : ($"{team} is working.", Schemes.Base);
    }

    private Step AppStep(TeamConfigs teams, string team, TeamSettings settings)
    {
        var owner = settings.RepoOwner;
        var reuse = teams.WithApp(owner, except: team);
        return new Step(
            reuse is null ? $"Give {team} a GitHub App?" : $"Use {owner}'s GitHub App for {team}?",
            [
                $"{team} works as its own GitHub App, and won't run without one.",
                reuse is null
                    ? $"a-team app create {team} registers one under {owner}, in your browser."
                    : $"{reuse} already has {owner}'s App, so {team} can use the same one.",
                $"Then install it on {settings.Repo}, on the page that opens.",
            ],
            reuse is null ? "create it" : "use it",
            "skip",
            line => aTeam.Stream(line, "app", "create", team),
            ShowOutput: true);
    }

    /// <summary>What <c>board setup --dry-run</c> printed, as the confirmation lists it: the Status options it would
    /// add or keep, then the labels it would create or update.</summary>
    public static IReadOnlyList<string> SetupPlan(string output, string owner, int? number, string repo)
    {
        List<string> options = [];
        List<string> labels = [];
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (OptionLine().Match(line) is { Success: true } option)
                options.Add($"  {option.Groups[1].Value.PadRight(6)}{option.Groups[2].Value.Trim()}");
            else if (LabelLine().Match(line) is { Success: true } label)
                labels.Add($"  {(label.Groups[1].Value == "created" ? "create" : "update")} label {label.Groups[2].Value.Trim()}");
        }
        return
        [
            $"On {owner} project {number}, field '{StatusField}':",
            .. options,
            $"On {repo}:",
            .. labels.Count == 0 ? ["  labels already set up"] : labels,
        ];
    }

    [GeneratedRegex(@"^\s+(keep|add)\s+(.+)$")]
    private static partial Regex OptionLine();

    [GeneratedRegex(@"^(?:\(dry run\) )?(created|updated) label (.+)$")]
    private static partial Regex LabelLine();
}
