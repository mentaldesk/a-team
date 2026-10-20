using System.Drawing;

namespace ATeam.Dashboard.Tests;

public class HoldsTests
{
    private static readonly ChainLink Release = new("tui", [], "MentalDesk.Tui's release", "yours");
    private static readonly ChainLink Bump = new("a-team", [], "a-team's bump", "yours");

    private static WaitingItem Held(IReadOnlyList<ChainLink>? chain = null) =>
        new(499, "Copies reach my laptop over SSH", "Ready", "https://github.com/mentaldesk/a-team/issues/499", "a-team",
            "upstream", "waiting on tui-style-guide#81: open", Upstream: "tui-style-guide#81: open", Chains: chain is null ? null : [chain]);

    private static readonly WaitingItem InReview =
        new(81, "An app without AppShell can install the clipboard", "In review", "https://github.com/mentaldesk/tui-style-guide/issues/81",
            "tui", "you", "awaiting your acceptance", Pr: 84, PrUrl: "https://github.com/mentaldesk/tui-style-guide/pull/84");

    private static readonly WaitingItem Other =
        new(79, "Pickers filter by camel humps", "In review", "https://github.com/mentaldesk/tui-style-guide/issues/79",
            "tui", "you", "awaiting your acceptance", Pr: 83, Priority: "Urgent");

    private static WaitingItem Find(IReadOnlyList<WaitingItem> items, string team, int number) =>
        items.Single(item => item.Team == team && item.Number == number);

    [Fact]
    public void The_card_at_the_link_holding_a_task_now_frees_it_and_the_task_says_whose_move_that_is()
    {
        var tagged = Holds.Tag([Held([new("tui", [81, 71, 84], "tui-style-guide#84", "open"), Release, Bump]), InReview, Other]);

        Assert.Equal("frees a-team #499", Holds.Frees(Find(tagged, "tui", 81)));
        Assert.Empty(Find(tagged, "tui", 79).Holding);
        Assert.Equal("waits on tui-style-guide#84 · in review, yours", Find(tagged, "a-team", 499).WaitsOn);
        Assert.Equal("a-team #499 waits on this · then MentalDesk.Tui's release · then a-team's bump", Find(tagged, "tui", 81).Line);
        Assert.Equal("#499 · waits on tui-style-guide#84 · in review, yours · then MentalDesk.Tui's release · then a-team's bump",
            Find(tagged, "a-team", 499).Line);
    }

    [Fact]
    public void A_task_in_review_waiting_on_another_repo_stays_in_Review_and_says_what_it_waits_on()
    {
        var reviewed = new WaitingItem(541, "Ask the Assistant pane anything", "In review", "https://github.com/mentaldesk/a-team/issues/541",
            "a-team", "you", "awaiting your acceptance", Pr: 551, Branch: "feat/assistant-pane",
            Chains: [[new("tui", [100], "tui-style-guide#100", "open"), Release, Bump]]);
        var upstreamPr = new WaitingItem(100, "Shared pane", "In review", "https://github.com/mentaldesk/tui-style-guide/pull/100",
            "tui", "you", "awaiting your acceptance", Pr: 100);
        var tagged = Holds.Tag([reviewed, upstreamPr, Other]);

        Assert.Equal("frees a-team #541", Holds.Frees(Find(tagged, "tui", 100)));
        Assert.Equal("a-team #541 waits on this · then MentalDesk.Tui's release · then a-team's bump", Find(tagged, "tui", 100).Line);
        Assert.Equal("waits on tui-style-guide#100 · in review, yours", Find(tagged, "a-team", 541).WaitsOn);
        Assert.Equal("#541 · waits on tui-style-guide#100 · in review, yours · then MentalDesk.Tui's release · then a-team's bump"
            + " · PR #551 · feat/assistant-pane", Find(tagged, "a-team", 541).Line);
        Assert.Equal("Review", WorkView.GateOf(Find(tagged, "a-team", 541)));
        Assert.Contains(("tui", 100), Holds.Holders(tagged));

        using var view = new WorkView(["a-team", "tui"]);
        view.Frame = new Rectangle(0, 0, 160, 20);
        view.Layout(new Size(160, 20));
        view.Show([reviewed, Other, upstreamPr]);
        view.Frame = new Rectangle(0, 0, 160, 20);
        view.Layout(new Size(160, 20));

        var theirs = view.Lanes[1].Columns.Single(column => column.Gate == "Review").CardText.Where(text => text.StartsWith('#')).ToList();
        Assert.StartsWith("#100 ⛓ frees a-team #541  ", theirs[0]);
        Assert.Equal("tui ⛓ 1 PR2", view.Lanes[1].Title);
        var ours = view.Lanes[0].Columns.Single(column => column.Gate == "Review").CardText;
        Assert.StartsWith("#541 ⛓ waits on tui-style-guide#100 · in review, yours  ", ours[0]);
        Assert.Contains(ours, text => text.Contains("#551"));

        Assert.Empty(Holds.Tag([reviewed with { Chains = null }, upstreamPr]).SelectMany(item => item.Holding));
    }

