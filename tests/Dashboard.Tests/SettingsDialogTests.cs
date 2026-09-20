using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace ATeam.Dashboard.Tests;

public class SettingsDialogTests : IDisposable
{
    private readonly string _configRoot = Path.Combine(Path.GetTempPath(), $"a-team-{Guid.NewGuid():n}");

    public void Dispose()
    {
        if (Directory.Exists(_configRoot))
            Directory.Delete(_configRoot, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Ctrl_Enter_keeps_the_theme_picked_in_the_dialog()
    {
        using var dialog = Open(out _);

        Assert.True(dialog.NewKeyDownEvent(Key.Enter.WithCtrl));
        Assert.True(dialog.Confirmed);
    }

    [Fact]
    public void Esc_closes_the_dialog_without_keeping_the_theme()
    {
        using var dialog = Open(out _);

        Assert.True(dialog.NewKeyDownEvent(Key.Esc));
        Assert.False(dialog.Confirmed);
    }

    [Fact]
    public void Enter_is_left_free_and_does_not_close_the_dialog()
    {
        using var dialog = Open(out _);
        var asked = false;
        dialog.Accepting += (_, _) => asked = true;

        Themes(dialog).NewKeyDownEvent(Key.Enter);

        Assert.False(asked);
        Assert.Null(dialog.Result);
        Assert.False(dialog.Confirmed);
    }

    [Fact]
    public void The_OK_button_keeps_the_theme_and_Cancel_does_not()
    {
        using var ok = Open(out _);
        ok.Buttons.Single(b => b.Text == "OK").InvokeCommand(Command.Accept);
        Assert.True(ok.Confirmed);

        using var cancel = Open(out _);
        cancel.Buttons.Single(b => b.Text == "Cancel").InvokeCommand(Command.Accept);
        Assert.False(cancel.Confirmed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_tool_calls_box_opens_showing_what_is_stored(bool expand)
    {
        using var dialog = Open(out _, expand);

        Assert.Equal(expand ? CheckState.Checked : CheckState.UnChecked, ToolCalls(dialog).Value);
        Assert.Equal(expand, dialog.ExpandToolCalls);
    }

    [Fact]
    public void Confirming_stores_the_tool_calls_box_as_it_was_left()
    {
        var settings = new DashboardSettings(_configRoot);
        using var dialog = Open(out var theme);
        ToolCalls(dialog).InvokeCommand(Command.Activate);
        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);

        dialog.Store(theme, settings);

        Assert.True(settings.ReadExpandToolCalls());
    }

    [Fact]
    public void Cancelling_leaves_the_stored_tool_calls_setting_alone()
    {
        var settings = new DashboardSettings(_configRoot);
        settings.WriteExpandToolCalls(true);
        using var dialog = Open(out var theme, expand: true);
        ToolCalls(dialog).InvokeCommand(Command.Activate);
        dialog.NewKeyDownEvent(Key.Esc);

        dialog.Store(theme, settings);

        Assert.True(settings.ReadExpandToolCalls());
    }

    [Fact]
    public void Confirming_stores_the_tool_calls_setting_without_losing_the_theme()
    {
        var settings = new DashboardSettings(_configRoot);
        using var dialog = Open(out var theme, keep: settings.WriteTheme);
        theme.Preview(BundledThemes.Daylight);
        ToolCalls(dialog).InvokeCommand(Command.Activate);
        dialog.NewKeyDownEvent(Key.Enter.WithCtrl);

        dialog.Store(theme, settings);

        Assert.Equal(BundledThemes.Daylight, settings.ReadTheme());
        Assert.True(settings.ReadExpandToolCalls());
    }

    [Fact]
    public void Space_on_the_tool_calls_box_turns_it_on_without_closing_the_dialog()
    {
        using var dialog = Open(out _);
        var box = ToolCalls(dialog);
        box.SetFocus();

        box.NewKeyDownEvent(Key.Space);

        Assert.True(dialog.ExpandToolCalls);
        Assert.False(dialog.Confirmed);
    }

    [Fact]
    public void Enter_on_the_tool_calls_box_does_not_close_the_dialog_either()
    {
        using var dialog = Open(out _);
        var box = ToolCalls(dialog);
        box.SetFocus();

        box.NewKeyDownEvent(Key.Enter);

        Assert.Null(dialog.Result);
        Assert.False(dialog.Confirmed);
    }

    private static OptionSelector Themes(SettingsDialog dialog) => dialog.SubViews.OfType<OptionSelector>().Single();

    private static CheckBox ToolCalls(SettingsDialog dialog) => dialog.SubViews.OfType<CheckBox>().Single();

    private static SettingsDialog Open(out ThemeSetting theme, bool expand = false, Action<string>? keep = null)
    {
        theme = new ThemeSetting(BundledThemes.Midnight, _ => { }, keep ?? (_ => { }));
        var dialog = new SettingsDialog(theme, expand, () => { });
        dialog.SetFocus();
        return dialog;
    }
}
