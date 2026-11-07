using System.Drawing;
using MentalDesk.Tui.Theming;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace ATeam.Dashboard.Tests;

public class GiveSecretDialogTests
{
    private static readonly SecretAsk Ask = new("WINGET_TOKEN", "release", "mentaldesk/TuiCode", "A classic PAT with public_repo.", "lead");

    private static WaitingItem Item => new(702, "Releases open the winget PR", "Ready", "", "TuiCode") { Secret = Ask };

    private static GiveSecretDialog Open(bool alreadySet = false, Func<string, Task<string?>>? save = null)
    {
        var dialog = new GiveSecretDialog(Item, Task.FromResult(alreadySet), save ?? (_ => Task.FromResult<string?>(null)));
        dialog.Layout(new Size(100, 30));
        return dialog;
    }

    private static Label ValueLabel(GiveSecretDialog dialog) => dialog.SubViews.OfType<Label>().Single(label => label.Text == "Value:");

    [Fact]
    public void It_says_who_asked_for_what_where_and_why_with_a_password_field_Save_and_Cancel()
    {
        using var dialog = Open();

        Assert.Equal("Give a secret · TuiCode #702", dialog.Title);
        var request = dialog.Request.Replace('\n', ' ');
        Assert.Contains("The Lead asks for WINGET_TOKEN, a secret in the release environment of mentaldesk/TuiCode.", request);
        Assert.Contains("Why: A classic PAT with public_repo.", request);
        Assert.True(dialog.Value.Secret);
        Assert.Equal(["Enter Save", "Esc Cancel"], dialog.ButtonLabels);
    }

    [Fact]
    public void An_underscore_in_the_secret_s_name_is_drawn_not_taken_for_a_hot_key()
    {
        using var dialog = Open(alreadySet: true);

        Assert.All(dialog.SubViews.OfType<Label>().Where(label => label.Text.Contains('_')), label => Assert.Equal(new System.Text.Rune(0xffff), label.HotKeySpecifier));
    }

    [Fact]
    public void A_repo_secret_says_it_s_the_repo_s()
    {
        Assert.Equal("a repo secret of mentaldesk/TuiCode", (Ask with { Environment = "" }).Place);
    }

    [Fact]
    public void A_secret_already_set_there_says_saving_will_replace_it()
    {
        using var set = Open(alreadySet: true);
        using var unset = Open(alreadySet: false);

        Assert.Contains("WINGET_TOKEN is already set there. Saving will replace it.", set.Note.Replace('\n', ' '));
        Assert.DoesNotContain("replace", unset.Note);
    }

    [Fact]
    public void Value_s_cursor_follows_the_dialog_to_where_it_ends_up()
    {
        using var dialog = Open();

        dialog.X = 10;
        dialog.Y = 5;
        dialog.Layout(new Size(100, 30));

        Assert.True(dialog.Value.HasFocus);
        Assert.Equal(dialog.Value.ViewportToScreen(Point.Empty), dialog.Value.Cursor.Position);
    }

    [Fact]
    public void A_late_answer_that_it_s_already_set_keeps_Value_s_focus_cursor_and_what_was_typed()
    {
        var alreadySet = new TaskCompletionSource<bool>();
        using var dialog = new GiveSecretDialog(Item, alreadySet.Task, _ => Task.FromResult<string?>(null));
        dialog.Layout(new Size(100, 30));
        dialog.Value.Text = "s3c";
        dialog.Value.InsertionPoint = 3;

        alreadySet.SetResult(true);
        Assert.True(dialog.TakeInAlreadySet());
        dialog.Layout(new Size(100, 30));

        Assert.Contains("Saving will replace it.", dialog.Note.Replace('\n', ' '));
        Assert.True(dialog.Value.HasFocus);
        Assert.Equal("s3c", dialog.Value.Text);
        Assert.Equal(dialog.Value.ViewportToScreen(new Point(3, 0)), dialog.Value.Cursor.Position);
    }

    [Fact]
    public void Save_stays_enabled_and_with_no_value_marks_Value_says_so_and_stays_open()
    {
        var saved = new List<string>();
        using var dialog = Open(save: value =>
        {
            saved.Add(value);
            return Task.FromResult<string?>(null);
        });

        Assert.True(dialog.Foot.Shown[0].Enabled);
        dialog.NewKeyDownEvent(Key.Enter);
        dialog.Foot.Shown[0].InvokeCommand(Command.Accept);

        Assert.Empty(saved);
        Assert.False(dialog.Saved);
        Assert.True(dialog.Foot.Shown[0].Enabled);
        Assert.Equal(SchemeNames.Error, ValueLabel(dialog).SchemeName);
        Assert.Equal("Value is needed", dialog.Alert.Message);
        Assert.True(dialog.Value.HasFocus);
    }

    [Fact]
    public void Save_hands_over_the_value_exactly_as_typed_and_closes()
    {
        var saved = new List<string>();
        using var dialog = Open(save: value =>
        {
            saved.Add(value);
            return Task.FromResult<string?>(null);
        });
        dialog.Value.Text = " s3cret ";

        dialog.NewKeyDownEvent(Key.Enter);

        Assert.Equal([" s3cret "], saved);
        Assert.True(dialog.Saved);
    }

    [Fact]
    public void A_refused_save_keeps_it_open_with_the_value_typed_marks_Value_and_says_why()
    {
        using var dialog = Open(save: _ => Task.FromResult<string?>("GitHub refused: you aren't an admin of mentaldesk/TuiCode"));
        dialog.Value.Text = "s3cret";

        dialog.NewKeyDownEvent(Key.Enter);

        Assert.False(dialog.Saved);
        Assert.Equal("s3cret", dialog.Value.Text);
        Assert.Equal(SchemeNames.Error, ValueLabel(dialog).SchemeName);
        Assert.Equal("GitHub refused: you aren't an admin of mentaldesk/TuiCode", dialog.Alert.Message);
        Assert.False(dialog.Value.ReadOnly);
    }

    [Fact]
    public void Esc_gives_nothing()
    {
        var saved = false;
        using var dialog = Open(save: _ =>
        {
            saved = true;
            return Task.FromResult<string?>(null);
        });
        dialog.Value.Text = "s3cret";

        Assert.True(dialog.NewKeyDownEvent(Key.Esc));

        Assert.False(saved);
        Assert.False(dialog.Saved);
    }
}
