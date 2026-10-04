using System.Text.Json;

namespace ATeam.Dashboard.Tests;

public class SessionLogTests : IDisposable
{
    private const string Ok = """{"type":"result","num_turns":1,"total_cost_usd":0.1}""";
    private const string Failed = """{"type":"result","is_error":true,"num_turns":1,"total_cost_usd":0.1}""";
    private const string Prose = """{"type":"assistant","message":{"content":[{"type":"text","text":"still going"}]}}""";
    private const string Call = """{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Bash","input":{"command":"ls"}}]}}""";
    private const string Init = """{"type":"system","subtype":"init","model":"claude","session_id":"sess-123"}""";
    private const string Broke = """{"type":"user","message":{"content":[{"type":"tool_result","is_error":true,"content":"boom"}]}}""";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"a-team-{Guid.NewGuid():n}");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_clipped_tool_call_copies_its_whole_field_heredoc_and_all()
    {
        var command = "cd ~/code && cat > /tmp/x.py <<'PY'\n" + string.Join("\n", Enumerable.Repeat(new string('y', 60), 70)) + "\nPY";
        var json = JsonSerializer.Serialize(new
        {
            type = "assistant",
            message = new { content = new[] { new { type = "tool_use", name = "Bash", input = new { command } } } },
        });

        var line = Assert.Single(SessionLog.Render(json));

        Assert.Equal("Bash cd ~/code && cat > /tmp/x.py <<'PY'", line.Text);
        Assert.Equal(command, line.Copied);
        Assert.False(line.Capped);
    }

    [Fact]
    public void A_clipped_tool_error_copies_the_whole_error()
    {
        var error = "first line\n" + new string('e', 300);
        var json = JsonSerializer.Serialize(new
        {
            type = "user",
            message = new { content = new[] { new { type = "tool_result", is_error = true, content = error } } },
        });

        var line = Assert.Single(SessionLog.Render(json));

        Assert.Equal("first line", line.Text);
        Assert.Equal(error, line.Copied);
    }

    [Fact]
    public void A_line_that_was_never_truncated_is_not_stored_twice()
    {
        Assert.Null(Assert.Single(SessionLog.Render(Broke)).Full);
        Assert.All(SessionLog.Render(Prose), line => Assert.Null(line.Full));
    }

    [Fact]
    public void A_field_longer_than_64_KB_is_cut_there_and_says_so()
    {
        var command = new string('x', SessionLog.MaxCopied + 10);
        var json = JsonSerializer.Serialize(new
        {
            type = "assistant",
            message = new { content = new[] { new { type = "tool_use", name = "Bash", input = new { command } } } },
        });

        var line = Assert.Single(SessionLog.Render(json));

        Assert.Equal(SessionLog.MaxCopied, line.Copied.Length);
        Assert.True(line.Capped);
        Assert.EndsWith("(line truncated)", LogCopy.Of([line]).Said, StringComparison.Ordinal);
    }

    [Fact]
    public void Copying_says_how_many_lines_and_characters()
    {
        Assert.Equal("copied 3,914 characters", LogCopy.Of([new LogLine("x", LogLineKind.ToolCall, new string('y', 3914))]).Said);
        Assert.Equal(
            "copied 3 lines, 4,102 characters",
            LogCopy.Of([new(new string('a', 1000), LogLineKind.Prose), new(new string('b', 2000), LogLineKind.Prose), new(new string('c', 1100), LogLineKind.Prose)]).Said);
    }

    [Fact]
    public void Several_lines_are_joined_by_newlines_with_none_trailing()
    {
        var copied = LogCopy.Of([new("one", LogLineKind.Prose), new("Bash ls", LogLineKind.ToolCall, "ls -la")]);

        Assert.Equal("one\nls -la", copied.Text);
    }

    [Fact]
    public void Init_is_a_session_boundary()
    {
        var line = Assert.Single(SessionLog.Render("""
            {"type":"system","subtype":"init","model":"opus"}
            """));
        Assert.Equal("── session started (opus) ──", line.Text);
        Assert.Equal(LogLineKind.SessionBoundary, line.Kind);
    }

    [Fact]
    public void Assistant_text_is_prose()
    {
        var lines = SessionLog.Render("""
            {"type":"assistant","message":{"content":[{"type":"text","text":"one\ntwo"}]}}
            """).ToList();
        Assert.Equal(["one", "two", ""], lines.Select(l => l.Text));
        Assert.All(lines, l => Assert.Equal(LogLineKind.Prose, l.Kind));
    }

    [Fact]
    public void Tool_use_is_a_tool_call()
    {
        var line = Assert.Single(SessionLog.Render("""
            {"type":"assistant","message":{"content":[{"type":"tool_use","name":"Bash","input":{"command":"ls"}}]}}
            """));
        Assert.Equal("Bash ls", line.Text);
        Assert.Equal(LogLineKind.ToolCall, line.Kind);
    }

    [Fact]
    public void Failed_tool_result_is_a_tool_error()
    {
        var line = Assert.Single(SessionLog.Render("""
            {"type":"user","message":{"content":[{"type":"tool_result","is_error":true,"content":"boom"}]}}
            """));
        Assert.Equal("boom", line.Text);
        Assert.Equal(LogLineKind.ToolError, line.Kind);
    }

    [Fact]
    public void Successful_tool_result_renders_nothing()
    {
        Assert.Empty(SessionLog.Render("""
            {"type":"user","message":{"content":[{"type":"tool_result","content":"fine"}]}}
            """));
    }

    [Fact]
    public void Result_without_an_error_is_result_ok()
    {
        var line = Assert.Single(SessionLog.Render("""
            {"type":"result","num_turns":34,"total_cost_usd":1.42}
            """));
        Assert.Equal("finished: ok, 34 turns, $1.42", line.Text);
        Assert.Equal(LogLineKind.ResultOk, line.Kind);
    }

    [Fact]
    public void Result_with_an_error_is_result_error()
    {
        var line = Assert.Single(SessionLog.Render("""
            {"type":"result","is_error":true,"num_turns":2,"total_cost_usd":0.1}
            """));
        Assert.Equal("finished: error, 2 turns, $0.10", line.Text);
        Assert.Equal(LogLineKind.ResultError, line.Kind);
    }

    [Fact]
    public void A_classified_line_carries_its_meaning_and_no_glyph_of_its_own()
    {
        LogLine[] lines =
        [
            Assert.Single(SessionLog.Render(Call)),
            Assert.Single(SessionLog.Render(Broke)),
            Assert.Single(SessionLog.Render(Ok)),
        ];

        Assert.Equal(
            [Icon.ToolCall, Icon.ToolError, Icon.Finished],
            lines.Select(line => Icons.For(line.Kind)));
        Assert.All(lines, line => Assert.DoesNotContain(line.Text, c => "▸✗■".Contains(c)));
    }

    [Fact]
    public void Unparseable_json_is_kept_as_prose()
    {
        var line = Assert.Single(SessionLog.Render("not json at all"));
        Assert.Equal("not json at all", line.Text);
        Assert.Equal(LogLineKind.Prose, line.Kind);
    }

    [Fact]
    public void The_session_id_comes_from_the_init_event()
    {
        var log = new SessionLog();

        log.Refresh(Write("not json", Prose, Init, Ok));

        Assert.Equal("sess-123", log.SessionId);
    }

    [Fact]
    public void A_log_with_no_init_event_has_no_session_id()
    {
        var log = new SessionLog();

        log.Refresh(Write(Prose, Ok));

        Assert.Null(log.SessionId);
    }

    [Fact]
    public void A_session_that_has_not_finished_has_no_verdict_yet()
    {
        var log = new SessionLog();

        log.Refresh(Write(Prose));

        Assert.Equal(RunVerdict.None, log.Verdict);
    }

    [Theory]
    [InlineData(RunVerdict.Ok, Ok)]
    [InlineData(RunVerdict.Error, Failed)]
    public void A_finished_run_takes_its_verdict_from_the_result(RunVerdict expected, string result)
    {
        var log = new SessionLog();

        log.Refresh(Write(Prose, result));

        Assert.Equal(expected, log.Verdict);
    }

    [Fact]
    public void A_later_result_replaces_an_earlier_one()
    {
        var log = new SessionLog();

        log.Refresh(Write(Ok, Failed));

        Assert.Equal(RunVerdict.Error, log.Verdict);
    }

    [Fact]
    public void The_verdict_survives_the_result_line_being_trimmed_off_the_tail()
    {
        var log = new SessionLog();

        log.Refresh(Write([Failed, .. Enumerable.Repeat(Prose, 600)]));

        Assert.DoesNotContain(log.Lines, line => line.Kind == LogLineKind.ResultError);
        Assert.Equal(RunVerdict.Error, log.Verdict);
    }

    [Fact]
    public void A_new_session_does_not_inherit_the_last_ones_verdict()
    {
        var log = new SessionLog();
        log.Refresh(Write(Failed));

        log.Refresh(Write(Prose));

        Assert.Equal(RunVerdict.None, log.Verdict);
    }

    private string Write(params string[] events)
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, $"{Guid.NewGuid():n}.jsonl");
        File.WriteAllLines(path, events);
        return path;
    }
}
