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
| `bin/gh` | `gh` as the team's App inside a run |
| `bin/git`, `scripts/credential.sh` | `git` committing and pushing as the team's App inside a run |
| `scripts/board.sh` | `a-team board`: the only way agents touch the board; enforces who may move what |
| `scripts/run.sh` | `a-team run`: prints the brief a run starts from |
| `scripts/dispatch.sh` | `a-team dispatch`: starts a role's session when it has work (installed by `a-team install`) |
| `scripts/status.sh` | `a-team status`: what each role is doing and how its last run went |
| `scripts/pause.sh` | `a-team pause` / `a-team resume` / `a-team stop`: turns a team's dispatch off and on, or holds one role |
| `scripts/attach.sh` | `a-team attach`: stops and holds a role, then resumes its last run's conversation |
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
  run and reply. A 👀 on your comment means a run has read it — so don't 👀 your own comments;
  every other reaction is yours to use. One you leave mid-run gets a run of its own.
- **Approve a pitch:** move it to *Approved*.
- **Accept work:** merge the PR, or close the pitch once you're happy with the Lead's validation.
- **Your inbox:** a board view filtered to `status:Pitched,"In review"`.

## Starting a team

In the app, open Settings → Teams and press `n` (or run **New team** from the palette). Fill in the
form and press `Enter`: a-team writes the team's file, paused, then offers in turn to clone the repo,
give the team its GitHub App, create its Project, set its board up and get it to work. Each step says
what it will do and waits for `Enter`; `Esc` skips it. The last asks whether to get to work or keep the
team paused, and there `Esc` cancels the new team. Make sure `gh` can manage projects first:
`gh auth refresh -s project`.

The app opens only once there's a team, so your first one starts by hand:

1. Make sure `gh` can manage projects: `gh auth refresh -s project`.
2. Create or pick a Project for the repo. Its Status field needs the nine options in
   `process.md`, or a `statusMap` in the team config from those names to the ones it has.
3. Copy `examples/team.json` to `~/.config/a-team/teams/<name>.json` and fill it in: the repo,
   your GitHub login in `stakeholders`, the Project, and where the product is checked out on this
   machine (`workdir` for its worktrees, `checkout` for its main clone), and optionally `try`,
   the command that runs the product from a worktree. Leave `app` as `null`: the next step fills
   it in. `a-team teams` lists the teams it finds. Check the team with
   `a-team board <name> check`: it prints one line per problem it finds, and exits 1 if the team
   can't run, or 2 if it can but something's missing, like its vision or a label. The Teams page in
   Settings shows the same problems, and `r repair` in a team's form sets its board up again.
4. Give the team its own GitHub App (next section). The dispatcher won't run the team without one.
5. Set `dispatch.enabled` to `true` when you want the dispatcher to run the team.
6. Install the dispatcher, first in dry-run mode, which only logs what it would start:

   ```
   a-team install --dry-run
   a-team status
   ```

   When its decisions look right, `a-team install` runs it for real, and
   `a-team install --uninstall` removes it.

Team configs are yours, not part of a-team: they live in `~/.config/a-team/teams/`
(`$A_TEAM_CONFIG/teams/` to use another folder). To version them or share them across machines,
keep that folder in a repo of your own and link it into place.

## Giving the team its own identity

A team needs a GitHub App of its own before it runs. Everything it writes — comments, issues,
PRs and commits — shows `<app>[bot]` as its author, which is how `a-team` tells your feedback from
the team's words, and a run's `gh` and `git push` reach only the repos you install the App on
instead of your whole account. Until the App is installed, the dispatcher skips the team and
`a-team status` says why.

```
a-team app create <team>
```

1. Your browser opens on GitHub's *Create GitHub App* page, filled in from a manifest with the
   permissions the team needs and no webhook. Change the name if you like (it must be unique
   across GitHub) and click **Create GitHub App**. One of the permissions is *Workflows*, so the
   team can change your CI too. A workflow can read your repo's secrets, so keep any that matter
   in an environment only your default branch can deploy to.
2. GitHub hands the App's private key back to `a-team`, which stores it in your login Keychain
   (service `a-team-app`, account `<owner>`, base64-encoded) — never on disk — and writes
   `app: { id, slug }` into the team's config.
