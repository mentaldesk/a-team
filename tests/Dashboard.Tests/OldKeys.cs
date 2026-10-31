using System.Text.Json.Nodes;

namespace ATeam.Dashboard.Tests;

/// <summary>Keys as the dashboard saved them before it moved onto MentalDesk.Tui's keymap: one per command.</summary>
internal static class OldKeys
{
    internal static void Save(string config, params (string Id, string Key)[] keys)
    {
        var saved = new JsonObject();
        foreach (var (id, key) in keys)
            saved[id] = key;
        Directory.CreateDirectory(config);
        File.WriteAllText(Path.Combine(config, "dashboard.json"), new JsonObject { ["keys"] = saved }.ToJsonString());
    }
}
