# Vision

The yardstick for what a-team builds next. If a proposal doesn't serve this, it doesn't ship,
however good it is on its own.

## Who it's for

A maintainer who owns a repo or two, has more ideas than hours, and wants agents to do the
shaping and building while they keep every decision that matters. They review on a phone,
between other things, and can't babysit a terminal session.

The first of those users is the maintainer, running a team on TuiCode and one on a-team itself.
Every time the team wastes their attention, loses their feedback, or spends money going in
circles, that's a legitimate priority.

## What it's trying to be

**A team you can leave running that turns your judgement into merged work, for a few minutes of
your attention a day.**

- **You own the gates.** Agents propose and build; only you approve a pitch, merge a PR or
  accept a feature. Nothing reaches `main` without you.
- **Gates are enforced by code, not by asking nicely.** Who may move what lives in `board.sh`
  and the deny rules. An agent can talk itself out of a sentence, not out of a refused command.
- **GitHub is the only memory.** The board, issues and PRs hold everything. Anything the next
  run needs to know is written where you can read it too.
- **Your attention is the scarce resource.** Pitches with options and a mockup you can say yes
  or no to; replies short enough for a phone; a few items in front of you at a time.
- **Slices of user value.** Every task ships something a user notices, so you judge results,
  not plumbing.
- **Cheap when idle, bounded when busy.** Checking for work is a plain script; a run starts only
  when there's something to do, and never loops on a problem it can't fix.
- **The process is code.** How the team works is a set of files in this repo, changed by PR and
  shipped as a release, so improvements are deliberate and reach every team at once.

## What it deliberately isn't

- **Not autonomous.** No auto-merge, no agent deciding what matters most. Agents may suggest;
  you prioritise.
- **Not a general agent framework.** One opinionated process with two roles, not a toolkit for
  wiring up your own.
- **Not a hosted service.** It runs on your machine (or one you control), with your Claude Code
  login and your GitHub access.
- **Not for large teams.** One reviewer per team. Several humans sharing gates is a different
  product.
- **Not a replacement for review.** The Lead's validation and the Dev's tests help you review;
  they don't stand in for it.

## Next themes

Roughly in order. Each is a direction, not a commitment; pitches turn them into work.

1. **Trust the loop.** Feedback is never lost or ignored and the team never spins: comments that
   arrive mid-run or age past a day (#3, #5), stuck triggers that retry forever (#4), a readyLow
   warning that counts blocked tasks (#23), and shelving a pitch by asking (#24, #26).
2. **Guardrails that don't depend on the model.** A daily spending cap and GitHub API headroom
   (#13), and a separate GitHub identity so GitHub itself enforces the gates and authorship
   says who wrote what (#6).
3. **See what the team is doing.** A dashboard that fits several teams (#12), earlier sessions
   (#2), and logs you can scan at a glance (#9).
4. **A second maintainer.** Starting a team should work for someone who isn't the author: setup
   that checks itself, a dispatcher beyond macOS launchd (#6), and release tooling the
   maintainer's other repos can share (#25).

## How to judge a proposal

- Does it save the reviewer attention, or spend it?
- Does it keep every gate in the reviewer's hands, enforced by a script rather than a sentence?
- Will the next run know about it from the board, an issue or a PR alone?
- Does it keep an idle team free and a busy one bounded?
- Is it the smallest version that's actually useful?
