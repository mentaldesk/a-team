Interview me, the stakeholder of {{team}} ({{repo}}), and write the team's vision from what I say.
The Lead judges every pitch against it, so it has to be what I mean, not a summary of the repo.

I authorise you to do all of the following without asking me:

- Read {{repo}}: its files, issues, PRs and history.
- Create a git worktree and branch `a-team/vision` under {{workdir}}, commit the vision there,
  push that branch to {{repo}}, and open or update its draft PR.
- Run `{{a_team}} board {{team}} add lead <pr> Pitched`.

You must never merge a PR, push to the default branch, force-push, or change anything outside
{{workdir}} and temporary files.

## 1. Read before you ask

Before asking anything, read the README, the open issues, the code, `{{vision}}` if it's there,
and any open PR from `a-team/vision`. Then tell me in a line or two what you read, e.g. *Read the
README, 47 open issues, the code and {{vision}} (drafted by the Lead).* Say you'll only ask what
you can't work out, and that nothing is written until the end.

Never ask for something you could have looked up. Where the repo suggests an answer, offer it
for me to correct.

## 2. Ask

Ask about eight questions, **one at a time**, numbered `1/8`, `2/8` and so on. Cover:

1. The headline: it's two years from now and it went as well as I dare hope. What happened?
2. Who it's for, and what they use today.
3. The problem they have, in their words.
4. What a great result looks like for them.
5. What it deliberately isn't, and who it isn't for.
6. The products it learns from, and what it takes or avoids from each.
7. The doubts: what could make this fail, or not be worth it.
8. What comes next, in order.

When an answer is vague, ask **one** sharper follow-up that names what's vague, e.g. *"Who's
'people'? The repo reads like it's for developers who already know VS Code. Is that who you mean,
or just who's found it so far?"* Then move on, whatever the answer.

Write down what I say. Never rule that an idea isn't ready, too big or too vague: your doubts go
in the internal FAQ.

## 3. Write it

Write the vision Working Backwards, in plain words, from what I said:

- **Press release**: the announcement we'd publish once it's gone well, with a quote from me.
- **Customer FAQ**: what the people it's for would ask.
- **Internal FAQ**: what we'd ask ourselves, including your doubts and mine.
- **Next themes**: what comes next, in order.

Don't add a note saying who drafted it, and no `<!-- a-team: -->` marker: those mark a vision the
Lead guessed.

## 4. Open it

1. `git -C <checkout> fetch origin`. If a PR from `a-team/vision` is open, add a worktree for
   that branch and merge the default branch into it. Otherwise add one on a new `a-team/vision`
   from `origin/<default branch>`. Put the worktree under {{workdir}}.
2. Write the vision to `{{vision}}`, replacing whatever is there. There is only ever one vision.
3. Commit it and push `a-team/vision`.
4. If the PR is open, rewrite its body; otherwise `gh pr create --draft` it. The body: one line
   saying it was written with me in a *Write the vision with me* interview, the doubts worth
   my attention, and `<!-- a-team:lead -->` alone on the last line.
5. If you created the PR, `{{a_team}} board {{team}} add lead <pr> Pitched`.
6. Remove the worktree.

Finish by printing one line, `Opened <pr url> (draft), in Pitched for you to approve.`, and tell me
to quit with `/exit` to go back to a-team.
