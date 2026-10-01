using System.Text.Json;
using System.Text.RegularExpressions;
using Terminal.Gui.Drawing;

namespace ATeam.Dashboard;

/// <summary>One question on the way to a new team's first run. <paramref name="Work"/> is what answering yes runs,
/// passing on each line it prints; <paramref name="Load"/> fills <paramref name="Lines"/> in once it's read.
/// <paramref name="ShowOutput"/> keeps the dialog open on what the work printed, for you to read.
/// <paramref name="Choose"/> offers Yes and No as options, leaving Esc to cancel the new team.</summary>
public sealed record Step(
    string Title,
    IReadOnlyList<string> Lines,
    string Yes,
    string No,
    Func<Action<string>, Task<string?>>? Work = null,
    Func<Task<(IReadOnlyList<string>? Lines, string? Failure)>>? Load = null,
    bool ShowOutput = false,
    bool Choose = false);

/// <summary>How a <see cref="Step"/> was left: done, skipped, cancelling the new team, or going back a step.</summary>
public enum Answer
{
    Done,
    Skipped,
    Cancelled,
    Back,
}

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

    /// <summary>Rewrites a new team's file with the form's values once Back has returned to it, or says why it couldn't.</summary>
    public string? Redo(TeamConfigs teams, string team, string name, TeamSettings settings)
    {
        try
        {
            teams.Redo(team, name, settings);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return $"Couldn't save {name}: {e.Message}";
        }
    }

    /// <summary>Offers each step the new team still needs, in order, through <paramref name="ask"/>. Returns the line to
    /// leave on the Teams page, or null where Back went past the first step, to the form.</summary>
    public (string Message, Schemes Scheme)? Follow(TeamConfigs teams, string team, Func<Step, Answer> ask)
    {
        string Checkout() => teams.Settings(team).CheckoutPath;
        bool Cloned() => Directory.Exists(TeamSettings.Expand(Checkout(), home));
        List<Func<Step?>> steps =
        [
            () => Cloned() ? null : CloneStep(teams.Settings(team), TeamSettings.Expand(Checkout(), home)),
            () => teams.HasApp(team) ? null : AppStep(teams, team, teams.Settings(team)),
            () => teams.Settings(team).NoProject ? ProjectStep(teams, team, teams.Settings(team)) : null,
            () => BoardStep(team, teams.Settings(team)),
            () => teams.HasApp(team) ? WorkStep(team) : null,
        ];
        const int projectStep = 2;
        Stack<int> shown = [];
        for (var at = 0; at < steps.Count;)
        {
            if (steps[at]() is not { } step)
            {
                at++;
                continue;
            }
            var answer = ask(step);
            if (answer == Answer.Back)
            {
                do
                {
                    if (!shown.TryPop(out at))
                        return null;
                }
                while (steps[at]() is null);
                continue;
            }
            shown.Push(at);
            if (at == projectStep && teams.Settings(team).NoProject)
                return ($"{team} is paused: it has no project to move its work across. Pick one in its settings.", Schemes.Accent);
            if (at == steps.Count - 1)
            {
                if (answer == Answer.Cancelled)
                {
                    teams.Delete(team);
                    return ($"{team} is cancelled. Its clone, App and project are still there.", Schemes.Base);
                }
                if (answer == Answer.Done)
                    teams.SetWorking(team, true);
            }
            at++;
        }

        if (!teams.HasApp(team))
            return ($"{team} is paused: it won't run until it has a GitHub App (a-team app create {team}).", Schemes.Accent);
        if (!Cloned())
            return ($"{Checkout()} isn't there, so the agents would have nothing to work in.", Schemes.Accent);
        return teams.IsPaused(team) ? ($"{team} is paused. Press p when you want it to start.", Schemes.Base) : ($"{team} is working.", Schemes.Base);
    }

    private Step CloneStep(TeamSettings settings, string clonePath) =>
        new(
            $"Clone {settings.Repo}?",
            [$"{settings.CheckoutPath} isn't there, so the agents would have nothing to work in.", $"gh repo clone {settings.Repo} {settings.CheckoutPath}"],
            "clone",
            "skip",
            line => gh.Stream(line, "repo", "clone", settings.Repo, clonePath, "--", "--progress"));

    private Step BoardStep(string team, TeamSettings settings) =>
        new(
            $"Set up {team}'s board?",
            ["Reading what setting it up would change…"],
            "set it up",
            "skip",
            line => aTeam.Stream(line, "board", team, "setup"),
            async () =>
            {
                var plan = aTeam.Read("board", team, "setup", "--dry-run");
                var project = Project(settings);
                return (await plan).Failure is { } failure
                    ? (null, $"Can't set up {team}'s board: {failure}")
                    : (SetupPlan((await plan).Output, await project, settings.Repo), null);
            });

    private Step WorkStep(string team)
    {
        List<string> lines = [$"{team}'s board is ready. Its lead will start looking for opportunities and its dev will start building what you approve."];
        if (!dispatcherInstalled())
            lines.Add("No dispatcher is installed on this Mac, though, so nothing will start it until you run a-team install.");
        return new Step("Get to work?", lines, "Get to work", "Keep the team paused for now", Choose: true);
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

    private Step ProjectStep(TeamConfigs teams, string team, TeamSettings settings)
    {
        var owner = settings.RepoOwner;
        return new Step(
            $"Create a project for {team}?",
            [
                $"{team} moves its work across a GitHub Project's board, and hasn't got one yet.",
                $"gh project create --owner {owner} --title {team}",
                $"Then link it to {settings.Repo}.",
            ],
            "create it",
            "skip",
            line => CreateProject(teams, team, line));
    }

    /// <summary>Creates the team's project unless an earlier try already did, and links it to the repo.</summary>
    private async Task<string?> CreateProject(TeamConfigs teams, string team, Action<string> line)
    {
        var settings = teams.Settings(team);
        var owner = settings.RepoOwner;
        if (settings.NoProject)
        {
            var made = await gh.Read("project", "create", "--owner", owner, "--title", team, "--format", "json");
            if (made.Failure is { } failure)
                return failure;
            if (CreatedNumber(made.Output) is not { } created)
                return "gh project create didn't say which number the new project got.";
            teams.Save(team, settings, settings with { ProjectOwner = owner, ProjectNumber = created });
            settings = teams.Settings(team);
            line($"Created project {new ProjectChoice(owner, created, team)}.");
        }
        return await gh.Stream(line, "project", "link", $"{settings.ProjectNumber}", "--owner", owner, "--repo", settings.Repo);
    }

    private static int? CreatedNumber(string output)
    {
        try
        {
            using var project = JsonDocument.Parse(output);
            return project.RootElement.TryGetProperty("number", out var number) &&
                   number.ValueKind == JsonValueKind.Number && number.TryGetInt32(out var value)
                ? value
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The team's project with its title, or with none where the list can't be read.</summary>
    private async Task<ProjectChoice> Project(TeamSettings settings)
    {
        var number = settings.ProjectNumber ?? 0;
        var listed = ProjectChoice.Parse(await Projects(settings.ProjectOwner), settings.ProjectOwner);
        return listed?.FirstOrDefault(project => project.Number == number) ?? new ProjectChoice(settings.ProjectOwner, number, "");
    }

    /// <summary>What <c>board setup --dry-run</c> printed, as the confirmation lists it: the Status options it would
    /// add or keep, then the labels it would create or update.</summary>
    public static IReadOnlyList<string> SetupPlan(string output, ProjectChoice project, string repo)
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
            $"On project {project}, field '{StatusField}':",
            .. options,
            $"On {repo}:",
            .. labels.Count == 0 ? ["  labels already set up"] : labels,
        ];
    }

    [GeneratedRegex(@"^\s+(keep|add|drop)\s+(.+)$")]
    private static partial Regex OptionLine();

    [GeneratedRegex(@"^(?:\(dry run\) )?(created|updated) label (.+)$")]
    private static partial Regex LabelLine();
}
