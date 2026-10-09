# Contributing to a-team

Teams run an installed release (`brew install mentaldesk/tap/a-team`), so merged changes reach
them only once they're released (see *Releasing*) and upgraded with `brew upgrade a-team`. The
installed copy is read afresh on every run, so an upgrade takes effect on the next run with
nothing to restart.

- **Every change goes through a pull request. Nobody pushes to `main`**: people and agents alike.
  Work in a worktree branched from a fresh `origin/main`, and try it out with that worktree's own
  `./bin/a-team` (README, *Trying out a clone or worktree*):

  ```
  git -C <your clone> fetch origin
  git -C <your clone> worktree add <your clone>.worktrees/<slug> -b <branch> origin/main
  ```

- **Rules live in one place.** Anything both roles follow goes in `process.md`; anything one
  role does goes in its role file. Team-specific facts go in the team's config (see `examples/team.json`) or in
  the product repo's own docs and skills, never in a role file.
- **Decisions that gate anything go in `board.sh`, not in prose.** Who may move an item where
  is the `allowed` table. An agent can talk itself out of a sentence, but not out of a
  refused command.
- **Anything a role is allowed to do goes in its task prompt (`tasks/<role>.md`).** Auto mode
  treats the prompt as the reviewer's intent and the brief `run.sh` prints as command output,
  so authorisation that only lives in a role file doesn't count.
- **Hard limits go in `settings/agents.json` as deny rules**, or in a role's own
  `settings/<role>.json`, not only in prose.
- **Keep triggers cheap and deterministic.** `a-team board <team> triggers` runs every 2 minutes per role:
  no per-item API calls where one call for the whole repo will do.
- **Try board changes against a real board, without changing it.** `list`, `wip`, `triggers`
  and `check` only read. For commands that write, `a-team board --dry-run <team> ...` (or
  `A_TEAM_DRY_RUN=1`) does every read and every permission check for real, and prints each
  change it would have made instead of making it. New writes to GitHub go through `write` so
  dry-run covers them.
- Role files are read by a model on every run: keep them short, imperative and free of history.
  Explain *why* a rule exists in the commit message instead.

## Layout

| Path | What it is |
|---|---|
| `process.md` | The shared rules: board states, gates, markers, what agents never do |
| `roles/lead.md`, `roles/dev.md`, `roles/customer.md`, `roles/reviewer.md` | What each role does on a run; the Customer lead and the Reviewer only run where `roles.customer` and `roles.reviewer` turn them on |
| `bin/a-team` | The one command: `a-team board`, `dispatch`, `install`, `status`, `pause`, `dashboard`, `run` |
| `bin/gh` | `gh` as the team's App inside a run |
| `bin/git`, `scripts/credential.sh` | `git` committing and pushing as the team's App inside a run |
| `scripts/board.sh` | `a-team board`: the only way agents touch the board; enforces who may move what |
| `scripts/run.sh` | `a-team run`: prints the brief a run starts from |
| `scripts/dispatch.sh` | `a-team dispatch`: starts a role's session when it has work (installed by `a-team install`) |
| `scripts/status.sh` | `a-team status`: what each role is doing and how its last run went |
| `scripts/pause.sh` | `a-team pause` / `a-team resume` / `a-team stop`: turns a team's dispatch off and on, or holds one role |
| `scripts/attach.sh` | `a-team attach`: stops and holds a role, then resumes its last run's conversation |
| `dashboard/` | `a-team`: the app, with a Work area, Overseer and the agent Dashboard |
| `tasks/<role>.md` | The prompt a run starts with, including what the role is authorised to do |
| `settings/agents.json` | Permission rules for every run |
| `settings/<role>.json` | Rules one role's runs add to those |
| `examples/team.json` | A starting point for a team's config (see the README, *Starting a team*) |

## Dashboard UI

`dashboard/` is a Terminal.Gui 2.5 app, the same stack as TuiCode.

