using System.Text;

namespace ATeam.Dashboard.Tests;

public class TeamSettingsTests : IDisposable
{
    private const string Config = """
        {
          "repo": "mentaldesk/a-team",
          "stakeholders": ["jamescrosswell"],
          "project": {
            "owner": "mentaldesk",
            "number": 3,
            "statusField": "Status",
            "statusMap": {}
          },
          "vision": "docs/vision.md",
          "skills": [
            "a-team"
          ],
          "wip": {
            "worktrees": 2,
            "pitched": 3,
            "exploring": 4,
            "ideas": 4,
            "readyFloor": 2
          },
          "try": "./bin/a-team dashboard",
          "workdir": "~/code/a-team",
          "checkout": "~/code/a-team/main",
          "dispatch": {
            "enabled": true,
            "hold": ["dev"],
            "retryAfter": 60
          },
          "app": {
            "id": 5104396,
            "slug": "a-team-app"
          },
          "later": {"a": [1, 2]}
        }

        """;

    private readonly string _home = Path.Combine(Path.GetTempPath(), $"a-team-{Guid.NewGuid():n}");

    public void Dispose()
    {
        if (Directory.Exists(_home))
            Directory.Delete(_home, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_config_reads_into_the_settings_the_form_shows()
    {
        var settings = TeamSettings.Read(Bytes(Config));

        Assert.Equal(
            new TeamSettings(
                "mentaldesk/a-team", "mentaldesk", 3, "docs/vision.md", "~/code/a-team", "./bin/a-team dashboard", true,
                2, 1, 3, 4, 4, 2, "~/code/a-team/main", settings.Stakeholders, settings.Skills),
            settings);
        Assert.Equal(["jamescrosswell"], settings.Stakeholders);
        Assert.Equal(["a-team"], settings.Skills);
        Assert.False(settings.SaysReviewer);
        Assert.Null(settings.OtherCheckout);
    }

    [Fact]
    public void Reading_a_config_into_the_form_and_saving_it_unchanged_writes_the_same_bytes()
    {
        var before = TeamSettings.Read(Bytes(Config));
        using var form = new TeamForm("a-team", before, _ => null);

        Assert.Equal(Config, Text(form.Current().Write(Bytes(Config), before)));
    }

    [Fact]
    public void Saving_a_changed_field_keeps_every_key_the_form_does_not_show()
    {
        var before = TeamSettings.Read(Bytes(Config));

        var after = Text((before with { Repo = "mentaldesk/other", Working = false, Ideas = 9 }).Write(Bytes(Config), before));

        Assert.Equal(
            Config
                .Replace("\"repo\": \"mentaldesk/a-team\"", "\"repo\": \"mentaldesk/other\"")
                .Replace("\"enabled\": true", "\"enabled\": false")
                .Replace("\"ideas\": 4", "\"ideas\": 9"),
            after);
        Assert.Contains("\"app\": {\n    \"id\": 5104396,", after);
        Assert.Contains("\"hold\": [\"dev\"]", after);
        Assert.Contains("\"checkout\": \"~/code/a-team/main\"", after);
        Assert.Contains("\"later\": {\"a\": [1, 2]}", after);
    }

    [Theory]
    [InlineData(null, TeamRelease.Never)]
    [InlineData("\"never\"", TeamRelease.Never)]
    [InlineData("\"daily\"", TeamRelease.Daily)]
    [InlineData("\"continuous\"", TeamRelease.Continuous)]
    [InlineData("\"weekly\"", TeamRelease.Never)]
    public void Release_reads_as_never_unless_the_file_says_daily_or_continuous(string? value, TeamRelease release) =>
        Assert.Equal(release, TeamSettings.Read(Bytes(value is null ? """{"repo": "o/r"}""" : $$"""{"release": {{value}}}""")).Release);

    [Fact]
    public void A_changed_release_is_written_in_lower_case_and_an_unchanged_one_is_left_as_it_was()
    {
        const string config = """{"release": "weekly","repo": "o/r"}""";
        var before = TeamSettings.Read(Bytes(config));

        Assert.Equal(config, Text(before.Write(Bytes(config), before)));
        Assert.Equal(
            """{"release": "continuous","repo": "o/r"}""",
            Text((before with { Release = TeamRelease.Continuous }).Write(Bytes(config), before)));
    }

    [Fact]
    public void A_team_without_devs_reads_as_one_and_keeps_its_file_until_devs_changes()
    {
        var before = TeamSettings.Read(Bytes(Config));

        Assert.Equal(1, before.Devs);
        Assert.Equal(Config, Text(before.Write(Bytes(Config), before)));
        var after = (before with { Devs = 3 }).Write(Bytes(Config), before);
        Assert.Equal(3, TeamSettings.Read(after).Devs);
    }

    [Fact]
    public void Changed_stakeholders_and_skills_are_written_as_lists_where_they_were()
    {
        var before = TeamSettings.Read(Bytes(Config));

        var after = Text((before with { Stakeholders = ["jamescrosswell", "someone"], Skills = [] }).Write(Bytes(Config), before));

        Assert.Equal(
            Config
                .Replace("\"stakeholders\": [\"jamescrosswell\"]", "\"stakeholders\": [\"jamescrosswell\", \"someone\"]")
                .Replace("\"skills\": [\n    \"a-team\"\n  ]", "\"skills\": []"),
            after);
    }

    [Fact]
    public void A_file_that_still_says_reviewer_shows_that_person_as_its_stakeholder()
    {
        var settings = TeamSettings.Read(Bytes(Config.Replace("\"stakeholders\": [\"jamescrosswell\"]", "\"reviewer\": \"jamescrosswell\"")));

        Assert.Equal(["jamescrosswell"], settings.Stakeholders);
        Assert.True(settings.SaysReviewer);
    }

    [Fact]
    public void Saving_a_file_that_says_reviewer_writes_stakeholders_in_its_place_even_with_nothing_changed()
    {
        var config = Config.Replace("\"stakeholders\": [\"jamescrosswell\"]", "\"reviewer\": \"jamescrosswell\"");
        var before = TeamSettings.Read(Bytes(config));

        Assert.Equal(Config, Text(before.Write(Bytes(config), before)));
    }

    [Fact]
    public void Stakeholders_take_precedence_over_a_leftover_reviewer()
    {
        const string config = """{"stakeholders": ["a", "b"], "reviewer": "c"}""";
        var settings = TeamSettings.Read(Bytes(config));

        Assert.Equal(["a", "b"], settings.Stakeholders);
        Assert.False(settings.SaysReviewer);
        Assert.Equal(config, Text(settings.Write(Bytes(config), settings)));
    }

    [Fact]
    public void A_file_with_no_stakeholders_or_skills_reads_as_none_and_gets_them_once_chosen()
    {
        const string config = """{"repo": "o/r"}""";
        var before = TeamSettings.Read(Bytes(config));

        var after = Text((before with { Stakeholders = ["me"], Skills = ["tuicode"] }).Write(Bytes(config), before));

        Assert.Empty(before.Stakeholders);
        Assert.Empty(before.Skills);
        Assert.Equal("""{"skills": ["tuicode"],"stakeholders": ["me"],"repo": "o/r"}""", after);
    }

    [Fact]
    public void The_Customer_lead_is_off_until_roles_customer_turns_it_on_and_is_saved_there()
    {
        var before = TeamSettings.Read(Bytes(Config));

        var on = (before with { Customer = true }).Write(Bytes(Config), before);
        var off = (TeamSettings.Read(on) with { Customer = false }).Write(on, TeamSettings.Read(on));

        Assert.False(before.Customer);
        Assert.True(TeamSettings.Read(on).Customer);
        Assert.Contains("\"roles\": {\"customer\": true}", Text(on));
        Assert.Contains("\"roles\": {\"customer\": false}", Text(off));
        Assert.False(TeamSettings.Read(off).Customer);
    }

    [Fact]
    public void A_setting_the_file_has_not_got_is_added_with_the_objects_on_the_way_to_it()
    {
        const string config = """{"repo": "o/r"}""";
        var before = TeamSettings.Read(Bytes(config));

        var after = Text((before with { ProjectOwner = "o", ProjectNumber = 7, Try = "make \"run\"" }).Write(Bytes(config), before));

        Assert.Equal("""{"try": "make \"run\"","project": {"number": 7,"owner": "o"},"repo": "o/r"}""", after);
    }

    [Theory]
    [InlineData("", "o", 1, "v", "w", "Repo is required.")]
    [InlineData("just-a-name", "o", 1, "v", "w", "Repo must be owner/repo, like mentaldesk/a-team, not just-a-name.")]
    [InlineData("o/r/x", "o", 1, "v", "w", "Repo must be owner/repo, like mentaldesk/a-team, not o/r/x.")]
    [InlineData("o/r", "", 1, "v", "w", "Project is required.")]
    [InlineData("o/r", "o", null, "v", "w", "Project is required.")]
    [InlineData("o/r", "o", 0, "v", "w", "Project number must be above 0.")]
    [InlineData("o/r", "o", 1, "", "w", "Vision is required.")]
    [InlineData("o/r", "o", 1, "v", "", "Workdir is required.")]
    [InlineData("o/r", "o", 1, "v", "w", null)]
    public void A_missing_field_or_a_repo_not_shaped_owner_repo_refuses_the_save(
        string repo, string owner, int? number, string vision, string workdir, string? refusal) =>
        Assert.Equal(refusal, (Settings() with
        {
            Repo = repo, ProjectOwner = owner, ProjectNumber = number, Vision = vision, Workdir = workdir,
        }).Refusal());

    [Theory]
    [InlineData("", null, null)]
    [InlineData("o", null, "Project is required.")]
    [InlineData("", 1, "Project is required.")]
    public void A_new_team_may_leave_its_project_out_but_not_half_filled(string owner, int? number, string? refusal) =>
        Assert.Equal(refusal, (Settings() with { Repo = "o/r", ProjectOwner = owner, ProjectNumber = number }).Refusal(projectOptional: true));

    [Fact]
    public void A_workdir_that_is_not_there_warns()
    {
        Assert.Equal(
            "~/code/nowhere isn't there, so the agents would have nothing to work in.",
            (Settings() with { Workdir = "~/code/nowhere" }).Warning(_home));
    }

    [Fact]
    public void A_vision_the_repo_has_not_got_warns_in_the_pitch_s_words()
    {
        Directory.CreateDirectory(Path.Combine(_home, "code", "demo", "main"));

        Assert.Equal(
            "docs/vision.md isn't there yet: the Lead will draft one and open it as a draft PR for you.",
            (Settings() with { Workdir = "~/code/demo", Vision = "docs/vision.md" }).Warning(_home));
    }

    [Fact]
    public void A_skill_this_machine_has_not_got_warns()
    {
        Directory.CreateDirectory(Path.Combine(_home, "code", "demo", "main"));
        Skill(".claude", "skills", "a-team");

        Assert.Equal(
            "No skill named tuicode in ~/.claude/skills: the agents will work without it.",
            (Settings() with { Workdir = "~/code/demo", Skills = ["a-team", "tuicode"] }).Warning(_home));
    }

    [Fact]
    public void A_skill_in_the_repo_s_own_skills_or_a_plugin_skill_does_not_warn()
    {
        Directory.CreateDirectory(Path.Combine(_home, "code", "demo", "main", "docs"));
        File.WriteAllText(Path.Combine(_home, "code", "demo", "main", "docs", "vision.md"), "");
        Skill("code", "demo", "main", ".claude", "skills", "demo");

        Assert.Null((Settings() with { Workdir = "~/code/demo", Skills = ["demo", "plugin:skill"] }).Warning(_home));
    }

    [Fact]
    public void The_skills_found_are_every_folder_holding_a_SKILL_md_here_and_in_the_repo()
    {
        Skill(".claude", "skills", "tuicode");
        Skill(".claude", "skills", "a-team");
        Directory.CreateDirectory(Path.Combine(_home, ".claude", "skills", "synced"));
        Skill("code", "demo", "main", ".claude", "skills", "demo");
        Skill("code", "demo", "main", ".claude", "skills", "a-team");

        var found = SkillsFound.Find(_home, "~/code/demo/main");

        Assert.Equal(["a-team", "tuicode", "demo"], found.Names);
        Assert.Equal(
            "3 found under ~/.claude/skills and ~/code/demo/main/.claude/skills. Type a name to add one that isn't listed.",
            found.Message);
    }

    [Fact]
    public void With_no_skills_folder_the_picker_says_so()
    {
        var found = SkillsFound.Find(_home, "~/code/demo/main");

        Assert.Empty(found.Names);
        Assert.Equal("No skills under ~/.claude/skills. Type a name to add one.", found.Message);
    }

    [Fact]
    public void The_stakeholders_offered_are_those_who_can_push_you_first()
    {
        const string json = """
            [
              {"login": "octocat", "permissions": {"admin": false, "push": true, "pull": true}},
              {"login": "reader", "permissions": {"admin": false, "push": false, "pull": true}},
              {"login": "jamescrosswell", "permissions": {"admin": true, "push": true, "pull": true}}
            ]
            """;

        Assert.Equal(["jamescrosswell", "octocat"], Stakeholders.Parse(new Reading(json, null), "jamescrosswell"));
    }

    [Theory]
    [InlineData("", "gh: Could not resolve host: api.github.com")]
    [InlineData("not json", null)]
    [InlineData("""{"message": "Not Found"}""", null)]
    public void Collaborators_that_cannot_be_read_are_null(string output, string? failure) =>
        Assert.Null(Stakeholders.Parse(new Reading(output, failure), "me"));

    [Fact]
    public void A_workdir_and_vision_that_are_there_say_nothing()
    {
        Directory.CreateDirectory(Path.Combine(_home, "code", "demo", "main", "docs"));
        File.WriteAllText(Path.Combine(_home, "code", "demo", "main", "docs", "vision.md"), "");

        Assert.Null((Settings() with { Workdir = "~/code/demo", Vision = "docs/vision.md" }).Warning(_home));
    }

    [Theory]
    [InlineData("~/code/demo/main", null)]
    [InlineData("~/code/demo/main/", null)]
    [InlineData(null, null)]
    [InlineData("~/elsewhere", "~/elsewhere")]
    public void Only_a_checkout_other_than_workdir_main_is_shown(string? checkout, string? shown) =>
        Assert.Equal(shown, (Settings() with { Workdir = "~/code/demo", Checkout = checkout }).OtherCheckout);

    [Fact]
    public void The_project_list_is_read_from_gh_project_list()
    {
        const string json = """
            {"projects":[
              {"number":3,"title":"a-team","owner":{"login":"mentaldesk","type":"Organization"}},
              {"number":4,"title":"TuiCode","owner":{"login":"mentaldesk","type":"Organization"}}
            ],"totalCount":2}
            """;

        Assert.Equal(
            [new ProjectChoice("mentaldesk", 3, "a-team"), new ProjectChoice("mentaldesk", 4, "TuiCode")],
            ProjectChoice.Parse(new Reading(json, null), "mentaldesk"));
        Assert.Equal("TuiCode (mentaldesk #4)", new ProjectChoice("mentaldesk", 4, "TuiCode").ToString());
    }

    [Theory]
    [InlineData("", "gh: missing scope read:project")]
    [InlineData("not json", null)]
    [InlineData("""{"totalCount": 0}""", null)]
    public void A_project_list_that_cannot_be_read_is_null(string output, string? failure) =>
        Assert.Null(ProjectChoice.Parse(new Reading(output, failure), "mentaldesk"));

    private static TeamSettings Settings() =>
        new("o/r", "o", 1, "docs/vision.md", "~/code/demo", "", true, 3, 1, 3, 6, 6, 3, null, [], []);

    private void Skill(params string[] path)
    {
        var directory = Path.Combine([_home, .. path]);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "SKILL.md"), "");
    }

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);
}
