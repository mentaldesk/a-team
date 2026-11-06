using MentalDesk.Tui;
using MentalDesk.Tui.Dialogs;
using MentalDesk.Tui.Keys;
using MentalDesk.Tui.Settings;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using LibrarySettings = MentalDesk.Tui.Settings.SettingsDialog;

namespace ATeam.Dashboard;

/// <summary>The dashboard's own preferences on the library's Settings screen: nothing is kept until Ctrl+Enter, and
/// Esc puts every page back.</summary>
public sealed class SettingsScreen : IDisposable
{
    internal const string KeysPageName = "Keyboard Shortcuts";
    private static readonly Key GuideKey = Key.G.WithCtrl;

    private readonly InputScopes _scopes = new();
    private readonly Action<string> _showGuide;

    public SettingsScreen(
        ThemeSetting theme,
        IconSetting icons,
        DashboardSettings settings,
        CommandRegistry commands,
        Action redraw,
        IconStyle auto,
        Action<string>? showGuide = null,
        ITerminalKeys? terminalKeys = null,
        KeyOverrides? keys = null)
    {
        _showGuide = showGuide ?? (_ => { });
        ShowHelp = sheet =>
        {
            if (Dialog?.App is { } app)
                KeysSheet.Show(app, sheet);
        };
        Confirm = confirm =>
        {
            if (Dialog?.App is { } app)
                app.Run(confirm);
        };
        keys ??= new KeyOverrides(commands.Library, commands.Keys, () => null, _ => { });
        Theme = new ThemePage(theme, redraw);
        KeysPage = new KeysPage(commands.Library, _scopes, keys, scope => scope.Name, confirm => Confirm(confirm)) { Title = KeysPageName };
        Dashboard = new DashboardPage(settings, icons, auto, redraw);
        Terminal = terminalKeys is null ? null : new TerminalPage(terminalKeys, Say);
        Overseer = new OverseerPage(settings, message => Say(message, Schemes.Error));
        Dialog = new LibrarySettings([Theme, KeysPage, Dashboard, .. Terminal is null ? Array.Empty<SettingsPage>() : [Terminal], Overseer]);
        Dialog.Height = Dim.Fill(1);
        Dialog.KeepButton.MouseEvent += (_, mouse) => mouse.Handled = mouse.Handled || (mouse.Flags != MouseFlags.None && RefusesLimits());
    }

    internal LibrarySettings Dialog { get; }

    internal ThemePage Theme { get; }

    internal KeysPage KeysPage { get; }

    internal DashboardPage Dashboard { get; }

    internal TerminalPage? Terminal { get; }

    internal OverseerPage Overseer { get; }

    internal Action<ConfirmDialog> Confirm { get; set; }

    internal Action<KeySheet> ShowHelp { get; set; }

    internal bool Confirmed => Dialog.Confirmed;

    /// <summary>What F1 shows: the page's own keys, then the screen's.</summary>
    internal KeySheet Sheet => DialogHost.Sheet(Dialog.Page?.Title ?? "Settings",
    [
        .. Dialog.Page == KeysPage
            ? [new("Enter", "Add a key"), new("Del", "Remove the key"), new(KeyNames.Short(Key.R.WithCtrl), "Reset to defaults…"), new("Type", "Filter")]
            : Array.Empty<KeyRow>(),
        new(KeyNames.Short(Key.Enter.WithCtrl), "Keep"),
        new("Ctrl+PgDn/Ctrl+PgUp", "Next / previous page"),
        new(KeyNames.Short(GuideKey), "Guide"),
        new("Tab/Shift+Tab", "Next / previous"),
        new("Esc", "Cancel"),
    ]);

    /// <summary>The app hands each key here before any view: the Keys page records it first, as AppShell does, then the
    /// screen's own keys answer it while nothing is open over it.</summary>
    internal bool Route(Key key)
    {
        if (_scopes.Top is not null && _scopes.Handle(key) != KeyResult.Pass)
            return true;
        if (Dialog.IsRunning && !Dialog.IsModal)
            return false;
        if (key == GuideKey)
        {
            _showGuide(GuideDialog.Teams);
            return true;
        }
        if (key == Key.F1)
            return DialogHost.ShowOver(Dialog, Sheet, ShowHelp);
        if (key == Key.Enter.WithCtrl && RefusesLimits())
            return true;
        return Dialog.Scope.Handle(key) != KeyResult.Pass;
    }

    /// <summary>Runs the screen, keeping every page's changes only if it was kept.</summary>
    public static void Show(
        IApplication app,
        DashboardSettings settings,
        CommandRegistry commands,
        KeyOverrides keys,
        Action<IconStyle> showIcons,
        IconStyle auto,
        Action<string>? showGuide = null,
        MacKeys? macKeys = null)
    {
        var icons = new IconSetting(settings.ReadIcons(), showIcons, settings.WriteIcons);
        using var screen = new SettingsScreen(ThemeSetting.Live(settings), icons, settings, commands, () => app.LayoutAndDraw(true), auto,
            showGuide, macKeys is null ? null : new TerminalKeys(macKeys), keys);
        EventHandler<Key> route = (_, key) => key.Handled = key.Handled || screen.Route(key);
        app.Keyboard.KeyDown += route;
        try
        {
            app.Run(screen.Dialog);
        }
        finally
        {
            app.Keyboard.KeyDown -= route;
        }
        if (!screen.Confirmed)
            screen.Revert();
    }

    internal void Revert()
    {
        foreach (var page in Dialog.Pages)
            page.Revert();
    }

    public void Dispose() => Dialog.Dispose();

    /// <summary>Opens Overseer on the limits it can't read, and marks them; false when they all read.</summary>
    private bool RefusesLimits()
    {
        if (Overseer.Unreadable() is null)
            return false;
        Dialog.Open(Dialog.Pages.ToList().IndexOf(Overseer));
        return Overseer.Refuse();
    }

    private void Say(string message, Schemes scheme) =>
        Dialog.ShowAlert(message, scheme == Schemes.Error ? Severity.Error : Severity.Info);
}
