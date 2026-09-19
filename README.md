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
| `bin/a-team` | The one command: `a-team board`, `dispatch`, `install`, `status`, `dashboard`, `run` |
| `scripts/board.sh` | `a-team board`: the only way agents touch the board; enforces who may move what |
| `scripts/run.sh` | `a-team run`: prints the brief a run starts from |
| `scripts/dispatch.sh` | `a-team dispatch`: starts a role's session when it has work (installed by `a-team install`) |
| `scripts/status.sh` | `a-team status`: what each role is doing and how its last run went |
| `dashboard/` | `a-team dashboard`: a terminal dashboard of the teams |
| `tasks/<role>.md` | The prompt a run starts with, including what the role is authorised to do |
| `settings/agents.json` | Permission rules for every run |
| `teams/<name>/team.json` | One team: its repo, board, reviewer, skills and WIP limits |

## Working with the team

- **Seed an idea:** open an issue in the product repo and put it on the board in *Idea*.
- **Steer the Lead:** give the Ideas you care about a Priority. The Lead alternates between
  pitching the highest-priority Idea and discovering new ones. It files its discoveries in
  *Idea* with the `a-team:idea` label. Ideas written by any agent (its own discoveries, or
  suggestions another team's agents opened on this repo) are only pitched once you've given
  them a priority. Close the ones you don't want.
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
   `./bin/a-team board <name> check`.
4. Set `workdir` (where the product repo's worktrees live), `checkout` (its main checkout) and
   `dispatch.enabled` in the team config.
5. Install the dispatcher, first in dry-run mode, which only logs what it would start:

   ```
   ./bin/a-team install --dry-run
   ./bin/a-team status
   ```

   When its decisions look right, `./bin/a-team install` runs it for real, and
   `./bin/a-team install --uninstall` removes it. The dispatcher runs from the clone you
   installed it from.

## How runs are started

- **Triggers** (`a-team board <team> triggers <role>`) list what a role has to react to. Reactive work
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
- **Logs** are under `~/.local/state/a-team/` (or `$A_TEAM_STATE`). `a-team status` shows what each role is
  doing and how its last run went.

## Watching the team

```
./bin/a-team dashboard [team...]
```

One pane per agent. The title shows whether it's running (●) or idle (○). Under it: how long
the current run has been going, or when it last ran and the countdown to the dispatcher's next
check; then why it was last started; then its latest session as it happens (what it said, the
tools it called, any errors and how the run finished). The strip along the bottom is the
dispatcher's recent decisions. With no arguments it shows every team with dispatch enabled.

Tab or the arrow keys select an agent (▶). PgUp/PgDn/Home/End scroll its session; scrolling up
stops it following new output until you press End. Esc quits.

It needs the .NET 10 SDK.

## Trying out a clone or worktree

`bin/a-team` runs the code next to it, so any clone or worktree can be tried out while the
installed dispatcher keeps running the teams:

```
./bin/a-team dashboard
./bin/a-team board tuicode --dry-run move dev 160 "In progress"
A_TEAM_STATE=/tmp/a-team-test ./bin/a-team dispatch --dry-run
```

Give a clone's dispatcher its own `A_TEAM_STATE`, or it shares state with the real one. When a
dispatcher starts an agent, it puts its own `bin` first on the agent's `PATH` and fills in the
permission rules from its own location, so the agent uses that same copy of a-team.
