using MentalDesk.Tui.Commands;
using MentalDesk.Tui.Dialogs;
using MentalDesk.Tui.Keys;
using MentalDesk.Tui.Shell;
using MentalDesk.Tui.Theming;
using Terminal.Gui.Input;
using KeyBinding = MentalDesk.Tui.Keys.KeyBinding;

namespace ATeam.Dashboard;

/// <summary>A MentalDesk.Tui dialog the dashboard lays out itself: its actions are buttons in <see cref="Foot"/>, and
/// F1 lists every key it binds.</summary>
public abstract class DashboardDialog : AppDialog
{
    private readonly List<(KeyRow Row, Func<bool>? When)> _also = [];
    private readonly Dictionary<string, (Action Run, Func<bool>? When)> _runs = [];
    private readonly Dictionary<string, Button> _buttons = [];

    protected DashboardDialog(string title)
        : base(title, width: 0, contentRows: 0)
    {
        this.Routed();
    }

    internal ButtonBar Foot { get; } = new();

    /// <summary>The labels of the buttons on show, in order.</summary>
    internal IReadOnlyList<string> ButtonLabels => [.. Foot.Shown.Select(button => button.Text.Trim())];

    /// <summary>Enter reaches a Dialog as Accept and would close it; what Enter does is up to the dialog's keys.</summary>
    protected override bool OnAccepting(CommandEventArgs args) => true;

    protected Button AddButton(ButtonKind kind, string commandId)
    {
        var button = AppButton.Of(kind, Caption(commandId, Commands.Find(commandId)!.Label));
        button.CanFocus = false;
        button.Accepting += (_, e) =>
        {
            e.Handled = true;
            Commands.Execute(commandId);
        };
        _buttons[commandId] = button;
        Foot.Add(button);
        return button;
    }

    /// <summary>Shows the button of each command that would run now, and hides the rest.</summary>
    protected void ShowButtons()
    {
        foreach (var (id, button) in _buttons)
            button.Visible = Commands.IsEnabled(id);
        Foot.Refresh();
    }

    /// <summary>Hides the button of <paramref name="id"/>, where there is one, while <paramref name="hidden"/>.</summary>
    protected void Hide(string id, bool hidden)
    {
        if (hidden && _buttons.TryGetValue(id, out var button))
        {
            button.Visible = false;
            Foot.Refresh();
        }
    }

    /// <summary>Greys the button of <paramref name="id"/>, where there is one, unless <paramref name="enabled"/>.</summary>
    protected void Grey(string id, bool enabled)
    {
        if (_buttons.TryGetValue(id, out var button))
            button.Enabled = enabled;
    }

    /// <summary>Colours the button of <paramref name="id"/>, where there is one, as <paramref name="kind"/>.</summary>
    protected void Restyle(string id, ButtonKind kind)
    {
        if (_buttons.TryGetValue(id, out var button))
            button.SchemeName = SchemeOf(kind);
    }

    internal static string SchemeOf(ButtonKind kind) => kind switch
    {
        ButtonKind.Primary => SchemeNames.ButtonPrimary,
        ButtonKind.Danger => SchemeNames.ButtonDanger,
        _ => SchemeNames.ButtonSecondary,
    };

    /// <summary>Gives a command, and its button, a new label.</summary>
    protected void Rename(string id, string label)
    {
        if (!_runs.TryGetValue(id, out var registered))
            return;
        var (run, when) = registered;
        Register(id, label, run, when);
        if (_buttons.TryGetValue(id, out var button))
            button.Text = $" {Caption(id, label)} ";
        Foot.Refresh();
    }

    /// <summary>What F1 shows: the keys the dialog binds, then those its views handle themselves, then Esc.</summary>
    internal KeySheet Sheet
    {
        get
        {
            var bound = Keys.Bindings
                .Where(binding => binding.CommandId != ShellCommands.ShowKeys && Commands.IsEnabled(binding.CommandId))
                .Select(binding => new KeyRow(Display(binding), Commands.Find(binding.CommandId)!.Label))
                .ToList();
            var esc = bound.FindIndex(row => row.Keys == "Esc");
            var last = esc < 0 ? [] : new[] { bound[esc] };
            if (esc >= 0)
                bound.RemoveAt(esc);
            bound.AddRange(_also.Where(row => row.When?.Invoke() != false).Select(row => row.Row));
            bound.AddRange(last);
            return new KeySheet(new KeyColumn(Title, [new KeyGroup(null, bound)]), null);
        }
    }

    /// <summary>Lists, under F1, keys one of the dialog's views handles itself, while <paramref name="when"/> holds.</summary>
    protected void Also(string keys, string label, Func<bool>? when = null) => _also.Add((new KeyRow(keys, label), when));

    /// <summary>Binds <paramref name="key"/> to a command that runs <paramref name="run"/> while <paramref name="when"/>
    /// holds, and is otherwise left to the views. A key already bound stays with the command bound first.</summary>
    protected void Bind(string id, Key key, string label, Action run, Func<bool>? when = null)
    {
        Register(id, label, run, when);
        if (Keys.CheckConflict([key], CommandScope.Global) != KeyConflict.ExactMatch)
            Keys.Bind([key], id);
    }

    private string Caption(string id, string label) =>
        Keys.For(id).FirstOrDefault() is { } binding ? $"{Display(binding)} {label}" : label;

    /// <summary>As the library shows a key, but with a letter in the case it's pressed in, as the window's keys are.</summary>
    private static string Display(KeyBinding binding) =>
        binding.Chord is [var key] && key.ToString() is [var letter] && char.IsLetter(letter) ? letter.ToString() : binding.Display;

    /// <summary>What <paramref name="id"/> does, and while <paramref name="when"/> holds; Esc's is <see cref="AppDialog.CancelId"/>.</summary>
    protected void Register(string id, string label, Action run, Func<bool>? when = null)
    {
        _runs[id] = (run, when);
        Commands.Register(id, label, run, isEnabled: when);
    }
}
