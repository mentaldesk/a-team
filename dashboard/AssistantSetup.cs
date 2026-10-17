using MentalDesk.Tui.Assistant;
using MentalDesk.Tui.Dialogs;

namespace ATeam.Dashboard;

/// <summary>What the Assistant pane runs: Claude Code from <paramref name="Launcher"/>, told <paramref name="Brief"/>
/// first, with its last conversation kept in <paramref name="Sessions"/>. <paramref name="Ask"/> stands in for the
/// permission dialog, and <paramref name="Post"/> for the UI thread.</summary>
public sealed record AssistantSetup(
    IAgentLauncher Launcher, string Brief, string Sessions, Func<PermissionRequest, bool>? Ask = null, Action<Action>? Post = null)
{
    /// <summary>The pane, working in <paramref name="folder"/>: reads go ahead, anything else asks first.</summary>
    internal AssistantPane Pane(string folder, Action<Action> post, Func<IApplication?> app)
    {
        var controller = new AssistantController(Launcher, new AssistantSessions(Sessions), Post ?? post, new MentalDesk.Tui.Assistant.Conversation())
        {
            SessionMeta = AssistantBrief.Meta(Brief),
        };
        controller.Configure(AgentSpec.ClaudeCode);
        controller.OpenFolder(folder);
        controller.AskPermission = request => Task.FromResult(Ask?.Invoke(request) ?? (app() is { } running && Allowed(running, request)));
        return new AssistantPane(controller);
    }

    internal static ConfirmDialog Dialog(PermissionRequest request) =>
        AssistantPermission.Dialog(request, AgentSpec.ClaudeCode.Name).Routed();

    private static bool Allowed(IApplication app, PermissionRequest request)
    {
        using var dialog = Dialog(request);
        DialogHost.Run(app, dialog);
        return dialog.Chosen == AssistantPermission.AllowOnce;
    }
}
