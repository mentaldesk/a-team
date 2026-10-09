namespace ATeam.Dashboard.Tests;

/// <summary>Adding an idea: open the issue, put it on the board, rank it, and pick up where a failure left off.</summary>
public class IdeaFilerTests
{
    private static readonly IdeaDraft Draft = new("team0", "  Remember my lane  ", "It starts on the first one.", Rank.None);

    private sealed class Board
    {
        internal List<string[]> Calls { get; } = [];
        internal string? Opened { get; private set; }
        internal HashSet<string> Failing { get; } = [];

        internal Task<Reading> Run(string[] args)
        {
            Calls.Add(args);
            if (args[2] == "new")
                Opened = File.ReadAllText(args[4]);
            return Task.FromResult(Failing.Contains(args[2])
                ? new Reading("", $"{args[2]} refused")
                : new Reading(args[2] == "new" ? "77\n" : "", null));
        }

        internal IEnumerable<string> Steps => Calls.Select(call => call[2]);
    }

    [Fact]
    public async Task Priority_None_opens_the_issue_as_you_and_adds_it_as_an_Idea_with_no_rank()
    {
        var board = new Board();
        var filer = new IdeaFiler(board.Run);

        Assert.Null(await filer.Add(Draft));

        Assert.Equal(["new", "add"], board.Steps);
        Assert.Equal(["board", "team0", "new", "you"], board.Calls[0][..4]);
        Assert.Equal(["board", "team0", "add", "you", "77", "Idea"], board.Calls[1]);
        Assert.Equal("Remember my lane\nIt starts on the first one.", board.Opened);
        Assert.Equal(("team0", 77), filer.Opened);
    }

    [Theory]
    [InlineData(Rank.Low)]
    [InlineData(Rank.Medium)]
    [InlineData(Rank.High)]
    [InlineData(Rank.Urgent)]
    public async Task Any_other_Priority_ranks_it_once_it_is_on_the_board(Rank rank)
    {
        var board = new Board();

        Assert.Null(await new IdeaFiler(board.Run).Add(Draft with { Rank = rank }));

        Assert.Equal(["board", "team0", "priority", "you", "77", rank.ToString()], board.Calls[2]);
    }

    [Fact]
    public async Task An_issue_that_won_t_open_says_so_and_goes_no_further()
    {
        var board = new Board { Failing = { "new" } };
        var filer = new IdeaFiler(board.Run);

        Assert.Equal("couldn't open the issue: new refused", await filer.Add(Draft));
        Assert.Equal(["new"], board.Steps);
        Assert.Null(filer.Opened);
    }

    [Fact]
    public async Task A_board_that_won_t_take_it_says_so_and_a_retry_opens_no_second_issue()
    {
        var board = new Board { Failing = { "add" } };
        var filer = new IdeaFiler(board.Run);

        Assert.Equal("#77 is open, but couldn't go on team0's board: add refused", await filer.Add(Draft));
        board.Failing.Clear();
        Assert.Null(await filer.Add(Draft));

        Assert.Equal(["new", "add", "add"], board.Steps);
    }

    [Fact]
    public async Task A_rank_that_won_t_set_says_so_and_a_retry_only_ranks()
    {
        var board = new Board { Failing = { "priority" } };
        var filer = new IdeaFiler(board.Run);

        Assert.Equal("#77 is on the board, but couldn't be ranked: priority refused", await filer.Add(Draft with { Rank = Rank.High }));
        board.Failing.Clear();
        Assert.Null(await filer.Add(Draft with { Rank = Rank.High }));

        Assert.Equal(["new", "add", "priority", "priority"], board.Steps);
    }
}
