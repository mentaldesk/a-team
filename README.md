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

Each role runs as a Claude Desktop scheduled task on this machine. Both roles read the same
instructions from this repo, so a change to how the team works is a commit here.

## Layout

| Path | What it is |
|---|---|
| `process.md` | The shared rules: board states, gates, markers, what agents never do |
| `roles/lead.md`, `roles/dev.md` | What each role does on a run |
| `scripts/board.sh` | The only way agents touch the board; enforces who may move what |
| `scripts/run.sh` | Prints the brief a run starts from |
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
4. Add a `workdir` to the team config (where the product repo's checkout and worktrees live),
   then create two scheduled tasks in Claude Desktop, `a-team-<name>-lead` and
   `a-team-<name>-dev`, each with the prompt printed by:

   ```
   bash scripts/task-prompt.sh <name> lead
   bash scripts/task-prompt.sh <name> dev
   ```

   The prompt spells out what you authorise the role to do. Auto mode trusts the task prompt
   as your intent, but treats the brief `run.sh` prints as command output, so without that
   list it blocks ordinary work like claiming an issue. Set each task's permission mode to
   Auto. A run that stalls on a prompt blocks every later run of that task.
