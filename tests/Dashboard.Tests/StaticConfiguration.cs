using Terminal.Gui.Configuration;

namespace ATeam.Dashboard.Tests;

[CollectionDefinition("StaticConfiguration", DisableParallelization = true)]
public sealed class StaticConfigurationCollection;

/// <summary>Base for tests that touch Terminal.Gui's process-wide theme and scheme statics.</summary>
[Collection("StaticConfiguration")]
public abstract class StaticConfigurationTest : IDisposable
{
    private readonly string _theme = ThemeManager.Theme;

    public void Dispose()
    {
        ThemeManager.Theme = _theme;
        ConfigurationManager.Apply();
        GC.SuppressFinalize(this);
    }
}