    [Fact]
    public void Holders_are_the_cards_a_task_waits_on_now_whether_or_not_work_shows_them()
    {
        var prerequisite = new WaitingItem(530, "", "In review", "", "a-team", "you", "", Frees: [531]);
        var items = Holds.Tag([Held([new("tui", [81, 84], "tui-style-guide#84", "open"), Release]), InReview, Other, prerequisite]);

        Assert.Equal(
            new HashSet<(string, int)> { ("tui", 81), ("tui", 84), ("a-team", 530) },
            Holds.Holders(items));
    }

    [Fact]
    public void A_card_is_found_by_its_PR_too()
    {
        var byPr = InReview with { Number = 90 };

        Assert.Equal("frees a-team #499", Holds.Frees(Find(Holds.Tag([Held([new("tui", [84], "tui-style-guide#84", "open")]), byPr]), "tui", 90)));
    }

    [Fact]
    public void A_link_with_no_card_says_what_the_board_said_and_tags_nothing()
    {
        var tagged = Holds.Tag([Held([Release, Bump]), InReview]);

        Assert.Equal("waits on MentalDesk.Tui's release · yours", Find(tagged, "a-team", 499).WaitsOn);
        Assert.All(tagged, item => Assert.Empty(item.Holding));
    }

    [Fact]
    public void The_tag_moves_along_the_chain_and_drops_off_once_the_task_is_free()
    {
        var bump = new WaitingItem(535, "Bump MentalDesk.Tui from 0.3.8 to 0.3.9", "In review", "https://github.com/mentaldesk/a-team/pull/535",
            "a-team", "you", "awaiting your acceptance", Dependency: true);

        var released = Holds.Tag([Held([new("a-team", [535], "#535", "yours")]), InReview, bump]);
        Assert.Empty(Find(released, "tui", 81).Holding);
        Assert.Equal("frees #499", Holds.Frees(Find(released, "a-team", 535)));
        Assert.Equal("waits on #535 · in review, yours", Find(released, "a-team", 499).WaitsOn);

        var free = Holds.Tag([InReview, bump]);
        Assert.All(free, item => Assert.Empty(item.Holding));
    }

    [Fact]
    public void A_chain_through_a_repo_with_no_team_still_names_the_link_on_the_held_task()
    {
        var tagged = Holds.Tag([Held([new("", [12], "lib#12", "open")]), InReview with { Team = "a-team", Number = 12 }]);

        Assert.Equal("waits on lib#12 · open", Find(tagged, "a-team", 499).WaitsOn);
        Assert.All(tagged, item => Assert.Empty(item.Holding));
    }

    [Fact]
    public void A_prerequisite_on_the_same_team_frees_the_tasks_waiting_on_it()
    {
        var tagged = Holds.Tag([InReview with { Team = "a-team", Number = 543, Frees = [540, 541] }]);

        Assert.Equal("frees #540 +1", Holds.Frees(tagged[0]));
        Assert.Equal("#540 and #541 wait on this", tagged[0].Line);
    }

