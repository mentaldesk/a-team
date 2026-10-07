# How a team works

A team is two agents working on one GitHub repository, and you. You can add a third, the
[Customer lead](#the-customer-lead). They coordinate through that repo's
Project board: if it isn't on the board, in an issue or in a PR, the team doesn't know about it.

## Who does what

- **You describe the project.** The Lead judges every idea against the repo's README and its vision
  document (`docs/vision.md` unless the team's settings say otherwise). The clearer they are about
  who it's for and where it's headed, in the short and the long term, the better the team builds it.
  Name the products you'd like it to learn from, competitors and inspiration alike: the Lead watches
  what they ship. Say what it deliberately isn't, too. If there's no vision yet, the Lead drafts one
  from the README and the code for you to edit and merge.
- **The Lead** finds work worth doing: gaps against the vision, open issues, and what those other
  products are doing. It writes a pitch for an Idea, with a mockup, revises it on your feedback,
  breaks an approved pitch into tasks, and checks the finished feature against the pitch.
- **The Dev** builds those tasks. It takes a Ready task, builds it in its own worktree with tests,
  opens a pull request and sees it through CI and your review.
- **You decide** which of the Lead's pitches get built, and you accept the finished work. Only you
  approve, merge or close.

Neither agent runs all the time. A dispatcher checks every couple of minutes whether a role has
something to do, like your feedback, an approved pitch or failing CI, and starts a run for it when it
does. An idle team costs nothing.

## The board

Every item on the board has a status, and moves through them left to right:

```
Idea → Exploring → Pitched → Approved → Building ───────────→ In review → Done
                                          └─ tasks: Ready → In progress → In review → Done
```

| Status | What it means |
|---|---|
| Idea | A problem or opportunity worth a look |
| Exploring | The Lead has drafted a pitch, waiting for room in Pitched |
| Pitched | The pitch is in front of you |
| Approved | You agreed; the Lead breaks it into tasks |
| Building | Its tasks are in flight |
| Ready | A task the Dev can pick up |
| In progress | The Dev is building it |
| In review | A PR, or a finished pitch, waiting for you |
| Done | Merged or accepted |

A pitch is an issue with the `pitch` label, and its tasks are its sub-issues. A task is one pull
request that changes something you can see or do.

## Where you decide

Work waits for you in two places, and no agent moves it on without you.

- **Pitched.** You approve a pitch, or comment on it to have it changed. Nothing gets built until
  you approve.
- **In review.** You merge a task's PR, or accept a finished pitch once the Lead has validated it.
  Nothing is done until you say so.

Both are in the [Work](work.md) area, where you can answer them without leaving the app.

## Talking to the team

Comment on the issue or the PR. The agents read your comments on their next run and reply there.

The 👀 reaction on your comment means a run has read it. Leave that one to the agents: every other
reaction is yours. A comment you make while a run is going gets a run of its own, so nothing you say
is missed.

When the Dev can't go on without you, it hands its task back with a question and the `blocked` label.
The Lead does the same on a pitch. Both wait in **Questions** in the Work area until you answer.

## The Customer lead

The Customer lead makes sure what the team ships is documented, so someone who didn't watch it being
built can find it and learn it. It's off until you turn it on: tick **Customer lead** under **Roles**
in the team's settings ([Teams](teams.md#a-teams-settings)), and its pane joins the team's on the
Dashboard.

- **When you accept a pitch**, it reads what the pitch shipped and checks the user docs cover it:
  the README, guides and in-app help written as text.
- **If they don't**, it writes what's missing into one docs PR, `Docs: what's changed since <date>`,
  whose description lists each change in a line. While that PR is open, the next accepted pitch adds
  to it rather than opening another.
- **If they already do**, it changes nothing.

Its PR waits in Review in the [Work](work.md) area, marked as the Customer lead's. Accept it like a
task, and the next change starts a new one. It never changes code, pitches or tasks.

## Ideas and priorities

You steer the Lead with the Priority field on the board: Low, Medium, High or Urgent.

- **Seed an idea** by opening an issue in the product repo and putting it on the board in Idea.
- **The Lead pitches the highest-priority Idea**, and between pitches it discovers new ones. Its own
  discoveries carry the `a-team:idea` label.
- **An Idea any agent wrote is only pitched once you give it a priority.** Until then it waits in
  Triage. Close the ones you don't want.
- **Only a few pitches are in front of you at once,** highest priority first. A higher-priority
  draft takes the place of a lower-priority pitch, which waits in Exploring for room again.

The Dev also takes the highest-priority Ready task first.
