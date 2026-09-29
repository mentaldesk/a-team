using System.Text.Json;
using System.Text.RegularExpressions;

namespace ATeam.Dashboard;

/// <summary>What the team form shows and changes. Every other key in the file is left as it is.</summary>
public sealed partial record TeamSettings(
    string Repo,
    string ProjectOwner,
    int? ProjectNumber,
    string Vision,
    string Workdir,
    string Try,
    bool Working,
    int Worktrees,
    int Pitched,
    int Exploring,
    int Ideas,
    int ReadyFloor,
    string? Checkout)
{
    public static TeamSettings Read(byte[] config)
    {
        using var document = JsonDocument.Parse(config);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("it isn't a JSON object");
        return new TeamSettings(
            Text(root, "repo"),
            Text(root, "project", "owner"),
            Number(root, "project", "number"),
            Text(root, "vision"),
            Text(root, "workdir"),
            Text(root, "try"),
            Find(root, "dispatch", "enabled") is { ValueKind: JsonValueKind.True },
            Number(root, "wip", "worktrees") ?? 0,
            Number(root, "wip", "pitched") ?? 0,
            Number(root, "wip", "exploring") ?? 0,
            Number(root, "wip", "ideas") ?? 0,
            Number(root, "wip", "readyFloor") ?? 0,
            Find(root, "checkout") is { ValueKind: JsonValueKind.String } checkout ? checkout.GetString() : null);
    }

    /// <summary>The config with each value that differs from <paramref name="before"/> set in place, so a save
    /// that changed nothing writes back the same bytes.</summary>
    public byte[] Write(byte[] config, TeamSettings before)
    {
        void Set(string now, string was, params string[] path)
        {
            if (now != was)
                config = ConfigEdit.Set(config, path, now);
        }
        void SetNumber(int? now, int? was, params string[] path)
        {
            if (now != was)
                config = ConfigEdit.Set(config, path, now);
        }
        void SetFlag(bool now, bool was, params string[] path)
        {
            if (now != was)
                config = ConfigEdit.Set(config, path, now);
        }

        Set(Repo, before.Repo, "repo");
        Set(ProjectOwner, before.ProjectOwner, "project", "owner");
        SetNumber(ProjectNumber, before.ProjectNumber, "project", "number");
        Set(Vision, before.Vision, "vision");
        Set(Workdir, before.Workdir, "workdir");
        Set(Try, before.Try, "try");
        SetFlag(Working, before.Working, "dispatch", "enabled");
        SetNumber(Worktrees, before.Worktrees, "wip", "worktrees");
        SetNumber(Pitched, before.Pitched, "wip", "pitched");
        SetNumber(Exploring, before.Exploring, "wip", "exploring");
        SetNumber(Ideas, before.Ideas, "wip", "ideas");
        SetNumber(ReadyFloor, before.ReadyFloor, "wip", "readyFloor");
        return config;
    }

    /// <summary>Why the form won't save these, or null when it will.</summary>
    public string? Refusal() =>
        Repo.Trim().Length == 0 ? "Repo is required."
        : !RepoShape().IsMatch(Repo.Trim()) ? $"Repo must be owner/repo, like mentaldesk/a-team, not {Repo.Trim()}."
        : ProjectOwner.Trim().Length == 0 || ProjectNumber is null ? "Project is required."
        : ProjectNumber <= 0 ? "Project number must be above 0."
        : Vision.Trim().Length == 0 ? "Vision is required."
        : Workdir.Trim().Length == 0 ? "Workdir is required."
        : null;

    /// <summary>What's true of these that the team won't like, though it's still worth saving: a workdir that
    /// isn't there, or a vision the repo hasn't got yet.</summary>
    public string? Warning(string home)
    {
        var workdir = Expand(Workdir.Trim(), home);
        if (!Directory.Exists(workdir))
            return $"{Workdir.Trim()} isn't there, so the agents would have nothing to work in.";
        var checkout = Expand(Checkout ?? Path.Combine(Workdir.Trim(), "main"), home);
        return Directory.Exists(checkout) && !File.Exists(Path.Combine(checkout, Vision.Trim()))
            ? $"{Vision.Trim()} isn't there yet: the Lead will draft one and open it as a draft PR for you."
            : null;
    }

    /// <summary>A checkout other than <c>&lt;workdir&gt;/main</c>, which the form shows but doesn't change.</summary>
    public string? OtherCheckout =>
        Checkout is { } checkout && checkout.TrimEnd('/') != $"{Workdir.TrimEnd('/')}/main" ? checkout : null;

    public string RepoOwner => Repo.Split('/')[0].Trim();

    private static string Expand(string path, string home) =>
        path == "~" ? home : path.StartsWith("~/", StringComparison.Ordinal) ? Path.Combine(home, path[2..]) : path;

    private static JsonElement? Find(JsonElement root, params string[] path)
    {
        var at = root;
        foreach (var key in path)
            if (at.ValueKind != JsonValueKind.Object || !at.TryGetProperty(key, out at))
                return null;
        return at;
    }

    private static string Text(JsonElement root, params string[] path) =>
        Find(root, path) is { ValueKind: JsonValueKind.String } value ? value.GetString() ?? "" : "";

    private static int? Number(JsonElement root, params string[] path) =>
        Find(root, path) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt32(out var number) ? number : null;

    [GeneratedRegex(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")]
    private static partial Regex RepoShape();
}
