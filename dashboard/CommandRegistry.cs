using Terminal.Gui.Input;

namespace ATeam.Dashboard;

/// <summary>One hint as a dialog's hint row draws it, and the command clicking it runs. One that isn't
/// <paramref name="Enabled"/> is drawn greyed.</summary>
public sealed record HintedCommand(string Id, string Text, bool Enabled = true);

/// <summary>A command as the registry holds it. <paramref name="OnCard"/> marks one that acts on the Work area's
/// selected card, which is what puts it in the Cards menu. <paramref name="MenuLabel"/> is how it reads in the menu,
/// where the title it sits under can say the rest.</summary>
public sealed record CommandDescriptor(string Id, string Label, Key Key, bool OnCard = false, string? MenuLabel = null)
{
    public string MenuLabel { get; init; } = MenuLabel ?? Label;
}

/// <summary>Everything the dashboard can do, by id, so a key or the Commands dialog can run any of it.</summary>
public sealed class CommandRegistry
{
    private readonly List<Entry> _entries = [];

    public IReadOnlyList<CommandDescriptor> Registered =>
        [.. _entries.Select(entry => new CommandDescriptor(entry.Id, entry.Label(), entry.Key, entry.OnCard, entry.MenuLabel?.Invoke()))];

    public CommandRegistry Register(
        string id,
        string label,
        Action handler,
        Key? key = null,
        Func<bool>? isEnabled = null,
        bool onCard = false) =>
        Register(id, () => label, handler, key, isEnabled, onCard);

    /// <summary>A command whose label depends on what it would do now, like pausing the selected team.</summary>
    public CommandRegistry Register(
        string id,
        Func<string> label,
        Action handler,
        Key? key = null,
        Func<bool>? isEnabled = null,
        bool onCard = false,
        Func<string>? menuLabel = null,
        Func<bool>? inMenu = null)
    {
        _entries.Add(new Entry(id, label, handler, key ?? Key.Empty, isEnabled, onCard, menuLabel, inMenu));
        return this;
    }

    /// <summary>Rebinds commands, in the order given, <see cref="Key.Empty"/> unbinding one. An id nobody registered,
    /// and a key another command still holds, are each ignored on their own, leaving that command on the key it had.</summary>
    public CommandRegistry Apply(IEnumerable<(string Id, Key Key)> keys)
    {
        foreach (var (id, key) in keys)
        {
            var index = _entries.FindIndex(entry => entry.Id == id);
            if (index < 0 || (key != Key.Empty && _entries.Exists(entry => entry.Id != id && entry.Key == key)))
                continue;
            _entries[index] = _entries[index] with { Key = key };
        }
        return this;
    }

    /// <summary>What <paramref name="id"/> is bound to, or <see cref="Key.Empty"/> when nothing is.</summary>
    public Key KeyFor(string id) => _entries.Find(entry => entry.Id == id)?.Key ?? Key.Empty;

    /// <summary>Whether <paramref name="id"/> would run now. One with no test of its own always would.</summary>
    public bool IsEnabled(string id) => _entries.Find(entry => entry.Id == id)?.IsEnabled?.Invoke() != false;

    /// <summary>Whether the menu shows <paramref name="id"/> now. One with no test of its own always does.</summary>
    public bool IsInMenu(string id) => _entries.Find(entry => entry.Id == id)?.InMenu?.Invoke() != false;

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

    private sealed record Entry(
        string Id, Func<string> Label, Action Handler, Key Key, Func<bool>? IsEnabled, bool OnCard, Func<string>? MenuLabel,
        Func<bool>? InMenu);
}
