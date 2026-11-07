using MentalDesk.Tui.Chrome;
using MentalDesk.Tui;
using MentalDesk.Tui.Keys;
using MentalDesk.Tui.Links;
using MentalDesk.Tui.Loading;
using System.Drawing;
using System.Globalization;
using System.Text.Json.Nodes;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace ATeam.Dashboard.Tests;

public partial class WorkAreaTests
{
    [Fact]
    public void Try_can_be_rebound_and_the_card_and_the_reader_share_the_new_key()
    {
        Directory.CreateDirectory(_root);
        OldKeys.Save(Config, ("work.try", "z"));
        var handed = new List<Handover>();
        ReaderTry? offered = null;
        using var window = Open(
            handOver: handed.Add,
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (_, _, _, _, _, _, tryIt) => offered = tryIt);
        OpenTheTaskInReview(window);

        Assert.Equal(new Key('z'), offered?.Key);
        Assert.Empty(handed);

        Assert.True(window.NewKeyDownEvent(new Key('z')));

        Assert.Equal(["try", "team0", "122"], Assert.Single(handed).Arguments);
    }

    [Fact]
    public void t_on_a_validated_pitch_hands_over_to_try_the_team_s_default_branch()
    {
        var handed = new List<Handover>();
        using var window = Open(
            read: team => Task.FromResult(new Reading(team == "team0" ? ReviewPitch() : "[]", null)),
            handOver: handed.Add);
        window.Refresh();
        LayOut(window, 120, 30);
        Assert.Equal(174, window.Work.Selected?.Number);

        Assert.True(window.NewKeyDownEvent(new Key('t')));

        var handover = Assert.IsType<TryHandover>(Assert.Single(handed));
        Assert.Equal(["try", "team0"], handover.Arguments);
        Assert.Equal(174, handover.Item.Number);
        Assert.Null(handover.Reader);
    }

    [Fact]
    public void t_in_a_validated_pitch_s_reader_hands_over_to_try_the_default_branch_and_reopens_it_after()
    {
        var handed = new List<Handover>();
        var shown = new IssueBody("## Opportunity");
        using var first = Open(
            read: team => Task.FromResult(new Reading(team == "team0" ? ReviewPitch() : "[]", null)),
            handOver: handed.Add,
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (_, _, _, _, _, _, tryIt) => tryIt!.Run(shown, 5));
        first.Refresh();
        LayOut(first, 120, 30);
        first.NewKeyDownEvent(Key.Enter);
        first.Refresh();
        var handover = Assert.IsType<TryHandover>(Assert.Single(handed));
        Assert.Equal(["try", "team0"], handover.Arguments);
        var reopened = new List<(WaitingItem Item, IssueBody Body, ReaderTry? Try)>();

        using var back = Open(
            showBody: (item, body, _, _, _, _, tryIt) => reopened.Add((item, body, tryIt)),
            resume: handover with { Failure = "try team0 exited 1" });
        back.Refresh();
        LayOut(back, 120, 30);
        back.FocusResumed();
        back.ReopenReader();

        var (item, body, again) = Assert.Single(reopened);
        Assert.Equal(174, item.Number);
        Assert.Same(shown, body);
        Assert.Equal(5, again?.Top);
        Assert.Equal("try team0 exited 1", again?.Failure);
    }

    [Fact]
    public void A_try_from_a_validated_pitch_s_card_that_failed_says_so_on_the_message_line()
    {
        var handed = new List<Handover>();
        using var first = Open(
            read: team => Task.FromResult(new Reading(team == "team0" ? ReviewPitch() : "[]", null)),
            handOver: handed.Add);
        first.Refresh();
        LayOut(first, 120, 30);
        first.NewKeyDownEvent(new Key('t'));

        using var back = Open(resume: handed.Single() with { Failure = "try team0 exited 1" });
        back.Refresh();
        LayOut(back, 120, 30);
        back.FocusResumed();

        Assert.Equal("try team0 exited 1", back.Says());
        Assert.Equal(Severity.Error, back.Status.MessageSeverity);
    }