Styles and conventions for how we build TUI apps live in the [MentalDesk TUI style guide](https://github.com/mentaldesk/tui-style-guide). Read it before describing UI in an issue or PR, and before building one. 

Each rule is built once in the guide's shared library, `MentalDesk.Tui`, and shown working in its reference app, Swatch.
The dashboard references it as the [`MentalDesk.Tui`](https://www.nuget.org/packages/MentalDesk.Tui) package. Where the
library has a type, use it rather than a dashboard copy; a change it needs goes to the style guide first, as its own PR.
Dependabot opens a PR here for each new version.

The dashboard has three custom views, each for a reason the built-ins can't cover: `LogView`, because
`TextView` can't scroll without moving its cursor, `LoadingView`, because `SpinnerView` draws a
single line in one colour, and Overseer's chip grid, because no list or table draws each cell in a
colour of its own.

Help (F1) only gets someone started: the Commands palette key, how to open the menu, moving around,
and quitting. Don't add rows to it. A new command belongs in the menu or the Commands palette.

Tests live in `tests/Dashboard.Tests` (`dotnet test tests/Dashboard.Tests/Dashboard.Tests.csproj`),
outside `dashboard/` because `Dashboard.csproj` globs `**/*.cs`. They can't drive Terminal.Gui's
draw loop, so cover the logic behind the views instead.

## Scripts

`tests/scripts.sh` is the shell side's test suite (`bash tests/scripts.sh`). CI runs it, along with
`bash -n` and ShellCheck over `scripts/`, `bin/` and `tests/`.

## Releasing

Run the **Release** workflow from the Actions tab. Leave *bump* on `auto` to pick the version from
the labels of PRs merged since the last release (`breaking` for major, `enhancement` for minor,
otherwise patch), or choose one. It builds the dashboard for each platform, publishes the GitHub
release with generated notes, and opens a PR updating the formula in `mentaldesk/homebrew-tap`,
set to merge by itself once the tap's CI has installed and tested it on every platform. The tag is the version: nothing in the repo holds a
version number. PRs that touch the dashboard, packaging or the workflow run the build part to
check packaging.

Release publishes only from `main`. The tap token is a fine-grained personal access token with
access to `mentaldesk/homebrew-tap` only (Contents and Pull requests: read and write), stored as
`PACKAGES_TOKEN` in a `release` environment on this repo and on TuiCode. The environment lets only
`main` deploy, so a workflow on any other branch can't read the token. When it expires, releases
still publish but the formula step fails: regenerate it under your GitHub settings → Developer
settings → Fine-grained tokens, and update the environment secret in both repos.

### The shared workflows

`release.yml` owns only the build matrix; the rest is two `workflow_call` workflows any repo can
call, and this repo's release is their first caller. A caller keeps its own build jobs and passes
the artifacts it uploaded.

**`release-version.yml`** — the next version.

| | |
|---|---|
| Inputs | `bump` (string, default `auto`): `auto`, `patch`, `minor` or `major`. |
| Outputs | `version` (no leading `v`; `0.0.0-pr<n>` on a pull request), `tag` (empty on a pull request). |
| Secrets | None. Uses the caller's `GITHUB_TOKEN`. |
| Permissions the caller must grant | `contents: read`, `pull-requests: read` (`auto` reads merged PR labels). |

**`release-publish.yml`** — the tag, the release, the tap PR and, optionally, the Scoop manifest.

| | |
|---|---|
| Inputs | `name` (package name), `version`, `tag`, `formula` (template path in the caller), `tap` (default `mentaldesk/homebrew-tap`), `artifacts` (artifact name pattern, default `*`), and — added after `v0.0.4` — `scoop` (Scoop manifest template path in the caller; omitted, no Scoop step runs) and `bucket` (default `mentaldesk/scoop-bucket`), and — added after `v0.1.2` — `environment` (an environment in the caller the job runs in; omitted, none). |
| Outputs | None. |
| Secrets | `packages-token` (optional): write access to `tap` and `bucket`. With `environment`, that environment's `PACKAGES_TOKEN` secret is used instead, and the caller passes `secrets: inherit`: it's the only way an environment's secrets reach a called workflow. Without either, the release still publishes and the packaging steps warn. |
| Permissions the caller must grant | `contents: write`. Permissions are not inherited, so the calling job declares them. |

Both templates are rendered from the artifacts, not from a list of platforms: `{{version}}`,
and `{{sha_<rid>}}` for every `<name>-<version>-<rid>.tar.gz` or `.zip` found, with the RID's
hyphens as underscores (`{{sha_osx_arm64}}`, `{{sha_win_x64}}`). A placeholder no artifact matched
fails the job. The template decides which platforms it mentions.

A caller that ships `.zip` platforms passes `scoop` as well, and gets `bucket/<name>.json` in the
bucket updated in the same run. That one is **committed straight to the bucket's default branch,
not opened as a PR**: the bucket has no CI to gate one. a-team passes no `scoop`, so none of it
runs for a-team's own release.

**Which release to pin.** Pin an exact release, never a branch or a moving tag: before v1.0.0
nothing here is promised across releases (*What callers can rely on*). The tables above describe
the newest release, so that's the one to take from
[Releases](https://github.com/mentaldesk/a-team/releases) for a new caller.

The example pins `v0.0.4` because that's its **floor**: the oldest release that declares every
input it passes. A later release adding an input raises the floor only for a caller that passes
it, so the example runs as copied and stays right without being bumped each release. Pin newer
freely; pin older only against that release's own copy of the file, e.g.
`git show v0.0.4:.github/workflows/release-publish.yml`. Three inputs sit above the example's
floor: `scoop` and `bucket`, added after `v0.0.4`, and `environment`, added after `v0.1.2`, each
first carried by the next release after it.

```yaml
jobs:
  version:
    uses: mentaldesk/a-team/.github/workflows/release-version.yml@v0.0.4
    permissions: { contents: read, pull-requests: read }
    with: { bump: "${{ inputs.bump || 'auto' }}" }

  build: ...                                   # the caller's own: platforms, signing, packaging

  publish:
    needs: [version, build]
    uses: mentaldesk/a-team/.github/workflows/release-publish.yml@v0.0.4
    permissions: { contents: write }
    with:
      name: tuicode
      version: ${{ needs.version.outputs.version }}
      tag: ${{ needs.version.outputs.tag }}
      formula: packaging/tuicode.rb
      # scoop: packaging/tuicode.json          # needs a newer pin than v0.0.4; omit it, no Scoop step runs
      # environment: release                   # needs a newer pin than v0.1.2; then `secrets: inherit` below
    secrets:
      packages-token: ${{ secrets.HOMEBREW_TAP_TOKEN }}
```

### What callers can rely on

**Before v1.0.0 there is no compatibility guarantee.** Inputs, outputs and secrets may change in
any release while the only callers are ones we control; a caller that breaks is fixed in its own
repo.

**From v1.0.0 the workflow contract is semver.** A change that would break a caller — renaming or
removing an input, output or secret, making an optional input required, or changing what a caller
must grant — bumps major. Anything a caller can ignore bumps minor or patch. So a caller may pin a
major and take any minor. Weigh every later change to these two files against that.

**The actions these workflows pin are ours to keep current.** A caller picks its own
`actions/upload-artifact`; it has no say in the `actions/checkout` and `actions/download-artifact`
that `release-version.yml` and `release-publish.yml` pin, so a deprecation warning or a behaviour
change from them is this repo's to fix, not the caller's. Bump them here when they drift, check
the new version's notes against what the publish job relies on (`pattern`, `merge-multiple`, and
the `dist/` layout the render step globs), and say in the PR that the download step is first
exercised for real by the next release.
