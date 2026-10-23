using System.Drawing;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;

namespace ATeam.Dashboard.Tests;

/// <summary>Every action key in the reader does what it says once a comment has posted, and waits for one that's
/// still posting, on each kind of card.</summary>
public class ReaderAfterCommentTests
{
    private const string Body = "## Context\n\nSomething to read.\n";

    private sealed class Epoch : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch;
    }

    private sealed class Card(string kind, Func<string, Task<string?>> post)
    {
        public List<string> Did { get; } = [];

        public ReaderDialog Open()
        {
            var item = kind switch
            {
                "pitch" => new WaitingItem(169, "A pitch", "Pitched", "https://x/169", "a-team", Pitch: true),
                "task" => new WaitingItem(516, "A task", "In review", "https://x/516", "a-team", Pr: 518, Branch: "feat/x"),
                "document" => new WaitingItem(220, "The vision", "Pitched", "https://x/220", "a-team", Pr: 220,
                    Pitch: true, Document: true, Branch: "docs/vision"),
                _ => new WaitingItem(531, "A question", "Ready", "https://x/531", "a-team", Question: "Which one?"),
            };
            var pitch = kind is "pitch" or "document";
            var dialog = new ReaderDialog(item, new IssueBody(Body), () =>
                {
                    Did.Add("github");
                    return null;
                },
                kind == "pitch" ? () =>
                {
                    Did.Add("approved");
                    return true;
                } : null,
                kind is "task" or "document" ? new ReaderCommand(new Key('a'), "accept", () =>
                {
                    Did.Add("accepted");
                    return true;
                }) : null,
                new ReaderComment(new Key('c'), "comment", post, new Epoch(),
                    pitch ? new ReaderDecline(new Key('x'), _ => Task.FromResult<string?>(null)) : null,
                    pitch ? new ReaderShelve(new Key('l'), (_, _) => Task.FromResult<string?>(null)) : null),
                kind is "task" or "document" ? new ReaderTry(new Key('t'), (_, _) => Did.Add("tried")) : null,
                width: 140,
                edit: kind == "document" ? new ReaderEdit(new Key('e'), (_, _) => Did.Add("edited")) : null,
                confirmDiscard: () => throw new InvalidOperationException("nothing should be left to discard"));
            dialog.Layout(new Size(140, 20));
            return dialog;
        }
    }

    private static void Write(ReaderDialog dialog, string text)
    {
        dialog.NewKeyDownEvent(new Key('c'));
        dialog.Layout(new Size(140, 20));
        foreach (var c in text)
            dialog.Field.NewKeyDownEvent(new Key(c));
    }

    private static void Pump(IApplication app, Func<bool> until)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!until() && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(20);
            app.TimedEvents!.RunTimers();
        }
    }

    public static TheoryData<string, string, string> Actions => new()
    {
        { "pitch", "a", "approved" },
        { "pitch", "g", "github" },
        { "task", "a", "accepted" },
        { "task", "t", "tried" },
        { "task", "g", "github" },
        { "document", "a", "accepted" },
        { "document", "e", "edited" },
        { "document", "t", "tried" },
        { "document", "g", "github" },
        { "question", "g", "github" },
    };

    [Theory]
    [MemberData(nameof(Actions))]
    public void After_a_comment_has_posted_each_action_key_does_its_action(string kind, string key, string did)
    {
        var card = new Card(kind, _ => Task.FromResult<string?>(null));
        using var dialog = card.Open();
        Write(dialog, "looks good");
        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);
        Assert.StartsWith("commented on #", dialog.Message.Says);

        Assert.True(dialog.NewKeyDownEvent(new Key(key[0])));

        Assert.Equal([did], card.Did);
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public void Clicking_its_button_after_a_comment_does_the_same(string kind, string key, string did)
    {
        var card = new Card(kind, _ => Task.FromResult<string?>(null));
        using var dialog = card.Open();
        Write(dialog, "looks good");
        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);

        dialog.Foot.Shown.Single(button => button.Text.Trim().StartsWith($"{key} ", StringComparison.Ordinal))
            .InvokeCommand(Command.Accept);

        Assert.Equal([did], card.Did);
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public void An_action_key_pressed_while_the_comment_posts_waits_for_it_then_does_its_action(string kind, string key, string did)
    {
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        var posted = new TaskCompletionSource<string?>();
        var card = new Card(kind, _ => posted.Task);
        using var dialog = card.Open();
        app.Begin(dialog);
        Write(dialog, "looks good");
        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);

        Assert.True(dialog.NewKeyDownEvent(new Key(key[0])));
        Assert.Equal("looks good", dialog.Field.Text);
        Assert.Equal(key == "g" ? [did] : [], card.Did);

        posted.SetResult(null);
        Pump(app, () => !dialog.CommentShown);

        Assert.Equal([did], card.Did);
        Assert.Equal("", dialog.Field.Text);
    }

    private static void StartPosting(IApplication app, ReaderDialog dialog)
    {
        app.Begin(dialog);
        Write(dialog, "looks good");
        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);
    }

    [Theory]
    [InlineData("x", true, false)]
    [InlineData("l", false, true)]
    public void Decline_or_Not_now_pressed_while_the_comment_posts_opens_once_it_has(string key, bool declining, bool shelving)
    {
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        var posted = new TaskCompletionSource<string?>();
        using var dialog = new Card("pitch", _ => posted.Task).Open();
        StartPosting(app, dialog);

        Assert.True(dialog.NewKeyDownEvent(new Key(key[0])));
        Assert.Equal((false, false), (dialog.Declining, dialog.Shelving));

        posted.SetResult(null);
        Pump(app, () => dialog.Declining || dialog.Shelving);

        Assert.Equal((declining, shelving), (dialog.Declining, dialog.Shelving));
        Assert.Equal("", dialog.Field.Text);
    }

    [Fact]
    public void c_pressed_while_the_comment_posts_starts_a_new_one_once_it_has()
    {
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        var posted = new TaskCompletionSource<string?>();
        using var dialog = new Card("task", _ => posted.Task).Open();
        StartPosting(app, dialog);

        Assert.True(dialog.NewKeyDownEvent(new Key('c')));
        Assert.False(dialog.Field.HasFocus);

        posted.SetResult(null);
        Pump(app, () => dialog.Field.HasFocus);

        Assert.True(dialog.CommentShown);
        Assert.True(dialog.Field.HasFocus);
        Assert.Equal("", dialog.Field.Text);
    }

    [Fact]
    public void Quoting_while_the_comment_posts_starts_a_new_one_with_the_quote_once_it_has()
    {
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        var posted = new TaskCompletionSource<string?>();
        using var dialog = new Card("task", _ => posted.Task).Open();
        StartPosting(app, dialog);
        dialog.NewKeyDownEvent(Key.CursorDown.WithShift);

        Assert.True(dialog.NewKeyDownEvent(new Key('q')));
        Assert.Equal("looks good", dialog.Field.Text);

        posted.SetResult(null);
        Pump(app, () => dialog.Field.Text.StartsWith('>'));

        Assert.StartsWith(">", dialog.Field.Text);
        Assert.True(dialog.Field.HasFocus);
    }

    [Fact]
    public void Esc_pressed_while_the_comment_posts_closes_the_reader_once_it_has()
    {
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        var posted = new TaskCompletionSource<string?>();
        using var dialog = new Card("task", _ => posted.Task).Open();
        StartPosting(app, dialog);

        Assert.True(dialog.NewKeyDownEvent(Key.Esc));
        Assert.False(dialog.StopRequested);

        posted.SetResult(null);
        Pump(app, () => dialog.StopRequested);

        Assert.True(dialog.StopRequested);
    }

    [Fact]
    public void Enter_pressed_while_the_comment_posts_sets_the_rank_once_it_has()
    {
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        var posted = new TaskCompletionSource<string?>();
        var item = new WaitingItem(169, "A pitch", "Pitched", "https://x/169", "a-team", Pitch: true);
        using var dialog = new ReaderDialog(item, new IssueBody(Body), () => null,
            comment: new ReaderComment(new Key('c'), "comment", _ => posted.Task, new Epoch()),
            width: 140, rank: Rank.Low);
        dialog.Layout(new Size(140, 20));
        StartPosting(app, dialog);
        dialog.NewKeyDownEvent(Key.CursorRight);

        dialog.NewKeyDownEvent(Key.Enter);
        Assert.Null(dialog.Chosen);

        posted.SetResult(null);
        Pump(app, () => dialog.Chosen is not null);

        Assert.Equal(Rank.Medium, dialog.Chosen);
        Assert.True(dialog.StopRequested);
    }

    [Fact]
    public void Clicking_a_rank_while_the_comment_posts_sets_it_once_it_has()
    {
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        var posted = new TaskCompletionSource<string?>();
        var item = new WaitingItem(169, "A pitch", "Pitched", "https://x/169", "a-team", Pitch: true);
        using var dialog = new ReaderDialog(item, new IssueBody(Body), () => null,
            comment: new ReaderComment(new Key('c'), "comment", _ => posted.Task, new Epoch()),
            width: 140, rank: Rank.Low);
        dialog.Layout(new Size(140, 20));
        StartPosting(app, dialog);

        dialog.Ranks!.Value = Rank.High;
        dialog.Ranks.SubViews.ElementAt((int)Rank.High).InvokeCommand(Command.Accept);
        Assert.Null(dialog.Chosen);

        posted.SetResult(null);
        Pump(app, () => dialog.Chosen is not null);

        Assert.Equal(Rank.High, dialog.Chosen);
        Assert.True(dialog.StopRequested);
    }

    [Fact]
    public void A_key_pressed_while_a_comment_posts_is_dropped_if_the_comment_fails_and_the_draft_is_kept()
    {
        using var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        var posted = new TaskCompletionSource<string?>();
        var card = new Card("task", _ => posted.Task);
        using var dialog = card.Open();
        app.Begin(dialog);
        Write(dialog, "looks good");
        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);
        dialog.NewKeyDownEvent(new Key('t'));

        posted.SetResult("gh: HTTP 502");
        Pump(app, () => dialog.Message.Says == "gh: HTTP 502");

        Assert.Empty(card.Did);
        Assert.Equal("looks good", dialog.Field.Text);
        Assert.True(dialog.Field.HasFocus);
    }

    [Theory]
    [InlineData("x", true, false)]
    [InlineData("l", false, true)]
    public void After_a_comment_on_a_pitch_decline_and_Not_now_open_as_they_do_on_one_you_haven_t_commented_on(
        string key, bool declining, bool shelving)
    {
        var card = new Card("pitch", _ => Task.FromResult<string?>(null));
        using var dialog = card.Open();
        Write(dialog, "looks good");
        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);

        Assert.True(dialog.NewKeyDownEvent(new Key(key[0])));

        Assert.True(dialog.CommentShown);
        Assert.Equal((declining, shelving), (dialog.Declining, dialog.Shelving));
        Assert.Equal("", dialog.Field.Text);
    }

    [Fact]
    public void Quoting_after_a_comment_starts_a_new_one_and_t_still_tries()
    {
        var card = new Card("task", _ => Task.FromResult<string?>(null));
        using var dialog = card.Open();
        dialog.NewKeyDownEvent(Key.CursorDown.WithShift);
        dialog.NewKeyDownEvent(new Key('q'));
        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);
        Assert.False(dialog.CommentShown);

        Assert.True(dialog.NewKeyDownEvent(new Key('t')));

        Assert.Equal(["tried"], card.Did);
    }

    [Fact]
    public void After_a_comment_Enter_still_sets_the_rank()
    {
        var item = new WaitingItem(169, "A pitch", "Pitched", "https://x/169", "a-team", Pitch: true);
        using var dialog = new ReaderDialog(item, new IssueBody(Body), () => null,
            comment: new ReaderComment(new Key('c'), "comment", _ => Task.FromResult<string?>(null), new Epoch()),
            width: 140, rank: Rank.Low);
        dialog.Layout(new Size(140, 20));
        Write(dialog, "looks good");
        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);

        dialog.NewKeyDownEvent(Key.CursorRight);
        dialog.NewKeyDownEvent(Key.Enter);

        Assert.Equal(Rank.Medium, dialog.Chosen);
    }
}
