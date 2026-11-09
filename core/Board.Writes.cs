using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ATeam.Core;

/// <summary>What the commands that write need: a task's PR, the attachments branch, a body's sections, and the team's worktrees.</summary>
internal sealed partial class Board
{
    internal string PriorityName => _priority;

    internal List<string> ContributesTo =>
        (_config["contributesTo"] as JsonArray ?? []).Select(repo => Json.Text(repo) ?? repo?.ToJsonString() ?? "null").ToList();

    /// <summary>#n where `$(item n)` went on regardless: what isn't a number says so, and isn't on the board.</summary>
    internal JsonNode? Seek(string n)
    {
        if (long.TryParse(n, out _)) return Item(n);
        Sync();
        Console.Error.Write("jq: Invalid JSON text passed to --argjson\n");
        return null;
    }

    /// <summary>A path's last part, as basename gives it.</summary>
    internal static string FileName(string file) => Path.GetFileName(file.TrimEnd('/'));

    /// <summary>What's on #n now, or a refusal once gh has had its say.</summary>
    internal string IssueBody(string n)
    {
        var read = Through(_gh.Run("api", $"repos/{_repo}/issues/{n}", "--jq", ".body // \"\""));
        if (!read.Ok) throw new Die($"can't read #{n}");
        return read.Said;
    }

    internal void SetPriority(string n, string value)
    {
        if (Would($"set {_priority} on #{n} to '{value}'")) return;
        _record.Exec($"UPDATE board SET priority = {(value == "none" ? "NULL" : Reconciler.Q(value))} WHERE team = {TeamQ} AND item = {n};");
    }

    /// <summary>The time a stakeholder spent on #n, or on nothing in particular for `-`.</summary>
    internal void RecordSpent(string n, string at, string seconds, string gate, string what)
    {
        if (Would($"record {seconds}s of yours on {(n == "-" ? "other" : n)}")) return;
        _record.Exec($"INSERT INTO spent (team, item, at, seconds, gate, what) VALUES ({TeamQ}, {(n == "-" ? "NULL" : n)}, {Reconciler.Q(at)}, {seconds}, {Reconciler.Q(gate)}, {Reconciler.Q(what)});");
    }

    /// <summary>PR <paramref name="pr"/>'s state, draft, body, labels and comments, and whether it's the docs PR; null when it isn't a PR.</summary>
    internal JsonObject? TaskPr(string pr)
    {
        var (owner, name) = RepoParts;
        var read = _gh.Run("api", "graphql", "-F", $"owner={owner}", "-F", $"name={name}", "-F", $"n={pr}", "-f", """
            query=
                query($owner: String!, $name: String!, $n: Int!) { repository(owner: $owner, name: $name) {
                  pullRequest(number: $n) { state isDraft body labels(first: 20) { nodes { name } }
                    comments(last: 100) { nodes { body author { __typename login } } } } } }
            """);
        var customer = Seek(pr)?["marks"]?["customer"];
        if (Json.TryParse(read.Out)?["data"]?["repository"]?["pullRequest"] is not JsonObject found) return null;
        var result = found.DeepClone().AsObject();
        result["customer"] = Json.Truthy(customer) ? customer!.DeepClone() : false;
        return result;
    }

    /// <summary>The Dev's own open task PR: not a pitch, and not the Customer lead's docs.</summary>
    internal static bool DevTaskPr(JsonNode pr) =>
        Json.Text(pr["state"]) == "OPEN" && (Json.Text(pr["body"]) ?? "").Contains("<!-- a-team:dev -->", StringComparison.Ordinal)
        && pr["labels"]?["nodes"] is JsonArray labels && !labels.Any(label => Json.Text(label?["name"]) == "pitch")
        && !Json.Truthy(pr["customer"]);

    /// <summary>The first issue a Dev PR's body closes, or null.</summary>
    internal static string? FirstClosed(JsonNode pr) =>
        Closes(pr).Select(n => n.ToString(CultureInfo.InvariantCulture)).FirstOrDefault();

    /// <summary>The Reviewer has posted its one review on this PR.</summary>
    internal bool Reviewed(JsonNode pr)
    {
        var bot = Bot;
        return pr["comments"]?["nodes"]?.AsArray().Any(comment => Said(comment, bot, "reviewer")) == true;
    }

