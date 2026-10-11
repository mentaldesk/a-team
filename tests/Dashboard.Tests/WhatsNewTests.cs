using System.Drawing;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;

namespace ATeam.Dashboard.Tests;

[Collection("StaticConfiguration")]
public class WhatsNewTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);

    private static Move At(int minutes, int item, string who, string what) => new(item, Start.AddMinutes(minutes), who, what);

    private static WhatsNew Read(params Move[] moves)
    {
        var news = new WhatsNew(Start);
        news.Read("team", moves);
        return news;
    }

    [Fact]
    public void Each_gate_that_waits_on_you_is_new_once_something_moves_into_it()
    {
        var news = Read(
            At(1, 7, "lead", "Exploring → Pitched"),
            At(2, 8, "dev", "In progress → In review"),
            At(3, 9, "customer", "added as In review"),
            At(4, 10, "dev", "In progress → Ready"),
            At(4, 10, "dev", "handed back with a question"));

        Assert.Equal([(10, Arrival.Question), (9, Arrival.Review), (8, Arrival.Review), (7, Arrival.Pitch)],
            news.Items.Select(item => (item.Number, item.Kind)));
    }

    [Fact]
    public void Nothing_from_before_start_up_is_new()
    {
        var news = Read(At(-1, 7, "lead", "Exploring → Pitched"), At(-5, 8, "dev", "In progress → In review"));

        Assert.Empty(news.Items);
    }

    [Fact]
    public void A_move_into_Triage_your_own_move_or_any_other_is_never_new()
    {
        var news = Read(
            At(1, 6, "lead", "added as Idea"),
            At(1, 7, "lead", "Pitched → Idea"),
            At(1, 8, "you", "In progress → In review"),
            At(1, 9, "dev", "Ready → In progress"),
            At(1, 10, "lead", "commented"));

        Assert.Empty(news.Items);
    }

    [Fact]
    public void Leaving_the_gate_clears_it_and_coming_back_makes_it_new_again()
    {
        var news = Read(At(1, 7, "lead", "Exploring → Pitched"), At(2, 7, "you", "Pitched → Approved"),
            At(3, 8, "dev", "In progress → Ready"), At(3, 8, "dev", "handed back with a question"), At(4, 8, "you", "question answered"),
            At(5, 9, "dev", "In progress → In review"), At(6, 9, "you", "accepted · PR #19 merged"));

        Assert.Empty(news.Items);

        news.Read("team", [At(1, 7, "lead", "Exploring → Pitched"), At(2, 7, "you", "Pitched → Approved"), At(8, 7, "lead", "Exploring → Pitched")]);

        Assert.Equal([7], news.Items.Select(item => item.Number));
    }

    [Fact]
    public void Reading_says_whether_it_brought_something_new()
    {
        var news = new WhatsNew(Start);
        Move[] first = [At(1, 7, "lead", "Exploring → Pitched")];

        Assert.True(news.Read("team", first));
        Assert.False(news.Read("team", first));
        Assert.False(news.Read("other", []));
        Assert.True(news.Read("team", [.. first, At(2, 8, "dev", "In progress → In review")]));
    }

    [Fact]
    public void Opening_it_clears_it_until_it_arrives_again()
    {
        var news = Read(At(1, 7, "lead", "Exploring → Pitched"));

        news.Seen("team", 7, Start.AddMinutes(2));

        Assert.Empty(news.Items);
        news.Read("team", [At(1, 7, "lead", "Exploring → Pitched"), At(3, 7, "you", "Pitched → Exploring"), At(4, 7, "lead", "Exploring → Pitched")]);
        Assert.Equal([7], news.Items.Select(item => item.Number));
    }

    [Fact]
    public void A_read_of_Work_that_doesn_t_show_it_at_its_gate_clears_it_but_one_asked_for_before_it_arrived_doesn_t()
    {
        var news = Read(At(1, 7, "lead", "Exploring → Pitched"), At(1, 8, "lead", "Exploring → Pitched"),
            At(1, 9, "lead", "Exploring → Pitched"));
        WaitingItem[] waiting =
        [
            new(7, "Ranked", "Pitched", "", "team", Priority: "High", Pitch: true),
            new(8, "Unranked, so in Triage", "Pitched", "", "team", Pitch: true),
            new(9, "With a question, so in Questions", "Pitched", "", "team", Priority: "High", Pitch: true, Question: "Which?"),
        ];

        news.Read("team", waiting, Start);
        Assert.Equal([7, 8, 9], news.Items.Select(item => item.Number).Order());

        news.Read("team", waiting, Start.AddMinutes(2));
        Assert.Equal([7, 9], news.Items.Select(item => item.Number).Order());
    }

    [Fact]
    public void The_alert_is_the_newest_s_icon_and_number_and_how_many_others()
    {
        Assert.Equal("", new WhatsNew(Start).Alert(IconStyle.Unicode));
        Assert.Equal("? #481", Read(At(1, 481, "dev", "handed back with a question")).Alert(IconStyle.Unicode));
        Assert.Equal("◇ #493", Read(At(1, 493, "lead", "Exploring → Pitched")).Alert(IconStyle.Unicode));

        var several = Read(At(1, 493, "lead", "Exploring → Pitched"), At(3, 481, "dev", "handed back with a question"),
            At(2, 470, "dev", "In progress → In review"));

        Assert.Equal("? #481 +2", several.Alert(IconStyle.Unicode));
        Assert.Equal($"{Icons.Glyph(Icon.Question, IconStyle.NerdFont)} #481 +2", several.Alert(IconStyle.NerdFont));
        Assert.Equal("‹› #470", Read(At(1, 470, "dev", "In progress → In review")).Alert(IconStyle.Unicode));
    }

    [Fact]
    public void The_record_s_moves_are_read_and_anything_else_is_none()
    {
        var moves = Move.Parse("""[{"item": 7, "at": "2026-10-11T09:01:00Z", "who": "lead", "what": "Exploring → Pitched"}]""");

        Assert.Equal([new Move(7, Start.AddMinutes(1), "lead", "Exploring → Pitched")], moves);
        Assert.Empty(Move.Parse("not json"));
        Assert.Empty(Move.Parse("""{"item": 7}"""));
    }

    [Fact]
    public void The_first_alert_slides_in_from_the_right_edge()
    {
        Assert.Equal(["    ? ", "  ? #4", "? #481"], StatusBar.Slide("", "? #481"));
    }

    [Fact]
    public void A_changed_alert_slides_the_old_one_out_and_the_new_one_in()
    {
        Assert.Equal(["  ? #4", "    ? ", "      ", "    ◇ ", "  ◇ #4", "◇ #493"], StatusBar.Slide("? #481", "◇ #493"));
    }

    [Fact]
    public void The_last_alert_slides_out_once_it_s_cleared()
    {
        Assert.Equal(["  ? #4", "    ? ", "      "], StatusBar.Slide("? #481", ""));
    }

    [Fact]
    public void A_wider_alert_slides_in_the_width_of_the_wider()
    {
        var frames = StatusBar.Slide("? #481", "? #481 +2");

        Assert.All(frames, frame => Assert.Equal(9, frame.Length));
        Assert.Equal("     ? #4", frames[0]);
        Assert.Equal("? #481 +2", frames[^1]);
    }

    [Fact]
    public void The_alert_sits_flush_right_with_the_read_time_kept_clear_of_it()
    {
        using var bar = new StatusBar { Alerts = true, Width = 80 };
        bar.ShowState("read 2m ago", "All items");
        LayOut(bar);
        Assert.Equal(80, bar.State.Frame.Right);

        bar.Alert("? #481 +2");
        LayOut(bar);
        var state = bar.State.Frame;
        bar.Alert("◇ #493");
        LayOut(bar);

        Assert.Equal(state, bar.State.Frame);
        Assert.Equal(80, bar.AlertView.Frame.Right);
        Assert.Equal("◇ #493", bar.AlertView.Text);
        Assert.True(bar.State.Frame.Right + 3 <= bar.AlertView.Frame.X, $"{bar.State.Frame} runs into {bar.AlertView.Frame}");

        bar.Alert("");
        LayOut(bar);
        Assert.Equal(80, bar.State.Frame.Right);
    }

    [Fact]
    public void The_alert_is_in_the_highlight_colour()
    {
        using var bar = new StatusBar { Alerts = true };

        var highlight = SchemeManager.GetScheme(Schemes.Base).GetAttributeForRole(VisualRole.Highlight);

        Assert.Equal(highlight, bar.AlertView.GetAttributeForRole(VisualRole.Normal));
        Assert.Equal(highlight, bar.AlertView.GetAttributeForRole(VisualRole.HotNormal));
    }

    [Fact]
    public void Clicking_the_alert_goes_to_it_only_while_it_says_something()
    {
        using var bar = new StatusBar { Alerts = true };
        var chosen = 0;
        bar.AlertChosen += () => chosen++;

        bar.AlertView.InvokeCommand(Command.Accept);
        bar.Alert("? #481");
        bar.AlertView.InvokeCommand(Command.Accept);

        Assert.Equal(1, chosen);
    }

    private static void LayOut(StatusBar bar)
    {
        bar.Frame = new Rectangle(0, 0, 80, 1);
        bar.Layout(new Size(80, 1));
    }
}
