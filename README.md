# a-team

A small team of Claude agents that works on one GitHub repository and coordinates through
that repo's Project board:

- **Lead** researches opportunities, pitches them to you with a mockup, revises them on your
  feedback, breaks approved pitches into tasks, and validates the finished feature.
- **Dev** picks up Ready tasks, builds each one in its own worktree with tests, opens a draft PR
  and sees it through CI and your review.
- **You** own the two gates: approving a pitch, and merging a PR or accepting a finished pitch.

```
Idea → Exploring → Pitched ⛔ → Approved → Building ─────────────→ In review ⛔ → Done
                                              └─ tasks: Ready → In progress → In review ⛔ → Done
```

A dispatcher checks every 2 minutes whether a role has something to do: your feedback, an
approved pitch, a merged PR, failing CI, a free slot. When it does, it starts a headless Claude
Code session for that role. Checking is a plain script, so an idle team costs nothing. Both
roles read their instructions from this repo, so a change to how the team works is a commit
here.

## Layout

| Path | What it is |
|---|---|
| `process.md` | The shared rules: board states, gates, markers, what agents never do |
| `roles/lead.md`, `roles/dev.md` | What each role does on a run |
| `scripts/board.sh` | The only way agents touch the board; enforces who may move what |
| `scripts/run.sh` | Prints the brief a run starts from |
| `scripts/dispatch.sh` | Starts a role's session when it has work (installed by `install.sh`) |
| `scripts/status.sh` | What each role is doing and how its last run went |
| `scripts/update.sh` | Releases merged changes to every team (fast-forwards the live checkout) |
| `dashboard/` | A terminal dashboard of the team (`scripts/dashboard.sh`) |
| `tasks/<role>.md` | The prompt a run starts with, including what the role is authorised to do |
| `settings/agents.json` | Permission rules for every run |
| `teams/<name>/team.json` | One team: its repo, board, reviewer, skills and WIP limits |

## Working with the team

- **Seed an idea:** open an issue in the product repo and put it on the board in *Idea*.
- **Steer the Lead:** give the Ideas you care about a Priority. The Lead alternates between
  pitching the highest-priority Idea and discovering new ones. It files its discoveries in
  *Idea* with the `a-team:idea` label, and only pitches one once you've given it a priority.
  Close the ones you don't want.
- **Give feedback:** comment on the pitch or PR. The agents pick up your comments on their next
  run and reply.
- **Approve a pitch:** move it to *Approved*.
- **Accept work:** merge the PR, or close the pitch once you're happy with the Lead's validation.
- **Your inbox:** a board view filtered to `status:Pitched,"In review"`.

## Starting a team

1. Make sure `gh` can manage projects: `gh auth refresh -s project`.
2. Create or pick a Project for the repo. Its Status field needs the nine options in
   `process.md`, or a `statusMap` in the team config from those names to the ones it has.
3. Add `teams/<name>/team.json` (copy `teams/tuicode`) and check it with
   `bash scripts/board.sh <name> check`.
4. Set `workdir` (where the product repo's worktrees live), `checkout` (its main checkout) and
   `dispatch.enabled` in the team config.
5. Install the dispatcher, first in dry-run mode, which only logs what it would start:

   ```
   bash scripts/install.sh --dry-run
   bash scripts/status.sh
   ```

   When its decisions look right, `bash scripts/install.sh` runs it for real, and
   `bash scripts/install.sh --uninstall` removes it.

## How runs are started

- **Triggers** (`board.sh <team> triggers <role>`) list what a role has to react to. Reactive work
  starts within a couple of minutes. Pitching and discovering happen at most every
  `dispatch.creativeEvery` minutes.
- **One run per role at a time.** A run that's still going after `dispatch.maxRuntime` minutes
  is stopped. If the same triggers are still there after a run, the dispatcher waits
  `dispatch.retryAfter` minutes before trying again, so a problem the role can't fix doesn't
  start a run every 2 minutes.
- **Permissions** come from `settings/agents.json` and the task prompt in `tasks/`. Runs use
  auto mode, and anything that would ask for permission is refused rather than waiting for
  someone to answer. The deny rules (merging, closing issues, force-pushing, pushing to `main`,
  editing this repo) hold even if the model tries.
- **Logs** are under `~/.local/state/a-team/`. `scripts/status.sh` shows what each role is
  doing and how its last run went.

## Releasing changes

Teams run from the live checkout at `~/code/a-team`. Merging a PR doesn't change what they run;
releasing it does:

```
bash ~/code/a-team/scripts/update.sh
```

It shows the merged commits that aren't live yet and asks before fast-forwarding. It refuses if
the checkout isn't a clean `main`, and says if an agent is mid-run (that run finishes on the old
version). Agents are denied running it.

## Watching the team

```
bash scripts/dashboard.sh [team...]
```

One pane per agent. The title shows whether it's running (●) or idle (○). Under it: how long
the current run has been going, or when it last ran and the countdown to the dispatcher's next
check; then why it was last started; then its latest session as it happens (what it said, the
tools it called, any errors and how the run finished). The strip along the bottom is the
dispatcher's recent decisions. With no arguments it shows every team with dispatch enabled.

Tab or the arrow keys select an agent (▶). PgUp/PgDn/Home/End scroll its session; scrolling up
stops it following new output until you press End. Esc quits.

It needs the .NET 10 SDK.
