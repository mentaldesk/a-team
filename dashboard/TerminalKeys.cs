using MentalDesk.Tui.Keys;

namespace ATeam.Dashboard;

/// <summary>The dashboard's Mac keys in the terminal it runs in, as the Terminal page sees them.</summary>
public interface ITerminalKeys
{
    string Terminal { get; }

    bool OnAMac { get; }

    /// <summary>False in a terminal that can't change keys for one app.</summary>
    bool CanInstall { get; }

    MacKeysState State { get; }

    /// <summary>The line to add to the terminal's own config once installed, or null where none is needed.</summary>
    string? ConfigLine { get; }

    string? ConfigFile { get; }

    void Install();

    void Remove();
}

public sealed class TerminalKeys(MacKeys keys) : ITerminalKeys
{
    private readonly TerminalIntegration? _here = keys.Here;

    public string Terminal => keys.Terminal;

    public bool OnAMac => keys.OnAMac;

    public bool CanInstall => _here is not null;

    public MacKeysState State => _here?.State ?? MacKeysState.NotInstalled;

    public string? ConfigLine => _here?.ConfigLine;

    public string? ConfigFile => _here?.ConfigFile;

    public void Install() => _here?.Install();

    public void Remove() => _here?.Remove();
}