    [Theory]
    [InlineData("Idea", true, "")]
    [InlineData("Pitched", true, "")]
    [InlineData("Pitched", true, "High")]
    [InlineData("Idea", false, "")]
    public void A_pitch_short_of_Review_or_an_Idea_has_no_try_on_its_card_or_in_its_reader(
        string status, bool pitch, string priority)
    {
        var handed = new List<Handover>();
        var offered = new List<bool>();
        using var window = Open(
            read: team => Task.FromResult(new Reading(team == "team0" ? Unvalidated(status, pitch, priority) : "[]", null)),
            handOver: handed.Add,
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (_, _, _, _, _, _, tryIt) => offered.Add(tryIt is not null));
        window.Refresh();
        LayOut(window, 120, 30);
        Assert.Equal(174, window.Work.Selected?.Number);

        Assert.False(window.Commands.IsEnabled("work.try"));
        window.NewKeyDownEvent(new Key('t'));
        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();

        Assert.Empty(handed);
        Assert.Equal([false], offered);
    }

    private static void OpenTheTaskInReview(DashboardWindow window)
    {
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();
    }

    /// <summary>Two gated items and an unranked Idea for the first team, one gated item for the second, so
    /// columns come back empty. One of the first team's is the Lead's move, so the filter has something to hide.</summary>
    private static string Waiting(string team) => team == "team0"
        ? """
          [{"number": 107, "title": "When the dashboard goes quiet", "status": "Pitched",
            "url": "https://github.com/mentaldesk/team0/issues/107", "team": "team0",
            "turn": "you", "reason": "awaiting your approval since 08:14", "priority": "High", "pitch": true},
           {"number": 108, "title": "A misconfigured team looks like a working one", "status": "Pitched",
            "url": "https://github.com/mentaldesk/team0/issues/108", "team": "team0",
            "turn": "lead", "reason": "answering your feedback since 09:30", "priority": "Medium", "pitch": true},
           {"number": 49, "title": "I can't change any of the keys", "status": "In review",
            "url": "https://github.com/mentaldesk/team0/issues/49", "team": "team0",
            "turn": "dev", "reason": "answering your feedback since 10:15",
            "pr": 122, "prUrl": "https://github.com/mentaldesk/team0/pull/122",
            "checks": "pass", "conflicting": false, "draft": false},
           {"number": 6, "title": "The agents can't say what they'd change", "status": "Idea",
            "url": "https://github.com/mentaldesk/team0/issues/6", "team": "team0",
            "turn": "you", "reason": "waiting to be ranked"}]
          """
        : """
          [{"number": 133, "title": "Notice when open files change on disk", "status": "Pitched",
            "url": "https://github.com/mentaldesk/team1/issues/133", "team": "team1",
            "turn": "you", "reason": "awaiting your approval since 21:37", "priority": "Low", "pitch": true}]
          """;

    /// <summary>What <paramref name="team"/>'s read prints, synced at <paramref name="at"/> and failed since at <paramref name="failed"/>.</summary>
    private static string SyncedAt(string team, DateTimeOffset at, DateTimeOffset? failed = null) =>
        $$"""{{Waiting(team).TrimEnd()[..^1]}}, {"team": "{{team}}", "synced": "{{Iso(at)}}", "failed": {{(failed is { } when ? $"\"{Iso(when)}\"" : "null")}}}]""";

