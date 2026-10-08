# a-team

A small team of Claude agents that works on one GitHub repository and coordinates through
that repo's Project board:

- **Lead** researches opportunities, pitches them to you with a mockup, revises them on your
  feedback, breaks approved pitches into tasks, and checks the finished feature.
- **Dev** picks up Ready tasks, builds each one in its own worktree with tests, opens a draft PR
  and sees it through CI and your review.
- **You** own the two gates: approving a pitch, and merging a task's PR.

```
Idea → Exploring → Pitched ⛔ → Approved → Building ──────────────────────────────────→ Done
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
release, whenever you like: a run already going finishes on the release it started on.

## Starting a team

In the app, open **Settings → Teams** and press `n`: the [guide](docs/guide/teams.md#starting-a-team)
takes it from there.

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
   Settings shows the same problems, and F12 (or *Repair*) in a team's form sets its board up again.
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
- **Dependency updates.** A Dependabot PR the Lead hasn't commented on starts a Lead run. It
  reads what the new version brings, files an Idea for each change the product should adopt (a
  style guide's new rule, say), and lists them on the PR. Merging the PR stays yours.
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
  the same way but lets a run already going finish. `a-team stop <team> dev <task>` ends only
  that task's run and holds only that task, until `a-team resume <team> dev <task>`.
- **Stepping into a run.** `a-team attach <team> <role>` does what `a-team stop` does, then runs
  `claude --resume` on the latest run's session in the team's `workdir`, so you pick up the
  conversation with everything it had worked out. Quitting lets the role start again. With a
  `<task>`, it does the same for that one Dev run.
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

The app has two areas: **Work**, everything waiting on you across every team, and **Dashboard**, what
each agent is doing. **Help → Guide** explains them inside the app, and the same pages are in [`docs/guide`](docs/guide/index.md).

Releases include a native build. Run from a clone, it's built from source and needs the
.NET 10 SDK.

## Trying out a pull request

`a-team try <team> <pr>` puts you in the product built from that PR, with nothing of yours at
risk, and takes it away again when you quit:

```
a-team try a-team 97     # PR #97
a-team try a-team        # main as it is now
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

To try what has landed, leave the PR out: `a-team try <team>` fetches the default branch and
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