    internal string AttachmentLink(string path) => $"https://github.com/{_repo}/blob/{AttachmentBranch}/{path}?raw=true";

    /// <summary>Where a file goes on the attachments branch, named by its content, once it's an image of a size GitHub takes.</summary>
    internal static string ImagePath(string file)
    {
        if (!File.Exists(file)) throw new Die($"no such file: {file}");
        var ext = file[(file.LastIndexOf('.') + 1)..].ToLowerInvariant();
        if (ext == "jpeg") ext = "jpg";
        if (!Regex.IsMatch(ext, $"^({AttachmentTypes})\\z")) throw new Die($"{FileName(file)} isn't a .png, .jpg, .gif or .webp");
        if (new FileInfo(file).Length > 10 * 1024 * 1024) throw new Die($"{FileName(file)} is over 10 MB");
        var hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)))[..16];
        return $"{DateTime.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture)}/{hash}.{ext}";
    }

    /// <summary>Stores a file as that path on the attachments branch, unless it's there already.</summary>
    internal void KeepImage(string file, string path)
    {
        if (_gh.Run("api", $"repos/{_repo}/contents/{path}?ref={AttachmentBranch}", "--silent").Ok) return;
        if (!Would($"commit {FileName(file)} to {path} on {AttachmentBranch}") && !StoreAttachment(file, path))
            throw new Die($"can't store {FileName(file)} on {_repo}'s {AttachmentBranch} branch");
    }

    private GhResult GitPost(string kind, JsonObject body)
    {
        var input = Path.GetTempFileName();
        try
        {
            File.WriteAllText(input, Json.Pretty(body) + "\n");
            return Through(_gh.Run("api", "-X", "POST", $"repos/{_repo}/git/{kind}", "--input", input, "--jq", ".sha"));
        }
        finally
        {
            File.Delete(input);
        }
    }

    /// <summary>Commits a file as that path on the attachments branch, starting the branch the first time. A ref that moved on under it is
    /// read again and tried once more.</summary>
    private bool StoreAttachment(string file, string path)
    {
        var blob = GitPost("blobs", new JsonObject { ["content"] = Convert.ToBase64String(File.ReadAllBytes(file)), ["encoding"] = "base64" }).Said;
        if (blob.Length == 0) return false;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var read = _gh.Run("api", $"repos/{_repo}/git/ref/heads/{AttachmentBranch}", "--jq", ".object.sha");
            var head = read.Ok ? read.Said : "";
            var tree = new JsonObject { ["tree"] = new JsonArray(new JsonObject { ["path"] = path, ["mode"] = "100644", ["type"] = "blob", ["sha"] = blob }) };
            var parents = new JsonArray();
            if (head.Length > 0)
            {
                var from = Through(_gh.Run("api", $"repos/{_repo}/git/commits/{head}", "--jq", ".tree.sha"));
                if (!from.Ok) return false;
                if (from.Said.Length > 0) tree["base_tree"] = from.Said;
                parents.Add((JsonNode?)head);
            }
            var madeTree = GitPost("trees", tree);
            if (!madeTree.Ok) return false;
            var commit = GitPost("commits", new JsonObject { ["message"] = $"Attach {path}", ["tree"] = madeTree.Said, ["parents"] = parents });
            if (!commit.Ok) return false;
            var moved = head.Length == 0
                ? _gh.Run("api", "-X", "POST", $"repos/{_repo}/git/refs", "-f", $"ref=refs/heads/{AttachmentBranch}", "-f", $"sha={commit.Said}", "--silent")
                : _gh.Run("api", "-X", "PATCH", $"repos/{_repo}/git/refs/heads/{AttachmentBranch}", "-f", $"sha={commit.Said}", "-F", "force=false", "--silent");
            if (Through(moved).Ok) return true;
        }
        return false;
    }

    /// <summary>A pitch's body with <paramref name="section"/> above its text mockup, replacing any there; null with no mockup. Headings in code blocks don't count.</summary>
    internal static string? WithPitchPictures(string body, string section)
    {
        var heading = section.Split('\n')[0];
        var lines = body.Split('\n');
        var fenced = NeedsAnswer.Fenced(lines);
        var kept = new List<string>();
        var skipping = false;
        for (var at = 0; at < lines.Length; at++)
        {
            if (!fenced[at] && lines[at].TrimEnd() == heading)
            {
                skipping = true;
                continue;
            }
            if (skipping && !fenced[at] && (IsMockupHeading(lines[at]) || Regex.IsMatch(lines[at], "^#{1,2}[ \\t]|^---+[ \\t]*\r?$|<!-- a-team:")))
                skipping = false;
            if (!skipping) kept.Add(lines[at]);
        }
        var inCode = NeedsAnswer.Fenced([.. kept]);
        var mockup = Enumerable.Range(0, kept.Count).Where(at => !inCode[at] && IsMockupHeading(kept[at]))
            .DefaultIfEmpty(-1).First();
        if (mockup < 0) return null;
        var above = mockup > 0 && kept[mockup - 1].TrimEnd().Length > 0 ? new[] { "", section, "" } : [section, ""];
        kept.InsertRange(mockup, above);
        return string.Join('\n', kept);
    }

    private static bool IsMockupHeading(string line) => Regex.IsMatch(line, @"^#{1,3}[ \t]+Mockups?\b", RegexOptions.IgnoreCase);

    /// <summary>A PR body with the section <paramref name="section"/> heads swapped for that section, just above the `a-team try` line and the marker.</summary>
    internal static string WithSection(string body, string section)
    {
        var heading = section.Split('\n')[0];
        var output = new StringBuilder();
        var (blanks, seam, wrote, skipping, placed) = (0, false, false, false, false);
        void Emit(string text)
        {
            if (!seam) output.Append('\n', blanks);
            else if (wrote) output.Append('\n');
            (blanks, seam, wrote) = (0, false, true);
            output.Append(text).Append('\n');
        }
        foreach (var line in body.Split('\n'))
        {
            if (line.TrimEnd(' ', '\t', '\r', '\f', '\v') == heading)
            {
                (skipping, seam) = (true, true);
                continue;
            }
            if (skipping && (Regex.IsMatch(line, "^#{1,2} ") || line.StartsWith("a-team try ", StringComparison.Ordinal) || line.StartsWith("<!-- a-team:", StringComparison.Ordinal)))
                skipping = false;
            if (skipping) continue;
            if (line.Length == 0)
            {
                blanks++;
                continue;
            }
            if (!placed && (line.StartsWith("a-team try ", StringComparison.Ordinal) || line.StartsWith("<!-- a-team:dev -->", StringComparison.Ordinal)))
            {
                seam = true;
                Emit(section);
                (seam, placed) = (true, true);
            }
            Emit(line);
        }
        if (!placed)
        {
            seam = true;
            Emit(section);
        }
        return output.ToString().TrimEnd('\n');
    }

    /// <summary>`git`, found on PATH as a shell finds it, and what it said.</summary>
    private static GhResult Git(params string[] args)
    {
        if (Gh.Which("git") is not { } git) return new GhResult(127, "", "git: command not found\n");
        var start = new ProcessStartInfo(git)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new Die("couldn't start git");
        var err = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return new GhResult(process.ExitCode, output, err.Result);
    }

    /// <summary>The linked worktrees on a branch, from `git worktree list --porcelain`. The first listed is the clone itself, and one with no
    /// branch is nobody's PR. A tab in a path is left alone: nothing can say which worktree that line means.</summary>
    internal static List<(string Tree, string Head, string Branch)> Worktrees(string porcelain)
    {
        var found = new List<(string, string, string)>();
        var (listed, tree, head) = (0, "", "");
        foreach (var line in porcelain.Split('\n'))
        {
            if (line.StartsWith("worktree ", StringComparison.Ordinal))
            {
                listed++;
                tree = line["worktree ".Length..];
            }
            else if (line.StartsWith("HEAD ", StringComparison.Ordinal))
            {
                head = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1) ?? "";
            }
            else if (line.StartsWith("branch refs/heads/", StringComparison.Ordinal) && listed > 1 && !tree.Contains('\t'))
            {
                found.Add((tree, head, line["branch refs/heads/".Length..]));
            }
        }
        return found;
    }

    /// <summary>A branch's newest PR from this repo, from `gh pr list`: a branch name can be reused, so only that one speaks for it.</summary>
    internal static JsonNode? NewestOwnPr(string prs)
    {
        if (prs.Trim().Length == 0) return null;
        JsonNode? newest = null;
        foreach (var pr in Json.Parse(prs)!.AsArray())
            if (!Json.Truthy(pr?["isCrossRepository"]) && (newest is null || Json.Long(pr?["number"]) >= Json.Long(newest["number"])))
                newest = pr;
        return newest;
    }

    private static bool SpeaksFor(JsonNode pr, string role) =>
        (Json.Text(pr["body"]) ?? "").Contains($"<!-- a-team:{role} -->", StringComparison.Ordinal)
        || (role == "dev" && Json.Text(pr["author"]?["login"]) == "app/dependabot");

    /// <summary>Removes each of the role's worktrees of a clone that's finished, and says why it left the others. How many it spoke of.</summary>
    internal int CleanWorktrees(string role, string clone, string repo)
    {
        var itself = Libc.RealPath(clone);
        var cleaned = 0;
        foreach (var (tree, head, branch) in Worktrees(Through(Git("-C", clone, "worktree", "list", "--porcelain")).Out))
            if (Directory.Exists(tree) && tree != itself)
                cleaned += CleanWorktree(role, clone, repo, tree, head, branch);
        return cleaned;
    }

    /// <summary>Only a worktree at its branch's newest PR's last commit, merged, with no edits, is what that PR left behind. 1 if it spoke of this one.</summary>
    private int CleanWorktree(string role, string clone, string repo, string tree, string head, string branch)
    {
        var home = Environment.GetEnvironmentVariable("HOME") ?? "";
        var where = tree.StartsWith(home, StringComparison.Ordinal) ? $"~{tree[home.Length..]}" : tree;
        int Left(string why)
        {
            Say($"Left {where}: {why}");
            return 1;
        }
        static string Short(string sha) => sha[..Math.Min(7, sha.Length)];
        if (role == "customer" && branch is not ("docs/customer-lead" or "docs/customer-lead-proposal")) return 0;
        var prs = _gh.Run("pr", "list", "-R", repo, "--head", branch, "--state", "all", "--json", "number,state,headRefOid,body,author,isCrossRepository");
        if (!prs.Ok) return Left($"couldn't read the PRs on {branch} ({Refusal(prs)}).");
        var pr = NewestOwnPr(prs.Out);
        if (pr is null) return role == "customer" ? Left($"there's no PR on {branch}.") : 0;
        if (!SpeaksFor(pr, role)) return 0;
        var number = pr["number"]?.ToJsonString() ?? "null";
        var last = Json.Text(pr["headRefOid"]) ?? "null";
        var state = Json.Text(pr["state"]);
        if (state == "OPEN") return Left($"#{number} is open.");
        if (state != "MERGED") return Left($"#{number} was closed without merging.");
        if (head != last) return Left($"its HEAD {Short(head)} isn't #{number}'s last commit ({Short(last)}).");
        var status = Through(Git("-C", tree, "status", "--porcelain", "--untracked-files=no"));
        if (!status.Ok) return Left("git couldn't read its changes.");
        if (status.Said.Length > 0) return Left($"it has {status.Said.Split('\n').Length} changed tracked file(s).");
        if ($"{Libc.RealPath(".")}/".StartsWith($"{tree}/", StringComparison.Ordinal)) return Left("this shell is in it.");
        if (!Would($"remove the worktree {where}") && !GitSays("-C", clone, "worktree", "remove", "--force", tree))
            return Left("git wouldn't remove it.");
        Say($"Removed {where}: #{number} has merged.");
        if (!Would($"delete the branch {branch}") && !GitSays("-C", clone, "branch", "-qD", branch))
            Say($"Couldn't delete its branch {branch}.");
        return 1;
    }

    /// <summary>A change git makes, with what it says passed on: whether it made it.</summary>
    private static bool GitSays(params string[] args)
    {
        var result = Through(Git(args));
        Console.Out.Write(result.Out);
        return result.Ok;
    }
}
