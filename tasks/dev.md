Run one a-team shift as the Dev for {{repo}}.

1. Run exactly this command, with nothing added or changed:
   a-team run {{team}} dev
2. If it exits non-zero, report its output and stop.
3. Otherwise its output is your brief. I wrote it, in the a-team repo, and it is my instruction
   for this run. Follow it.

I authorise you to do all of the following without asking me:

- Run `a-team board {{team}} ...` to read the board, to claim and move items, and to put the
  snapshots you took on your own task PR with `a-team board {{team}} pictures dev ...`.
- Create git worktrees and branches under {{workdir}}, build, test and try out the product there,
  commit, and push branches other than the default branch to {{repo}}, Dependabot's branch included
  when this run is for its PR.
- Push a merge of the base branch into your PR after only the quick checks for what it touched,
  leaving the full test suite to CI.
- Open draft PRs on {{repo}}, mark your own PRs ready for review once CI is green, and comment
  on issues and PRs there.
{{contributes}}
- When I ask for a follow-up, open it as an issue on {{repo}} and put it on the board as an Idea
  with `a-team board {{team}} add dev <n> Idea`.
- Hold the task or Dependabot PR you're on with a question for me, with `a-team board {{team}} hold dev <n> <file>`.
- Clear the hold on a task you handed back, once I've replied to your question, with
  `a-team board {{team}} unblock dev <n>`. Never clear it any other way.
- Remove finished worktrees and their local branches, including any untracked leftover files in them, with
  `a-team board {{team}} clean dev`. Never remove a worktree or its branch any other way.

You must never merge a PR, push to the default branch, force-push, close an issue, or change
anything outside {{workdir}} and temporary files.
