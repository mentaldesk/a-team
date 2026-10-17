using System.Collections.ObjectModel;
using MentalDesk.Tui.Filtering;
using MentalDesk.Tui.Dialogs;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace ATeam.Dashboard;

/// <summary>Everything the dashboard can do, or any other list of names, narrowed by typing and picked by name. The
/// best match comes first, as in every MentalDesk app's lists.</summary>
public sealed class CommandsDialog : DashboardDialog
{
    private readonly IReadOnlyList<CommandDescriptor> _commands;
    private readonly List<CommandDescriptor> _matches = [];
    private readonly int _labelWidth;
    private readonly TextField _filter;
    private readonly ListView _list;
    private readonly Label _noMatches;
    private readonly Button _enter;

    public CommandsDialog(IReadOnlyList<CommandDescriptor> commands, string title = "Commands", string verb = "Run")
        : base(title)
    {
        _commands = commands;
        _labelWidth = commands.Count == 0 ? 0 : commands.Max(command => command.Label.Length);

        Width = Dim.Percent(80);
        Height = Dim.Percent(80);

        _filter = new TextField { X = 0, Y = 0, Width = Dim.Fill() };
        _filter.ValueChanged += (_, _) => Narrow();
        // The filter binds Home/End to its own cursor, so the list would never see them.
        _filter.KeyBindings.Remove(Key.Home);
        _filter.KeyBindings.Remove(Key.End);
        _list = new ListView { X = 0, Y = Pos.Bottom(_filter), Width = Dim.Fill(), Height = Dim.Fill(Dim.Func(_ => Foot.Rows + 1, this)) };
        _noMatches = new Label { X = 0, Y = Pos.Bottom(_filter), Text = "(no matches)", Enabled = false, Visible = false };
        Bind("commands.run", Key.Enter, verb, () => Run(), () => _matches.Count > 0);
        Bind("commands.up", Key.CursorUp, "Previous", () => Move(-1));
        Bind("commands.down", Key.CursorDown, "Next", () => Move(+1));
        Bind("commands.pageUp", Key.PageUp, "Page up", () => Move(-Page));
        Bind("commands.pageDown", Key.PageDown, "Page down", () => Move(+Page));
        Bind("commands.home", Key.Home, "First", () => MoveTo(0));
        Bind("commands.end", Key.End, "Last", () => MoveTo(_matches.Count - 1));
        Bind("commands.switch", Key.Tab, "Filter or list", () => MoveFocus());
        Register(CancelId, "Cancel", () => Dismiss());
        _enter = AddButton(ButtonKind.Primary, "commands.run");
        AddButton(ButtonKind.Secondary, CancelId);
        Foot.Y = Pos.Func(_ => Viewport.Height - Foot.Rows, this);
        Add(_filter, _list, _noMatches, Foot);

        Narrow();
        _filter.SetFocus();
    }

    internal string? Chosen { get; private set; }

    internal IReadOnlyList<CommandDescriptor> Matches => _matches;

    internal ListView List => _list;

    internal TextField Filter => _filter;

    internal bool NoMatches => _noMatches.Visible;

    internal bool OffersEnter => _enter.Visible;

    /// <summary>A double-click on a row reaches the dialog as Accept.</summary>
    protected override bool OnAccepting(CommandEventArgs args) => Run();

    private void Narrow()
    {
        _matches.Clear();
        _matches.AddRange(ListFilter.Apply(_filter.Text ?? "", _commands, command => Row(command).TrimEnd()));
        _list.SetSource(new ObservableCollection<string>(_matches.Select(Row)));
        _list.Value = _matches.Count == 0 ? null : 0;
        _noMatches.Visible = _matches.Count == 0;
        ShowButtons();
    }

    private string Row(CommandDescriptor command) =>
        $"{command.Label.PadRight(_labelWidth)}  {(command.Key == Key.Empty ? "" : command.Key.ToString())}";

    private int Page => Math.Max(1, _list.Viewport.Height);

    private bool Move(int step) => MoveTo((_list.Value ?? 0) + step);

    private bool MoveTo(int index)
    {
        if (_matches.Count > 0)
        {
            _list.Value = Math.Clamp(index, 0, _matches.Count - 1);
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

    private bool Dismiss()
    {
        Chosen = null;
        RequestStop();
        return true;
    }

    /// <summary>Runs what was picked only once the dialog has closed, so Quit stops the dashboard and not this.</summary>
    public static void Show(IApplication app, CommandRegistry commands, IReadOnlyList<CommandDescriptor>? offered = null)
    {
        using var dialog = new CommandsDialog(offered ?? commands.Enabled);
        DialogHost.Run(app, dialog);
        if (dialog.Chosen is { } id)
            commands.Execute(id);
    }

    internal static CommandsDialog ForNames(string title, IReadOnlyList<string> names) =>
        new([.. names.Select(name => new CommandDescriptor(name, name, Key.Empty))], title, "Go");

    /// <summary>One of <paramref name="names"/>, or null when the dialog is cancelled.</summary>
    public static string? Pick(IApplication app, string title, IReadOnlyList<string> names)
    {
        using var dialog = ForNames(title, names);
        DialogHost.Run(app, dialog);
        return dialog.Chosen;
    }
}
