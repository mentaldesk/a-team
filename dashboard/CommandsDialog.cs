using System.Collections.ObjectModel;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace ATeam.Dashboard;

/// <summary>Everything the dashboard can do, narrowed by typing and run by name.</summary>
public sealed class CommandsDialog : Dialog
{
    private readonly IReadOnlyList<CommandDescriptor> _commands;
    private readonly List<CommandDescriptor> _matches = [];
    private readonly int _labelWidth;
    private readonly TextField _filter;
    private readonly ListView _list;

    public CommandsDialog(IReadOnlyList<CommandDescriptor> commands)
    {
        _commands = commands;
        _labelWidth = commands.Count == 0 ? 0 : commands.Max(command => command.Label.Length);

        Title = "Commands";
        Width = Dim.Percent(80);
        Height = Dim.Percent(80);

        _filter = new TextField { X = 0, Y = 0, Width = Dim.Fill() };
        _filter.ValueChanged += (_, _) => Narrow();
        _list = new ListView { X = 0, Y = Pos.Bottom(_filter), Width = Dim.Fill(), Height = Dim.Fill(1) };
        var hints = new StatusBar([
            Shortcut("Up/Down", "select", () => Move(+1)),
            Shortcut("Enter", "run", () => Run()),
            Shortcut("Esc", "cancel", () => Cancel()),
        ]);
        Add(_filter, _list, hints);

        Narrow();
        _filter.SetFocus();
    }

    internal string? Chosen { get; private set; }

    internal IReadOnlyList<CommandDescriptor> Matches => _matches;

    internal ListView List => _list;

    internal TextField Filter => _filter;

    /// <summary>Enter reaches a Dialog as Accept, from the filter or the list alike, and never as a key.</summary>
    protected override bool OnAccepting(CommandEventArgs args) => Run();

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Esc)
            return Cancel();
        if (key == Key.CursorUp)
            return Move(-1);
        if (key == Key.CursorDown)
            return Move(+1);
        if (key == Key.Tab)
            return MoveFocus();
        return base.OnKeyDown(key);
    }

    private void Narrow()
    {
        var filter = _filter.Text ?? "";
        _matches.Clear();
        _matches.AddRange(_commands.Where(command =>
            command.Label.Contains(filter, StringComparison.OrdinalIgnoreCase)));
        _list.SetSource(new ObservableCollection<string>(_matches.Select(Row)));
        _list.Value = _matches.Count == 0 ? null : 0;
    }

    private string Row(CommandDescriptor command) =>
        $"{command.Label.PadRight(_labelWidth)}  {(command.Key == Key.Empty ? "" : command.Key.ToString())}";

    private bool Move(int step)
    {
        if (_matches.Count > 0)
        {
            _list.Value = Math.Clamp((_list.Value ?? 0) + step, 0, _matches.Count - 1);
            _list.EnsureSelectedItemVisible();
        }
        return true;
    }

    private bool MoveFocus()
    {
        (_filter.HasFocus ? (View)_list : _filter).SetFocus();
        return true;
    }

    private bool Run()
    {
        if (_list.Value is { } index && index >= 0 && index < _matches.Count)
        {
            Chosen = _matches[index].Id;
            RequestStop();
        }
        return true;
    }

    private bool Cancel()
    {
        Chosen = null;
        RequestStop();
        return true;
    }

    /// <summary>A StatusBar draws its Shortcuts help-first, so the keys go in the help and the verb in the title.</summary>
    private static Shortcut Shortcut(string keys, string what, Action action) =>
        new() { Title = what, HelpText = keys, Key = Key.Empty, Action = action, CanFocus = false };

    /// <summary>Runs what was picked only once the dialog has closed, so Quit stops the dashboard and not this.</summary>
    public static void Show(IApplication app, CommandRegistry commands)
    {
        using var dialog = new CommandsDialog(commands.Registered);
        app.Run(dialog);
        if (dialog.Chosen is { } id)
            commands.Execute(id);
    }
}
