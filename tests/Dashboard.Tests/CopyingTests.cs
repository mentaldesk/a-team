using MentalDesk.Tui.Copying;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;

namespace ATeam.Dashboard.Tests;

public sealed class CopyingTests
{
    [Fact]
    public void A_copy_the_machine_s_clipboard_took_gives_its_counts()
    {
        Assert.Equal(("Copied 214 lines  •  18,302 characters", Schemes.Accent),
            Copying.Said(new CopyOutcome.Copied(214, 18_302)));
    }

    [Fact]
    public void A_copy_only_the_terminal_took_says_so()
    {
        Assert.Equal(("Copied 214 lines  •  18,302 characters through the terminal", Schemes.Accent),
            Copying.Said(new CopyOutcome.Copied(214, 18_302, ThroughTerminal: true)));
    }

    [Fact]
    public void A_copy_nothing_took_says_why_in_the_error_colour()
    {
        Assert.Equal(("Copy failed: too large to send through the terminal (the limit is 750 KB)", Schemes.Error),
            Copying.Said(new CopyOutcome.Failed(TerminalClipboard.TooLarge)));
    }

    [Theory]
    [InlineData(false, "Copied feat/keys")]
    [InlineData(true, "Copied feat/keys through the terminal")]
    public void A_named_copy_says_the_name_instead_of_its_counts(bool throughTerminal, string said)
    {
        Assert.Equal(said, Copying.Said(new CopyOutcome.Copied(1, 9, throughTerminal), "feat/keys").Text);
    }

    [Fact]
    public void A_truncated_line_keeps_saying_so()
    {
        Assert.Equal("Copied 1 line  •  65,536 characters through the terminal (line truncated)",
            Copying.Said(new CopyOutcome.Copied(1, 65_536, ThroughTerminal: true), truncated: true).Text);
    }

    [Fact]
    public void A_plain_clipboard_that_takes_the_text_counts_it_as_the_style_guide_does()
    {
        Assert.Equal(new CopyOutcome.Copied(2, 6), Copying.Copy(new FakeClipboard(), "one\ntwo\n"));
    }

    [Fact]
    public void With_no_clipboard_the_copy_fails()
    {
        Assert.Equal(new CopyOutcome.Failed("there is no clipboard"), Copying.Copy(null, "text"));
    }

    [Fact]
    public void An_installed_terminal_clipboard_takes_the_copy()
    {
        using var terminal = new InstalledTerminal();

        Assert.Equal(new CopyOutcome.Copied(1, 4), Copying.Copy(terminal.Clipboard, "text"));
        Assert.Equal("text", terminal.Platform.Text);
    }
}

/// <summary>An app with <see cref="TerminalClipboard"/> installed over a clipboard that remembers what it was given,
/// so nothing reaches the machine's own clipboard.</summary>
internal sealed class InstalledTerminal : IDisposable
{
    private readonly IApplication _app = Application.Create().Init(DriverRegistry.Names.ANSI);

    public InstalledTerminal()
    {
        _app.Driver!.Clipboard = Platform;
        Clipboard = TerminalClipboard.Install(_app);
    }

    public Remembering Platform { get; } = new();

    public TerminalClipboard Clipboard { get; }

    public void Dispose() => _app.Dispose();

    internal sealed class Remembering : IClipboard
    {
        public string? Text { get; private set; }

        public bool IsSupported => true;

        public string GetClipboardData() => Text ?? "";

        public void SetClipboardData(string text) => Text = text;

        public bool TryGetClipboardData(out string result)
        {
            result = GetClipboardData();
            return true;
        }

        public bool TrySetClipboardData(string text)
        {
            Text = text;
            return true;
        }
    }
}
