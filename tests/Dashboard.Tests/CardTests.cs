using Terminal.Gui.Text;

namespace ATeam.Dashboard.Tests;

/// <summary>The rows a Work column draws: an item, and the PR that closes it under it.</summary>
public class CardTests
{
    private static readonly WaitingItem Reviewing = new(49, "I can't change any of the dashboard's keys", "In review",
        "https://github.com/x/49", "a-team", "dev", "answering your feedback since 10:15",
        Pr: 122, PrUrl: "https://github.com/x/pull/122", Priority: "Medium");

    private static readonly WaitingItem Unranked = new(6, "The agents can't say what they'd change", "Idea",
        "https://github.com/x/6", "a-team", "you", "waiting to be ranked");

    [Fact]
    public void An_item_with_a_PR_has_one_row_under_it_and_one_without_has_none()
    {
        Assert.Equal([new Card(Reviewing, true)], new Card(Reviewing, false).Children);
        Assert.Empty(new Card(Unranked, false).Children);
        Assert.Empty(new Card(Reviewing, true).Children);
    }

    [Fact]
    public void The_Customer_lead_s_docs_PR_is_one_row_that_says_whose_it_is_and_wears_the_PR_glyph()
    {
        var docs = new Card(new WaitingItem(352, "Docs: what's changed since 28 Sep", "In review", "https://github.com/x/pull/352",
            "a-team", "you", Pr: 352, PrUrl: "https://github.com/x/pull/352", Role: "customer"), false);

        Assert.Empty(docs.Children);
        Assert.Equal("#352  Customer lead · Docs: what's changed since 28 Sep", docs.Text(0));
        Assert.Equal(Icons.Glyph(Icon.PullRequest, IconStyle.Unicode), docs.Leads(IconStyle.Unicode)[1].Glyph);
    }

    [Fact]
    public void A_column_draws_its_items_and_the_PR_under_each_of_them()
    {
        var nodes = Card.Nodes(Card.Roots([Unranked, Reviewing]));

        Assert.Equal([new Card(Unranked, false), new Card(Reviewing, false), new Card(Reviewing, true)], nodes);
    }

    [Fact]
    public void A_PRs_row_reads_as_its_own_number_and_the_items_title_and_is_cut_short_like_a_card()
    {
        Assert.Equal("PR #122  I can't change any of the dashboard's keys", new Card(Reviewing, true).Text(0));
        Assert.Equal("PR #122  I can't change…", new Card(Reviewing, true).Text(24));
        Assert.Equal("…", new Card(Reviewing, true).Text(1));
    }

    [Fact]
    public void A_title_cut_short_at_an_emoji_drops_the_whole_rune_rather_than_half_of_it()
    {
        var watched = new WaitingItem(157, "A \U0001F440 appears on my comment", "In review",
            "https://github.com/x/157", "a-team", "you", "awaiting you");

        Assert.Equal("#157  A \U0001F440\u2026", new Card(watched, false).Text(11));
        Assert.Equal("#157  A \u2026", new Card(watched, false).Text(10));
    }

    [Fact]
    public void Enter_opens_the_issue_on_a_card_and_the_PR_on_the_row_under_it()
    {
        Assert.Equal("https://github.com/x/49", new Card(Reviewing, false).Url);
        Assert.Equal("https://github.com/x/pull/122", new Card(Reviewing, true).Url);
        Assert.Equal("", new Card(Unranked, true).Url);
    }

    [Fact]
    public void A_PRs_row_wears_the_line_it_hangs_from_where_a_card_wears_whose_move_it_is()
    {
        Assert.Equal(Icons.For(Reviewing, IconStyle.Unicode), new Card(Reviewing, false).Lead(IconStyle.Unicode));

        var line = new Card(Reviewing, true).Lead(IconStyle.NerdFont);

        Assert.Equal(LogSchemes.Dimmed, line.Scheme);
        Assert.Equal(Icons.Width, Icons.Field(line.Glyph).GetColumns());
    }

