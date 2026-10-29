using MentalDesk.Tui.Commands;
using MentalDesk.Tui.Keys;
using Terminal.Gui.Input;
using LibraryCommands = MentalDesk.Tui.Commands.CommandRegistry;

namespace ATeam.Dashboard;

/// <summary>A command as the registry holds it. <paramref name="OnCard"/> marks one that acts on the Work area's
/// selected card, which is what puts it in the Cards menu. <paramref name="MenuLabel"/> is how it reads in the menu,
/// where the title it sits under can say the rest, and <paramref name="MenuLabels"/> every one it can read as there.
/// <paramref name="Keys"/> are its keys as they read on screen. <paramref name="Scopes"/> are the regions it works in,
/// where it belongs to some; one with none is global.</summary>
public sealed record CommandDescriptor(
    string Id, string Label, string Keys = "", bool OnCard = false, string? MenuLabel = null, IReadOnlyList<CommandScope>? Scopes = null,
    IReadOnlyList<string>? MenuLabels = null)
{
    public string MenuLabel { get; init; } = MenuLabel ?? Label;

    public IReadOnlyList<string> MenuLabels { get; init; } = MenuLabels ?? [MenuLabel ?? Label];

    public IReadOnlyList<CommandScope> Scopes { get; init; } = Scopes ?? [];
}

/// <summary>Everything the dashboard can do, by id, so a key or the Commands dialog can run any of it. Its keys live in
/// MentalDesk.Tui's <see cref="Keymap"/>, so a command can have chords and more than one key.</summary>
public sealed class CommandRegistry
{
    private readonly List<Entry> _entries = [];
    private int _ran;

    public CommandRegistry() => Keys = new Keymap(Library);

    /// <summary>The same commands as MentalDesk.Tui holds them, for its keymap and <see cref="KeysPage"/>. A command
    /// that works in more than one region is global there, with its keys bound in each of its regions.</summary>
    public LibraryCommands Library { get; } = new();

    public Keymap Keys { get; }

    public string? PendingChord => Keys.PendingChord;

    public IReadOnlyList<CommandDescriptor> Registered =>
        [.. _entries.Select(entry => new CommandDescriptor(entry.Id, entry.Label(), KeysFor(entry.Id, Where(entry)), entry.OnCard, entry.MenuLabel?.Invoke(),
            entry.Scopes, entry.MenuLabels))];

    public CommandRegistry Register(
        string id,
        string label,
        Action handler,
        Key? key = null,
        Func<bool>? isEnabled = null,
        bool onCard = false,
        CommandScope[]? scopes = null,
        string? chord = null) =>
        Register(id, () => label, handler, key, isEnabled, onCard, scopes: scopes, chord: chord);

    /// <summary>A command whose label depends on what it would do now, like pausing the selected team. Its
    /// <paramref name="menuLabels"/> are every label the menu can show it under, so its letter is one they all have, and
    /// <paramref name="name"/> what the Keys page calls it where its label now wouldn't do.</summary>
    public CommandRegistry Register(
        string id,
        Func<string> label,
        Action handler,
        Key? key = null,
        Func<bool>? isEnabled = null,
        bool onCard = false,
        Func<string>? menuLabel = null,
        Func<bool>? inMenu = null,
        CommandScope[]? scopes = null,
        string[]? menuLabels = null,
        string? chord = null,
        string? name = null)
    {
        var entry = new Entry(id, label, handler, isEnabled, onCard, menuLabel, inMenu, scopes ?? [], menuLabels);
        _entries.Add(entry);
        Library.Register(id, name ?? label(), () => Run(entry), entry.Scopes is [var only] ? only : CommandScope.Global, isEnabled);
        IReadOnlyList<Key>? keys = chord is not null ? KeyChord.Parse(chord) : key is { } single && single != Key.Empty ? [single] : null;
        if (keys is null)
            return this;
        foreach (var scope in entry.Scopes.DefaultIfEmpty(CommandScope.Global))
        {
            if (Keys.CheckConflict(keys, scope) is not null)
                throw new InvalidOperationException($"{KeyChord.Display(keys)} for {id} clashes with another key in {scope}.");
            Keys.Bind(keys, id, scope);
        }
        return this;
    }

    /// <summary>The single key <paramref name="id"/> runs on, for a hint or a dialog that answers it itself, or
    /// <see cref="Key.Empty"/> where its first key is a chord or it has none.</summary>
    public Key KeyFor(string id) =>
        Keys.For(id).FirstOrDefault() is { Chord: [var only] } ? only : Key.Empty;

    public string KeysFor(string id, CommandScope? scope = null) =>
        string.Join(", ", Keys.For(id, scope).Select(binding => binding.Display).Distinct());

    public IReadOnlyList<CommandDescriptor> Enabled => [.. Registered.Where(command => IsEnabled(command.Id))];

    /// <summary>Whether <paramref name="id"/> would run now. One with no test of its own always would.</summary>
    public bool IsEnabled(string id) => _entries.Find(entry => entry.Id == id)?.IsEnabled?.Invoke() != false;

    /// <summary>Whether the menu shows <paramref name="id"/> now. One with no test of its own always does.</summary>
    public bool IsInMenu(string id) => _entries.Find(entry => entry.Id == id)?.InMenu?.Invoke() != false;

    /// <summary>Runs a command by name, enabled or not. False when nothing is registered under that id.</summary>
    public bool Execute(string id)
    {
        if (_entries.Find(entry => entry.Id == id) is not { } entry)
            return false;
        Run(entry);
        return true;
    }

    /// <summary>Runs what <paramref name="key"/> finishes, in the focused region or everywhere, or carries a chord on.
    /// Commands may share a key across regions, or with one everywhere, where the region's own is disabled. False leaves
    /// the key to whoever else wants it.</summary>
    public bool Press(Key key) => Press(key, out _);

    /// <summary>As <see cref="Press(Key)"/>, saying in <paramref name="cancelled"/> when the key ended a chord without
    /// finishing it, which then runs nothing.</summary>
    public bool Press(Key key, out bool cancelled)
    {
        var chording = Keys.PendingChord is not null;
        var ran = _ran;
        var result = Keys.Handle(key);
        cancelled = chording && Keys.PendingChord is null && _ran == ran;
        return result != KeyResult.Pass;
    }

    private CommandScope? Where(Entry entry)
    {
        var focused = Keys.FocusedScope();
        return entry.Scopes.Contains(focused) ? focused : entry.Scopes.Length > 0 ? entry.Scopes[0] : null;
    }

    private void Run(Entry entry)
    {
        _ran++;
        entry.Handler();
    }

    private sealed record Entry(
        string Id, Func<string> Label, Action Handler, Func<bool>? IsEnabled, bool OnCard, Func<string>? MenuLabel,
        Func<bool>? InMenu, CommandScope[] Scopes, string[]? MenuLabels);
}
