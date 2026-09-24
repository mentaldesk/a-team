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

## Install

```
brew install mentaldesk/tap/a-team
```

Claude Code must be installed and logged in, and `gh` needs the `project` scope
(`gh auth refresh -s project`). Then add a team (see *Starting a team*) and run
`a-team install` to start the dispatcher. `brew upgrade a-team` moves every team to the latest
release.

## Layout

| Path | What it is |
|---|---|
| `process.md` | The shared rules: board states, gates, markers, what agents never do |
| `roles/lead.md`, `roles/dev.md` | What each role does on a run |
| `bin/a-team` | The one command: `a-team board`, `dispatch`, `install`, `status`, `pause`, `dashboard`, `run` |
| `scripts/board.sh` | `a-team board`: the only way agents touch the board; enforces who may move what |
| `scripts/run.sh` | `a-team run`: prints the brief a run starts from |
| `scripts/dispatch.sh` | `a-team dispatch`: starts a role's session when it has work (installed by `a-team install`) |
| `scripts/status.sh` | `a-team status`: what each role is doing and how its last run went |
| `scripts/pause.sh` | `a-team pause` / `a-team resume`: turns a team's dispatch off and on |
| `dashboard/` | `a-team`: the app, with a Work area and the agent Dashboard |
| `tasks/<role>.md` | The prompt a run starts with, including what the role is authorised to do |
| `settings/agents.json` | Permission rules for every run |
| `examples/team.json` | A starting point for a team's config (see *Starting a team*) |

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
3. Copy `examples/team.json` to `~/.config/a-team/teams/<name>.json` and fill it in: the repo,
   your GitHub login as `reviewer`, the Project, and where the product is checked out on this
   machine (`workdir` for its worktrees, `checkout` for its main clone). `a-team teams` lists
   the teams it finds. Check the board with
   `a-team board <name> check`.
4. Set `dispatch.enabled` to `true` when you want the dispatcher to run the team.
5. Install the dispatcher, first in dry-run mode, which only logs what it would start:

   ```
   a-team install --dry-run
   a-team status
   ```

   When its decisions look right, `a-team install` runs it for real, and
   `a-team install --uninstall` removes it.

Team configs are yours, not part of a-team: they live in `~/.config/a-team/teams/`
(`$A_TEAM_CONFIG/teams/` to use another folder). To version them or share them across machines,
keep that folder in a repo of your own and link it into place.

## How runs are started

- **Triggers** (`a-team board <team> triggers <role>`) list what a role has to react to. Reactive work
  starts within a couple of minutes. Pitching and discovering happen at most every
  `dispatch.creativeEvery` minutes, except that the Lead pitches straight away when nothing is
  Pitched or Exploring, so the reviewer always has a pitch to decide on.
- **Feedback is never too old to start a run.** The two-minute check reads the last day of
  comments repo-wide, so every `dispatch.sweepEvery` minutes (30 by default) a role's own items
  are read in full instead, however old the comments on them are. The gap is elapsed time, not
  passes, so a Mac that was off all weekend sweeps on its first pass: feedback left on Friday
  starts a run on Monday.
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

## The app

```
a-team                      the area you were last in
a-team dashboard [team...]  straight to the agents
```

The app has two areas: **Work**, everything waiting on you across every team, and **Dashboard**,
what each agent is doing. `d` and `w` switch between them, Esc goes back to the Dashboard, and the
menu across the top — F10, then the arrows — carries the same commands: View (Dashboard, Work,
Settings, Quit), Team (Pause or Resume) and Help (Keys, Commands, About). Whichever area you were
in is what it opens in next time; a first run lands on Work.

### What's waiting on you

A swimlane per team, and two columns in each: **Pitches** (waiting for you to approve) and
**Review** (waiting for you to merge or accept), each headed by its count. Enter opens the selected
card's issue in your browser, and `p` the PR that closes it — on a card with no open PR it says so
instead.

A gated column doesn't mean it's your turn: a card that's your move wears a green check, one an
agent owes you an answer on a dimmed headset and names that role in front of its title, and the
line at the foot says why — `#118 · lead · answering your feedback since 08:14`. It's theirs from
the moment you comment — on the card's own issue or on the PR that closes it — until they answer,
which is the same test the dispatcher makes when it decides what to start a run for. A PR that's
failing CI, conflicting with its base or still a draft is theirs too, and the card names which —
`#124  dev · CI failing · A finished task` — so you never open one to find CI still running on it.
`m` hides everything that isn't your move and `m` again brings it
back; the counts follow, the right of that same line says which you're looking at — *All items* or
*My items* — and it opens the way you left it.

