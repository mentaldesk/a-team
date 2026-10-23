using System.Text.Json.Nodes;
using MentalDesk.Tui.Assistant;
using Terminal.Gui.Input;

namespace ATeam.Dashboard.Tests;

[Collection("StaticConfiguration")]
public sealed class AssistantTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"a-team-{Guid.NewGuid():n}");
    private readonly FakeAgent _agent = new();
    private readonly Lock _gate = new();
    private readonly List<PermissionRequest> _asked = [];
    private bool _allow;

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string Sessions => Path.Combine(_root, "assistant.json");

    private DashboardWindow Open(IReadOnlyList<(string, string)>? agents = null)
    {
        Directory.CreateDirectory(_root);
        var config = Path.Combine(_root, "config");
        var setup = new AssistantSetup(_agent, "the brief", Sessions, request =>
        {
            _asked.Add(request);
            return _allow;
        }, action =>
        {
            lock (_gate)
                action();
        });
        return new DashboardWindow(
            agents ?? [("a-team", "lead"), ("a-team", "dev")],
            _root,
            new DashboardSettings(config),
            new TeamConfigs(config),
            _ => Task.FromResult<string?>(null),
            _ => Task.FromResult(new Reading("[]", null)),
            _ => Task.FromResult(new Reading("{}", null)),
            Opening.Opened,
            (_, _, _, _, _, _, _, _, _, _, _, _) => null,
            Area.Dashboard,
            IconStyle.Unicode,
            assistant: setup);
    }

    private async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (true)
        {
            lock (_gate)
                if (condition())
                    return;
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException();
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private static string Chat(DashboardWindow window) =>
        string.Join('\n', window.Assistant!.Controller.Conversation.Render(200).Select(line => line.Text));

    private static void Type(DashboardWindow window, string text)
    {
        foreach (var c in text)
            window.NewKeyDownEvent(new Key(c));
    }

    private static async Task Ask(DashboardWindow window, string question)
    {
        Type(window, question);
        window.NewKeyDownEvent(Key.Enter);
        await Task.Yield();
    }

    [Fact]
    public void The_brief_names_the_roles_brief_each_teams_config_and_where_runs_are_recorded()
    {
        var brief = AssistantBrief.For("/opt/a-team", "/home/me/.config/a-team/teams", "/home/me/.local/state/a-team", ["a-team", "tuicode"]);

        Assert.Contains("a-team, tuicode", brief);
        Assert.Contains("/opt/a-team/process.md", brief);
        Assert.Contains("/opt/a-team/roles/<role>.md", brief);
        Assert.Contains("/home/me/.config/a-team/teams/<team>.json", brief);
        Assert.Contains("Under /home/me/.local/state/a-team:", brief);
        Assert.Contains("<team>/<role>/", brief);
        Assert.Contains("history.db", brief);
        Assert.Contains("dispatch.log", brief);
        Assert.Contains("a-team board <team> <command> you", brief);
    }

    [Fact]
    public void The_brief_goes_after_Claude_Codes_own_prompt_with_none_of_the_users_settings()
    {
        var meta = AssistantBrief.Meta("the brief");

        Assert.Equal("the brief", (string?)meta["systemPrompt"]!["append"]);
        Assert.Empty(meta["claudeCode"]!["options"]!["settingSources"]!.AsArray());
        Assert.False((bool)meta["claudeCode"]!["options"]!["allowDangerouslySkipPermissions"]!);
    }

    [Theory]
    [InlineData(Area.Work, "a-team", 499, null, false, false, "on #499 in Work")]
    [InlineData(Area.Work, "a-team", 499, null, false, true, "on a-team #499 in Work")]
    [InlineData(Area.Work, "a-team", null, null, false, false, "in Work")]
    [InlineData(Area.Work, "a-team", null, null, false, true, "on a-team in Work")]
    [InlineData(Area.Overseer, "tuicode", 12, null, false, true, "on tuicode #12 in Overseer")]
    [InlineData(Area.Overseer, "tuicode", null, null, false, true, "on tuicode in Overseer")]
    [InlineData(Area.Dashboard, "a-team", null, "dev", false, false, "on a-team's Dev in the Dashboard")]
    [InlineData(Area.Dashboard, null, null, null, true, true, "on the dispatcher in the Dashboard")]
    public void Each_question_says_where_it_was_asked_and_what_was_selected(
        Area area, string? team, int? number, string? role, bool dispatcher, bool manyTeams, string expected) =>
        Assert.Equal(expected, AssistantBrief.Context(area, new AskedAbout(team, number, role, dispatcher), manyTeams));

    [Theory]
    [InlineData(Area.Work, "in Work")]
    [InlineData(Area.Overseer, "in Overseer")]
    [InlineData(Area.Dashboard, "in the Dashboard")]
    public void With_nothing_selected_a_question_says_only_the_area(Area area, string expected) =>
        Assert.Equal(expected, AssistantBrief.Context(area, null, manyTeams: true));

    [Fact]
    public void Assistant_is_in_the_View_menu_with_no_key()
    {
        using var window = Open();

        Assert.Contains(AppMenu.Layout.Single(menu => menu.Title == "_View").Ids, id => id == "assistant");
        Assert.Contains(window.Commands.Registered, command => command.Id == "assistant" && command.Label == "Assistant");
        Assert.Equal(Key.Empty, window.Commands.KeyFor("assistant"));
    }

    [Fact]
    public void Choosing_Assistant_opens_it_with_focus_in_its_question_box_and_again_closes_it()
    {
        using var window = Open();

        window.Commands.Execute("assistant");
        var opened = (window.Assistant!.Visible, window.Assistant.InputHasFocus);
        window.Commands.Execute("assistant");

        Assert.Equal((true, true), opened);
        Assert.False(window.Assistant.Visible);
    }

    [Fact]
    public void It_stays_open_from_one_area_to_the_next()
    {
        using var window = Open();
        window.Commands.Execute("assistant");

        window.Commands.Execute("view.work");
        window.Commands.Execute("view.overseer");
        window.Commands.Execute("view.dashboard");

        Assert.True(window.Assistant!.Visible);
    }

    [Fact]
    public void F6_moves_between_the_Assistant_and_the_area_beside_it()
    {
        using var window = Open();
        window.NewKeyDownEvent(Key.Tab);
        window.Commands.Execute("assistant");

        window.NewKeyDownEvent(Key.F6);
        var away = (window.Assistant!.HasFocus, window.Panes[0].HasFocus);
        window.NewKeyDownEvent(Key.F6);

        Assert.Equal((false, true), away);
        Assert.True(window.Assistant.InputHasFocus);
    }

    [Fact]
    public void Typing_in_the_question_box_runs_no_single_letter_command()
    {
        using var window = Open();
        window.Commands.Execute("assistant");

        Type(window, "why q");

        Assert.Equal(Area.Dashboard, window.CurrentArea);
        Assert.Equal("why q", window.Assistant!.Question);
    }

    [Fact]
    public void F1_in_the_pane_lists_its_keys_under_Assistant()
    {
        using var window = Open();
        window.Commands.Execute("assistant");

        var here = window.Keys.Here!;

        Assert.Equal("Assistant", here.Heading);
        Assert.Equal([("Enter", "Send question"), ("Ctrl+L", "Start a new conversation")],
            here.Groups.SelectMany(group => group.Rows).Select(row => (row.Keys, row.Label)));
    }

    [Fact]
    public async Task A_question_carries_the_selected_agent_and_its_answer_streams_in_with_a_line_per_lookup()
    {
        using var window = Open();
        window.NewKeyDownEvent(Key.Tab);
        window.Commands.Execute("assistant");
        _agent.OnPrompt = async (agent, _) =>
        {
            agent.Say("It's ");
            agent.Tool("t", "Read latest.jsonl", "completed");
            agent.Say("waiting on CI.");
            await Task.Yield();
        };

        await Ask(window, "Why?");
        await Until(() => Chat(window).Contains("waiting on CI") && !window.Assistant!.Controller.IsAnswering);

        Assert.Equal("You · on a-team's Lead in the Dashboard\nWhy?\n\nClaude Code\nIt's\n▸ Read latest.jsonl\nwaiting on CI.", Chat(window));
        var sent = (string?)_agent.Received("session/prompt").Single()!["prompt"]![0]!["text"];
        Assert.Equal("[on a-team's Lead in the Dashboard]\n\nWhy?", sent);
    }

    [Fact]
    public async Task The_session_starts_with_the_brief_in_the_state_folder()
    {
        using var window = Open();
        window.Commands.Execute("assistant");

        await Until(() => _agent.Received("session/new").Any());

        var opened = _agent.Received("session/new").Single()!;
        Assert.Equal(_root, (string?)opened["cwd"]);
        Assert.Equal("the brief", (string?)opened["_meta"]!["systemPrompt"]!["append"]);
    }

    [Fact]
    public async Task The_session_switches_to_Claude_Codes_Auto_mode()
    {
        _agent.Modes.AddRange(["default", "acceptEdits", "auto"]);
        using var window = Open();
        window.Commands.Execute("assistant");

        await Until(() => _agent.Received("session/set_mode").Any());

        Assert.Equal("auto", (string?)_agent.Received("session/set_mode").Single()!["modeId"]);
    }

    [Fact]
    public async Task Esc_stops_the_answer_and_keeps_what_arrived()
    {
        using var window = Open();
        window.Commands.Execute("assistant");
        _agent.OnPrompt = async (agent, _) =>
        {
            agent.Say("Partly");
            await agent.Cancelled.Task;
        };

        await Ask(window, "Why?");
        await Until(() => Chat(window).Contains("Partly"));
        window.NewKeyDownEvent(Key.Esc);
        await Until(() => !window.Assistant!.Controller.IsAnswering);

        Assert.EndsWith("Partly\n\nStopped.", Chat(window));
    }

    [Theory]
    [InlineData(true, "allow")]
    [InlineData(false, "reject")]
    public async Task A_command_runs_only_once_allowed(bool allow, string answer)
    {
        _allow = allow;
        using var window = Open();
        window.Commands.Execute("assistant");
        string? chosen = null;
        _agent.OnPrompt = async (agent, _) => chosen = await agent.AskPermission("execute", "Move #499", "a-team board a-team accept you 499");

        await Ask(window, "Merge it");
        await Until(() => !window.Assistant!.Controller.IsAnswering);

        Assert.Equal("a-team board a-team accept you 499", _asked.Single().Command);
        Assert.Equal(answer, chosen);
    }

    [Fact]
    public async Task Reading_goes_ahead_without_asking()
    {
        using var window = Open();
        window.Commands.Execute("assistant");
        string? chosen = null;
        _agent.OnPrompt = async (agent, _) => chosen = await agent.AskPermission("read", "Read dispatch.log");

        await Ask(window, "What happened?");
        await Until(() => !window.Assistant!.Controller.IsAnswering);

        Assert.Empty(_asked);
        Assert.Equal("allow", chosen);
    }

    [Fact]
    public void The_permission_dialog_says_what_Claude_Code_wants_to_run_with_Deny_focused()
    {
        using var dialog = AssistantSetup.Dialog(PermissionRequest.Parse(JsonNode.Parse(
            """{ "toolCall": { "title": "Bash", "kind": "execute", "rawInput": { "command": "a-team board a-team accept you 499" } }, "options": [] }""")));
        dialog.BeginInit();
        dialog.EndInit();

        Assert.Equal("Claude Code wants to run a command", dialog.Title);
        Assert.Contains(dialog.ButtonRow, button => button.Text.Contains("Allow once"));
        Assert.True(dialog.CancelButton.HasFocus);
        Assert.Contains("Deny", dialog.CancelButton.Text);
    }

    [Fact]
    public async Task The_last_conversation_is_back_after_a_restart_and_Ctrl_L_starts_a_new_one()
    {
        using (var first = Open())
        {
            first.Commands.Execute("assistant");
            await Ask(first, "Hi");
            await Until(() => Chat(first).Contains("Hello") && !first.Assistant!.Controller.IsAnswering);
        }
        _agent.History.Add((true, "[in the Dashboard]\n\nHi"));
        _agent.History.Add((false, "Hello"));

        using var again = Open();
        again.Commands.Execute("assistant");
        await Until(() => Chat(again).Contains("Hello"));

        Assert.Equal("session-1", (string?)_agent.Received("session/load").Single()!["sessionId"]);
        Assert.StartsWith("You · in the Dashboard\nHi", Chat(again));

        again.NewKeyDownEvent(Key.L.WithCtrl);

        Assert.Equal("", Chat(again));
        Assert.DoesNotContain("session-1", File.ReadAllText(Sessions));
    }

    [Fact]
    public async Task Without_Claude_Code_the_pane_says_so()
    {
        _agent.Missing = true;
        using var window = Open();
        window.Commands.Execute("assistant");

        await Until(() => Chat(window).Length > 0);

        Assert.Equal("npx isn't on PATH, so Claude Code can't start.", Chat(window));
    }

    [Fact]
    public async Task Signed_out_the_pane_says_how_to_sign_in()
    {
        _agent.NewSessionError = (-32000, "Authentication required");
        using var window = Open();
        window.Commands.Execute("assistant");

        await Until(() => Chat(window).Contains("Sign in"));

        Assert.Contains("Sign in by running `claude auth login` in a terminal, then ask again.", Chat(window));
    }
}
