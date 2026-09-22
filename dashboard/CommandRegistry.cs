using Terminal.Gui.Input;

namespace ATeam.Dashboard;

[Flags]
public enum Mode
{
    Grid = 1,
    Expanded = 2,
    Both = Grid | Expanded,
}

/// <summary>How a command reads in the window title. Commands sharing a hint are named once, like the four arrows.</summary>
public sealed record Hint(string Keys, string Text, Mode Modes = Mode.Both);

public sealed record CommandDescriptor(string Id, string Label, Key Key, Hint? Hint);

/// <summary>Everything the dashboard can do, by id, so a key or the Commands dialog can run any of it.</summary>
public sealed class CommandRegistry
{
    private readonly List<Entry> _entries = [];

    public IReadOnlyList<CommandDescriptor> Registered =>
        [.. _entries.Select(entry => new CommandDescriptor(entry.Id, entry.Label(), entry.Key, entry.Hint))];

    public CommandRegistry Register(
        string id,
        string label,
        Action handler,
        Key? key = null,
        Hint? hint = null,
        Func<bool>? isEnabled = null) =>
        Register(id, () => label, handler, key, hint, isEnabled);

    /// <summary>A command whose label depends on what it would do now, like pausing the selected team.</summary>
    public CommandRegistry Register(
        string id,
        Func<string> label,
        Action handler,
        Key? key = null,
        Hint? hint = null,
        Func<bool>? isEnabled = null)
    {
        _entries.Add(new Entry(id, label, handler, key ?? Key.Empty, hint, isEnabled));
        return this;
    }

    /// <summary>Runs a command by name, enabled or not. False when nothing is registered under that id.</summary>
    public bool Execute(string id)
    {
        if (_entries.Find(entry => entry.Id == id) is not { } entry)
            return false;
        entry.Handler();
        return true;
    }

    /// <summary>Runs what <paramref name="key"/> is bound to. False leaves the key to whoever else wants it.</summary>
    public bool Press(Key key)
    {
        if (_entries.Find(entry => entry.Key == key && entry.Key != Key.Empty) is not { } entry)
            return false;
        if (entry.IsEnabled?.Invoke() == false)
            return false;
        entry.Handler();
        return true;
    }

    /// <summary>The hint bar for <paramref name="mode"/>, in registration order, each hint once.</summary>
    public string Hints(Mode mode) => string.Join(" · ", _entries
        .Select(entry => entry.Hint)
        .OfType<Hint>()
        .Where(hint => hint.Modes.HasFlag(mode))
        .Distinct()
        .Select(hint => $"{hint.Keys}: {hint.Text}"));

    private sealed record Entry(string Id, Func<string> Label, Action Handler, Key Key, Hint? Hint, Func<bool>? IsEnabled);
}
