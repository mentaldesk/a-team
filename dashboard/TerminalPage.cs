using MentalDesk.Tui.Keys;
using Terminal.Gui.Drawing;

namespace ATeam.Dashboard;

/// <summary>Settings' Terminal page: the dashboard's Mac keys in the terminal it runs in, installed, updated and
/// removed straight away, like pausing a team.</summary>
public sealed class TerminalPage : View
{
    internal const string Name = "Terminal";
    private const int ValueX = 11;
    private const int Indent = 2;
    private const int Gap = 4;

    private const string Explained = """
        Lets Cmd and Option keys work in the dashboard the way they do
        in TuiCode. Only the dashboard is affected.
        """;

    private readonly ITerminalKeys _keys;
    private readonly IClipboard? _clipboard;
    private readonly Action<string, Schemes> _say;
    private readonly Label _note = new() { Y = 3 };
    private readonly Label _configLine = new() { X = Indent };
    private readonly View _rows;

    public TerminalPage(ITerminalKeys keys, Action<string, Schemes> say, IClipboard? clipboard = null)
    {
        _keys = keys;
        _say = say;
        _clipboard = clipboard;
        CanFocus = true;

        State = new Label { X = ValueX, Y = 1 };
        CopyButton = new Button { Text = "Copy", X = Pos.Right(_configLine) + Gap, Y = Pos.Top(_configLine) };
        CopyButton.Accepting += (_, e) => e.Handled = Copy();
        InstallButton = PageButton("Install", Install);
        UpdateButton = PageButton("Update", Install);
        RemoveButton = PageButton("Remove", Remove);
        RemoveButton.X = Pos.Right(UpdateButton) + 1;

        var width = MacKeys.Rows.Max(row => row.Keys.Length) + Gap;
        _rows = new View { X = Indent, Width = Dim.Fill(), Height = MacKeys.Rows.Count };
        _rows.Add([.. MacKeys.Rows.SelectMany((row, i) => new View[]
        {
            new Label { Y = i, Text = row.Keys },
            new Label { X = width, Y = i, Text = row.Does },
        })]);

        Add(new Label { Text = Name }, new Label { X = ValueX, Text = keys.Terminal },
            new Label { Y = 1, Text = "Mac keys" }, State, _note, _configLine, CopyButton, _rows,
            InstallButton, UpdateButton, RemoveButton);
        Show();
    }

    public Label State { get; }

    public Button InstallButton { get; }

    public Button UpdateButton { get; }

    public Button RemoveButton { get; }

    public Button CopyButton { get; }

    internal string Note => _note.Text;

    internal string ConfigLine => _configLine.Visible ? _configLine.Text : "";

    internal bool ShowsKeys => _rows.Visible;

    private static Button PageButton(string text, Action act)
    {
        var button = new Button { Text = text };
        button.Accepting += (_, e) =>
        {
            act();
            e.Handled = true;
        };
        return button;
    }

    internal void Install()
    {
        if (!_keys.CanInstall)
            return;
        var updating = _keys.State == MacKeysState.OutOfDate;
        if (Try(_keys.Install, "install"))
            _say(updating ? "Updated the Mac keys." : $"Installed the Mac keys for {_keys.Terminal}.", Schemes.Accent);
        Show();
        Refocus();
    }

    internal void Remove()
    {
        if (!_keys.CanInstall)
            return;
        if (Try(_keys.Remove, "remove"))
            _say("Removed the Mac keys.", Schemes.Accent);
        Show();
        Refocus();
    }

    private void Refocus()
    {
        if (HasFocus)
            (InstallButton.Visible ? InstallButton : UpdateButton).SetFocus();
    }

    private bool Try(Action act, string verb)
    {
        try
        {
            act();
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _say($"Couldn't {verb} the Mac keys: {e.Message}", Schemes.Error);
            return false;
        }
    }

    private bool Copy()
    {
        if (_keys.ConfigLine is not { } line || !CopyButton.Visible)
            return true;
        var clipboard = _clipboard ?? App?.Clipboard;
        if (clipboard is { IsSupported: true } && clipboard.TrySetClipboardData(line))
            _say($"Copied {line}", Schemes.Accent);
        else
            _say("there's no clipboard to copy to", Schemes.Error);
        return true;
    }

    private void Show()
    {
        var state = _keys.State;
        var supported = _keys.CanInstall;
        State.Text = !supported ? "Can't be installed here" : state switch
        {
            MacKeysState.Installed => "Installed",
            MacKeysState.OutOfDate => "Out of date",
            _ => "Not installed",
        };
        var configLine = supported && state != MacKeysState.NotInstalled ? _keys.ConfigLine : null;
        _note.Text = !supported ? Unsupported()
            : configLine is not null ? $"Add this line to {_keys.ConfigFile},\nthen {AfterAdding()}:"
            : Explained;
        _configLine.Text = configLine ?? "";
        _configLine.Y = Pos.Bottom(_note);
        _configLine.Visible = CopyButton.Visible = configLine is not null;
        _rows.Visible = supported;
        _rows.Y = Pos.Bottom(configLine is null ? _note : _configLine) + 1;
        var buttons = Pos.Bottom(_rows) + 1;
        InstallButton.Y = UpdateButton.Y = RemoveButton.Y = buttons;
        InstallButton.Visible = supported && state == MacKeysState.NotInstalled;
        UpdateButton.Visible = RemoveButton.Visible = supported && state != MacKeysState.NotInstalled;
        SetNeedsLayout();
        SetNeedsDraw();
    }

    private string AfterAdding() => _keys.Terminal == "kitty"
        ? "reload kitty's config with Ctrl+Cmd+,"
        : "reopen the dashboard";

    private string Unsupported() => _keys.OnAMac
        ? $"""
          {_keys.Terminal} can't change keys for one app only. These work without it:
          Option+←/→ moves a word, fn+←/→ goes to the start and end of the line.
          iTerm2, kitty and WezTerm can do the rest.
          """
        : """
          Cmd and Option keys come from a Mac's keyboard. In iTerm2, kitty or
          WezTerm on a Mac, this page turns them on.
          """;
}
