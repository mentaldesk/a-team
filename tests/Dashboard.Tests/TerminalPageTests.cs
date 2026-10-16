using MentalDesk.Tui.Fields;
using MentalDesk.Tui.Keys;
using Terminal.Gui.Drawing;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace ATeam.Dashboard.Tests;

public class TerminalPageTests
{
    private const string Line = "one two three";
    private const string Lines = "one two three\nfour five six";

    private readonly List<(string Message, Schemes Scheme)> _said = [];

    private TerminalPage Page(FakeKeys keys, IClipboard? clipboard = null) =>
        new(keys, (message, scheme) => _said.Add((message, scheme)), clipboard);

    [Theory]
    [InlineData("iTerm2")]
    [InlineData("kitty")]
    [InlineData("WezTerm")]
    public void It_names_the_terminal_says_not_installed_and_offers_Install(string terminal)
    {
        using var page = Page(new FakeKeys(terminal));

        Assert.Equal(terminal, Shown(page)[1]);
        Assert.Equal("Not installed", page.State.Text);
        Assert.True(page.InstallButton.Visible);
        Assert.False(page.UpdateButton.Visible);
        Assert.False(page.RemoveButton.Visible);
        Assert.True(page.ShowsKeys);
        Assert.Contains("Only the dashboard is affected.", page.Note);
    }

    [Fact]
    public void It_lists_the_keys_the_integration_adds()
    {
        using var page = Page(new FakeKeys("kitty"));

        Assert.All(MacKeys.Rows, row =>
        {
            Assert.Contains(row.Keys, Shown(page));
            Assert.Contains(row.Does, Shown(page));
        });
    }

    [Fact]
    public void Install_installs_and_the_buttons_become_Update_and_Remove()
    {
        var keys = new FakeKeys("iTerm2");
        using var page = Page(keys);

        page.InstallButton.InvokeCommand(Command.Accept);

        Assert.Equal(MacKeysState.Installed, keys.State);
        Assert.Equal("Installed", page.State.Text);
        Assert.False(page.InstallButton.Visible);
        Assert.True(page.UpdateButton.Visible);
        Assert.True(page.RemoveButton.Visible);
        Assert.Equal("", page.ConfigLine);
        Assert.Equal(("Installed the Mac keys for iTerm2.", Schemes.Accent), _said.Single());
    }

    [Theory]
    [InlineData("kitty", "include ateam.conf", "~/.config/kitty/kitty.conf", "reload kitty's config with Ctrl+Cmd+,")]
    [InlineData("WezTerm", "require('ateam').apply(config)", "~/.wezterm.lua", "reopen the dashboard")]
    public void In_kitty_and_WezTerm_installing_shows_the_line_to_add_and_Copy_copies_it(string terminal, string line, string file, string then)
    {
        var clipboard = new FakeClipboard();
        using var page = Page(new FakeKeys(terminal, line, file), clipboard);
        Assert.False(page.CopyButton.Visible);

        page.InstallButton.InvokeCommand(Command.Accept);

        Assert.Equal(line, page.ConfigLine);
        Assert.Equal($"Add this line to {file},\nthen {then}:", page.Note);
        Assert.True(page.CopyButton.Visible);
        page.CopyButton.InvokeCommand(Command.Accept);
        Assert.Equal(line, clipboard.GetClipboardData());
        Assert.Equal(($"Copied {line}", Schemes.Accent), _said[^1]);
    }

    [Fact]
    public void Remove_takes_it_away_and_the_state_goes_back_to_not_installed()
    {
        var keys = new FakeKeys("kitty", "include ateam.conf", "~/.config/kitty/kitty.conf") { State = MacKeysState.Installed };
        using var page = Page(keys);

        page.RemoveButton.InvokeCommand(Command.Accept);

        Assert.Equal(MacKeysState.NotInstalled, keys.State);
        Assert.Equal("Not installed", page.State.Text);
        Assert.True(page.InstallButton.Visible);
        Assert.False(page.RemoveButton.Visible);
        Assert.False(page.CopyButton.Visible);
    }

    [Fact]
    public void An_out_of_date_install_says_so_and_Update_fixes_it()
    {
        var keys = new FakeKeys("WezTerm") { State = MacKeysState.OutOfDate };
        using var page = Page(keys);
        Assert.Equal("Out of date", page.State.Text);
        Assert.True(page.UpdateButton.Visible);

        page.UpdateButton.InvokeCommand(Command.Accept);

        Assert.Equal("Installed", page.State.Text);
        Assert.Equal(("Updated the Mac keys.", Schemes.Accent), _said.Single());
    }

    [Theory]
    [InlineData("Ghostty")]
    [InlineData("Terminal.app")]
    public void A_terminal_that_cant_do_it_says_so_with_no_Install_and_the_keys_that_work_there(string terminal)
    {
        using var page = Page(new FakeKeys(terminal) { CanInstall = false });

        Assert.Equal("Can't be installed here", page.State.Text);
        Assert.False(page.InstallButton.Visible);
        Assert.False(page.UpdateButton.Visible);
        Assert.False(page.RemoveButton.Visible);
        Assert.False(page.ShowsKeys);
        Assert.StartsWith($"{terminal} can't change keys for one app only.", page.Note);
        Assert.Contains("Option+←/→ moves a word, fn+←/→ goes to the start and end of the line.", page.Note);
    }

