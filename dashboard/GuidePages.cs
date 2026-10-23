namespace ATeam.Dashboard;

/// <summary>The guide's pages, by their path in <c>docs/guide</c>, and each page's Markdown.</summary>
internal static class GuidePages
{
    private const string Prefix = "guide/";

    /// <summary>The pages built into the app, so the Guide matches the version that opens it wherever it runs.</summary>
    internal static IReadOnlyDictionary<string, string> Bundled()
    {
        var assembly = typeof(GuidePages).Assembly;
        var pages = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.StartsWith(Prefix, StringComparison.Ordinal)))
        {
            using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
            pages[Normal(name[Prefix.Length..])] = reader.ReadToEnd();
        }
        return pages;
    }

    internal static IReadOnlyDictionary<string, string> In(string folder) =>
        Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*.md", SearchOption.AllDirectories)
                .ToDictionary(file => Normal(Path.GetRelativePath(folder, file)), File.ReadAllText, StringComparer.Ordinal)
            : new Dictionary<string, string>();

    /// <summary>A link's target, from the page it's on, as the path of a page.</summary>
    internal static string Resolve(string from, string link)
    {
        var parts = new List<string>(Normal(from).Split('/')[..^1]);
        foreach (var part in Normal(Uri.UnescapeDataString(link)).Split('/'))
            if (part == ".." && parts.Count > 0 && parts[^1] != "..")
                parts.RemoveAt(parts.Count - 1);
            else if (part is not ("." or ""))
                parts.Add(part);
        return string.Join('/', parts);
    }

    private static string Normal(string path) => path.Replace('\\', '/');
}
