using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;

namespace ATeam.Dashboard.Tests;

public class LogStyleTests
{
    public static TheoryData<LogLineKind, string?, VisualRole> Styles => new()
    {
        { LogLineKind.SessionBoundary, null, VisualRole.HotNormal },
        { LogLineKind.Prose, null, VisualRole.Normal },
        { LogLineKind.ToolCall, null, VisualRole.Disabled },
        { LogLineKind.ToolError, SchemeManager.SchemesToSchemeName(Schemes.Error), VisualRole.Normal },
        { LogLineKind.ResultError, SchemeManager.SchemesToSchemeName(Schemes.Error), VisualRole.Normal },
        { LogLineKind.ResultOk, LogSchemes.Success, VisualRole.Normal },
    };

    [Theory]
    [MemberData(nameof(Styles))]
    public void Each_kind_draws_from_its_own_scheme_role(LogLineKind kind, string? scheme, VisualRole role) =>
        Assert.Equal(new LogStyle(scheme, role), LogStyle.For(kind));

    [Fact]
    public void Only_prose_is_drawn_as_prose()
    {
        var prose = LogStyle.For(LogLineKind.Prose);

        foreach (var kind in Enum.GetValues<LogLineKind>().Where(kind => kind != LogLineKind.Prose))
            Assert.NotEqual(prose, LogStyle.For(kind));
    }

    [Fact]
    public void An_unmapped_kind_is_refused_rather_than_drawn_as_prose() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => LogStyle.For((LogLineKind)99));

    [Fact]
    public void The_named_schemes_are_ones_the_scheme_manager_knows()
    {
        LogSchemes.Register();

        foreach (var name in Enum.GetValues<LogLineKind>().Select(kind => LogStyle.For(kind).Scheme).OfType<string>())
            Assert.True(SchemeManager.TryGetScheme(name, out _), name);
    }
}