    private static string Iso(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>One task In review and nothing else, its PR in <paramref name="unready"/> trouble where there's any.</summary>
    private static string Review(string unready = "", int pr = 122) =>
        $$"""
          [{"number": 49, "title": "I can't change any of the keys", "status": "In review",
            "url": "https://github.com/mentaldesk/team0/issues/49", "team": "team0",
            "turn": "you", "reason": "awaiting your acceptance since 10:15",
            "pr": {{pr}}, "prUrl": "https://github.com/mentaldesk/team0/pull/{{pr}}", "base": "main",
            "unready": "{{unready}}"}]
          """;

    private const string TwoReviews =
        """
        [{"number": 49, "title": "I can't change any of the keys", "status": "In review",
          "url": "https://github.com/mentaldesk/team0/issues/49", "team": "team0",
          "turn": "you", "reason": "awaiting your acceptance since 10:15",
          "pr": 122, "prUrl": "https://github.com/mentaldesk/team0/pull/122", "base": "main", "unready": ""},
         {"number": 50, "title": "Nothing says when a run is cut short", "status": "In review",
          "url": "https://github.com/mentaldesk/team0/issues/50", "team": "team0",
          "turn": "you", "reason": "awaiting your acceptance since 10:20",
          "pr": 123, "prUrl": "https://github.com/mentaldesk/team0/pull/123", "base": "main", "unready": ""}]
        """;

    private static string DocumentPitch() =>
        """
        [{"number": 60, "title": "Vision review: themes 1 and 5 have run dry", "status": "Pitched",
          "url": "https://github.com/mentaldesk/team0/pull/60", "team": "team0", "pitch": false, "document": true,
          "turn": "you", "reason": "awaiting your approval since 08:14"}]
        """;

    private const string VisionDocument =
        """{"number": 60, "files": ["docs/vision.md"], "path": "docs/vision.md", "ref": "a-team/vision", "sha": "0123abc", "text": "# Vision\n\nThe tenet.\n"}""";

    private static string ReviewPitch(int open = 0) =>
        $$"""
          [{"number": 174, "title": "Accepting finished work", "status": "In review",
            "url": "https://github.com/mentaldesk/team0/issues/174", "team": "team0", "pitch": true,
            "turn": "you", "reason": "awaiting your acceptance since 10:15", "tasks": 3, "openTasks": {{open}}}]
          """;

    private static string Unvalidated(string status, bool pitch, string priority) =>
        $$"""
          [{"number": 174, "title": "Accepting finished work", "status": "{{status}}",
            "url": "https://github.com/mentaldesk/team0/issues/174", "team": "team0", "pitch": {{(pitch ? "true" : "false")}},
            "turn": "you", "reason": "waiting", "priority": "{{priority}}"}]
          """;

    /// <summary><see cref="Waiting"/> read again later, every reason moved on and team0's cards in
    /// <paramref name="order"/>, any it leaves out gone.</summary>
    private static string ReadAgain(string team, params int[] order)
    {
        var cards = JsonNode.Parse(Waiting(team))!.AsArray();
        if (team == "team0")
            cards = [.. order.Select(n => cards.Single(card => card!["number"]!.GetValue<int>() == n)!.DeepClone())];
        foreach (var card in cards)
            card!["reason"] = $"{card["reason"]}, and later";
        return cards.ToJsonString();
    }

    /// <summary>A pitch carrying no Priority, which waits in Triage until it's ranked.</summary>
    private const string Unranked =
        """
        [{"number": 107, "title": "When the dashboard goes quiet", "status": "Pitched",
          "url": "https://github.com/mentaldesk/team0/issues/107", "team": "team0",
          "turn": "you", "reason": "awaiting your approval since 08:14"}]
        """;

    /// <summary>Two Ideas of one team's own, so ranking the first leaves the selection somewhere to go.</summary>
    private const string Queue =
        """
        [{"number": 6, "title": "The agents can't say what they'd change", "status": "Idea",
          "url": "https://github.com/mentaldesk/team0/issues/6", "team": "team0",
          "turn": "you", "reason": "waiting to be ranked"},
         {"number": 26, "title": "A pitch I've shelved", "status": "Idea",
          "url": "https://github.com/mentaldesk/team0/issues/26", "team": "team0",
          "turn": "you", "reason": "waiting to be ranked"}]
        """;

    /// <summary>A task the Dev handed back with a question, and nothing else.</summary>
    private const string Question =
        """
        [{"number": 192, "title": "I can reply to a pitch", "status": "Ready",
          "url": "https://github.com/mentaldesk/team0/issues/192", "team": "team0",
          "turn": "you", "reason": "asked you since 08:23", "question": "Which marker should it post?", "heldBy": "dev"}]
        """;

    /// <summary>That question, and a task waiting on a change in another repo.</summary>
    private const string QuestionAndUpstream =
        """
        [{"number": 192, "title": "I can reply to a pitch", "status": "Ready",
          "url": "https://github.com/mentaldesk/team0/issues/192", "team": "team0",
          "turn": "you", "reason": "asked you since 08:23", "question": "Which marker should it post?"},
         {"number": 469, "title": "Multi-line boxes keep what I type in view", "status": "Ready",
          "url": "https://github.com/mentaldesk/team0/issues/469", "team": "team0", "turn": "upstream",
          "upstream": "tui-style-guide#63: open", "reason": "waiting on tui-style-guide#63: open"}]
        """;

    /// <summary>A pitch the Lead needs an answer on, and nothing else.</summary>
    private const string PitchQuestion =
        """
        [{"number": 257, "title": "I wait on the team", "status": "Pitched", "pitch": true,
          "url": "https://github.com/mentaldesk/team0/issues/257", "team": "team0", "priority": "High",
          "turn": "you", "reason": "asked you since 08:00", "question": "## Needs your answer\n\n1. Which?"}]
        """;


    [Fact]
    public void A_recommended_Triage_card_puts_the_Lead_s_case_on_the_bar_and_opens_on_its_recommendation()
    {
        Rank? offered = null;
        using var window = Open(
            read: team => Task.FromResult(new Reading(team == "team0" ? Recommended : "[]", null)),
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            chooseRank: (_, _, rank) => offered = rank);
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.Equal(6, window.Work.Selected?.Number);
        Assert.Equal("#6 · High? · theme 2, small · clears the queue", window.Says());

        window.Commands.Execute("work.priority");
        window.Refresh();

        Assert.Equal(Rank.High, offered);
    }

    /// <summary>An Idea the Lead has recommended a rank for, and nothing else.</summary>
    private const string Recommended =
        """
        [{"number": 6, "title": "The agents can't say what they'd change", "status": "Idea",
          "url": "https://github.com/mentaldesk/team0/issues/6", "team": "team0",
          "turn": "you", "reason": "theme 2, small · clears the queue", "recommendation": "High"}]
        """;
    private const string Body = """{"number": 6, "title": "t", "body": "## Opportunity"}""";

    private static IEnumerable<View> Descendants(View view) =>
        view.SubViews.SelectMany(child => Descendants(child).Prepend(child));

    private static IEnumerable<string> Titles(DashboardWindow window) =>
        window.Work.Lanes.SelectMany(lane => lane.Columns).Select(column => column.Title);

    private static void LayOut(DashboardWindow window, int width, int height)
    {
        window.Frame = new Rectangle(0, 0, width, height);
        window.Layout(new Size(width, height));
    }

    [Fact]
    public void F5_reads_what_s_waiting_again_and_r_no_longer_does()
    {
        var reads = 0;
        using var window = Open(read: team =>
        {
            reads++;
            return Task.FromResult(new Reading(Waiting(team), null));
        });
        window.Refresh();

        Assert.False(window.NewKeyDownEvent(new Key('r')));
        Assert.Equal(2, reads);

        Assert.True(window.NewKeyDownEvent(Key.F5));
        Assert.Equal(4, reads);
    }

    [Fact]
    public void A_rebound_refresh_keeps_its_key_and_the_menu_shows_it()
    {
        var reads = 0;
        Directory.CreateDirectory(Config);
        OldKeys.Save(Config, ("work.refresh", "z"));
        using var window = Open(read: team =>
        {
            reads++;
            return Task.FromResult(new Reading(Waiting(team), null));
        });
        window.Refresh();

        Assert.True(window.NewKeyDownEvent(new Key('z')));

        Assert.Equal(4, reads);
        Assert.Equal("Z", MenuItem(window, "work.refresh").KeyView.Text);
    }
}
