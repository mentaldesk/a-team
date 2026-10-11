namespace ATeam.Dashboard.Tests;

public class DocumentPitchTests : IDisposable
{
    private static readonly WaitingItem Item = new(60, "Vision review", "Pitched", "https://github.com/o/r/pull/60", "team0", Document: true);
    private static readonly DocumentPitch Vision = new(["docs/vision.md"], "docs/vision.md", "a-team/vision", "0123abc", "# Vision\n");
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 9, 30, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"a-team-{Guid.NewGuid():n}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void It_reads_the_one_file_a_document_pitch_changes()
    {
        var read = DocumentPitch.Parse("""
            {"number": 60, "files": ["docs/vision.md"], "path": "docs/vision.md", "ref": "a-team/vision", "sha": "0123abc", "text": "# Vision\n"}
            """);

        Assert.Equal(Vision with { Files = read!.Files }, read);
        Assert.Equal(["docs/vision.md"], read.Files);
        Assert.Null(read.Uneditable(60));
    }

    [Fact]
    public void One_that_changes_more_than_one_file_can_t_be_edited_and_points_to_GitHub()
    {
        var read = DocumentPitch.Parse("""{"number": 60, "files": ["docs/vision.md", "README.md"]}""");

        Assert.False(read!.Editable);
        Assert.Equal("#60 changes 2 files, so it can't be edited here: g opens it on GitHub", read.Uneditable(60));
    }

    [Fact]
    public void The_body_shows_the_file_after_the_pitch_s_own_words()
    {
        var body = new IssueBody("## Why now\n\nThemes ran dry.\n").With(Vision);

        Assert.Equal("## Why now\n\nThemes ran dry.\n\n---\n\n# docs/vision.md\n\n# Vision\n", body.Text);
        Assert.Same(Vision, body.Document);
    }

    [Fact]
    public void Quitting_without_saving_commits_nothing()
    {
        var handover = Handover();
        var commits = new List<string[]>();

        var back = handover.Open(_ => null, arguments => { commits.Add(arguments); return null; }, Kept, Now);

        Assert.Empty(commits);
        Assert.False(back.Committed);
        Assert.Equal("no change to docs/vision.md", back.Said);
        Assert.False(File.Exists(handover.File));
    }

    [Fact]
    public void Saved_edits_are_committed_as_you_from_the_blob_they_started_from()
    {
        var handover = Handover();
        var commits = new List<string[]>();

        var back = handover.Open(_ => { File.WriteAllText(handover.File, "# Vision, edited\n"); return null; },
            arguments => { commits.Add(arguments); return null; }, Kept, Now);

        Assert.Equal([["board", "team0", "edit", "you", "60", "docs/vision.md", "0123abc", handover.File]], commits);
        Assert.True(back.Committed);
        Assert.Equal("committed your edit to docs/vision.md", back.Said);
        Assert.Null(back.Failure);
        Assert.False(File.Exists(handover.File));
    }

    [Fact]
    public void A_refused_commit_keeps_your_edit_and_says_where()
    {
        var handover = Handover();

        var back = handover.Open(_ => { File.WriteAllText(handover.File, "# Vision, edited\n"); return null; },
            _ => "board.sh: docs/vision.md changed on GitHub while you were editing it", Kept, Now);

        var kept = Path.Combine(Kept, "team0-60-20261011093000-vision.md");
        Assert.Equal($"board.sh: docs/vision.md changed on GitHub while you were editing it: your edit is kept in {kept}", back.Failure);
        Assert.Equal("# Vision, edited\n", File.ReadAllText(kept));
        Assert.False(back.Committed);
    }

    [Fact]
    public void An_editor_that_won_t_start_commits_nothing_and_says_so()
    {
        var handover = Handover();
        var commits = new List<string[]>();

        var back = handover.Open(_ => "couldn't start vim", arguments => { commits.Add(arguments); return null; }, Kept, Now);

        Assert.Empty(commits);
        Assert.Equal("couldn't start vim", back.Failure);
    }

    [Fact]
    public void The_editor_gets_the_file_under_the_document_s_own_name()
    {
        var handover = Handover() with { Editor = ["code", "--wait"] };

        Assert.EndsWith("a-team-team0-60-vision.md", DocumentHandover.FileFor(Item, Vision));
        Assert.Equal(["--wait", handover.File], handover.Arguments);
        Assert.Equal(Area.Work, handover.Area);
    }

    private string Kept => Path.Combine(_root, "edits");

    private DocumentHandover Handover()
    {
        Directory.CreateDirectory(_root);
        var file = Path.Combine(_root, "vision.md");
        File.WriteAllText(file, Vision.Text);
        return new DocumentHandover(Item, Vision, file, ["vim"], false, [Item], null, new ReaderPlace(new IssueBody("## Why now"), Item.Url, 0));
    }
}
