using System.Runtime.Versioning;
using System.Text.Json;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;

namespace ATeam.Dashboard.Tests;

[UnsupportedOSPlatform("windows")]
public class TeamStartTests : IDisposable
{
    public static bool HasAShell => !OperatingSystem.IsWindows();

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"a-team-{Guid.NewGuid():n}");
    private readonly TeamConfigs _teams;

    public TeamStartTests() => _teams = new TeamConfigs(Path.Combine(_root, "config"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static string Example
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "examples", "team.json")))
                    return Path.Combine(dir.FullName, "examples", "team.json");
            throw new FileNotFoundException("examples/team.json");
        }
    }

    [Fact]
    public void A_new_team_starts_from_the_example_s_vision_and_limits_with_you_as_its_stakeholder()
    {
        var settings = TeamSettings.New(File.ReadAllBytes(Example), "jamescrosswell");

        Assert.Equal(("", "", (int?)null, "", ""), (settings.Repo, settings.ProjectOwner, settings.ProjectNumber, settings.Workdir, settings.Try));
        Assert.Equal("docs/vision.md", settings.Vision);
        Assert.Equal((3, 3, 6, 6, 3), (settings.Worktrees, settings.Pitched, settings.Exploring, settings.Ideas, settings.ReadyFloor));
        Assert.Equal(["jamescrosswell"], settings.Stakeholders);
        Assert.Empty(settings.Skills);
        Assert.False(settings.Working);
    }

    [Fact]
    public void A_new_team_s_file_is_the_example_with_the_form_s_values_its_checkout_under_workdir_and_paused()
    {
        var settings = TeamSettings.New(File.ReadAllBytes(Example), "jamescrosswell") with
        {
            Repo = "mentaldesk/fretty",
            ProjectOwner = "mentaldesk",
            ProjectNumber = 7,
            Workdir = "~/code/fretty",
            Working = true,
        };

        _teams.Create("fretty", File.ReadAllBytes(Example), settings);

        using var file = JsonDocument.Parse(File.ReadAllText(Path.Combine(_teams.TeamsDirectory, "fretty.json")));
        var root = file.RootElement;
        Assert.Equal("mentaldesk/fretty", root.GetProperty("repo").GetString());
        Assert.Equal(7, root.GetProperty("project").GetProperty("number").GetInt32());
        Assert.Equal("Status", root.GetProperty("project").GetProperty("statusField").GetString());
        Assert.Equal("~/code/fretty", root.GetProperty("workdir").GetString());
        Assert.Equal("~/code/fretty/main", root.GetProperty("checkout").GetString());
        Assert.Equal("jamescrosswell", root.GetProperty("stakeholders")[0].GetString());
        Assert.False(root.GetProperty("dispatch").GetProperty("enabled").GetBoolean());
        Assert.Equal(180, root.GetProperty("dispatch").GetProperty("creativeEvery").GetInt32());
        Assert.Equal(["fretty"], _teams.Names());
        Assert.True(_teams.IsPaused("fretty"));
    }

    [Fact]
    public void Creating_a_team_never_replaces_one_that_s_there()
    {
        Directory.CreateDirectory(_teams.TeamsDirectory);
        File.WriteAllText(Path.Combine(_teams.TeamsDirectory, "fretty.json"), "{}");

        Assert.Throws<IOException>(() => _teams.Create("fretty", File.ReadAllBytes(Example), TeamSettings.New(File.ReadAllBytes(Example), "")));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(_teams.TeamsDirectory, "fretty.json")));
    }

    [Theory]
    [InlineData("", "Name is required.")]
    [InlineData("a-team", "There's already a team named a-team.")]
    [InlineData("my team", "my team can't be a file name: use letters, digits, '.', '_' and '-'.")]
    [InlineData("../x", "../x can't be a file name: use letters, digits, '.', '_' and '-'.")]
    [InlineData("fretty", null)]
    public void A_new_team_s_name_must_be_free_and_usable_as_a_file_name(string name, string? refusal) =>
        Assert.Equal(refusal, TeamSettings.NameRefusal(name, ["a-team"]));

    [Fact]
    public void The_setup_dry_run_becomes_the_list_the_confirmation_shows()
    {
        const string output = """
              keep  Idea
              add   Exploring
              add   Pitched
              keep  Done
              drop  Todo
            (dry run) created label pitch
            (dry run) created label a-team:dev
            (dry run) updated label blocked: colour and description
            """;

        Assert.Equal(
            [
                "On project fretty (mentaldesk #7), field 'Status':",
                "  keep  Idea",
                "  add   Exploring",
                "  add   Pitched",
                "  keep  Done",
                "  drop  Todo",
                "On mentaldesk/fretty:",
                "  create label pitch",
                "  create label a-team:dev",
                "  update label blocked: colour and description",
            ],
            TeamStart.SetupPlan(output, new ProjectChoice("mentaldesk", 7, "fretty"), "mentaldesk/fretty"));
    }

    [Fact(Skip = "a-team is a shell script", SkipUnless = nameof(HasAShell))]
    public void Every_step_is_offered_in_turn_and_Get_to_work_starts_the_team()
    {
        Team("fretty", app: false);
        var start = Start();
        List<string> asked = [];

        var said = start.Follow(_teams, "fretty", step =>
        {
            asked.Add(step.Title);
            return step.Title == "Get to work?" ? Answer.Done : Answer.Skipped;
        });

        Assert.Equal(["Clone mentaldesk/fretty?", "Give fretty a GitHub App?", "Set up fretty's board?"], asked);
        Assert.Equal(("fretty won't run until it has a GitHub App (a-team app create fretty).", Schemes.Accent), said);
        Assert.True(_teams.IsPaused("fretty"));
        Assert.Empty(Ran());
    }

    [Fact(Skip = "a-team is a shell script", SkipUnless = nameof(HasAShell))]
    public void The_clone_and_App_steps_are_skipped_when_the_checkout_and_App_are_there()
    {
        Team("fretty", app: true, checkout: true);
        List<string> asked = [];

        Start().Follow(_teams, "fretty", step =>
        {
            asked.Add(step.Title);
            return Answer.Skipped;
        });

        Assert.Equal(["Set up fretty's board?", "Get to work?"], asked);
    }

    [Fact(Skip = "a-team is a shell script", SkipUnless = nameof(HasAShell))]
    public void An_owner_s_App_already_installed_on_the_repo_is_reused_without_asking()
    {
        Team("tuicode", app: true);
        Team("fretty", app: false, checkout: true);
        List<string> asked = [];

        Start().Follow(_teams, "fretty", step =>
        {
            asked.Add(step.Title);
            return Answer.Skipped;
        });

        Assert.Equal(["Set up fretty's board?", "Get to work?"], asked);
        Assert.True(_teams.HasApp("fretty"));
        Assert.Contains("app create fretty --no-open", Ran());
    }

    [Fact(Skip = "a-team is a shell script", SkipUnless = nameof(HasAShell))]
    public void An_owner_s_App_not_yet_installed_on_the_repo_is_written_in_and_its_install_offered()
    {
        Team("tuicode", app: true);
        Team("fretty", app: false, checkout: true);
        Step? offered = null;

        var said = Start(installed: false).Follow(_teams, "fretty", step =>
        {
            offered ??= step;
            return step.Title == "Get to work?" ? Answer.Done : Answer.Skipped;
        });

        Assert.Equal("Install mentaldesk's GitHub App on mentaldesk/fretty?", offered?.Title);
        Assert.Equal("open the install page", offered?.Yes);
        Assert.Contains("fretty uses the App tuicode has, and won't run until it's installed on mentaldesk/fretty.", offered!.Lines);
        Assert.True(_teams.HasApp("fretty"));
        Assert.False(_teams.IsPaused("fretty"));
        Assert.Equal(("fretty won't run until its GitHub App is installed on mentaldesk/fretty (a-team app create fretty).", Schemes.Accent), said);
    }

    [Fact(Skip = "a-team is a shell script", SkipUnless = nameof(HasAShell))]
    public async Task The_board_is_changed_only_when_setting_it_up_is_confirmed()
    {
        Team("fretty", app: true, checkout: true);
        Step? setup = null;
        Start().Follow(_teams, "fretty", step =>
        {
            setup ??= step;
            return Answer.Skipped;
        });

        var (lines, failure) = await setup!.Load!();
        Assert.Null(failure);
        Assert.Equal(["On project fretty (mentaldesk #7), field 'Status':", "  add   Exploring", "On mentaldesk/fretty:", "  create label pitch"], lines);
        Assert.DoesNotContain("board fretty setup", Ran());

        Assert.Null(await setup.Work!(_ => { }));
        Assert.Contains("board fretty setup", Ran());
    }

    [Fact(Skip = "a-team is a shell script", SkipUnless = nameof(HasAShell))]
    public void Get_to_work_starts_the_team_only_on_Enter()
    {
        Team("fretty", app: true, checkout: true);

        var notYet = Start().Follow(_teams, "fretty", _ => Answer.Skipped);
        Assert.True(_teams.IsPaused("fretty"));
        Assert.Equal(("fretty is paused. Press p when you want it to start.", Schemes.Base), notYet);

        var working = Start().Follow(_teams, "fretty", step => step.Title == "Get to work?" ? Answer.Done : Answer.Skipped);
        Assert.False(_teams.IsPaused("fretty"));
        Assert.Equal(("fretty is working.", Schemes.Base), working);
    }

    [Fact(Skip = "a-team is a shell script", SkipUnless = nameof(HasAShell))]
    public void Cancelling_Get_to_work_removes_the_new_team_s_file()
    {
        Team("fretty", app: true, checkout: true);

        var said = Start().Follow(_teams, "fretty", step => step.Title == "Get to work?" ? Answer.Cancelled : Answer.Skipped);

        Assert.DoesNotContain("fretty", _teams.Names());
        Assert.Equal(("fretty is cancelled. Its clone, App and project are still there.", Schemes.Base), said);
    }

    [Fact(Skip = "a-team is a shell script", SkipUnless = nameof(HasAShell))]
    public void Get_to_work_says_so_when_no_dispatcher_is_installed()
    {
        Team("fretty", app: true, checkout: true);
        Step? asked = null;

        Start(dispatcher: false).Follow(_teams, "fretty", step =>
        {
            asked = step;
            return Answer.Skipped;
        });

        Assert.Equal("Get to work?", asked?.Title);
        Assert.Contains("No dispatcher is installed on this Mac, though, so nothing will start it until you run a-team install.", asked!.Lines);
    }

    [Fact(Skip = "a-team is a shell script", SkipUnless = nameof(HasAShell))]
    public async Task Answering_the_clone_step_clones_the_repo_into_the_checkout()
    {
        Team("fretty", app: true);
        Step? clone = null;
        Start().Follow(_teams, "fretty", step =>
        {
            clone ??= step;
            return Answer.Skipped;
        });

        Assert.Equal(["~/code/fretty/main isn't there, so the agents would have nothing to work in.", "gh repo clone mentaldesk/fretty ~/code/fretty/main"], clone!.Lines);
        Assert.Equal(("clone", "skip"), (clone.Yes, clone.No));
        await clone.Work!(_ => { });
        Assert.Contains($"repo clone mentaldesk/fretty {Path.Combine(_root, "home", "code", "fretty", "main")} -- --progress", Ran());
    }

    [Fact(Skip = "a-team is a shell script", SkipUnless = nameof(HasAShell))]
    public void A_skipped_clone_leaves_the_warning_rather_than_the_status()
    {
        Team("fretty", app: true);

        var said = Start().Follow(_teams, "fretty", step => step.Title == "Get to work?" ? Answer.Done : Answer.Skipped);

        Assert.Equal(("~/code/fretty/main isn't there, so the agents would have nothing to work in.", Schemes.Accent), said);
    }

    [Fact(Skip = "a-team is a shell script", SkipUnless = nameof(HasAShell))]
    public void A_team_without_a_project_is_offered_one_before_its_board_and_stays_paused_without_it()
    {
        Team("fretty", app: true, checkout: true, project: false);
        List<string> asked = [];

        var said = Start().Follow(_teams, "fretty", step =>
        {
            asked.Add(step.Title);
            return Answer.Skipped;
        });

        Assert.Equal(["Create a project for fretty?"], asked);
        Assert.Equal(("fretty is paused: it has no project to move its work across. Pick one in its settings.", Schemes.Accent), said);
        Assert.Empty(Ran());
    }

    [Fact(Skip = "a-team is a shell script", SkipUnless = nameof(HasAShell))]
    public async Task Creating_the_project_saves_its_number_and_links_it_and_a_retry_doesn_t_create_another()
    {
        Team("fretty", app: true, checkout: true, project: false);
        Step? create = null;
        Start().Follow(_teams, "fretty", step =>
        {
            create ??= step;
            return Answer.Skipped;
        });

        Assert.Equal("gh project create --owner mentaldesk --title fretty", create!.Lines[1]);
        Assert.Null(await create.Work!(_ => { }));
        Assert.Equal(("mentaldesk", (int?)9), (_teams.Settings("fretty").ProjectOwner, _teams.Settings("fretty").ProjectNumber));
        await create.Work!(_ => { });
        Assert.Equal(
            [
                "project create --owner mentaldesk --title fretty --format json",
                "project link 9 --owner mentaldesk --repo mentaldesk/fretty",
                "project link 9 --owner mentaldesk --repo mentaldesk/fretty",
            ],
            Ran());
    }

    [Fact(Skip = "a-team is a shell script", SkipUnless = nameof(HasAShell))]
    public void Back_returns_to_the_step_before_and_past_the_first_to_the_form()
    {
        Team("fretty", app: false);
        List<string> asked = [];
        var answers = new Queue<Answer>([Answer.Skipped, Answer.Back, Answer.Back]);

        var said = Start().Follow(_teams, "fretty", step =>
        {
            asked.Add(step.Title);
            return answers.Dequeue();
        });

        Assert.Null(said);
        Assert.Equal(["Clone mentaldesk/fretty?", "Give fretty a GitHub App?", "Clone mentaldesk/fretty?"], asked);
    }

    [Fact(Skip = "a-team is a shell script", SkipUnless = nameof(HasAShell))]
    public void Back_passes_over_a_step_that_s_been_done_since()
    {
        Team("fretty", app: false);
        List<string> asked = [];

        var said = Start().Follow(_teams, "fretty", step =>
        {
            asked.Add(step.Title);
            if (asked.Count > 1)
                return Answer.Back;
            Directory.CreateDirectory(Path.Combine(_root, "home", "code", "fretty", "main"));
            return Answer.Done;
        });

        Assert.Null(said);
        Assert.Equal(["Clone mentaldesk/fretty?", "Give fretty a GitHub App?"], asked);
    }

    [Fact]
    public void Redoing_a_new_team_keeps_what_its_steps_saved_and_moves_its_checkout_with_its_workdir()
    {
        Team("fretty", app: true);
        var settings = _teams.Settings("fretty") with { Workdir = "~/code/fret" };

        _teams.Redo("fretty", "fret", settings);

        Assert.Equal(["fret"], _teams.Names());
        Assert.True(_teams.HasApp("fret"));
        Assert.Equal(("~/code/fret", "~/code/fret/main", (int?)7), (_teams.Settings("fret").Workdir, _teams.Settings("fret").CheckoutPath, _teams.Settings("fret").ProjectNumber));
    }

    [Fact]
    public void Backspace_goes_back_without_running_the_step()
    {
        var ran = false;
        var step = new Step("Clone?", ["line"], "clone", "skip", _ =>
        {
            ran = true;
            return Task.FromResult<string?>(null);
        });

        using var dialog = new StepDialog(step);
        dialog.NewKeyDownEvent(Key.Backspace);

        Assert.True(dialog.WentBack);
        Assert.False(dialog.Done);
        Assert.False(ran);
    }

    [Fact]
    public void A_step_s_work_runs_on_Enter_and_a_skip_runs_nothing()
    {
        var ran = 0;
        var step = new Step("Clone?", ["line"], "clone", "skip", line =>
        {
            ran++;
            line("Cloning into 'main'...");
            return Task.FromResult<string?>(null);
        }, ShowOutput: true);

        using var skipped = new StepDialog(step);
        Assert.Equal("Enter clone · Esc skip · Backspace back", skipped.Hints.Says);
        skipped.NewKeyDownEvent(Key.Esc);
        Assert.False(skipped.Done);
        Assert.Equal(0, ran);

        using var dialog = new StepDialog(step);
        dialog.Yes();
        Assert.Equal(1, ran);
        Assert.True(dialog.Done);
        Assert.Equal(["Cloning into 'main'..."], dialog.Printed);
        Assert.Equal("Enter continue", dialog.Hints.Says);
    }

    [Fact]
    public void A_choice_step_does_the_option_chosen_on_Enter_and_cancels_on_Esc()
    {
        var step = new Step("Get to work?", ["line"], "Get to work", "Keep the team paused for now", Choose: true);

        using var yes = new StepDialog(step);
        Assert.Equal(["Get to work", "Keep the team paused for now"], yes.Choice!.Labels);
        Assert.Equal("Enter choose · Esc cancel new team · Backspace back", yes.Hints.Says);
        yes.Yes();
        Assert.True(yes.Done);

        using var no = new StepDialog(step);
        no.Choice!.Value = 1;
        no.Yes();
        Assert.False(no.Done);
        Assert.False(no.Cancelled);

        using var cancelled = new StepDialog(step);
        cancelled.NewKeyDownEvent(Key.Esc);
        Assert.True(cancelled.Cancelled);
    }

    [Fact]
    public void A_step_whose_work_fails_says_why_and_can_be_tried_again_or_skipped()
    {
        var step = new Step("Clone?", ["line"], "clone", "skip", _ => Task.FromResult<string?>("fatal: repository not found"));

        using var dialog = new StepDialog(step);
        dialog.Yes();

        Assert.False(dialog.Done);
        Assert.Equal("fatal: repository not found", dialog.Message.Says);
        Assert.Equal("Enter clone · Esc skip · Backspace back", dialog.Hints.Says);
    }

    [Fact]
    public void A_step_that_can_t_read_what_it_would_do_only_offers_to_skip()
    {
        var ran = false;
        var step = new Step("Set up?", ["Reading…"], "set it up", "skip", _ =>
        {
            ran = true;
            return Task.FromResult<string?>(null);
        }, () => Task.FromResult<(IReadOnlyList<string>?, string?)>((null, "Can't set up fretty's board: no field")));

        using var dialog = new StepDialog(step);
        dialog.Yes();

        Assert.False(ran);
        Assert.Equal("Esc skip · Backspace back", dialog.Hints.Says);
        Assert.Equal("Can't set up fretty's board: no field", dialog.Message.Says);
    }

    private TeamStart Start(bool dispatcher = true, bool installed = true) =>
        new(Example, new TeamCommand(Fake("a-team", $$"""
            if [ "$1 $3 $4" = "board setup --dry-run" ]; then
              printf '  add   Exploring\n(dry run) created label pitch\n'
            fi
            if [ "$1 $2" = "app create" ]; then
              sed -i.bak 's/"dispatch"/"app": {"id": 1, "slug": "a-team-mentaldesk"}, "dispatch"/' "{{_teams.TeamsDirectory}}/$3.json"
              {{(installed ? "" : "echo \"a-team: a-team-mentaldesk isn't installed on mentaldesk/$3 yet\" >&2; exit 1")}}
            fi
            """)), new TeamCommand(Fake("gh", """
            if [ "$1 $2" = "project create" ]; then
              echo '{"number": 9, "title": "fretty"}'
            fi
            if [ "$1 $2" = "project list" ]; then
              echo '{"projects": [{"number": 7, "title": "fretty", "owner": {"login": "mentaldesk"}}]}'
            fi
            """)), Path.Combine(_root, "home"), () => dispatcher);

    private void Team(string name, bool app, bool checkout = false, bool project = true)
    {
        Directory.CreateDirectory(_teams.TeamsDirectory);
        File.WriteAllText(Path.Combine(_teams.TeamsDirectory, $"{name}.json"), $$$"""
            {"repo": "mentaldesk/{{{name}}}", "project": {{{(project ? "{\"owner\": \"mentaldesk\", \"number\": 7}" : "{\"owner\": \"\", \"number\": null}")}}}, "vision": "docs/vision.md",
             "workdir": "~/code/{{{name}}}", "checkout": "~/code/{{{name}}}/main", {{{(app ? "\"app\": {\"id\": 1, \"slug\": \"a-team-mentaldesk\"}," : "")}}}
             "dispatch": {"enabled": false}}
            """);
        if (checkout)
            Directory.CreateDirectory(Path.Combine(_root, "home", "code", name, "main", "docs"));
    }

    private string Fake(string name, string body)
    {
        var bin = Path.Combine(_root, "bin");
        Directory.CreateDirectory(bin);
        var path = Path.Combine(bin, name);
        File.WriteAllText(path, $"#!/bin/sh\necho \"$*\" >> {Path.Combine(_root, "ran")}\n{body}\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    private IReadOnlyList<string> Ran()
    {
        var log = Path.Combine(_root, "ran");
        return File.Exists(log) ? File.ReadAllLines(log) : [];
    }
}
