namespace ATeam.Dashboard;

public enum WriteKind
{
    Accept,
    Approve,
    Rank,
    Decline,
    Shelve,
    HoldTask,
    Release,
    VisionReview,
    Hold,
    Interrupt,
}

/// <summary>One board write: what it runs, what the status bar says while it runs, what it says when you act on the
/// same thing again, and how the quit dialog names it queued. A second write with the same <paramref name="Key"/>
/// collides with it. <paramref name="Scratch"/> is a file it reads, deleted once it's done with.</summary>
public sealed record Write(
    WriteKind Kind, string Key, string[] Arguments, string Doing, string Already, string Queued,
    WaitingItem? Item = null, Rank Rank = Rank.None, string Team = "", string? Scratch = null, int Weeks = 0)
{
    public static string KeyOf(WaitingItem item) => $"{item.Team}#{item.Number}";
}

/// <summary>The dashboard's board writes, run one at a time in the order they were made, so nothing waits on them.
/// It outlives a window, so a handover doesn't drop what's queued.</summary>
public sealed class WriteQueue(Func<string[], Task<string?>> run)
{
    private readonly Queue<Write> _queued = [];
    private (Write Write, Task<string?> Task)? _running;

    public Write? Running => _running?.Write;

    public IReadOnlyList<Write> Queued => [.. _queued];

    /// <summary>The write under way, for whoever is quitting to wait on.</summary>
    public Task? InFlight => _running?.Task;

    /// <summary>The write already in hand for <paramref name="key"/>, running or queued.</summary>
    public Write? Holding(string key) =>
        Running is { } running && running.Key == key ? running : _queued.FirstOrDefault(write => write.Key == key);

    public bool Holds(WaitingItem item) => Holding(Write.KeyOf(item)) is not null;

    /// <summary>Starts <paramref name="write"/>, or queues it behind the one under way.</summary>
    public void Add(Write write)
    {
        _queued.Enqueue(write);
        if (_running is null)
            Next();
    }

    /// <summary>The writes that have finished since the last call, each with why the board refused it or null, and
    /// starts the next.</summary>
    public IReadOnlyList<(Write Write, string? Failure)> Settle()
    {
        List<(Write, string?)> done = [];
        while (_running is { Task.IsCompleted: true } finished)
        {
            done.Add((finished.Write, Failure(finished.Task)));
            Discard(finished.Write);
            _running = null;
            Next();
        }
        return done;
    }

    /// <summary>Drops every write that hasn't started, leaving the one under way to finish.</summary>
    public IReadOnlyList<Write> Drop()
    {
        var dropped = Queued;
        _queued.Clear();
        foreach (var write in dropped)
            Discard(write);
        return dropped;
    }

    /// <summary>What the status bar says: the write under way, then how many wait behind it.</summary>
    public string Status() => Running is not { } running ? ""
        : _queued.Count == 0 ? running.Doing
        : $"{running.Doing} · {_queued.Count} more queued";

    private void Next()
    {
        if (!_queued.TryDequeue(out var write))
            return;
        Task<string?> task;
        try
        {
            task = run(write.Arguments);
        }
        catch (Exception e)
        {
            task = Task.FromException<string?>(e);
        }
        _running = (write, task);
    }

    private static void Discard(Write write)
    {
        if (write.Scratch is { } file)
            File.Delete(file);
    }

    private static string? Failure(Task<string?> task) => task.Status == TaskStatus.RanToCompletion
        ? task.Result
        : task.Exception?.GetBaseException().Message ?? "the command didn't finish";
}
