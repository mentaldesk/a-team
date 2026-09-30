namespace ATeam.Dashboard;

/// <summary>The skills a team's agents could load: those installed for Claude Code, and the repo's own.</summary>
public sealed record SkillsFound(IReadOnlyList<string> Names, IReadOnlyList<string> Places)
{
    internal const string Installed = "~/.claude/skills";
    internal const string InRepo = ".claude/skills";

    /// <summary>Every directory holding a <c>SKILL.md</c> under <c>~/.claude/skills</c> and the checkout's own.</summary>
    public static SkillsFound Find(string home, string checkout)
    {
        var places = new List<string>();
        var names = new List<string>();
        foreach (var (place, directory) in new[]
                 {
                     (Installed, Path.Combine(home, ".claude", "skills")),
                     ($"{checkout.TrimEnd('/')}/{InRepo}", Path.Combine(TeamSettings.Expand(checkout, home), ".claude", "skills")),
                 })
        {
            if (!Directory.Exists(directory))
                continue;
            places.Add(place);
            names.AddRange(Directory.GetDirectories(directory)
                .Where(skill => File.Exists(Path.Combine(skill, "SKILL.md")))
                .Select(Path.GetFileName)
                .OfType<string>()
                .Where(name => !names.Contains(name))
                .Order(StringComparer.Ordinal));
        }
        return new SkillsFound(names, places);
    }

    public bool Has(string name) => Names.Contains(name);

    /// <summary>What the picker says about what it found and where.</summary>
    public string Message =>
        Places.Count == 0
            ? $"No skills under {Installed}. Type a name to add one."
            : $"{Names.Count} found under {string.Join(" and ", Places)}. Type a name to add one that isn't listed.";
}
