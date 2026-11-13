# How a team works

A team is two agents working on one GitHub repository, and you. You can add the
[Customer lead](#the-customer-lead) and the [Reviewer](#the-reviewer). They coordinate through the team's
board: if it isn't on the board, in an issue or in a PR, the team doesn't know about it. A-Team keeps the board on
this machine, and can copy it to a GitHub Project, its mirror, so you can see it on GitHub too.

## Who does what

- **You describe the project.** The Lead judges every idea against the repo's README and its vision
  document (`docs/vision.md` unless the team's settings say otherwise). The clearer they are about
  who it's for and where it's headed, in the short and the long term, the better the team builds it.
  Name the products you'd like it to learn from, competitors and inspiration alike: the Lead watches
  what they ship. Say what it deliberately isn't, too. [Write the vision with me](teams.md#writing-the-vision)
  writes it with you; if there's no vision yet, the Lead drafts one from the README and the code
  for you to edit and merge.
- **The Lead** finds work worth doing: gaps against the vision, open issues, and what those other
  products are doing. It writes a pitch for an Idea, with a mockup, revises it on your feedback,
  breaks an approved pitch into tasks, and checks the finished feature against the pitch.
- **The Dev** builds those tasks. It takes a Ready task, builds it in its own worktree with tests,
  opens a pull request and sees it through CI and your review. It says on the PR which acceptance
  criteria it saw working on screen or proved with a test, and the rest are
  [left for you](work.md#criteria). A later run removes that worktree once
  the PR has merged, if it's still at the PR's last commit with no tracked file changed. Any other
  worktree stays, and the run's summary says why. The Customer lead's are cleaned up the same way.
- **You decide** which of the Lead's pitches get built, and you accept the finished work. Only you
  approve or merge, and the Lead closes a pitch only once its tasks have all closed.

Neither agent runs all the time. A dispatcher checks every couple of minutes whether a role has
something to do, like your feedback, an approved pitch or failing CI, and starts a run for it when it
does. An idle team costs nothing.

## The board

Every item on the board has a status, and moves through them left to right:

```
Idea → Exploring → Pitched → Approved → Building ─────────────────────────────→ Done
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
| In progress | The Dev is building it, or another team's Dev is getting its change to this repo ready |
| In review | A PR waiting for you: a task's, the Customer lead's docs PR, or another team's Dev's change to this repo |
| Done | Merged or closed, or a pitch whose tasks have all closed |

A pitch is an issue with the `pitch` label, and its tasks are its sub-issues. The Lead also gives
it the vision theme it serves. A task is one
pull request that changes something you can see or do. A task that needs another merged first is
blocked by it on GitHub, and becomes ready to start by itself when that one closes.

A task that needs a change in another repo first, like a fix in a shared library, waits on that
repo's issue or PR instead. It waits either until that closes or until a
published release carries the change; a pre-release doesn't count. The board checks every couple
of minutes, and once the wait is over it frees the task and says so on it, naming the
release and the package to upgrade, so the Dev can pick it up with no click from you. An issue
closed by hand, with no change linked to it, counts as released in the first release after it
closed. If Dependabot already has a PR open bringing that release in, the task waits for it to
merge too, which [it does by itself](#dependency-updates). Until then
the task's card in **Blocked** says which link it waits on and whose move that is, though it asks
you nothing. If its PR is already up, its card stays in **Review** and says the same. Open its
card's details in the [Overseer](overseer.md) (`Enter`) to see how far the change has got, like
`Waiting on tui-style-guide#84: merged, not released`.

Whatever another task waits on comes first: the Dev takes a Ready task some task waits on before
any other, and the Lead pitches such an Idea before any other, whatever their Priority. Priority
still orders them among themselves, and nobody's Priority changes.

One kind of task has no pitch: a **fix**. When CI fails on the default branch, say after two PRs
that each passed broke it together, the Lead files one task, a fix, that makes it
green again. It goes straight to Ready, skipping Pitched since it only restores work you've already
accepted, and the Dev takes it before anything else. You still accept its PR. Meanwhile a PR failing
only what the branch fails isn't counted as the Dev's failure. [Work](work.md#when-main-is-red) shows
all of this on one line.

A proposal that is itself a document, like the vision or the Customer lead's docs proposal, is a
draft pull request in Pitched instead. You agree to it by merging it, with `a` in
[Work](work.md#answering-from-a-card), where `e` also edits it.

## Dependency updates

A Dependabot PR doesn't need you. The Lead reads what the update brings and files an Idea for any new
rule worth adopting, and the PR never waits on those. Once its CI passes and the Lead has commented, the
dispatcher squash-merges it and deletes its branch, on its next pass. Its history and the
[dispatcher's log](overseer.md#the-dispatchers-log) say so.

When its CI fails on jobs that pass on the default branch, the PR goes to the Dev, ahead of every Ready
task except a fix, and the Dev changes the code on the PR's own branch until it's green. When it fails
only what the default branch fails, it waits for the fix. It only reaches you if the Dev can't make it
build: it shows in [Blocked](work.md#columns) as a question, with what broke, what the Dev tried and what
it needs. Answer on the PR and it's back with the Dev; close the PR and it's gone.

A green PR reaches you the same way if GitHub keeps refusing to merge it, because of a rule on the repo, say,
with GitHub's reason. The dispatcher goes on trying, so changing what's stopping it is enough to merge it.

## Pictures in a pitch

Tick **Mockups** on a [team's form](teams.md#a-teams-settings) and a pitch that adds or changes a
screen shows it, built for real. Before the pitch goes up, the Lead builds the screen in a scratch
worktree with the real controls, the real theme and made-up rows, and takes one picture per screen.
They go in the pitch under **What it'll look like**, each captioned with what it shows, above the
text **Mockup**, which stays because it names each control for the Dev. Where the two disagree, the
picture is what you approved.

The scratch code is thrown away and never pushed, and the Dev starts from the pitch. A pitch that
changes no screen has no such section. A picture never holds a pitch up: if the screen won't build or
can't be captured, the pitch still goes up with its text mockup and one line saying why there's no
picture. The pictures are stored on the `a-team/attachments` branch like any attached image, so
in a public repo they're public, and the rows are made up for that reason.

## Where you decide

Work waits for you in two places, and no agent moves it on without you.

- **Pitched.** You approve a pitch, comment on it to have it changed, say Not now to it for a few
  weeks, or decline it. Nothing gets built until you approve. A plain yes in a comment, like
  "Let's build it", approves it too, just as Not now in a comment shelves it: the Lead acts on your
  word and says so. A yes with a condition leaves the pitch for you to approve once it's revised.
- **In review.** You merge a task's PR. Nothing is done until you say so. A task reaches you here
  by itself once its PR is out of draft, green and clear of its base, however it left draft; if the
  Reviewer never read it, the PR says so. Once every task of a
  pitch has closed, the Lead checks the whole and closes it, so a finished pitch doesn't come back
  to you. A pitch another pitch's work already built, the Lead closes too, naming the merged PRs
  that built it.

Both are in the [Work](work.md) area, where you can answer them without leaving the app.

## Talking to the team

Comment on the issue or the PR. The agents read your comments on their next run and reply there.
Ask the Lead, the Dev or the Customer lead for a follow-up and it files it as an Idea, which waits
in Triage until you rank it.

The 👀 reaction on your comment means a run has read it. Leave that one to the agents: every other
reaction is yours. A comment you make while a run is going gets a run of its own, so nothing you say
is missed.

When the Dev can't go on without you, it hands its task back with a question and holds it.
The Lead may hold a task too while it breaks a pitch down, and asks on a pitch under
**Needs your answer** in its description. All of them wait in **Blocked** in the Work area until you
answer: a held task with the latest ask on it, or `held, no reason given` where no agent said why.

You never need to take the hold off a task an agent held. Reply on it instead, from the reader or on GitHub:
the agent that held it reads your reply on its next run and, if it says the thing is done, takes the
hold off and the task goes back in the Dev's queue. If your reply doesn't say so, it asks again in a
line and the task stays held. A task that waits on a change in another repo is never held: it's
recorded as waiting on that repo, and frees itself ([The board](#the-board)).

The dispatcher can hold an item too, when three runs started for the same reasons each changed
nothing, so a problem the team can't fix stops costing runs. It waits in **Blocked** saying what
was tried, and any reply from you frees it ([Work](work.md#answering-from-a-card)).

When a task needs a GitHub Actions secret, like a deploy token, the agent asks for it by name and never for
its value in a comment. You [give it in Work](work.md#answering-from-a-card), and that frees the task with no reply.

To hold a task yourself, select its card in Work and press `h` (**Hold**), and say why. To take any
hold off, yours or an agent's, press `h` again (**Release hold**), or use **Release hold** in the
reader ([Work](work.md#answering-from-a-card)).

## What a card says about an item

a-team keeps what it knows about an item beyond its Status and Priority in its own record, not as
labels on your issues: the rank the Lead recommends, the theme a pitch serves, a shelve and its date,
a skip, a hold, and what a task waits on in another repo. A card's details in Overseer and the line
under the reader's title show them, like `Idea · Priority none · Lead recommends Medium · theme:
Consistently good UX`. The GitHub Project shows the Lead's rank and the theme as its **Lead rank** and
**Theme** fields, which setting the board up adds, or else the first copy that needs one. If your
project already has a field of either name that isn't text, a-team can't copy to it: `check`, setup
and the mirror's note in Overseer say so, and renaming your field clears it. Upgrading
takes the old `rank:`, `theme:` and `a-team:` labels, and `blocked`, off your open issues once their
state is in the record; `pitch` and your own labels stay.

## What the team remembers

When you answer something that will come up again, the Lead records it in a decision register,
on this machine, beside the team's history. Before it pitches, breaks a pitch down or assumes anything,
it searches the register, and a new pitch cites what it relied on under **Assumed**: *From the
register: you, on #34*. A register entry is a leaning, not a law: comment to overturn it like any
other assumption. Your latest word replaces an older entry, and the Lead's reply says which.

Each entry is a **rule**, something you said generally, or a **lean**, a call on one pitch that
looks like it will carry. It belongs to its team or to all teams.

Open **Decisions…** from the **View** menu or Commands (`Ctrl+E`) to see the selected team's
entries and all teams', newest confirmed first. Type in **Search** to narrow them, untick **Include
all-teams** to see the team's own, and tick **Show replaced** for the ones replaced or retired. The
selected entry is shown in full below the list: why, where and when it was said, when a pitch last
relied on it, and what it replaced.

| Key | What it does |
|---|---|
| `Enter` | Open where it was said on GitHub |
| `e` | Edit its wording; `Enter` saves, `Esc` leaves it. Left empty, it saves nothing: `Decision` turns red and the foot says `Decision is needed` |
| `k` | Make it a rule or a lean |
| `w` | Widen it to all teams, or narrow it again |
| `r` | Retire it, or bring it back |

The Lead never changes back an entry you've corrected.

## The Customer lead

The Customer lead makes sure what the team ships is documented, so someone who didn't watch it being
built can find it and learn it. It's off until you turn it on: tick **Customer lead** under **Roles**
in the team's settings ([Turning on the Customer lead](teams.md#turning-on-the-customer-lead)), and it gets a line in the team's
panel in [Overseer](overseer.md#the-teams-panel).

- **If the docs' first page isn't in the repo yet**, it proposes the docs first: a draft PR in
  Pitched, `Docs proposal: where <product>'s user docs live`, that adds that page with their outline.
  Comment on it to change the plan. Accept it in Work to agree, and its next run writes the docs.
  Until then it writes none. Only GitHub saying the page isn't there starts this: if GitHub doesn't
  answer, the Customer lead waits for the next pass.
- **When a pitch is done**, it reads what the pitch shipped and checks the user docs cover it:
  the README, guides and in-app help written as text.
- **If they don't**, it writes what's missing into one docs PR, `Docs: what's changed since <date>`,
  whose description lists each change in a line. While that PR is open, the next pitch that's done adds
  to it rather than opening another.
- **If they already do**, it changes nothing.
- **When you comment on its PR**, on GitHub or with **Comment** on its Review card, it makes the
  change, updates the list in the description and replies saying what it did. If you ask for
  something only a product change would fix, it files that as an Idea rather than touching code.
- **If only the product can fix it**, say a feature is hard to find, it files an Idea written as your
  user's problem ("I can't find how to…"), with what it looked at. It waits in Triage with the Lead's
  discoveries, and counts towards the same limit, **Ideas** in the team's settings.
- **Once a week**, and within a few minutes of being turned on, it audits the docs as a whole: what's
  wrong against the product as it is now, what a shipped feature is missing, and whether pages are
  where a newcomer would look. Each fix is a line in the same docs PR. If it finds nothing, it
  changes nothing.

Its PR waits in Review in the [Work](work.md) area, marked as the Customer lead's. Accept it like a
task, and the next change starts a new one. It never changes code, pitches or tasks.

## The Reviewer

The Reviewer gives each of the Dev's task PRs the sanity check you'd otherwise ask a fresh session
for, before the PR reaches you. It's off until you turn it on: tick **Reviewer** under **Roles** in
the team's settings ([Teams](teams.md#a-teams-settings)), and it gets a line in the team's
panel in [Overseer](overseer.md#the-teams-panel). Each review is a run, so it costs one run per task.

- **When it runs.** Once a task's PR goes green and is still a draft, the Reviewer reads it before
  the Dev marks it ready. It reads the task and its pitch as well as the diff, and changes no code.
- **What it looks at.** For a task that changes what you see, the Dev puts a picture of each screen
  the task names in the PR's description, under **What it looks like**, captioned with what it
  shows. The Reviewer checks each one against its caption and the style guide, and a task that
  changes the screen with no pictures is a **Needs changing** point of its own.
- **What it says.** One comment on the PR, in two sections. **Needs changing** has each point with
  the file and line and why: a missed acceptance criterion, a test that doesn't test the change, or
  a wrong result in code the PR changes, naming the case, like *when the read is rate limited, it
  says the PR isn't merged*, whether or not the task mentioned that case. **Worth considering** holds
  only what's optional: other approaches, naming and readability, and problems in code the PR didn't
  touch. It leaves out whatever is fine. If nothing needs changing, it says so in one line.
- **What happens next.** The Dev fixes each **Needs changing** point with a test, or replies why
  not, then marks the PR ready as usual. A point that means changing code the PR doesn't otherwise
  touch it says so about and leaves, for you to see on the PR. It leaves **Worth considering** to
  you: tick the ones you want with [Ask the Dev to fix](work.md#answering-from-a-card).
- **Once only.** A PR is reviewed once. The Dev's fixes come to you without a second review, so the
  two agents can't go round in circles.
- **Only task PRs.** Pitches, the Customer lead's docs PR and your own PRs are never reviewed.

In the [Work](work.md) area, a reviewed task's card says `reviewed`, and how many points it left to
consider, like `reviewed · 1 to consider`. Its reader shows the review and the Dev's reply in the
conversation, under `reviewer` and `dev`.

## Ideas and priorities

You steer the Lead with the Priority field on the board: Low, Medium, High or Urgent.

- **Seed an idea** with [**New idea**](work.md#adding-an-idea) (`Ctrl+N`). An issue anyone opens on
  the product repo reaches Idea too, with or without a mirror.
- **The Lead pitches the highest-priority Idea**, and between pitches it discovers new ones. Its own
  discoveries, and the Customer lead's, are marked as theirs.
- **An Idea any agent wrote, or an issue someone else opened, is only pitched once you give it a
  priority.** Until then it waits in Triage. Close the ones you don't want.
- **The Lead recommends a rank for every Idea**, saying which theme in the vision it serves and how
  big it is. Your Priority always wins; among Ideas with the same Priority, or none, the Lead pitches
  the one it recommends higher first.
- **Only a few pitches are in front of you at once,** highest priority first. A higher-priority
  draft takes the place of a lower-priority pitch, which waits in Exploring for room again. A draft
  with the same Priority never does, whatever the Lead recommends.
- **A pitch you say Not now to** goes back to Idea, and the Lead won't pitch it again until the
  date you picked, or until you change its Priority. See [Work](work.md#answering-from-a-card).
- **The Lead's own Ideas are limited too.** Once **Ideas** in the team's settings are waiting for
  your Priority, it stops discovering. Ranking one, as well as closing it, makes room for the next.
  One it skipped doesn't wait on you, until you comment on it.
- **An Idea the Lead sets aside** is marked skipped, with a comment saying why. Comment
  on it to put it back in the running.

The Dev also takes the highest-priority Ready task first. A task takes its pitch's Priority unless you
give it one of its own.
