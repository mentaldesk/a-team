namespace ATeam.Dashboard;

/// <summary>A page of the guide, or one of its <c>##</c> sections, which opens the page at its heading.</summary>
internal sealed record GuideTopic(string Page, string PageName, string Name, string Anchor, IReadOnlyList<GuideTopic> Sections)
{
    internal bool IsPage => Anchor.Length == 0;
}

/// <summary>The guide's pages and their sections, read from the pages' own headings.</summary>
internal static class GuideTopics
{
    /// <summary>The pages the contents page links to, in its order, then any others by name. The contents page is left
    /// out: the sidebar does its job.</summary>
    internal static IReadOnlyList<GuideTopic> Read(IReadOnlyDictionary<string, string> guide)
    {
        var pages = guide.Keys.Where(page => page != GuideDialog.Contents).Order(StringComparer.Ordinal).ToList();
        var linked = (guide.TryGetValue(GuideDialog.Contents, out var contents) ? Linked(contents) : [])
            .Where(pages.Contains).Distinct().ToList();
        return [.. linked.Concat(pages.Except(linked)).Select(page => Parse(page, guide[page]))];
    }

    internal static GuideTopic Parse(string page, string markdown)
    {
        var name = GuideDialog.Named(markdown) is { Length: > 0 } named ? named : Path.GetFileNameWithoutExtension(page);
        return new GuideTopic(page, name, name, "", [.. Headings(markdown).Select(heading =>
            new GuideTopic(page, name, heading, Slug(heading), []))]);
    }

    /// <summary>The <c>##</c> headings, leaving out any in a fenced code block.</summary>
    internal static IEnumerable<string> Headings(string markdown)
    {
        var fenced = false;
        foreach (var line in markdown.Split('\n').Select(line => line.TrimEnd('\r')))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
                fenced = !fenced;
            else if (!fenced && line.StartsWith("## ", StringComparison.Ordinal))
                yield return line[3..].Trim();
        }
    }

    /// <summary>GitHub's anchor for a heading, which is also the one the guide's Markdown view scrolls to.</summary>
    internal static string Slug(string heading) =>
        string.Concat(heading.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_')).Replace(' ', '-');

    private static IEnumerable<string> Linked(string contents)
    {
        foreach (var line in contents.Split('\n'))
            for (var at = line.IndexOf("](", StringComparison.Ordinal); at >= 0; at = line.IndexOf("](", at + 2, StringComparison.Ordinal))
                if (line.IndexOf(')', at) is var end and > 0)
                    yield return GuideDialog.Split(line[(at + 2)..end]).Path;
    }
}
