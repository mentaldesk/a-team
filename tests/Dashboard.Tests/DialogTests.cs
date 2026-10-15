using MentalDesk.Tui.Dialogs;
using MentalDesk.Tui.Keys;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;

namespace ATeam.Dashboard.Tests;

/// <summary>What every dialog's tests ask of it: the sheet F1 shows.</summary>
internal static class DialogTests
{
    /// <summary>Presses F1 in <paramref name="dialog"/>, and returns the sheet it shows.</summary>
    internal static KeySheet F1(AppDialog dialog)
    {
        KeySheet? shown = null;
        EventHandler<KeySheet> show = (_, sheet) => DialogHost.ShowKeys(dialog, sheet, keys => shown = keys);
        dialog.KeysRequested += show;
        try
        {
            Assert.True(dialog.NewKeyDownEvent(Key.F1));
        }
        finally
        {
            dialog.KeysRequested -= show;
        }
        Assert.NotNull(shown);
        Assert.Null(shown.Everywhere);
        return shown;
    }

    /// <summary>An app with <paramref name="dialog"/> on it, which the arrows need to choose an option as they move.</summary>
    internal static IApplication Begin(IRunnable dialog)
    {
        var app = Application.Create().Init(DriverRegistry.Names.ANSI);
        app.Begin(dialog);
        return app;
    }

    internal static List<(string Keys, string Label)> Rows(KeySheet sheet) =>
        [.. sheet.Here!.Groups.SelectMany(group => group.Rows).Select(row => (row.Keys, row.Label))];
}
