using System.Drawing;
using System.Text.RegularExpressions;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace ATeam.Dashboard.Tests;

/// <summary>The guide: a page at a time, links between pages and to headings, back, and out to the browser.</summary>
public partial class GuideDialogTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"a-team-guide-{Guid.NewGuid():n}");

    public GuideDialogTests()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "index.md"), """
            # Contents

            Read [Work](work.md), or the [columns](work.md#columns) on it.

            [Missing](nowhere.md) and [GitHub](https://github.com/mentaldesk/a-team).
            """);
        File.WriteAllText(Path.Combine(_folder, "work.md"), $"""
            # Work

            {string.Join("\n\n", Enumerable.Range(1, 30).Select(line => $"Paragraph {line}."))}

            ## Columns

            Back to the [top](#work).
            """);
    }

    public void Dispose()
    {
        Directory.Delete(_folder, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void It_opens_on_the_contents_page_titled_after_it()
    {
        using var dialog = Open();

        Assert.Equal("index.md", dialog.Page);
        Assert.Equal("Guide · Contents", dialog.Title);
        Assert.StartsWith("# Contents", dialog.View.Text);
    }

    [Fact]
    public void The_hint_row_names_the_keys()
    {
        using var dialog = Open();

        Assert.Equal(["Tab next link", "Enter follow", "Backspace back", "Esc close"], dialog.Hints.Hints.Select(hint => hint.Text));
    }

    [Fact]
    public void Enter_on_the_first_link_Tab_lands_on_follows_it_to_its_page()
    {
        using var dialog = Open();

        dialog.View.AdvanceFocus(NavigationDirection.Forward, TabBehavior.TabStop);
        dialog.NewKeyDownEvent(Key.Enter);

        Assert.Equal("work.md", dialog.Page);
        Assert.Equal("Guide · Work", dialog.Title);
        Assert.False(dialog.Closed);
    }

    [Fact]
    public void A_link_to_a_heading_on_another_page_opens_it_there()
    {
        using var dialog = Open();

        dialog.Follow("work.md#columns");
        dialog.Layout(new Size(60, 12));

        Assert.Equal("work.md", dialog.Page);
        Assert.True(dialog.View.Viewport.Y > 0);
    }

    [Fact]
    public void Backspace_goes_back_to_the_page_and_line_the_link_was_on()
    {
        using var dialog = Open();
        dialog.Follow("work.md#columns");
        dialog.Layout(new Size(60, 12));

        dialog.NewKeyDownEvent(Key.Backspace);
        dialog.Layout(new Size(60, 12));

        Assert.Equal("index.md", dialog.Page);
        Assert.Equal(0, dialog.View.Viewport.Y);
    }

    [Fact]
    public void A_heading_on_the_same_page_is_somewhere_Backspace_comes_back_from()
    {
        using var dialog = Open();
        dialog.Follow("work.md#columns");
        dialog.Layout(new Size(60, 12));
        dialog.View.AdvanceFocus(NavigationDirection.Forward, TabBehavior.TabStop);
        var link = dialog.View.Viewport.Y;

        dialog.NewKeyDownEvent(Key.Enter);
        dialog.Layout(new Size(60, 12));
        Assert.Equal(0, dialog.View.Viewport.Y);

        dialog.NewKeyDownEvent(Key.Backspace);
        dialog.Layout(new Size(60, 12));

        Assert.Equal("work.md", dialog.Page);
        Assert.Equal(link, dialog.View.Viewport.Y);
    }

    [Fact]
    public void Backspace_on_the_first_page_stays_there()
    {
        using var dialog = Open();

        Assert.True(dialog.NewKeyDownEvent(Key.Backspace));

        Assert.Equal("index.md", dialog.Page);
        Assert.False(dialog.Closed);
    }

    [Fact]
    public void A_web_link_opens_in_the_browser_and_the_page_stays()
    {
        var opened = new List<string>();
        using var dialog = Open(opened.Add);

        dialog.Follow("https://github.com/mentaldesk/a-team");

        Assert.Equal(["https://github.com/mentaldesk/a-team"], opened);
        Assert.Equal("index.md", dialog.Page);
    }

    [Fact]
    public void A_link_to_a_page_that_is_not_there_says_so()
    {
        using var dialog = Open();

        dialog.Follow("nowhere.md");

        Assert.Equal("index.md", dialog.Page);
        Assert.Equal("There's no nowhere.md in the guide", dialog.Message.Says);
        dialog.NewKeyDownEvent(Key.Backspace);
        Assert.Equal("index.md", dialog.Page);
    }

    [Fact]
    public void A_guide_with_no_contents_page_says_so()
    {
        using var dialog = new GuideDialog(Path.Combine(_folder, "gone"), _ => { });

        Assert.Equal("Guide", dialog.Title);
        Assert.Equal("There's no index.md in the guide", dialog.Message.Says);
    }

    [Fact]
    public void Esc_closes_it()
    {
        using var dialog = Open();

        Assert.True(dialog.NewKeyDownEvent(Key.Esc));

        Assert.True(dialog.Closed);
    }

    public static TheoryData<string, string> Links() => [.. GuideLinks()];

    [Theory]
    [MemberData(nameof(Links))]
    public void Every_link_in_the_guide_goes_to_a_page_and_heading_that_exist(string page, string link)
    {
        using var dialog = new GuideDialog(Guide(), _ => { }, page);
        dialog.Layout(new Size(100, 40));

        dialog.Follow(link);
        dialog.Layout(new Size(100, 40));

        Assert.Equal("", dialog.Message.Says);
        if (GuideDialog.Split(link).Anchor is { Length: > 0 } anchor)
            Assert.True(dialog.View.ScrollToAnchor(anchor), $"{page} links to #{anchor}, which {dialog.Page} has no heading for");
    }

    [Fact]
    public void The_guide_has_the_pages_the_contents_page_promises()
    {
        Assert.Contains(("index.md", "how-a-team-works.md"), GuideLinks());
        Assert.Contains(("index.md", "work.md"), GuideLinks());
    }

    private GuideDialog Open(Action<string>? openUrl = null)
    {
        var dialog = new GuideDialog(_folder, openUrl ?? (_ => { }));
        dialog.Layout(new Size(60, 12));
        return dialog;
    }

    private static IEnumerable<(string Page, string Link)> GuideLinks() =>
        from page in Directory.GetFiles(Guide(), "*.md")
        from Match link in Link().Matches(File.ReadAllText(page))
        where !GuideDialog.IsWeb(link.Groups[1].Value)
        select (Path.GetFileName(page), link.Groups[1].Value);

    private static string Guide()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "docs", "guide")))
                return Path.Combine(dir.FullName, "docs", "guide");
        throw new DirectoryNotFoundException("no docs/guide above the test run");
    }

    [GeneratedRegex(@"\]\(([^)\s]+)\)")]
    private static partial Regex Link();
}
