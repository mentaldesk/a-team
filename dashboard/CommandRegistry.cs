using Terminal.Gui.Input;

namespace ATeam.Dashboard;

[Flags]
public enum Mode
{
    Grid = 1,
    Expanded = 2,
    Work = 4,
    Both = Grid | Expanded,
}

/// <summary>How a command reads in the window title, named by the key it's bound to. Commands sharing a hint are
/// named once, like the four arrows.</summary>
public sealed record Hint(string Text, Mode Modes = Mode.Both);

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

    /// <summary>Rebinds commands, in the order given. An id nobody registered, and a key another command still
    /// holds, are each ignored on their own, leaving that command on the key it had.</summary>
    public CommandRegistry Apply(IEnumerable<(string Id, Key Key)> keys)
    {
        foreach (var (id, key) in keys)
        {
            var index = _entries.FindIndex(entry => entry.Id == id);
            if (index < 0 || key == Key.Empty || _entries.Exists(entry => entry.Id != id && entry.Key == key))
                continue;
            _entries[index] = _entries[index] with { Key = key };
        }
        return this;
    }

    /// <summary>What <paramref name="id"/> is bound to, or <see cref="Key.Empty"/> when nothing is.</summary>
    public Key KeyFor(string id) => _entries.Find(entry => entry.Id == id)?.Key ?? Key.Empty;

    /// <summary>Whether <paramref name="id"/> would run now. One with no test of its own always would.</summary>
    public bool IsEnabled(string id) => _entries.Find(entry => entry.Id == id)?.IsEnabled?.Invoke() != false;

    /// <summary>Runs a command by name, enabled or not. False when nothing is registered under that id.</summary>
    public bool Execute(string id)
    {
        if (_entries.Find(entry => entry.Id == id) is not { } entry)
            return false;
        entry.Handler();
        return true;
    }

    /// <summary>Runs what <paramref name="key"/> is bound to. Commands may share a key where only one of them is
    /// ever enabled, like Enter in each area. False leaves the key to whoever else wants it.</summary>
    public bool Press(Key key)
    {
        if (_entries.Find(entry => entry.Key == key && entry.Key != Key.Empty && entry.IsEnabled?.Invoke() != false)
            is not { } entry)
            return false;
        entry.Handler();
        return true;
    }

    /// <summary>The hint bar for <paramref name="mode"/>, in registration order, each hint once. A hint with no
    /// bound key has nothing to name and is left out.</summary>
    public string Hints(Mode mode) => string.Join(" · ", _entries
        .Where(entry => entry.Hint is { } hint && hint.Modes.HasFlag(mode))
        .GroupBy(entry => entry.Hint!)
        .Select(hinted => (Keys: Keys(hinted), hinted.Key.Text))
        .Where(hint => hint.Keys.Length > 0)
        .Select(hint => $"{hint.Keys}: {hint.Text}"));

    private static string Keys(IGrouping<Hint, Entry> hinted) => string.Join('/', hinted
        .Where(entry => entry.Key != Key.Empty)
        .Select(entry => KeyNames.Short(entry.Key))
        .Distinct());

    private sealed record Entry(string Id, Func<string> Label, Action Handler, Key Key, Hint? Hint, Func<bool>? IsEnabled);
}