    [Fact]
    public void A_task_whose_waits_are_all_met_but_not_yet_freed_says_how_far_they_got()
    {
        var tagged = Holds.Tag([Held() with { Upstream = "tui-style-guide#81: released in v0.3.9" }]);

        Assert.Equal("waits on tui-style-guide#81: released in v0.3.9", tagged[0].WaitsOn);
    }

    [Fact]
    public void A_card_whose_tags_have_not_changed_is_the_same_item()
    {
        var once = Holds.Tag([Held([new("tui", [81], "tui-style-guide#84", "open")]), InReview]);

        Assert.All(Holds.Tag(once).Zip(once), pair => Assert.Same(pair.Second, pair.First));
    }

    [Fact]
    public void The_board_s_chains_and_frees_reach_the_item()
    {
        var item = Assert.Single(WaitingItem.Parse("""
            [{"number": 499, "title": "t", "status": "Ready", "team": "a-team", "turn": "upstream", "upstream": "tui-style-guide#81: open",
              "chains": [[{"team": "tui", "refs": [81, 84], "what": "tui-style-guide#84", "shown": "open"},
                          {"team": null, "refs": [], "what": "MentalDesk.Tui's release", "shown": "yours"}]],
              "frees": [12]}]
            """));

        Assert.Equal([new ChainLink("tui", [81, 84], "tui-style-guide#84", "open"), new ChainLink("", [], "MentalDesk.Tui's release", "yours")],
            item.Chains![0], new LinkComparer());
        Assert.Equal([12], item.Frees!);
    }

    [Fact]
    public void Tagged_cards_come_first_in_their_column_then_by_Priority_and_the_tab_counts_them()
    {
        using var view = new WorkView(["a-team", "tui"]);
        view.Frame = new Rectangle(0, 0, 160, 20);
        view.Layout(new Size(160, 20));

        view.Show([Held([new("tui", [81], "tui-style-guide#84", "open")]), Other, InReview,
            Other with { Number = 78, Priority = "" }]);
        view.Frame = new Rectangle(0, 0, 160, 20);
        view.Layout(new Size(160, 20));

        var review = view.Lanes[1].Columns.Single(column => column.Gate == "Review");
        var cards = review.CardText.Where(text => text.StartsWith('#')).ToList();
        Assert.Equal(3, cards.Count);
        Assert.StartsWith("#81 ⛓ frees a-team #499  ", cards[0]);
        Assert.StartsWith("#79  ", cards[1]);
        Assert.StartsWith("#78  ", cards[2]);
        Assert.Equal("tui ⛓ 1 PR3", view.Lanes[1].Title);
        Assert.Equal("a-team ⊘ 1", view.Lanes[0].Title);
        var questions = view.Lanes[0].Columns.Single(column => column.Gate == "Blocked");
        Assert.StartsWith("#499 ⛓ waits on tui-style-guide#84 · in review, yours  Copies", questions.CardText[0]);
    }

    [Fact]
    public void The_tag_wears_the_Nerd_Font_glyph_when_icons_are_on()
    {
        using var view = new WorkView(["tui"]);
        view.ShowIcons(IconStyle.NerdFont);
        view.Show(Holds.Tag([InReview with { Frees = [90] }]));
        view.Frame = new Rectangle(0, 0, 120, 20);
        view.Layout(new Size(120, 20));

        Assert.StartsWith($"tui {Icons.Field(Icon.Chain, IconStyle.NerdFont)}1", view.Lanes[0].Title);
        Assert.StartsWith($"#81 {Icons.Glyph(Icon.Chain, IconStyle.NerdFont)} frees #90", view.Lanes[0].Columns.Single(column => column.Gate == "Review").CardText[0]);
    }

    private sealed class LinkComparer : IEqualityComparer<ChainLink>
    {
        public bool Equals(ChainLink? one, ChainLink? other) =>
            one is not null && other is not null && one.Team == other.Team && one.Refs.SequenceEqual(other.Refs)
            && one.What == other.What && one.Shown == other.Shown;

        public int GetHashCode(ChainLink link) => link.What.GetHashCode();
    }
}
