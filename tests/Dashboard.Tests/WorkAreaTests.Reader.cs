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
    public void The_reader_heads_a_PR_s_body_with_its_branch_and_base()
    {
        IssueBody? shown = null;
        using var window = Open(read: team => Task.FromResult(new Reading(team == "team0" ? Branched() : "[]", null)),
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (_, body, _, _, _, _, _) => shown = body);
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();

        Assert.Equal("PR #122 · feat/keys → main\n\n## Opportunity", shown?.Text);
    }

    [Fact]
    public void work_try_is_on_t_and_acts_on_the_card()
    {
        using var window = Open();

        var command = window.Commands.Registered.Single(registered => registered.Id == "work.try");

        Assert.Equal("T", command.Keys);
        Assert.True(command.OnCard);
    }

    [Fact]
    public void work_try_is_disabled_until_a_card_with_a_PR_is_selected()
    {
        using var window = Open();

        Assert.False(window.Commands.IsEnabled("work.try"));

        window.Refresh();
        LayOut(window, 120, 30);
        Assert.Equal(6, window.Work.Selected?.Number);
        Assert.False(window.Commands.IsEnabled("work.try"));

        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);
        Assert.Equal(49, window.Work.Selected?.Number);
        Assert.True(window.Commands.IsEnabled("work.try"));

        window.NewKeyDownEvent(Key.CursorDown);
        Assert.Null(window.Work.SelectedCard);
        Assert.True(window.Commands.IsEnabled("work.try"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void t_hands_the_terminal_to_try_for_the_cards_team_and_PR_from_either_of_its_rows(bool onPr)
    {
        var handed = new List<Handover>();
        using var window = Open(handOver: handed.Add);
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);
        if (onPr)
            window.NewKeyDownEvent(Key.CursorDown);

        Assert.True(window.NewKeyDownEvent(new Key('t')));

        var handover = Assert.IsType<TryHandover>(Assert.Single(handed));
        Assert.Equal(["try", "team0", "122"], handover.Arguments);
        Assert.Equal(49, handover.Item.Number);
        Assert.Equal(onPr, handover.OnPr);
        Assert.Equal(window.Work.Items, handover.Items);
    }

    [Fact]
    public void t_on_a_card_with_no_PR_does_nothing_and_its_Cards_item_is_greyed_out()
    {
        var handed = new List<Handover>();
        using var window = Open(handOver: handed.Add);
        window.Refresh();
        LayOut(window, 120, 30);
        window.Menus.Single(menu => menu.Title == AppMenu.Cards).PopoverMenu!.Enabled = true;
        var item = window.MenuItems.Single(entry => entry.Id == "work.try").Item;

        Assert.False(window.NewKeyDownEvent(new Key('t')));
        window.Refresh();

        Assert.Empty(handed);
        Assert.True(item.Dimmed);

        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);
        window.Refresh();
        Assert.False(item.Dimmed);
    }

    [Fact]
    public void Picking_Try_from_Cards_hands_over_as_t_would()
    {
        var handed = new List<Handover>();
        using var window = Open(handOver: handed.Add);
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);

        window.MenuItems.Single(entry => entry.Id == "work.try").Item.Action!();

        Assert.Equal(["try", "team0", "122"], Assert.Single(handed).Arguments);
    }

    [Fact]
    public void The_card_commands_stay_on_the_selected_card_while_the_menu_has_focus()
    {
        using var window = Open();
        window.Refresh();
        LayOut(window, 120, 30);
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);
        window.NewKeyDownEvent(Key.CursorRight);

        // An open menu is a popover outside the window, so the window loses focus to it.
        window.HasFocus = false;

        Assert.Equal(49, window.Work.Selected?.Number);
        Assert.All(["work.read", "work.priority", "work.try", "work.github"], id => Assert.True(window.Commands.IsEnabled(id), id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Back_from_a_try_the_same_row_is_selected_with_the_same_cards_and_no_re_read(bool onPr)
    {
        var handed = new List<Handover>();
        using var first = Open(handOver: handed.Add);
        first.Refresh();
        LayOut(first, 120, 30);
        first.NewKeyDownEvent(Key.CursorRight);
        first.NewKeyDownEvent(Key.CursorRight);
        first.NewKeyDownEvent(Key.CursorRight);
        if (onPr)
            first.NewKeyDownEvent(Key.CursorDown);
        first.NewKeyDownEvent(new Key('t'));
        var titles = Titles(first).ToList();
        var reads = 0;

        using var back = Open(read: _ =>
        {
            reads++;
            return Task.FromResult(new Reading("[]", null));
        }, resume: handed.Single());
        back.Refresh();
        LayOut(back, 120, 30);
        back.FocusResumed();

        Assert.Equal(0, reads);
        Assert.Equal(titles, Titles(back));
        Assert.Equal(49, back.Work.Selected?.Number);
        Assert.Equal(onPr, back.Work.SelectedCard is null);
        Assert.All(back.Work.Lanes, lane => Assert.NotEqual(LoadState.Loading, lane.Load.State));
    }

    [Fact]
    public void A_try_that_failed_says_so_in_the_error_colour_once_you_re_back()
    {
        var handed = new List<Handover>();
        using var first = Open(handOver: handed.Add);
        first.Refresh();
        LayOut(first, 120, 30);
        first.NewKeyDownEvent(Key.CursorRight);
        first.NewKeyDownEvent(Key.CursorRight);
        first.NewKeyDownEvent(Key.CursorRight);
        first.NewKeyDownEvent(new Key('t'));

        using var back = Open(resume: handed.Single() with { Failure = "try team0 122 exited 1" });
        back.Refresh();
        LayOut(back, 120, 30);
        back.FocusResumed();

        Assert.Equal("try team0 122 exited 1", back.Says());
        Assert.Equal(Severity.Error, back.Status.MessageSeverity);
    }

    [Fact]
    public void Time_in_a_card_s_reader_counts_against_it_at_its_gate_with_the_comment_written_there()
    {
        var clock = new Clock();
        var visits = new List<Visit>();
        var attention = new Attention(clock, visits.Add);
        var opened = clock.Now;
        using var window = Open(
            clock: clock,
            attention: attention,
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            showBody: (_, _, _, _, _, comment, _) =>
            {
                clock.Now += TimeSpan.FromMinutes(2);
                attention.Key();
                Assert.Null(comment!.Post("Looks right").Result);
                clock.Now += TimeSpan.FromMinutes(1);
            });

        OpenTheTaskInReview(window);

        var visit = Assert.Single(visits);
        Assert.Equal(new Visit("team0", 49, "Review", opened, TimeSpan.FromMinutes(3), []), visit with { Did = [] });
        Assert.Equal("commented", visit.What);
    }
}