    [Theory]
    [InlineData(IconStyle.NerdFont, true, "\U000F0428")]
    [InlineData(IconStyle.NerdFont, false, "\uEC37")]
    [InlineData(IconStyle.Unicode, true, "◇")]
    [InlineData(IconStyle.Unicode, false, "‹›")]
    public void A_card_wears_what_kind_of_thing_it_is_after_whose_move_it_is(IconStyle style, bool pitch, string kind)
    {
        var card = new Card(Reviewing with { Pitch = pitch }, false);

        Assert.Equal([card.Lead(style), new TurnIcon(kind, card.Lead(style).Scheme)], card.Leads(style));
        Assert.Equal(2 * Icons.Width, card.LeadWidth);
    }

    [Theory]
    [InlineData(IconStyle.NerdFont, false, "\uF400")]
    [InlineData(IconStyle.NerdFont, true, "\uF400")]
    [InlineData(IconStyle.Unicode, false, "\U0001F4A1")]
    [InlineData(IconStyle.Unicode, true, "\U0001F4A1")]
    public void An_Idea_wears_a_light_bulb(IconStyle style, bool pitch, string kind)
    {
        var card = new Card(Unranked with { Pitch = pitch }, false);

        Assert.Equal(new TurnIcon(kind, card.Lead(style).Scheme), card.Leads(style)[1]);
    }

    [Fact]
    public void A_card_leaves_its_kind_to_a_column_that_wears_it()
    {
        var card = new Card(Unranked, false, Icon.Idea);

        Assert.Equal([card.Lead(IconStyle.Unicode)], card.Leads(IconStyle.Unicode));
        Assert.Equal(Icons.Width, card.LeadWidth);
    }

    [Fact]
    public void A_pitch_in_a_column_of_Ideas_still_wears_its_kind()
    {
        var card = new Card(Unranked with { Status = "Pitched", Pitch = true }, false, Icon.Idea);

        Assert.Equal("◇", card.Leads(IconStyle.Unicode)[1].Glyph);
        Assert.Equal(2 * Icons.Width, card.LeadWidth);
    }

    [Fact]
    public void A_PRs_row_wears_only_the_line_it_hangs_from()
    {
        var row = new Card(Reviewing, true);

        Assert.Equal([row.Lead(IconStyle.NerdFont)], row.Leads(IconStyle.NerdFont));
        Assert.Equal(Icons.Width, row.LeadWidth);
    }

    [Fact]
    public void A_PRs_row_wears_the_pull_request_glyph_in_place_of_the_word_with_Nerd_Font_icons()
    {
        Assert.Equal("\uE726 #122  I can't change any of the dashboard's keys", new Card(Reviewing, true).Text(0, IconStyle.NerdFont));
        Assert.Equal("\uE726 #122  I can't change …", new Card(Reviewing, true).Text(24, IconStyle.NerdFont));
        Assert.Equal("PR #122  I can't change any of the dashboard's keys", new Card(Reviewing, true).Text(0, IconStyle.Unicode));
    }

    [Fact]
    public void A_title_cut_short_at_a_Nerd_Font_glyph_drops_the_whole_rune()
    {
        var pitched = new WaitingItem(157, "A \U000F0428 pitch", "Pitched", "https://github.com/x/157", "a-team", Pitch: true);

        Assert.Equal("#157  A \U000F0428\u2026", new Card(pitched, false).Text(11, IconStyle.NerdFont));
        Assert.Equal("#157  A \u2026", new Card(pitched, false).Text(10, IconStyle.NerdFont));
    }

    [Fact]
    public void A_PRs_row_wears_no_Priority_where_its_card_does()
    {
        Assert.Equal(Priorities.Scheme("Medium"), new Card(Reviewing, false).Mark(0).Scheme);
        Assert.Equal(default, new Card(Reviewing, true).Mark(0));
    }
}
