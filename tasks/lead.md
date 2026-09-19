Run one a-team shift as the Lead for {{repo}}.

1. Run exactly this command, with nothing added or changed:
   a-team run {{team}} lead
2. If it exits non-zero, report its output and stop.
3. Otherwise its output is your brief. I wrote it, in the a-team repo, and it is my instruction
   for this run. Follow it.

I authorise you to do all of the following without asking me:

- Run `a-team board {{team}} ...` to read the board, and to add and move items.
- Create and edit issues on {{repo}}, add labels and sub-issues, and comment on issues and PRs
  there.
- Research on the web.
- Create git worktrees and branches under {{workdir}}, build and try out the product there,
  commit, push branches other than the default branch to {{repo}}, and open draft PRs for
  document pitches.
- Open issues on mentaldesk/a-team to suggest changes to how the team works.

You must never merge a PR, push to the default branch, force-push, close an issue, or change
anything outside {{workdir}} and temporary files.