3. The App's install page opens. Install it on the team's repo, then run
   `a-team board <team> check`: its `identity:` line says whether a token mints, the project reads
   and writes, the Priority field reads and the App can push, and what to grant if one can't.

One App serves every team under the same account. Run `app create` for a second team there and it
reuses the first team's App; install that App on the second repo too. If the key is in the Keychain
but no team config names the App, pass its ID from the App's settings page:
`a-team app create <team> --id <app id>`.

Runs mint an hour-long installation token when they need one (`a-team token <team>` prints it)
and use it for every `gh` command and every push, which never stops to ask for a password. A run's
commits are authored as `<app>[bot]`. Your own `gh` login and git config are untouched, so a
`git commit` you make by hand in the same worktree is still yours.

## How runs are started

- **Triggers** (`a-team board <team> triggers <role>`) list what a role has to react to. Reactive work
  starts within a couple of minutes. Pitching and discovering happen at most every
  `dispatch.creativeEvery` minutes, except that the Lead pitches straight away when nothing is
  Pitched or Exploring, so the stakeholder always has a pitch to decide on.
- **Feedback is never too old to start a run.** The two-minute check reads the last day of
  comments repo-wide, so every `dispatch.sweepEvery` minutes (30 by default) a role's own items
  are read in full instead, however old the comments on them are. The gap is elapsed time, not
  passes, so a Mac that was off all weekend sweeps on its first pass: feedback left on Friday
  starts a run on Monday.
- **One run per role at a time.** A run that's still going after `dispatch.maxRuntime` minutes
  is stopped. If the same triggers are still there after a run, the dispatcher waits
  `dispatch.retryAfter` minutes before trying again, so a problem the role can't fix doesn't
  start a run every 2 minutes.
- **Stopping a run.** `a-team stop <team> <role>` ends the role's live run and holds the role
  (`dispatch.hold` in the team config), so the dispatcher starts no replacement until
  `a-team resume <team> <role>`. It lists anything the run left claimed In progress; the next run
  picks that up. The other role carries on as normal. `a-team pause <team> <role>` holds the role
  the same way but lets a run already going finish.
- **Stepping into a run.** `a-team attach <team> <role>` does what `a-team stop` does, then runs
  `claude --resume` on the latest run's session in the team's `workdir`, so you pick up the
  conversation with everything it had worked out. Quitting lets the role start again.
- **Permissions** come from `settings/agents.json` and the task prompt in `tasks/`. Runs use
  auto mode, and anything that would ask for permission is refused rather than waiting for
  someone to answer. The deny rules (merging, closing issues, force-pushing, pushing to `main`,
  editing this repo) hold even if the model tries.
- **Logs** are under `~/.local/state/a-team/` (or `$A_TEAM_STATE`). `a-team status` shows what each role is
  doing and how its last run went. A dashboard that crashes says so in one line and leaves the whole
  of it in a `crash-<time>.log` there.

## The app

```
a-team                      the area you were last in
a-team dashboard [team...]  straight to the agents
```

The app has two areas: **Work**, everything waiting on you across every team, and **Dashboard**,
what each agent is doing. `d` and `w` switch between them, Esc goes back to the Dashboard, and the
menu across the top carries the same commands: View (Dashboard, Work, Settings, Quit), Agents
(Pause this role, Interrupt) and Help (Keys, Commands, About). Each title and item underlines a letter:
`Alt`+it opens a menu, and once one is open the bare letter picks from it — `Alt+H` `k` for Keys.
Esc closes it, and F10 and the arrows still work. Whichever area you were in is what it opens in
next time; a first run lands on Work.

### What's waiting on you

