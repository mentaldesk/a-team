using System.Drawing;
using MentalDesk.Tui.Theming;
using Terminal.Gui.Input;

namespace ATeam.Dashboard.Tests;

/// <summary>The New idea dialog: its fields, what Ctrl+Enter adds, and what Esc throws away.</summary>
[Collection("StaticConfiguration")]
public class NewIdeaDialogTests
{
    private static NewIdeaDialog Open(
        Func<string[], Task<Reading>>? board = null, Func<bool>? confirmDiscard = null, string team = "team1")
    {
        var dialog = new NewIdeaDialog(["team0", "team1"], team,
            new IdeaFiler(board ?? (args => Task.FromResult(new Reading(args[2] == "new" ? "77" : "", null)))), confirmDiscard);
        dialog.Layout(new Size(80, 30));
        return dialog;
    }

    [Fact]
    public void It_opens_on_the_team_it_was_given_with_every_team_to_pick_from_and_the_title_to_type()
    {
        using var dialog = Open();

        Assert.Equal("New idea", dialog.Title);
        Assert.Equal("team1", dialog.Team.Text);
        Assert.Equal(["team0", "team1"], dialog.Team.Source!.ToList().Cast<string>());
        Assert.True(dialog.IdeaTitle.HasFocus);
        Assert.True(dialog.Description.WordWrap);
    }

    [Fact]
    public void Typing_past_the_bottom_of_the_description_keeps_the_cursor_in_view_and_adds_what_was_typed()
    {
        using var dialog = Open();
        dialog.IdeaTitle.Text = "Remember my lane";
        dialog.Description.SetFocus();
        var lines = Enumerable.Range(1, 20).Select(line => $"line {line}").ToList();

        foreach (var (line, i) in lines.Select((line, i) => (line, i)))
        {
            if (i > 0)
                dialog.NewKeyDownEvent(Key.Enter);
            foreach (var c in line)
                dialog.NewKeyDownEvent(new Key(c));
        }

        var viewport = dialog.Description.Viewport;
        Assert.True(viewport.Y > 0);
        Assert.InRange(lines.Count - 1, viewport.Y, viewport.Bottom - 1);
        Assert.Equal(string.Join('\n', lines), dialog.Draft.Description);
        Assert.True(dialog.NewKeyDownEvent(Key.Enter.WithCtrl));
        Assert.Equal(("team1", 77), dialog.Added);
    }

    [Fact]
    public void The_description_leaves_Tab_to_move_focus_and_Esc_cancels()
    {
        var asked = 0;
        using var dialog = Open(confirmDiscard: () =>
        {
            asked++;
            return false;
        });
        dialog.Description.SetFocus();
        dialog.Description.Text = "Some words";

        Assert.False(dialog.Description.NewKeyDownEvent(Key.Tab));
        Assert.Equal("Some words", dialog.Description.Text);
        Assert.True(dialog.NewKeyDownEvent(Key.Esc));
        Assert.Equal(1, asked);
    }

    [Fact]
    public void Priority_starts_on_None_and_says_what_None_does()
    {
        using var dialog = Open();

        Assert.Equal(["None", "Low", "Medium", "High", "Urgent"], dialog.Ranks.Labels);
        Assert.Equal(Rank.None, dialog.Ranks.Value);
        Assert.Contains(dialog.SubViews, view => view.Text == "None leaves it in Triage to rank later.");
    }

    [Fact]
    public void Left_and_Right_set_the_rank_with_the_keyboard_on_it_without_Space()
    {
        using var dialog = Open();
        using var app = DialogTests.Begin(dialog);
        dialog.Ranks.SetFocus();

        dialog.NewKeyDownEvent(Key.CursorRight);
        dialog.NewKeyDownEvent(Key.CursorRight);

        Assert.Equal(Rank.Medium, dialog.Ranks.Value);
        Assert.Equal((int)Rank.Medium, dialog.Ranks.FocusedItem);
        dialog.NewKeyDownEvent(Key.CursorLeft);
        Assert.Equal(Rank.Low, dialog.Ranks.Value);
    }

    [Fact]
    public void Its_actions_are_buttons_with_Add_the_Primary_on_Ctrl_Enter_and_no_hint_row()
    {
        using var dialog = Open();

        Assert.Equal(["Ctrl+Enter Add", "Esc Cancel"], dialog.ButtonLabels);
        Assert.Equal(SchemeNames.ButtonPrimary, dialog.Foot.Shown[0].SchemeName);
        Assert.Empty(dialog.SubViews.OfType<StatusBar>());
    }

