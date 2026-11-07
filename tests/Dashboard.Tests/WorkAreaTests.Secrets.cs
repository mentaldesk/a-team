using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace ATeam.Dashboard.Tests;

public partial class WorkAreaTests
{
    private const string AsksSecret =
        """
        [{"number": 702, "title": "Releases open the winget PR", "status": "Ready",
          "url": "https://github.com/mentaldesk/team0/issues/702", "team": "team0", "turn": "you", "heldBy": "lead",
          "reason": "held by the Lead since 08:23", "question": "This needs WINGET_TOKEN", "facts": "held · needs WINGET_TOKEN",
          "secret": {"name": "WINGET_TOKEN", "environment": "release", "by": "lead", "why": "For the fork.", "repo": "mentaldesk/team0"}},
         {"number": 469, "title": "A plain held task", "status": "Ready",
          "url": "https://github.com/mentaldesk/team0/issues/469", "team": "team0", "turn": "you", "heldBy": "dev",
          "reason": "asked you since 08:23", "question": "Which one?"}]
        """;

    private static GitHubSecrets NoGh => new(new TeamCommand("/nonexistent/gh"));

    [Fact]
    public void A_card_asking_for_a_secret_wears_a_key_and_says_who_asked_and_for_what()
    {
        var card = new Card(WaitingItem.Parse(AsksSecret)[0], false, Icon.Blocked);

        Assert.Equal("#702  lead · needs WINGET_TOKEN · Releases open the winget PR", card.Text(80));
        Assert.Equal("\U0001F511", card.Leads(IconStyle.Unicode)[1].Glyph);
        Assert.Equal("#702 · the Lead asks for a secret", card.Item.Line);
    }

    [Fact]
    public void K_is_only_on_a_card_asking_for_a_secret_which_the_status_bar_says()
    {
        using var window = Open(read: team => Task.FromResult(new Reading(team == "team0" ? AsksSecret : "[]", null)), secrets: NoGh);
        window.Refresh();
        LayOut(window, 120, 30);
        var command = window.Commands.Registered.Single(each => each.Id == "work.secret");

        Assert.Equal(702, window.Work.Selected?.Number);
        Assert.Equal("K", command.Keys);
        Assert.Equal("Give secret…", command.Label);
        Assert.True(command.OnCard);
        Assert.True(window.Commands.IsEnabled("work.secret"));
        Assert.Equal(["k Give secret", "Press F1 for help"], window.Status.Hints.SubViews.OfType<Button>().Select(hint => hint.Text));

        window.NewKeyDownEvent(Key.CursorDown);

        Assert.Equal(469, window.Work.Selected?.Number);
        Assert.False(window.Commands.IsEnabled("work.secret"));
        Assert.Equal(["Press F1 for help"], window.Status.Hints.SubViews.OfType<Button>().Select(hint => hint.Text));
    }

    [Fact]
    public void Saving_tells_the_board_only_that_it_was_given_and_the_card_leaves_Blocked()
    {
        const string value = "distinctive-value-5f3a";
        var calls = new List<string[]>();
        var messages = new List<string>();
        using var window = Open(
            read: team => Task.FromResult(new Reading(team == "team0" ? AsksSecret : "[]", null)),
            run: arguments =>
            {
                calls.Add(arguments);
                return Task.FromResult<string?>(null);
            },
            secrets: new GitHubSecrets(new TeamCommand("/nonexistent/gh"), dryRun: true),
            giveSecret: (_, _, save) =>
            {
                save(value).GetAwaiter().GetResult();
                return true;
            });
        window.Refresh();
        LayOut(window, 120, 30);

        Assert.True(window.NewKeyDownEvent(new Key('k')));
        messages.Add(window.Says());
        window.Refresh();
        messages.Add(window.Says());

        Assert.Equal(["board", "team0", "gave", "you", "702"], Assert.Single(calls));
        Assert.DoesNotContain(calls.SelectMany(call => call), part => part.Contains(value));
        Assert.DoesNotContain(messages, message => message.Contains(value));
        Assert.Equal("#702 back in the Dev's queue · its secret is saved", window.Says());
        Assert.Equal(1, window.Work.Lanes[0].Columns.Single(column => column.Gate == "Blocked").Count);
    }

    [Fact]
    public void Cancelling_Give_a_secret_tells_the_board_nothing()
    {
        var calls = new List<string[]>();
        using var window = Open(
            read: team => Task.FromResult(new Reading(team == "team0" ? AsksSecret : "[]", null)),
            run: arguments =>
            {
                calls.Add(arguments);
                return Task.FromResult<string?>(null);
            },
            secrets: NoGh,
            giveSecret: (_, _, _) => false);
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(new Key('k'));
        window.Refresh();

        Assert.Empty(calls);
    }

    [Fact]
    public void The_reader_offers_Give_secret_on_a_task_asking_for_one_only()
    {
        var offered = new List<(int Number, ReaderCommand? Secret)>();
        using var window = Open(
            read: team => Task.FromResult(new Reading(team == "team0" ? AsksSecret : "[]", null)),
            readBody: _ => Task.FromResult(new Reading(Body, null)),
            secrets: NoGh,
            showBody: (item, _, _, _, _, comment, _) => offered.Add((item.Number, comment?.Secret)));
        window.Refresh();
        LayOut(window, 120, 30);

        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();
        window.NewKeyDownEvent(Key.CursorDown);
        window.NewKeyDownEvent(Key.Enter);
        window.Refresh();

        Assert.Equal([702, 469], offered.Select(each => each.Number));
        Assert.Equal(new Key('k'), offered[0].Secret?.Key);
        Assert.Null(offered[1].Secret);
    }
}
