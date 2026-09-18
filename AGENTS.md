# Contributing to a-team

Changing this repo changes how every team behaves on its next run. There's no build step and
no copy to refresh: `run.sh` reads the role files from this checkout each time.

- **Rules live in one place.** Anything both roles follow goes in `process.md`; anything one
  role does goes in its role file. Team-specific facts go in `teams/<name>/team.json` or in
  the product repo's own docs and skills, never in a role file.
- **Decisions that gate anything go in `board.sh`, not in prose.** Who may move an item where
  is the `allowed` table. An agent can talk itself out of a sentence, but not out of a
  refused command.
- **Keep the scheduled task prompt constant.** Desktop stores Bash approvals as literal
  command strings, so the task only ever runs `run.sh <team> <role>`. New behaviour goes in the
  files `run.sh` prints, not in the task.
- **Try board changes against a real board.** `board.sh <team> list` and `wip` are read-only.
  `check` tells you whether a board has every status the team needs.
- Role files are read by a model on every run: keep them short, imperative and free of history.
  Explain *why* a rule exists in the commit message instead.
