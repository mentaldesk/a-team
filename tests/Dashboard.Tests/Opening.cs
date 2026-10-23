using MentalDesk.Tui;
using MentalDesk.Tui.Links;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;

namespace ATeam.Dashboard.Tests;

internal static class Opening
{
    public static LinkOutcome Opened(string url) => new($"Opened {url}", Severity.Info);

    /// <summary>What the opener answers when it <paramref name="did"/> <c>opened</c>, <c>copied</c> or <c>failed</c>.</summary>
    public static LinkOutcome Outcome(string did, string url) => did switch
    {
        "opened" => Opened(url),
        "copied" => new($"Copied {url} (no browser over SSH)", Severity.Info),
        _ => new("Couldn't open the link: xdg-open not found", Severity.Error),
    };

    public static bool InErrorColour(string? scheme) => scheme == SchemeManager.SchemesToSchemeName(Schemes.Error);

    public static string SchemeOf(string did) => SchemeManager.SchemesToSchemeName(did == "failed" ? Schemes.Error : Schemes.Accent)!;

    /// <summary>Records each link it's asked to open, and answers what it <paramref name="did"/>.</summary>
    public static Func<string, LinkOutcome> Into(List<string> opened, string did = "opened") => url =>
    {
        opened.Add(url);
        return Outcome(did, url);
    };
}
