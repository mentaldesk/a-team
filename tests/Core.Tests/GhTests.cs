using Xunit;

namespace ATeam.Core.Tests;

public class GhTests
{
    [Fact]
    public void A_gh_that_exits_without_reading_its_input_gives_its_own_answer()
    {
        var bin = Directory.CreateTempSubdirectory("a-team-gh");
        var path = Environment.GetEnvironmentVariable("PATH");
        try
        {
            var gh = Path.Combine(bin.FullName, "gh");
            File.WriteAllText(gh, "#!/bin/sh\necho refused >&2\nexit 3\n");
            File.SetUnixFileMode(gh, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Environment.SetEnvironmentVariable("PATH", $"{bin.FullName}:{path}");
            for (var run = 0; run < 20; run++)
            {
                var result = new Gh(null).Feed(new string('x', 1 << 20), "issue", "comment");
                Assert.Equal(3, result.Code);
                Assert.Equal("refused\n", result.Err);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", path);
            bin.Delete(recursive: true);
        }
    }
}