    [Fact]
    public void F1_lists_every_key_under_New_idea()
    {
        using var dialog = Open();

        var sheet = DialogTests.F1(dialog);

        Assert.Equal("New idea", sheet.Here!.Heading);
        Assert.Equal(
            [("Ctrl+Enter", "Add"), ("Ctrl+V", "Paste"), ("←/→", "Priority"), ("Tab/Shift+Tab", "Next / previous"), ("Esc", "Cancel")],
            DialogTests.Rows(sheet));
    }

    [Fact]
    public void Clicking_Add_adds_the_idea()
    {
        using var dialog = Open();
        dialog.IdeaTitle.Text = "Remember my lane";

        dialog.Foot.Shown[0].InvokeCommand(Command.Accept);

        Assert.Equal(("team1", 77), dialog.Added);
    }

    private static NewIdeaDialog OpenAttaching(
        List<(string Team, string File)> uploads, Func<string?> pickFile, Func<string[], Task<Reading>>? board = null,
        Task<Reading>? upload = null)
    {
        var dialog = new NewIdeaDialog(["team0", "team1"], "team1",
            new IdeaFiler(board ?? (args => Task.FromResult(new Reading(args[2] == "new" ? "77" : "", null)))), null,
            attaching: new Attaching(Key.U.WithCtrl, (team, file) =>
            {
                uploads.Add((team, file));
                return upload ?? Task.FromResult(new Reading("https://example.com/shot.png", null));
            }, new ImageAttachTests.Clipboard(png: 10), pickFile));
        dialog.Layout(new Size(80, 30));
        return dialog;
    }

    [Fact]
    public void With_attaching_Attach_file_is_a_button_too()
    {
        using var dialog = OpenAttaching([], () => null);

        Assert.Equal(["Ctrl+Enter Add", "Ctrl+U Attach file", "Esc Cancel"], dialog.ButtonLabels);
        Assert.Contains(("Ctrl+U", "Attach file"), DialogTests.Rows(DialogTests.F1(dialog)));
    }

    [Fact]
    public void Ctrl_U_attaches_the_file_picked_to_the_description_for_the_team_picked()
    {
        var image = Path.Combine(Directory.CreateTempSubdirectory().FullName, "shot.png");
        File.WriteAllBytes(image, new byte[10]);
        var uploads = new List<(string, string)>();
        using var dialog = OpenAttaching(uploads, () => image);
        dialog.Description.Text = "See: ";
        dialog.Description.CaretOffset = 5;

        Assert.True(dialog.NewKeyDownEvent(Key.U.WithCtrl));

        Assert.Equal([("team1", image)], uploads);
        Assert.True(dialog.Description.HasFocus);
        Assert.Equal("See: ![shot](https://example.com/shot.png)", dialog.Description.Text);
        Assert.Equal("Attached shot.png  •  10 B", dialog.Message.Says);
    }

    [Fact]
    public void Ctrl_V_in_the_description_attaches_a_screenshot_on_the_clipboard()
    {
        var uploads = new List<(string, string)>();
        using var dialog = OpenAttaching(uploads, () => null);
        dialog.Description.SetFocus();

        Assert.True(dialog.Description.NewKeyDownEvent(Key.V.WithCtrl));

        Assert.Single(uploads);
        Assert.StartsWith("![Screenshot ", dialog.Description.Text);
    }

    [Fact]
    public void A_paste_into_the_title_is_left_alone()
    {
        var uploads = new List<(string, string)>();
        using var dialog = OpenAttaching(uploads, () => null);
        var paste = new PasteEventArgs("");

        dialog.OnAppPaste(null, paste);

        Assert.False(paste.Handled);
        Assert.Empty(uploads);
    }

    [Fact]
    public void An_idea_isn_t_added_while_its_image_is_still_uploading()
    {
        var image = Path.Combine(Directory.CreateTempSubdirectory().FullName, "shot.png");
        File.WriteAllBytes(image, new byte[10]);
        var calls = 0;
        using var dialog = OpenAttaching([], () => image, board: _ =>
        {
            calls++;
            return Task.FromResult(new Reading("77", null));
        }, upload: new TaskCompletionSource<Reading>().Task);
        dialog.IdeaTitle.Text = "Clipped dialog";
        dialog.AttachFile();

        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);

        Assert.Equal(0, calls);
        Assert.Equal("Wait for the image to finish uploading", dialog.Message.Says);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_title_adds_nothing_and_says_the_title_is_needed(string title)
    {
        var calls = 0;
        using var dialog = Open(board: _ =>
        {
            calls++;
            return Task.FromResult(new Reading("77", null));
        });
        dialog.IdeaTitle.Text = title;
        dialog.Description.Text = "Some words";

        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);

