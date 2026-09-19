# Contributing to a-team

Changing this repo changes how every team behaves on its next run. There's no build step and
no copy to refresh: `run.sh` reads the role files from the live checkout (the clone the
dispatcher was installed from) each time.

- **Every change goes through a pull request. Nobody pushes to `main`**: people and agents alike.
  Work in a worktree outside the live checkout, branched from a fresh `origin/main`:

  ```
  git -C <live checkout> fetch origin
  git -C <live checkout> worktree add <live checkout>.worktrees/<slug> -b <branch> origin/main
  ```

  Never edit the live checkout directly. It only changes by pulling merged work, and pulling is
  what puts a change live for every team, so do it deliberately.

- **Rules live in one place.** Anything both roles follow goes in `process.md`; anything one
  role does goes in its role file. Team-specific facts go in the team's config (see `examples/team.json`) or in
  the product repo's own docs and skills, never in a role file.
- **Decisions that gate anything go in `board.sh`, not in prose.** Who may move an item where
  is the `allowed` table. An agent can talk itself out of a sentence, but not out of a
  refused command.
- **Anything a role is allowed to do goes in its task prompt (`tasks/<role>.md`).** Auto mode
  treats the prompt as the reviewer's intent and the brief `run.sh` prints as command output,
  so authorisation that only lives in a role file doesn't count.
- **Hard limits go in `settings/agents.json` as deny rules**, not only in prose.
- **Keep triggers cheap and deterministic.** `a-team board <team> triggers` runs every 2 minutes per role:
  no per-item API calls where one call for the whole repo will do.
- **Try board changes against a real board, without changing it.** `list`, `wip`, `triggers`
  and `check` only read. For commands that write, `a-team board --dry-run <team> ...` (or
  `A_TEAM_DRY_RUN=1`) does every read and every permission check for real, and prints each
  change it would have made instead of making it. New writes to GitHub go through `write` so
  dry-run covers them.
- Role files are read by a model on every run: keep them short, imperative and free of history.
  Explain *why* a rule exists in the commit message instead.

## Dashboard UI

`dashboard/` is a Terminal.Gui 2.1 app, the same stack as TuiCode.

- Build UI from Terminal.Gui's [built-in views](https://tui-cs.github.io/Terminal.Gui/docs/views) before writing a
  custom one. `LogView` is custom only because `TextView` can't scroll without moving its cursor.
- Pick an appropriate control for the input (e.g. `CheckBox` for on/off, `OptionSelector<T>` for a limited number of choices,
  `FlagSelector<T>` for several on/off flags, `NumericUpDown<T>` (with a minimum and maximum) for a numbers,
  `DropDownList<T>` or `ListView` for longer lists, `TextField` for free text).
- When describing UI in an issue or PR, name the control for each element and sketch it in the mockup, e.g.
  `[x] Follow output`, `(•) All ( ) Running`, `Refresh: [ 2 ▲▼]s`.

## Releasing

Run the **Release** workflow from the Actions tab. Leave *bump* on `auto` to pick the version from
the labels of PRs merged since the last release (`breaking` for major, `enhancement` for minor,
otherwise patch), or choose one. It builds the dashboard for each platform, publishes the GitHub
release with generated notes, and opens a PR updating the formula in `mentaldesk/homebrew-tap`,
set to merge by itself once the tap's CI has installed and tested it on every platform. The tag is the version: nothing in the repo holds a
version number. PRs that touch the dashboard, packaging or the workflow run the build part to
check packaging.

`HOMEBREW_TAP_TOKEN` is a fine-grained personal access token with access to
`mentaldesk/homebrew-tap` only (Contents and Pull requests: read and write), stored as a secret on
this repo and on TuiCode. When it expires, releases still publish but the formula step fails:
regenerate it under your GitHub settings → Developer settings → Fine-grained tokens, and update
the secret in both repos.
