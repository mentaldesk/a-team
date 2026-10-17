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
    public void Back_and_Close_are_buttons_and_F1_lists_the_rest_under_the_page_s_title()
    {
        using var dialog = Open();

        Assert.Equal(["Backspace Back", "Esc Close"], dialog.ButtonLabels);
        Assert.Empty(dialog.SubViews.OfType<StatusBar>());
        var sheet = DialogTests.F1(dialog);
        Assert.Equal("Guide · Contents", sheet.Here!.Heading);
        Assert.Equal(
            [("Backspace", "Back"), ("Ctrl+Shift+C", "Contents"), ("Ctrl+Shift+I", "Index"), ("Tab", "Next link"),
                ("Enter", "Follow the link"), ("Esc", "Close")],
            DialogTests.Rows(sheet));
    }

    [Fact]
    public void F1_in_the_Index_lists_moving_between_matches_and_not_Back()
    {
        using var dialog = Open();
        dialog.ShowIndex();

        var rows = DialogTests.Rows(DialogTests.F1(dialog));

        Assert.Contains(("↑", "Previous match"), rows);
        Assert.Contains(("Enter", "Go to the section"), rows);
        Assert.DoesNotContain(("Backspace", "Back"), rows);
    }

    [Fact]
    public void Enter_on_the_first_link_Tab_lands_on_follows_it_to_its_page()
    {
        using var dialog = Open();

        dialog.View.AdvanceFocus(NavigationDirection.Forward, TabBehavior.TabStop);
        dialog.NewKeyDownEvent(Key.Enter);

        Assert.Equal("work.md", dialog.Page);
        Assert.Equal("Guide · Work", dialog.Title);
        Assert.False(dialog.HasClosed);
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
    public void Opened_on_a_page_Backspace_goes_to_the_contents_page()
    {
        using var dialog = new GuideDialog(_folder, _ => { }, "work.md");
        dialog.Layout(new Size(60, 12));

        Assert.Equal("Guide · Work", dialog.Title);
        dialog.NewKeyDownEvent(Key.Backspace);

        Assert.Equal("index.md", dialog.Page);
        Assert.False(dialog.HasClosed);
    }

    [Fact]
    public void Backspace_on_the_first_page_stays_there()
    {
        using var dialog = Open();

        Assert.True(dialog.NewKeyDownEvent(Key.Backspace));

        Assert.Equal("index.md", dialog.Page);
        Assert.False(dialog.HasClosed);
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

        Assert.True(dialog.HasClosed);
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
        Assert.Contains(("index.md", "dashboard.md"), GuideLinks());
        Assert.Contains(("index.md", "teams.md"), GuideLinks());
        Assert.Contains(("index.md", "overseer.md"), GuideLinks());
    }

    [Theory]
    [InlineData(GuideDialog.Work)]
    [InlineData(GuideDialog.Dashboard)]
    [InlineData(GuideDialog.Teams)]
    [InlineData(GuideDialog.Overseer)]
    public void Every_page_the_app_opens_the_guide_on_is_there(string page)
    {
        using var dialog = new GuideDialog(Guide(), _ => { }, page);

        Assert.Equal(page, dialog.Page);
        Assert.Equal("", dialog.Message.Says);
    }

    [Fact]
    public void Contents_has_each_page_with_its_sections_and_leaves_the_contents_page_out()
    {
        using var dialog = Open();

        var page = Assert.Single(Pages(dialog));
        Assert.Equal(("work.md", "Work"), (page.Page, page.Name));
        Assert.Equal(["Columns"], page.Sections.Select(section => section.Name));
    }

    [Fact]
    public void A_page_added_to_the_guide_shows_up_after_the_ones_the_contents_page_links_to()
    {
        File.WriteAllText(Path.Combine(_folder, "added.md"), "# Added\n\n## First\n\n```\n## Not a section\n```\n\n### Too deep\n");

        using var dialog = Open();

        Assert.Equal(["Work", "Added"], Pages(dialog).Select(page => page.Name));
        Assert.Equal(["First"], Pages(dialog).Last().Sections.Select(section => section.Name));
    }

    [Fact]
    public void The_page_it_opens_on_is_expanded_and_selected_in_Contents()
    {
        using var dialog = new GuideDialog(_folder, _ => { }, "work.md");

        Assert.True(dialog.ContentsTree.IsExpanded(Pages(dialog).First()));
        Assert.Equal("Work", dialog.ContentsTree.SelectedObject?.Name);
    }

    [Fact]
    public void Following_a_link_and_going_back_move_the_Contents_selection_with_you()
    {
        using var dialog = new GuideDialog(_folder, _ => { }, "work.md");
        dialog.Layout(new Size(80, 12));

        dialog.Follow("#columns");
        Assert.Equal("Columns", dialog.ContentsTree.SelectedObject?.Name);

        dialog.NewKeyDownEvent(Key.Backspace);
        Assert.Equal("Work", dialog.ContentsTree.SelectedObject?.Name);
    }

    [Fact]
    public void Enter_on_a_section_in_Contents_opens_its_page_there_and_reads_on_in_the_page()
    {
        using var dialog = Open();
        dialog.NewKeyDownEvent(Key.C.WithCtrl.WithShift);
        dialog.ContentsTree.SelectedObject = Pages(dialog).First().Sections[0];

        dialog.NewKeyDownEvent(Key.Enter);
        dialog.Layout(new Size(80, 12));

        Assert.Equal("work.md", dialog.Page);
        Assert.True(dialog.View.Viewport.Y > 0);
        Assert.True(dialog.View.HasFocus);
        Assert.False(dialog.HasClosed);
    }

    [Fact]
    public void Ctrl_Shift_C_and_Ctrl_Shift_I_show_and_focus_their_tab_from_the_page_and_from_each_other()
    {
        using var dialog = Open();
        Assert.True(dialog.View.HasFocus);

        Assert.True(dialog.NewKeyDownEvent(Key.I.WithCtrl.WithShift));
        Assert.True(dialog.ShowsIndex);
        Assert.True(dialog.IndexFilter.HasFocus);

        Assert.True(dialog.NewKeyDownEvent(Key.C.WithCtrl.WithShift));
        Assert.False(dialog.ShowsIndex);
        Assert.True(dialog.ContentsTree.HasFocus);

        Assert.True(dialog.NewKeyDownEvent(Key.I.WithCtrl.WithShift));
        Assert.True(dialog.IndexFilter.HasFocus);
    }

    [Fact]
    public void The_Index_filters_by_any_part_of_a_section_s_name_whatever_its_case()
    {
        using var dialog = Open();
        dialog.NewKeyDownEvent(Key.I.WithCtrl.WithShift);
        Assert.Equal(["Columns"], dialog.Matches.Select(topic => topic.Name));

        dialog.IndexFilter.Text = "LUMN";
        Assert.Equal(["Columns"], dialog.Matches.Select(topic => topic.Name));

        dialog.IndexFilter.Text = "rows";
        Assert.Empty(dialog.Matches);
    }

    [Fact]
    public void Each_match_in_the_Index_names_its_page_under_it()
    {
        using var dialog = Open();

        Assert.Equal(["Columns", "  Work"], dialog.IndexList.Source!.ToList().Cast<string>());
    }

    [Fact]
    public void Backspace_in_the_Index_edits_the_filter_and_stays_on_the_page()
    {
        using var dialog = new GuideDialog(_folder, _ => { }, "work.md");
        dialog.NewKeyDownEvent(Key.I.WithCtrl.WithShift);
        dialog.IndexFilter.Text = "col";
        dialog.IndexFilter.MoveEnd();

        dialog.NewKeyDownEvent(Key.Backspace);

        Assert.Equal("co", dialog.IndexFilter.Text);
        Assert.Equal("work.md", dialog.Page);
    }

    [Fact]
    public void Enter_in_the_Index_opens_the_match_s_page_at_its_heading()
    {
        using var dialog = Open();
        dialog.NewKeyDownEvent(Key.I.WithCtrl.WithShift);
        dialog.IndexFilter.Text = "col";

        dialog.NewKeyDownEvent(Key.Enter);
        dialog.Layout(new Size(80, 12));

        Assert.Equal("work.md", dialog.Page);
        Assert.True(dialog.View.Viewport.Y > 0);
        Assert.Equal("Columns", dialog.ContentsTree.SelectedObject?.Name);
    }

    [Fact]
    public void Up_and_Down_in_the_Index_move_from_section_to_section()
    {
        using var dialog = new GuideDialog(Guide(), _ => { }, GuideDialog.Work);
        dialog.NewKeyDownEvent(Key.I.WithCtrl.WithShift);
        dialog.IndexFilter.Text = "pau";
        Assert.Equal(["Pausing a role", "Pausing a team"], dialog.Matches.Select(topic => topic.Name));

        dialog.NewKeyDownEvent(Key.CursorDown);
        Assert.Equal("Pausing a team", dialog.Chosen?.Name);
        dialog.NewKeyDownEvent(Key.CursorDown);
        Assert.Equal("Pausing a team", dialog.Chosen?.Name);
        dialog.NewKeyDownEvent(Key.CursorUp);
        Assert.Equal("Pausing a role", dialog.Chosen?.Name);
    }

    public static TheoryData<string, string> Sections() =>
        [.. from page in Directory.GetFiles(Guide(), "*.md")
            where Path.GetFileName(page) != GuideDialog.Contents
            from line in File.ReadLines(page)
            where line.StartsWith("## ", StringComparison.Ordinal)
            select (Path.GetFileName(page), line[3..].Trim())];

    [Theory]
    [MemberData(nameof(Sections))]
    public void Every_section_of_the_guide_is_in_Contents_and_the_Index_and_opens_at_its_heading(string page, string heading)
    {
        using var dialog = new GuideDialog(Guide(), _ => { }, GuideDialog.Work);
        dialog.Layout(new Size(100, 40));

        var section = Assert.Single(Pages(dialog).Single(topic => topic.Page == page).Sections, topic => topic.Name == heading);
        dialog.IndexFilter.Text = heading;
        Assert.Contains(section, dialog.Matches);

        dialog.Go(section);
        dialog.Layout(new Size(100, 40));

        Assert.Equal(page, dialog.Page);
        Assert.True(dialog.View.ScrollToAnchor(section.Anchor), $"{page} has no heading for #{section.Anchor}");
    }

    [Fact]
    public void Contents_and_the_Index_hold_only_the_guide_s_sections()
    {
        using var dialog = new GuideDialog(Guide(), _ => { }, GuideDialog.Work);

        Assert.Equal(Sections().Count, Pages(dialog).Sum(topic => topic.Sections.Count));
        Assert.Equal(Sections().Count, dialog.Matches.Count);
    }

    private GuideDialog Open(Action<string>? openUrl = null)
    {
        var dialog = new GuideDialog(_folder, openUrl ?? (_ => { }));
        dialog.Layout(new Size(60, 12));
        return dialog;
    }

    private static List<GuideTopic> Pages(GuideDialog dialog) => [.. dialog.ContentsTree.Objects ?? []];

    private static IEnumerable<(string Page, string Link)> GuideLinks() =>
        (from page in Directory.GetFiles(Guide(), "*.md")
        from Match link in Link().Matches(CodeSpan().Replace(File.ReadAllText(page), ""))
        where !GuideDialog.IsWeb(link.Groups[1].Value)
        select (Path.GetFileName(page), link.Groups[1].Value)).Distinct();

    private static string Guide()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "docs", "guide")))
                return Path.Combine(dir.FullName, "docs", "guide");
        throw new DirectoryNotFoundException("no docs/guide above the test run");
    }

    [GeneratedRegex(@"\]\(([^)\s]+)\)")]
    private static partial Regex Link();

    [GeneratedRegex("`[^`\n]*`")]
    private static partial Regex CodeSpan();
}
