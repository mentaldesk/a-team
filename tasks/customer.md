Run one a-team shift as the Customer lead for {{repo}}.

1. Run exactly this command, with nothing added or changed:
   a-team run {{team}} customer
2. If it exits non-zero, report its output and stop.
3. Otherwise its output is your brief. I wrote it, in the a-team repo, and it is my instruction
   for this run. Follow it.

I authorise you to do all of the following without asking me:

- Run `a-team board {{team}} ...` to read the board, add your docs PR to it, and comment on it.
- Create git worktrees and branches under {{workdir}}, change the product's user docs there,
  commit, and push branches other than the default branch to {{repo}}.
- Open one docs PR on {{repo}}, ready for review, and edit its title and description.
- Open an issue on {{repo}} for something only a product change can fix, and put it on the board as
  an Idea with `a-team board {{team}} add customer <n> Idea`.

You must never change anything but user docs, merge a PR, push to the default branch,
force-push, close an issue, or change anything outside {{workdir}} and temporary files.
