using MentalDesk.Tui;
using MentalDesk.Tui.Keys;
using MentalDesk.Tui.Settings;
using Terminal.Gui.Drawing;

namespace ATeam.Dashboard;

/// <summary>Settings' Terminal page: the dashboard's Mac keys in the terminal it runs in, installed, updated or
/// removed only when Settings is kept.</summary>
public sealed class TerminalPage : SettingsPage
{
    internal const string Name = "Terminal";
    private const int ValueX = 11;
    private const int Indent = 2;
    private const int Gap = 4;
    private const int BoxRow = 3;

    private const string Explained = """
        Lets Cmd and Option keys work in the dashboard the way they do
        in TuiCode. Only the dashboard is affected.
        """;

    private readonly ITerminalKeys _keys;
    private readonly IClipboard? _clipboard;
    private readonly Action<string, Schemes> _say;
    private readonly Label _default;
    private readonly Label _pending = new() { X = Indent, Y = BoxRow + 1 };
    private readonly Label _note = new() { Y = BoxRow + 3 };
    private readonly Label _configLine = new() { X = Indent };
    private readonly View _rows;

    public TerminalPage(ITerminalKeys keys, Action<string, Schemes>? say = null, IClipboard? clipboard = null) : base(Name)
    {
        _keys = keys;
        _say = say ?? ((_, _) => { });
        _clipboard = clipboard;

        State = new Label { X = ValueX, Y = 1 };
        TurnedOn = new CheckBox
        {
            Y = BoxRow,
            Text = $"Cmd and Option keys in {keys.Terminal}",
            Enabled = keys.CanInstall,
            Value = keys.State == MacKeysState.NotInstalled ? CheckState.UnChecked : CheckState.Checked,
        };
        TurnedOn.ValueChanged += (_, _) =>
        {
            Show();
            OnChanged();
        };
        _default = Dimmed(new Label { X = Pos.Right(TurnedOn) + 2, Y = BoxRow });
        CopyButton = new Button { Text = "Copy", X = Pos.Right(_configLine) + Gap, Y = Pos.Top(_configLine) };
        CopyButton.Accepting += (_, e) => e.Handled = Copy();

        var width = MacKeys.Rows.Max(row => row.Keys.Length) + Gap;
        _rows = new View { X = Indent, Width = Dim.Fill(), Height = MacKeys.Rows.Count };
        _rows.Add([.. MacKeys.Rows.SelectMany((row, i) => new View[]
        {
            new Label { Y = i, Text = row.Keys },
            new Label { X = width, Y = i, Text = row.Does },
        })]);

        Add(new Label { Text = Name }, new Label { X = ValueX, Text = keys.Terminal },
            new Label { Y = 1, Text = "Mac keys" }, State, TurnedOn, _default, Dimmed(_pending), _note, _configLine, CopyButton, _rows);
        Show();
    }

    public Label State { get; }

    public CheckBox TurnedOn { get; }

    public Button CopyButton { get; }

    internal string Note => _note.Text;

    internal string Pending => _pending.Text;

    internal string Default => _default.Text;

    internal string ConfigLine => _configLine.Visible ? _configLine.Text : "";

    internal bool ShowsKeys => _rows.Visible;

    private bool Ticked => TurnedOn.Value == CheckState.Checked;

    public override bool IsChanged =>
        _keys.CanInstall && (Ticked ? _keys.State != MacKeysState.Installed : _keys.State != MacKeysState.NotInstalled);

    public override void Keep()
    {
        if (!_keys.CanInstall)
            return;
        if (Ticked)
            _keys.Install();
        else
            _keys.Remove();
        Show();
    }

    public override void Revert() =>
        TurnedOn.Value = _keys.State == MacKeysState.NotInstalled ? CheckState.UnChecked : CheckState.Checked;

    private bool Copy()
    {
        if (_keys.ConfigLine is not { } line || !CopyButton.Visible)
            return true;
        var (said, scheme) = Copying.Said(Copying.Copy(_clipboard ?? App?.Clipboard, line), line);
        _say(said, scheme);
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
        _default.Text = Ticked ? DefaultNote("off") : "";
        _pending.Text = !IsChanged ? ""
            : !Ticked ? "Removes them when you press Ctrl+Enter."
            : state == MacKeysState.OutOfDate ? "Updates them when you press Ctrl+Enter."
            : "Installs when you press Ctrl+Enter.";
        var configLine = supported && state != MacKeysState.NotInstalled ? _keys.ConfigLine : null;
        _note.Text = !supported ? Unsupported()
            : configLine is not null ? $"Add this line to {_keys.ConfigFile},\nthen {AfterAdding()}:"
            : Explained;
        _configLine.Text = configLine ?? "";
        _configLine.Y = Pos.Bottom(_note);
        _configLine.Visible = CopyButton.Visible = configLine is not null;
        _rows.Visible = supported;
        _rows.Y = Pos.Bottom(configLine is null ? _note : _configLine) + 1;
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