The two icons are Nerd Font glyphs. No terminal reports its font, so if yours draws them as boxes,
turn *Nerd Font icons on the cards* off under Settings → Dashboard and they become `✓` and `·`.

The card list is read when the area opens and when you press `r`, never on a timer, so an app left
open overnight costs nothing against the rate limit the agents share; the header says how long ago
it read. A read that fails — offline, rate-limited — says so at the foot in red and leaves the
cards and that stamp exactly as they were.

### Watching the team

One pane per agent. The title shows whether it's running (●) or paused (⏸), and when it's neither,
how its last run went: ✓ clean, ✗ failed, ○ never run. A failed run draws the pane's border, title
and status row in the error colour until the next run clears it. Under the title: how long the
current run has been going, or when it last ran and the countdown to the dispatcher's next check;
then why it was last started; then its latest session as it happens (what it said, the
tools it called, any errors and how the run finished). The strip along the bottom is the
dispatcher's recent decisions. With no arguments it shows every configured team, paused or not.

Tab or the arrow keys select an agent (▶). Enter expands it over the whole agent area, wide
enough to read a session without scrolling, and there Tab and Shift+Tab read the next and previous
agent without leaving the expanded view; the dispatcher strip stays put. PgUp/PgDn/Home/End scroll
the selected session in either view; scrolling up stops it following new output until you press End. A run of tool calls draws as one row so the
agent's narration isn't pushed off the top; t shows every call in the selected pane again (the
title says [tool calls]) and t again folds them back up. s opens Settings, a page at a time: the
pages down the left, the one picked on the right, Tab into it and Tab back. Theme is one of
Midnight, Daylight, Turbo Pascal or Modern Borland, the same four as TuiCode; Keyboard Shortcuts
is a row per command with the key that runs it; Dashboard is whether panes start with every tool
call showing, and whether the Work area's cards wear Nerd Font icons. Enter on a shortcut row takes
the next key you press; a key another command already holds is refused, naming the one that holds
it. Ctrl+Enter keeps what's picked on any page, Esc
discards it. What you keep is written to `~/.config/a-team/dashboard.json` (`$A_TEAM_CONFIG/dashboard.json`) and is
what the dashboard comes up in next time, with t still folding and unfolding a pane for the
session. Esc goes back to the grid from an expanded agent and does nothing when there isn't one;
q quits the app, from either area.

Keys can be set by hand in that file too, which is the way out of a key your terminal or
multiplexer swallows. A `keys` object maps a command's id — the ones Ctrl+E lists — to a key
name, spelled as Terminal.Gui spells it (`PageUp`, not the `PgUp` the hint bar abbreviates it to):

```json
{ "theme": "Midnight", "keys": { "settings": "Ctrl+,", "log.toolCalls": "d" } }
```

A command named there is reached by that key instead of the one it ships with, and only by that
key. An id nothing is registered under, a name that isn't a key, and a key another command already
holds are each ignored on their own: that command keeps the key it had, and the rest of the file
still applies.

Ctrl+E opens Commands: everything the dashboard can do, with the key bound to it. Type to narrow
the list, Up/Down (or PgUp/PgDn and Home/End) to pick, Enter to run it, Esc to close. Every key
above is one of those commands, so anything you can press you can also run by name. Among them is
Pause (or Resume) for the selected agent's team, named after it: it runs `a-team pause` and the
panes follow within a second. While it runs, a line at the foot of the window says so; if it
fails, that line says why, in red.

F1 opens Keys: the handful worth having in your fingers, the first of them Ctrl+E for everything
else. Esc closes it.

Releases include a native build. Run from a clone, it's built from source and needs the
.NET 10 SDK.

## Trying out a clone or worktree

`bin/a-team` runs the code next to it, so any clone or worktree can be tried out while the
installed dispatcher keeps running the teams:

```
./bin/a-team dashboard
./bin/a-team board tuicode --dry-run move dev 160 "In progress"
A_TEAM_STATE=/tmp/a-team-test ./bin/a-team dispatch --dry-run
```

Give a clone's dispatcher its own `A_TEAM_STATE`, or it shares state with the real one.
`a-team install` asks before replacing a dispatcher installed from somewhere else, so trying a
clone can't take over the real one by accident. When a
dispatcher starts an agent, it puts its own `bin` first on the agent's `PATH` and fills in the
permission rules from its own location, so the agent uses that same copy of a-team.
