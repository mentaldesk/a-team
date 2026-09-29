using System.Text;

namespace ATeam.Dashboard.Tests;

public class TeamSettingsTests : IDisposable
{
    private const string Config = """
        {
          "repo": "mentaldesk/a-team",
          "reviewer": "jamescrosswell",
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
                2, 3, 4, 4, 2, "~/code/a-team/main"),
            settings);
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
        new("o/r", "o", 1, "docs/vision.md", "~/code/demo", "", true, 3, 3, 6, 6, 3, null);

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);
}
