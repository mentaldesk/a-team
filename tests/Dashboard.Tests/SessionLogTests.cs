namespace ATeam.Dashboard.Tests;

public class SessionLogTests
{
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
        Assert.Equal("▸ Bash ls", line.Text);
        Assert.Equal(LogLineKind.ToolCall, line.Kind);
    }

    [Fact]
    public void Failed_tool_result_is_a_tool_error()
    {
        var line = Assert.Single(SessionLog.Render("""
            {"type":"user","message":{"content":[{"type":"tool_result","is_error":true,"content":"boom"}]}}
            """));
        Assert.Equal("  ✗ boom", line.Text);
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
        Assert.Equal("■ finished: ok, 34 turns, $1.42", line.Text);
        Assert.Equal(LogLineKind.ResultOk, line.Kind);
    }

    [Fact]
    public void Result_with_an_error_is_result_error()
    {
        var line = Assert.Single(SessionLog.Render("""
            {"type":"result","is_error":true,"num_turns":2,"total_cost_usd":0.1}
            """));
        Assert.Equal("■ finished: error, 2 turns, $0.10", line.Text);
        Assert.Equal(LogLineKind.ResultError, line.Kind);
    }

    [Fact]
    public void Unparseable_json_is_kept_as_prose()
    {
        var line = Assert.Single(SessionLog.Render("not json at all"));
        Assert.Equal("not json at all", line.Text);
        Assert.Equal(LogLineKind.Prose, line.Kind);
    }
}