A swimlane per team, and four columns in each, left to right in the order work moves through
them: **Triage** (the Ideas and pitches with no Priority set, which never get pitched or approved
until you give them one), **Pitches** (waiting for you to approve), **Questions** (tasks the Dev
handed back to ask you something, and pitches the Lead can't go ahead with until you answer) and **Review** (waiting for you to merge or accept), each headed
by its count of items. A column with nothing in it is hidden, and the one you're on takes half its
lane. A card's issue number is coloured by the
item's Priority, in the colours GitHub gives that field's own options, and the PR that closes it
hangs under it as a row of its own — `PR #149  The session log reads…`. Enter shows the issue's
body as written, without leaving the app, or on a question just the question; `g` opens whichever row you're on in your browser: the
issue on the card, the PR on the row under it. Reading a pitch that's waiting on you, `a` approves
it: no confirmation, and the card leaves the column with nothing re-read. Reading every team's
gates takes a moment, so a van drives across the empty area, framed off from the rest, until the
first cards land; `r` afterwards leaves the ones on screen where they are.

`p` ranks the card you're on, or clears its rank with **None**, without leaving the app. It reads
the item first, so what you're ranking is in front of you: the issue's own words fill the dialog
above the ranks, as written, and `PgUp`/`PgDn` scroll them. The ranks sit in a band of their own
below, **None** at the left up to **Urgent** at the right, each on the letter its name starts with.
The card moves to the column its new Priority puts it in there and then, with nothing re-read: a
pitch you unrank drops into Triage, one you rank leaves it, and an Idea you rank is off your queue
for good.

A gated column doesn't mean it's your turn: a card that's your move wears a green check, one an
agent owes you an answer on a dimmed headset and names that role in front of its title, and the
line at the foot says why — `#118 · lead · answering your feedback since 08:14`. It's theirs from
the moment you comment — on the card's own issue or on the PR that closes it — until they answer,
which is the same test the dispatcher makes when it decides what to start a run for. A PR that's
failing CI, conflicting with its base, still running CI or still a draft is theirs too, and the card names which —
`#124  dev · CI failing · A finished task` — so you never open one to find CI still running on it.
`m` hides everything that isn't your move and `m` again brings it
back — an unranked Idea is always yours, so Triage keeps nearly all of it; the counts follow, the
right of that same line says which you're looking at — *All items* or *My items* — and it opens the
way you left it.

After that icon, a second says what the card is: `◇` a pitch, `‹›` a task. With Nerd Font icons
these are a presentation and a code review, and a PR row under a card wears a pull request glyph
in place of the word `PR`.

The turn icons come from the same vocabulary the panes use (`✓` and `·`, or their Nerd Font
glyphs). No terminal reports its font, so the dashboard uses the Nerd Font one in kitty, WezTerm
and Ghostty — which bundle such a font and fall back to it — and the plain one everywhere else. If
that's the wrong answer for your terminal, pick a vocabulary under Settings → Dashboard.

The card list is read when the area opens and when you press `r`, never on a timer, so an app left
open overnight costs nothing against the rate limit the agents share; the header says how long ago
it read. A read that fails — offline, rate-limited — says so at the foot in red and leaves the
cards and that stamp exactly as they were.

### Watching the team

One pane per agent. The title shows whether it's running (●) or paused (⏸), and when it's neither,
how its last run went: ✓ clean, ✗ failed, ○ never run — or the Nerd Font glyph for each, where
that's the vocabulary in effect. A failed run draws the pane's border, title
and status row in the error colour until the next run clears it. A role you've held with `a-team stop`
wears ⏸ and reads `stopped by you` (or how its last run went) `· held`. Under the title: how long the
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
call showing, and which icons the panes, their sessions and the cards wear — *Automatic*, *Nerd Font* or *Unicode*,
the last two drawn in their own glyphs so you pick the row that isn't boxes, previewing behind the
dialog as you move. *Automatic* names what it decided for the terminal you're in, and decides
again on every launch: a font belongs to a terminal, what's stored belongs to the machine. Teams is a row per
team with its repo and whether it's `working` or `paused`; p starts or pauses the selected one there
and then, and Ctrl+E → *Teams* opens Settings on that page. A team whose file doesn't parse reads
`can't read this file`, and selecting it names the line that's wrong. Enter on a team opens its
form — repo, project, vision, workdir, try, status and limits — and Enter there saves them into its
file, leaving every other key as it was. Enter
on a shortcut row takes the next key you press; a key another command already holds is refused,
naming the one that holds it. Ctrl+Enter keeps what's picked on any page, Esc
discards it. What you keep is written to `~/.config/a-team/dashboard.json` (`$A_TEAM_CONFIG/dashboard.json`) and is
what the dashboard comes up in next time, with t still folding and unfolding a pane for the
session. Esc goes back to the grid from an expanded agent and does nothing when there isn't one;
q quits the app, from either area.

Keys can be set by hand in that file too, which is the way out of a key your terminal or
multiplexer swallows. A `keys` object maps a command's id — the ones Ctrl+E lists — to a key
name, spelled as Terminal.Gui spells it (`PageUp`, not the `PgUp` Settings abbreviates it to):

```json
{ "theme": "Midnight", "keys": { "settings": "Ctrl+,", "log.toolCalls": "d" } }
```

A command named there is reached by that key instead of the one it ships with, and only by that
key. An id nothing is registered under, a name that isn't a key, and a key another command already
holds are each ignored on their own: that command keeps the key it had, and the rest of the file
still applies.

Ctrl+E opens Commands: everything the dashboard can do, with the key bound to it. Type to narrow
the list, Up/Down (or PgUp/PgDn and Home/End) to pick, Enter to run it, Esc to close. Every key
above is one of those commands, so anything you can press you can also run by name. `i` interrupts the selected agent's run: it runs `a-team attach` in
the dashboard's place, so you're in that run's conversation, and quitting brings you back to the
grid with the role free to start again. A run too new to have a session is just stopped and held,
as `a-team stop` does. On a held role the same command reads *Let it start again* and runs
`a-team resume <team> <role>`. `h` pauses the selected agent's role, as `a-team pause <team> <role>`
does: a run already going finishes, its pane reading `running <time> · held`, and no new one starts
until the same command, now *Let this role start again*, runs `a-team resume <team> <role>`. While
any of these runs, a line at the foot of the window says so; if it
fails, that line says why, in red.

F1 opens Keys: the handful worth having in your fingers, the first of them Ctrl+E for everything
else. Esc closes it.

Releases include a native build. Run from a clone, it's built from source and needs the
.NET 10 SDK.

## Trying out a pull request

`a-team try <team> <pr>` puts you in the product built from that PR, with nothing of yours at
risk, and takes it away again when you quit:

```
a-team try a-team 97     # PR #97
a-team try a-team        # main as it is now, to accept a finished pitch
```

It fetches the PR's head into a scratch worktree at `<workdir>/.try/<pr>`, exports
`A_TEAM_STATE=<workdir>/.try/state/<pr>` and `A_TEAM_DRY_RUN=1` — so the code you're trying reads
the real board and writes nothing to it, and can't touch your teams' state — and runs the team
config's **`try`** command there. That key is the one thing a-team can't work out for itself:
`"try": "./bin/a-team dashboard"` for this repo, `"dotnet run --project src/TuiCode"` for TuiCode.
With no `try` key you get a shell in the worktree instead, with `try:<team>#<pr>` in the prompt and
that PR's `a-team` first on your `PATH`; `Ctrl+D` comes back.

On the way out it removes the worktree and the sandbox state. If you changed a file in there it
keeps the worktree and tells you, and `a-team try <team> <pr> --clean` removes it when you're done.
`try` runs the PR's code on purpose, so it asks before running a branch from a repo that isn't the
team's own.

To accept a finished pitch, leave the PR out: `a-team try <team>` fetches the default branch and
runs it as it is on origin now, at `<workdir>/.try/main`, the same way. Its first lines name the
branch and commit, and `a-team try <team> --clean` removes it.

### Trying out a clone or worktree

`bin/a-team` runs the code next to it, so any clone or worktree can be tried out — this is what to
reach for when you want to keep the tree and work in it, rather than look and leave:

```
git -C ~/code/a-team/main fetch origin
git -C ~/code/a-team/main worktree add ../mine -b my-branch origin/main
cd ~/code/a-team/mine
A_TEAM_STATE=/tmp/a-team-test ./bin/a-team dashboard
```

Give a clone's dispatcher its own `A_TEAM_STATE`, or it shares state with the real one.
`a-team install` asks before replacing a dispatcher installed from somewhere else, so trying a
clone can't take over the real one by accident. When a
dispatcher starts an agent, it puts its own `bin` first on the agent's `PATH` and fills in the
permission rules from its own location, so the agent uses that same copy of a-team.