    [Fact]
    public void A_failed_install_says_why()
    {
        var keys = new FakeKeys("kitty") { Fails = new UnauthorizedAccessException("no access") };
        using var page = Page(keys);

        page.InstallButton.InvokeCommand(Command.Accept);

        Assert.Equal(("Couldn't install the Mac keys: no access", Schemes.Error), _said.Single());
        Assert.Equal("Not installed", page.State.Text);
    }

    // The keys each mapped Mac key arrives as, from MentalDesk.Tui's key map.
    public static TheoryData<string, string, int, string> TextFieldKeys => new()
    {
        { "Home", Line, 0, "" },
        { "End", Line, 13, "" },
        { "Shift+Home", Line, 0, "one tw" },
        { "Shift+End", Line, 13, "o three" },
        { "Ctrl+Home", Line, 0, "" },
        { "Ctrl+End", Line, 13, "" },
        { "Ctrl+CursorLeft", Line, 4, "" },
        { "Ctrl+CursorRight", Line, 8, "" },
        { "Ctrl+Shift+CursorLeft", Line, 4, "tw" },
        { "Ctrl+Shift+CursorRight", Line, 8, "o " },
        { "Ctrl+Shift+Backspace", "o three", 0, "" },
        { "Ctrl+Backspace", "one o three", 4, "" },
        { "Ctrl+A", Line, 13, Line },
    };

    [Theory]
    [MemberData(nameof(TextFieldKeys))]
    public void Each_mapped_key_moves_selects_or_deletes_in_a_single_line_box(string key, string text, int caret, string selected)
    {
        _ = new MacKeys("ateam");
        using var field = new TextField { Width = 20, Text = Line };
        field.InsertionPoint = 6;
        field.ClearAllSelection();

        field.NewKeyDownEvent(Key.TryParse(key, out var parsed) ? parsed : throw new ArgumentException(key));

        Assert.Equal((text, caret, selected), (field.Text, field.InsertionPoint, field.SelectedText ?? ""));
    }

    // Ctrl+Shift+Backspace cuts, which needs a running app's clipboard: MentalDesk.Tui's own tests cover it.
    public static TheoryData<string, string, int, string> MultiLineKeys => new()
    {
        { "Home", Lines, 14, "" },
        { "End", Lines, 27, "" },
        { "Shift+Home", Lines, 14, "four f" },
        { "Shift+End", Lines, 27, "ive six" },
        { "Ctrl+Home", Lines, 0, "" },
        { "Ctrl+End", Lines, 27, "" },
        { "Ctrl+Shift+Home", Lines, 0, "one two three\nfour f" },
        { "Ctrl+Shift+End", Lines, 27, "ive six" },
        { "Ctrl+CursorLeft", Lines, 19, "" },
        { "Ctrl+CursorRight", Lines, 24, "" },
        { "Ctrl+Shift+CursorLeft", Lines, 19, "f" },
        { "Ctrl+Shift+CursorRight", Lines, 24, "ive " },
        { "Ctrl+Backspace", "one two three\nfour ive six", 19, "" },
        { "Ctrl+A", Lines, 27, Lines },
    };

    [Theory]
    [MemberData(nameof(MultiLineKeys))]
    public void Each_mapped_key_moves_selects_or_deletes_in_a_multi_line_box(string key, string text, int caret, string selected)
    {
        _ = new MacKeys("ateam");
        using var field = new MultiLineField { Width = 30, Height = 4, Text = Lines };
        field.ClearSelection();
        field.CaretOffset = 20;

        field.NewKeyDownEvent(Key.TryParse(key, out var parsed) ? parsed : throw new ArgumentException(key));

        Assert.Equal((text, caret, selected),
            (field.Text.ReplaceLineEndings("\n"), field.CaretOffset, field.SelectedText.ReplaceLineEndings("\n")));
    }

    private static List<string> Shown(View view) =>
        [.. view.SubViews.Where(sub => sub.Visible).SelectMany(sub => sub is Label ? [sub.Text] : Shown(sub))];

    /// <summary>A terminal's install held in memory: the library's own tests cover the files themselves.</summary>
    internal sealed class FakeKeys(string terminal, string? configLine = null, string? configFile = null) : ITerminalKeys
    {
        public string Terminal => terminal;

        public bool OnAMac => true;

        public bool CanInstall { get; init; } = true;

        public MacKeysState State { get; set; }

        public string? ConfigLine => configLine;

        public string? ConfigFile => configFile;

        public Exception? Fails { get; init; }

        public void Install() => State = Fails is null ? MacKeysState.Installed : throw Fails;

        public void Remove() => State = Fails is null ? MacKeysState.NotInstalled : throw Fails;
    }
}