        Assert.Equal(0, calls);
        Assert.Null(dialog.Added);
        Assert.Equal("Nothing to add: the title is needed", dialog.Message.Says);
    }

    [Fact]
    public void Ctrl_Enter_adds_what_the_dialog_holds_and_closes_with_the_idea()
    {
        var calls = new List<string[]>();
        using var dialog = Open(board: args =>
        {
            calls.Add(args);
            return Task.FromResult(new Reading(args[2] == "new" ? "77" : "", null));
        });
        dialog.IdeaTitle.Text = "Remember my lane";
        dialog.Ranks.Value = Rank.High;

        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);

        Assert.Equal(("team1", 77), dialog.Added);
        Assert.Equal(["new", "add", "priority"], calls.Select(call => call[2]));
        Assert.All(calls, call => Assert.Equal("team1", call[1]));
    }

    [Fact]
    public void Enter_alone_adds_nothing()
    {
        var calls = 0;
        using var dialog = Open(board: _ =>
        {
            calls++;
            return Task.FromResult(new Reading("77", null));
        });
        dialog.IdeaTitle.Text = "Remember my lane";

        dialog.NewKeyDownEvent(Key.Enter);

        Assert.Equal(0, calls);
        Assert.Null(dialog.Added);
    }

    [Fact]
    public void A_step_that_fails_keeps_the_text_names_the_step_and_holds_the_team_once_the_issue_is_open()
    {
        using var dialog = Open(board: args => Task.FromResult(args[2] == "add"
            ? new Reading("", "gh: Not Found (HTTP 404)")
            : new Reading("77", null)));
        dialog.IdeaTitle.Text = "Remember my lane";
        dialog.Description.Text = "Every morning.";

        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);

        Assert.Null(dialog.Added);
        Assert.Equal("#77 is open, but couldn't go on team1's board: gh: Not Found (HTTP 404)", dialog.Message.Says);
        Assert.Equal("Remember my lane", dialog.IdeaTitle.Text);
        Assert.Equal("Every morning.", dialog.Description.Text);
        Assert.False(dialog.Team.Enabled);
    }

    [Fact]
    public void A_failure_before_the_issue_opens_leaves_the_team_to_change()
    {
        using var dialog = Open(board: _ => Task.FromResult(new Reading("", "gh: HTTP 502")));
        dialog.IdeaTitle.Text = "Remember my lane";

        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);

        Assert.Equal("couldn't open the issue: gh: HTTP 502", dialog.Message.Says);
        Assert.True(dialog.Team.Enabled);
    }

    [Fact]
    public void Esc_with_nothing_typed_closes_without_asking()
    {
        var asked = 0;
        using var dialog = Open(confirmDiscard: () =>
        {
            asked++;
            return false;
        });

        Assert.True(dialog.NewKeyDownEvent(Key.Esc));

        Assert.Equal(0, asked);
        Assert.Null(dialog.Added);
    }

    [Theory]
    [InlineData("A title", "")]
    [InlineData("", "A description")]
    public void Esc_with_something_typed_asks_before_throwing_it_away(string title, string description)
    {
        var asked = 0;
        using var dialog = Open(confirmDiscard: () =>
        {
            asked++;
            return false;
        });
        dialog.IdeaTitle.Text = title;
        dialog.Description.Text = description;

        dialog.NewKeyDownEvent(Key.Esc);

        Assert.Equal(1, asked);
        Assert.Equal(title, dialog.IdeaTitle.Text);
    }

    [Fact]
    public void Discarding_an_idea_asks_in_its_own_words()
    {
        Assert.Equal("Discard your idea?", DiscardDialog.Idea.Title);
        Assert.Contains("hasn't been added", DiscardDialog.Idea.Says);
    }

    [Fact]
    public void A_description_it_s_given_is_already_written_with_the_title_still_to_type()
    {
        using var dialog = new NewIdeaDialog(["team0", "team1"], "team0", new IdeaFiler(_ => Task.FromResult(new Reading("", null))),
            description: "> a line\n\nFrom https://github.com/x/180");
        dialog.Layout(new Size(80, 30));

        Assert.Equal("team0", dialog.Team.Text);
        Assert.Equal("> a line\n\nFrom https://github.com/x/180", dialog.Description.Text);
        Assert.True(dialog.IdeaTitle.HasFocus);
        Assert.Equal("", dialog.IdeaTitle.Text);
    }

    [Fact]
    public void Esc_with_only_the_description_it_was_given_still_asks()
    {
        var asked = 0;
        using var dialog = new NewIdeaDialog(["team0"], "team0", new IdeaFiler(_ => Task.FromResult(new Reading("", null))),
            () => ++asked < 0, "From https://github.com/x/180");

        dialog.NewKeyDownEvent(Key.Esc);

        Assert.Equal(1, asked);
        Assert.Equal("From https://github.com/x/180", dialog.Description.Text);
    }
}
