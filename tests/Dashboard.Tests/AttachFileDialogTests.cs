using System.Drawing;
using MentalDesk.Tui.Keys;
using Terminal.Gui.Input;

namespace ATeam.Dashboard.Tests;

public class AttachFileDialogTests
{
    private static AttachFileDialog Open()
    {
        var dialog = new AttachFileDialog();
        dialog.Layout(new Size(80, 20));
        return dialog;
    }

    [Fact]
    public void Its_actions_are_buttons_and_there_is_no_hint_row()
    {
        using var dialog = Open();

        Assert.Equal(["Ctrl+Enter Attach", "Esc Cancel"], dialog.ButtonLabels);
        Assert.Empty(dialog.SubViews.OfType<StatusBar>());
    }

    [Fact]
    public void Ctrl_Enter_in_the_field_attaches_what_was_typed()
    {
        using var dialog = Open();
        dialog.File.Text = " shot.png ";

        Assert.True(dialog.NewKeyDownEvent(Key.Enter.WithCtrl));

        Assert.Equal("shot.png", dialog.Chosen);
    }

    [Fact]
    public void Clicking_Attach_does_what_Ctrl_Enter_does()
    {
        using var dialog = Open();
        dialog.File.Text = "shot.png";

        dialog.Foot.Shown[0].InvokeCommand(Command.Accept);

        Assert.Equal("shot.png", dialog.Chosen);
    }

    [Fact]
    public void F1_shows_its_keys_under_its_title_and_focus_comes_back_to_the_field()
    {
        using var dialog = Open();
        dialog.File.Text = "sh";
        dialog.File.InsertionPoint = 1;
        KeySheet? shown = null;
        dialog.KeysRequested += (_, sheet) => DialogHost.ShowKeys(dialog, sheet, keys =>
        {
            shown = keys;
            dialog.Foot.SetFocus();
        });

        Assert.True(dialog.NewKeyDownEvent(Key.F1));

        Assert.NotNull(shown);
        Assert.Null(shown.Everywhere);
        Assert.Equal(AttachFileDialog.Name, shown.Here!.Heading);
        Assert.Equal([("Ctrl+Enter", "Attach"), ("Esc", "Cancel")], Rows(shown));
        Assert.True(dialog.File.HasFocus);
        Assert.Equal(1, dialog.File.InsertionPoint);
        Assert.Equal("sh", dialog.File.Text);
    }

    [Fact]
    public void Your_latest_screenshot_starts_in_File_with_when_you_took_it_under_it()
    {
        var now = new DateTimeOffset(2026, 11, 11, 21, 42, 12, TimeSpan.Zero);
        const string path = "/Users/me/Desktop/Screenshot 2026-11-11 at 21.40.12.png";
        using var dialog = new AttachFileDialog(new Screenshot(path, now.AddMinutes(-2)), new FixedClock(now));
        dialog.Layout(new Size(80, 20));

        Assert.Equal(path, dialog.File.Text);
        Assert.Equal("Your latest screenshot, taken 2 minutes ago", dialog.Taken!.Text);
        Assert.True(dialog.NewKeyDownEvent(Key.Enter.WithCtrl));
        Assert.Equal(path, dialog.Chosen);
    }

    [Fact]
    public void With_no_recent_screenshot_File_starts_empty()
    {
        using var dialog = Open();

        Assert.Equal("", dialog.File.Text);
        Assert.Null(dialog.Taken);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    internal static List<(string Keys, string Label)> Rows(KeySheet sheet) =>
        [.. sheet.Here!.Groups.SelectMany(group => group.Rows).Select(row => (row.Keys, row.Label))];
}
