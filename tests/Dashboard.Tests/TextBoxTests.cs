using System.Text.RegularExpressions;

namespace ATeam.Dashboard.Tests;

public class TextBoxTests
{
    private static string DashboardSource
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "dashboard", "Dashboard.csproj")))
                    return Path.Combine(dir.FullName, "dashboard");
            throw new DirectoryNotFoundException("dashboard");
        }
    }

    [Fact]
    public void Every_text_box_is_a_library_field_so_none_falls_back_to_the_terminal_cursor()
    {
        var unpainted = Directory.EnumerateFiles(DashboardSource, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(file => File.ReadLines(file).Select(line => (File: Path.GetFileName(file), Line: line)))
            .Where(source => Regex.IsMatch(source.Line, @"\b(TextField|TextView)\b|\bCanEdit\s*=\s*true\b"))
            .Select(source => $"{source.File}: {source.Line.Trim()}");

        Assert.Empty(unpainted);
    }
}
