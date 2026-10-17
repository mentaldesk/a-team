using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using MentalDesk.Tui.Assistant;

namespace ATeam.Dashboard.Tests;

// In-process fake ACP agents, after MentalDesk.Tui's own. Each opens sessions, keeps what it's sent, and answers a prompt by running OnPrompt,
// which streams updates and may ask things back; then it stops with end_turn, or cancelled once a session/cancel has come.
internal sealed class FakeAgent : IAgentLauncher
{
    private readonly ConcurrentQueue<(string Method, JsonNode? Params)> _received = new();
    private int _sessions;

    public bool Missing { get; set; }

    public bool LoadSession { get; set; } = true;

    // Answers session/new with this error, as an agent that isn't signed in does.
    public (int Code, string Message)? NewSessionError { get; set; }

    public Func<FakeAgent, JsonNode?, Task> OnPrompt { get; set; } = (agent, _) =>
    {
        agent.Say("Hello");
        return Task.CompletedTask;
    };

    // What a session/load replays first: (user?, text).
    public List<(bool User, string Text)> History { get; } = [];

    public List<(string Program, IReadOnlyList<string> Arguments, string Directory)> Launches { get; } = [];

    public List<InProcessAgent> Agents { get; } = [];

    public InProcessAgent Current => Agents[^1];

    public string? SessionId { get; private set; }

    public TaskCompletionSource Cancelled { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IEnumerable<JsonNode?> Received(string method) => _received.Where(m => m.Method == method).Select(m => m.Params);

    public IAgentProcess? Launch(string program, IReadOnlyList<string> arguments, string directory)
    {
        Launches.Add((program, arguments, directory));
        if (Missing) return null;
        var agent = new InProcessAgent();
        agent.Connection.AsyncRequestHandler = (method, parameters) =>
        {
            _received.Enqueue((method, parameters?.DeepClone()));
            return AnswerAsync(method, parameters);
        };
        agent.Connection.NotificationReceived += (method, parameters) =>
        {
            _received.Enqueue((method, parameters?.DeepClone()));
            if (method == "session/cancel") Cancelled.TrySetResult();
        };
        agent.Connection.Start();
        Agents.Add(agent);
        return agent;
    }

    private async Task<JsonNode?> AnswerAsync(string method, JsonNode? parameters)
    {
        switch (method)
        {
            case "initialize":
                return new JsonObject
                {
                    ["protocolVersion"] = 1,
                    ["agentCapabilities"] = new JsonObject
                    {
                        ["loadSession"] = LoadSession,
                        ["promptCapabilities"] = new JsonObject { ["embeddedContext"] = true },
                    },
                };
            case "session/new":
                if (NewSessionError is { } error) throw new JsonRpcException(error.Code, error.Message);
                SessionId = $"session-{Interlocked.Increment(ref _sessions)}";
                return new JsonObject { ["sessionId"] = SessionId };
            case "session/load":
                SessionId = parameters!["sessionId"]!.GetValue<string>();
                foreach (var (user, text) in History) Update(user ? "user_message_chunk" : "agent_message_chunk", Text(text));
                return null;
            case "session/prompt":
                Cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                await OnPrompt(this, parameters);
                return new JsonObject { ["stopReason"] = Cancelled.Task.IsCompleted ? "cancelled" : "end_turn" };
            default:
                throw new JsonRpcException(JsonRpcConnection.MethodNotFound, method);
        }
    }

    public void Say(string text) => Update("agent_message_chunk", Text(text));

    public void Tool(string id, string title, string status = "pending") =>
        Current.Connection.Notify("session/update", new JsonObject
        {
            ["sessionId"] = SessionId,
            ["update"] = new JsonObject { ["sessionUpdate"] = "tool_call", ["toolCallId"] = id, ["title"] = title, ["status"] = status },
        });

    public Task<JsonNode?> Ask(string method, JsonObject parameters)
    {
        parameters["sessionId"] = SessionId;
        return Current.Connection.RequestAsync(method, parameters);
    }

    // The answer's option id, or null when cancelled.
    public async Task<string?> AskPermission(string kind, string title, string? command = null)
    {
        var call = new JsonObject { ["toolCallId"] = "perm", ["title"] = title, ["kind"] = kind };
        if (command is not null) call["rawInput"] = new JsonObject { ["command"] = command };
        var answer = await Ask("session/request_permission", new JsonObject
        {
            ["toolCall"] = call,
            ["options"] = new JsonArray(
                new JsonObject { ["optionId"] = "allow", ["name"] = "Allow", ["kind"] = "allow_once" },
                new JsonObject { ["optionId"] = "always", ["name"] = "Always allow", ["kind"] = "allow_always" },
                new JsonObject { ["optionId"] = "reject", ["name"] = "Reject", ["kind"] = "reject_once" }),
        });
        var outcome = answer!["outcome"]!;
        return outcome["outcome"]!.GetValue<string>() == "selected" ? outcome["optionId"]!.GetValue<string>() : null;
    }

    private void Update(string kind, JsonObject content) =>
        Current.Connection.Notify("session/update", new JsonObject
        {
            ["sessionId"] = SessionId,
            ["update"] = new JsonObject { ["sessionUpdate"] = kind, ["content"] = content },
        });

    private static JsonObject Text(string text) => new() { ["type"] = "text", ["text"] = text };
}
