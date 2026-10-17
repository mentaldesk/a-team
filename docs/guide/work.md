# Work

Everything waiting on you, across every team. `w` opens it, `d` goes to the Dashboard and `o` to
[Overseer](overseer.md). The app opens in whichever area you were in last, and on Work the first time.

Work's title says whether the queue is shrinking, how much you've accepted, and how much of your
time it took, across every team:

```
Work · 7 waiting on you (12 a week ago) · 23 accepted in 7 days · 3h 10m of yours · 7.3 per hour
```

*Waiting on you* counts every card in Work, including those `m` hides. *Accepted* counts the task
PRs merged and the pitches accepted, whether from here or on GitHub. a-team counts from the day it's
installed: until it has a week's record the comparison is left out and *accepted* covers the days it
has, and with nothing recorded yet the title shows only what's waiting.

*Of yours* is the time you spent in Work in the last 7 days, and *per hour* what you accepted for
each hour of it. a-team times you only here, never on GitHub, and only while you're active: after 5
minutes with no key pressed it stops counting until the next one. Time with a card's reader open,
commenting included, counts against that card, and a try counts in full, up to 30 minutes. Time in
Work with no reader open counts as *other* for the team whose lane is selected. Time on the
Dashboard and Overseer doesn't count. Neither figure shows until some time has been recorded.

To compare the teams day by day, open **Trends** in Commands (`Ctrl+E`). It charts the last 14 days,
one line per team, for what was waiting on you, what you accepted each day, the hours to accept:
the median time the tasks you accepted that day took from first entering Ready, or **Your time**,
in minutes a day. With *Your time* chosen, a table beside the legend splits this week's time by gate:
Triage, Pitches, Questions, Review and Other.
`Left`/`Right` switch between them. Days before a team's record began are left blank,
and so are days with nothing accepted when charting hours. Underneath, a table lists each team's
waiting now and a week ago, what you accepted in the last 7 days, what its runs cost in that time,
and for the tasks you accepted, their median **Cycle** and how much of it was **With you**: In
review, waiting for a slot your reviews were holding, or waiting for your answer to a question. Then **Yours (7d)**, your time on it in the last 7 days, and **/h**, what you accepted per
hour of it. Days and weeks before your time was first recorded are left blank, not zero. The **All**
row at the bottom covers every team. A task that entered Ready before its team's
record began isn't counted in either. `Esc` closes it.

To see what the money bought, open **Spending** in Commands (`Ctrl+E`). It rolls the cost of every
team's runs up by team, then by the vision theme each pitch serves, then by pitch, over the last 7
days, the last 30 or everything recorded. Each row shows its runs, what each role cost and the
total, with `–` for nothing. A pitch counts the runs on its tasks too, and a run on several items is
split evenly between them, so a team's 7-day total matches Trends. Under each team, *Discovery &
upkeep* is runs on no item, and *Other items* is runs on Ideas never pitched and on your own items.
The Lead labels each pitch `theme: <heading>` when it drafts it; relabel it on GitHub and Spending
follows. `Right` and `Left` expand and collapse a row, `Enter` on a pitch opens its card, and `Esc`
closes it.

Each team has a tab, in the order the teams are set up, titled with the team and how many cards
wait in each column, after the column's icon: like `a-team 💡4 ◇ 2 PR1` for 4 in Triage, 2 in
Pitches and 1 in Review, with `?` counting Questions. With `m` on, it counts only your moves, and a
column with nothing showing isn't counted. Work opens on the first team with something waiting. Each tab has up to four
columns, left to right in the order work moves through them. A column with nothing in it is hidden,
and the one you're on takes half the tab. Only that column highlights its card, the one your keys act
on; the others, and the other tabs, keep their place for when you come back.

## Columns

| Column | What it holds |
|---|---|
| Triage | Ideas and pitches with no Priority, in the order the Lead recommends, highest first. Nothing here is approved, and nothing the team found is pitched, until you give it one. |
| Pitches | Pitches waiting for your approval, at [the first place you decide](how-a-team-works.md#where-you-decide), and document pitches: a vision draft or review, or the Customer lead's docs proposal, which you agree to by accepting it. |
| Questions | Tasks the Dev handed back to ask you something, and pitches the Lead can't go on with until you answer. |
| Review | Finished work you can accept now: a task whose PR is ready to merge, the Customer lead's docs PR, or a Dependabot PR once the Lead has said what the update brings. |

Each column's heading counts its cards. Under the Review cards, a line sums up the work that's in
review but not ready for you yet, grouped by whose move it is, like
`1 with the Reviewer: #12 · 2 with the Dev: #14 CI failing · #15 still a draft`.

## A card

A card reads as the issue's number, then its title:

```
✓ ‹› #313  Help → Guide explains how a team works
  └ PR #320  Help → Guide explains how a team works
```

- **Whose move it is.** A green `✓` means it's yours. A dimmed `·` means an agent owes you an
  answer, and the card names the role in front of its title, like `#118  lead · ...`. It's theirs
  from the moment you comment on the issue or its PR until they reply. A `⋯` in its place means
  something you did to it is still being written to the board ([below](#while-its-being-written)).
- **What it is.** 💡 an Idea, `◇` a pitch, `‹›` a task, 📄 the Customer lead's docs PR. Triage
  and Pitches show the kind in their heading instead, so only a card of another kind wears its own.
- **Its Priority.** The issue number is coloured by Priority, in the colours GitHub gives them:
  green for Low, amber for Medium, red for High and pink for Urgent. An unranked card's number is
  plain.
- **The Lead's recommendation.** An Idea you haven't ranked shows the rank the Lead would give it
  after its number, like `#183  High? · ...`, and selecting it puts the Lead's one-line case on the
  status bar.
- **Its PR.** The pull request that closes a task hangs under its card as a row of its own.
- **Its branch.** Selecting a card with a PR (a task, the docs PR or a document pitch) ends the
  status bar line with the PR and its branch, like `PR #459 · feat/path-pickers`, and the reader
  heads its body with them and the base, like `PR #459 · feat/path-pickers → main`. `y` (**Cards →
  Copy branch name**) copies the branch, on the card or in the reader.
- **Its review.** With the [Reviewer](how-a-team-works.md#the-reviewer) on, a task whose PR it has
  reviewed says `reviewed` after its number, and how many points it left for you to consider, like
  `#412  reviewed · 1 to consider · ...`.
- **Trouble.** A PR that's failing CI, conflicts with its base, is still running CI or is still a
  draft is the Dev's to fix, and the card says which, like `#124  dev · CI failing · ...`. With the
  Reviewer on, a green draft it hasn't reviewed yet is its turn instead: `#124  reviewer · awaiting
  review · ...`. A PR whose base just moved shows `resolving mergeable status` until GitHub has worked out whether it
  still merges.

In kitty, WezTerm and Ghostty, which bundle a Nerd Font, the icons are that font's glyphs instead,
and a PR row wears a pull request glyph in place of `PR`. No terminal says which font it has, so if
the icons come out wrong, pick *Nerd Font* or *Unicode* under **Settings → Dashboard**.

The line at the foot says why the card you're on is where it is, like
`#118 · lead · answering your feedback since 08:14`, and how long ago the cards were read.

In every area, at the top right beside the menus, an alert like `? #481 +2` says what has become your move since you
opened the app: a pitch in Pitched, a PR in review or the Dev's question, the newest and how many
others. `u` (**View → Go to what's new**) or a click takes you to it, and each new card wears a `●`
before its title until you open it.

## Moving around

| Key | Does |
|---|---|
| Arrows | Move between cards and columns, stopping at the ends of a column |
| `Ctrl+PgDn` / `Ctrl+PgUp` | Go to the next team's tab, or the previous one's, round from the last to the first |
| `1` to `9` | Go to the first team's tab, the second's, and so on |
| `Enter` | Read the card, and on a card in Triage, rank it |
| `g` | Open the card, or the PR row you're on, on GitHub |
| `b` | Open the team's Project board on GitHub, for everything that isn't waiting on you |
| `p` | Read the card and set its Priority |
| `m` | Show only what's your move, or everything again. The foot says which, and it's kept for next time |
| `u` | Go to the newest card that has become your move |
| `F5` | Read what's waiting again |

To pick a team by name, open **Go to team…** from Commands (`Ctrl+E`) or the View menu, and type
to narrow the list as in Commands.

The cards are read when you open Work, when you press `F5`, and by themselves every five minutes
while Work is in front. Coming back from the Dashboard puts you on the card you left, and reads again
only if an agent has started or finished a run, or five minutes have passed, since the last read.
That costs about 250 of the 5,000 GraphQL points an hour GitHub allows; the Dashboard reads nothing. If a read fails, the foot says so in red and the cards stay as they were.

## Reading a card

`Enter` opens the issue as it was written, then everything said since on it and on its PR, each
comment headed by who said it and when. What you can do to it, like approving, commenting or
declining, is a button at its foot with its key; `g` (*GitHub*) opens it on GitHub, and `Esc`
closes it. `F1` lists every key that works where you are in it.

**Where it opens.** A card you've opened before on this machine opens at the first comment posted
since you last did, and each comment since then has `new` at the end of its heading. If nothing has
been said since, it opens at the newest comment. A card you've never opened, or one nobody has
commented on, opens at the top. A card in Questions reads the same, and opens at the question: the
Dev's comment, or the pitch's **Needs your answer** heading. In the body, `Alt+↑` and `Alt+↓` go to
the previous and next comment's heading, with the top of the body counting as the first.

Beside it, **History** lists what a-team has done to the card, newest first: each move, approval,
accept, decline, Not now, comment, Priority, recommendation, skip, review, link and dependency, with when
and who (`you`, or the role: `lead`, `dev`, `customer` or `reviewer`). What
you or anyone else does on GitHub directly (a merge, a close, a comment, a move on the Project board)
appears there too, within a read or two of Work, with someone else shown by their GitHub login. It
keeps history from the day it's installed. A comment you post from the reader appears there straight
away.
Each run the dispatcher started for the card is a line too: `running since 12:51` while it goes, then
how long it took and what it cost, like `run 38 min · $4.12`. One that didn't finish says why:
`stopped` by you, `killed` at the time limit, `error`, or `died` with no result, and no cost.
Each of your visits to the card is a line as well: how long you spent on it and what you did, like
`6m · approved` or `1m · read`.
`Tab` moves between the body and History. `Enter` or a click on a comment's line in History shows
that comment in the body. `h` hides
History to give the body the full width, and shows it again; it stays as you left it until you
restart the app. On a terminal too narrow for both, the reader opens with History hidden.

**Copy from it.** The body and History each have a cursor. Move it with the arrows, `Home`, `End`,
`PgUp` and `PgDn` (`Ctrl+Home` and `Ctrl+End` for the top and the end, `Option+←`/`→` or
`Ctrl+←`/`→` for a word), and hold `Shift` to select,
as in any editor. You can also drag with the mouse, or double-click a word, like an issue number or
a URL. `Ctrl+C` copies the selection, and `Esc` clears it.

## Answering from a card

- **Approve a pitch.** Reading a pitch in Pitches, press `a`. It's approved there and then, and
  the card leaves.
- **Reply.** Reading any card, press `c` to write a comment. It opens in a **Comment** pane on the
  left, with the body and History still beside it. `Tab` and `Shift+Tab` move between the panes,
  `Esc` in the comment goes back to the pane you came from, and `c` comes back to what you'd written.
  It moves and selects as in [a description](#adding-an-idea).
  `Ctrl+Enter` posts it from any pane: it's posted as you, joins the end of what you're reading, and
  the agent picks it up on its next run. On a terminal too narrow for three panes, History hides
  while you write; `h` still shows it.
- **Quote what you're answering.** Select it in the body or History and press `q`. It goes into the
  comment where its cursor is, opening the comment if it isn't, as `> ` lines the agents read just
  as they read a quote reply on GitHub, and takes you to the comment below it to write your answer;
  `Esc` goes back for the next part. Closing the reader with something unposted in the comment asks
  before throwing it away: `Ctrl+Enter` discards it, and `Esc` keeps writing.
- **Answer a question.** Read it in Questions, and reply with `c`.
- **Decline it.** On an Idea or a pitch, reading it or with its card selected, press `x`. The
  Comment pane opens as **Decline #n: why?**, keeping anything you'd already written, and you can
  quote into it with `q` as into a comment. `Ctrl+Enter` posts your reason as a comment, closes the
  issue as not planned on GitHub, and the card leaves Work; History records it as `declined`, and it
  doesn't count as accepted. If the board refuses the decline, the status bar says why and the
  reason you typed is lost. A reason is required. `Esc` turns the pane back into a plain comment,
  keeping what you wrote, and declines nothing. To undo a decline, reopen the issue on GitHub.
- **Say Not now to a pitch.** On a pitch in Pitches, reading it or with its card selected, press
  `l`. The Comment pane opens as **Not now #n**, with **Back in [2] weeks** above it: `Shift+Tab`
  reaches the weeks, and `↑`/`↓` or typing sets any whole number from 1. A reason is optional.
  `Ctrl+Enter` posts it, with the date, as a comment, and the card leaves Pitches; the status bar
  says `#81 back on 25 Oct`, and History records `shelved until 25 Oct`. Until then the Lead won't
  pitch it again. After that it comes back at its Priority in turn, and the Lead says it was
  shelved and why. Changing its Priority brings it back sooner; clearing it leaves it in Triage.
  `Esc` turns the pane back into a plain comment and shelves nothing. Asking the Lead in a comment
  to shelve a pitch does the same, for the weeks you name or 2, and its reply gives the date.
- **Rank it.** `Enter` on a card in Triage, or `p` on any card, opens the reader with a row of
  ranks under it, **None** to **Urgent**, starting on the card's own, or on the Lead's
  recommendation if it has none, so `Enter` agrees with it. `←`/`→` or a rank's initial
  moves between them, and `Enter` sets it: the reader closes and the card moves to the column its
  new Priority puts it in. `Esc` closes without changing it. You can still scroll, comment with
  `c`, approve with `a` and open it with `g` while the ranks show; there, `h` picks **High**
  rather than hiding History.
- **Try it.** On a task with a PR, or reading one, press `t`. The terminal runs that PR's version,
  with its acceptance criteria printed first; quit it to come back. If you were reading the task,
  it opens again where you left it, ready for `a` or `c`, and says there if the try failed.
- **Edit a document pitch.** Reading a vision draft, a vision review or a docs proposal, which shows
  the file it proposes under its description, press `e`. The file opens in your editor (`$VISUAL`,
  else `$EDITOR`). Save and quit, and your change is committed to the pitch as you, and the reader
  opens again showing it. Quit without saving and nothing changes. If the pitch changed on GitHub
  while you were editing, nothing is overwritten: the reader says so, and where your edit is kept.
  A pitch that changes more than one file can't be edited here; `g` opens it on GitHub.
- **Accept finished work.** On a card in Review, or reading one, press `a`. It asks first:
  `Ctrl+Enter` accepts, `Esc` cancels, and `Enter` does nothing, so a stray one can't
  ([Confirming](dashboard.md#confirming)). For a task, its PR is squash-merged and its
  branch deleted. The Customer lead's docs PR and a Dependabot PR are merged the same way, and so is a document pitch,
  from Pitches: the card leaves, and the Lead's next run reads the new vision. If it isn't ready yet,
  `a` says why instead.

### While it's being written

Approving, accepting, ranking or declining a card, and on the Dashboard pausing or interrupting
an agent or asking for a vision review, never makes you wait: carry straight on to the next card.
Each one is written to the board in the background, one at a time in the order you made them. The
card wears `⋯` in place of its `✓` until the board has taken it, then leaves or moves as above,
and the status bar says what's being written and how many more are queued, like
`Merging PR #459… · 1 more queued`. Acting on that card again says it's already under way. If the
board refuses one, the status bar says which card and why, the card stays where it was, and the
rest still run.
Quitting while some are still queued asks first: `Ctrl+Enter` quits and drops them, `Esc` stays.
The one being written finishes either way.

## Adding an idea

Press `n`, or pick **New idea** from the Cards menu or Commands (`Ctrl+E`). **Team** starts on the
lane you're in, or the first team from the Dashboard. Type a **Title**, and a **Description** if
you like; `Tab` moves between the fields. **Priority** starts on **None**, which puts the idea in
that team's Triage to rank later; pick another with `←`/`→` and it goes straight into the Lead's
queue.

**Writing in a comment or description.** The reader's Comment and New idea's Description scroll to
keep what you're typing in view, with a scroll bar once the text is taller than the box.
`Option+←`/`→` (or `Ctrl+←`/`→`) moves a word, `Home` and `End` (`fn+←`/`→` on a Mac laptop) go to
the start and end of the row you can see, and `Ctrl+Home` and `Ctrl+End` to the start and end of
the text. Hold `Shift` with any of them to select; `Ctrl+A` selects everything.

**Attaching a screenshot.** In a comment or a description, there are three ways to attach an image:

- **Paste it.** With a screenshot on the clipboard (`⌃⇧⌘4` on a Mac), press `Ctrl+V`. That works in
  every terminal, iTerm2 and Ghostty included. `⌘V` works too where the terminal passes on a paste
  with nothing in it; if it does nothing, use `Ctrl+V`. With text on the clipboard, both paste the
  text.
- **Drop it.** Drag a `.png`, `.jpg`, `.gif` or `.webp` file onto the terminal. A dropped file of any
  other kind pastes its path, as before.
- **Name it.** `Ctrl+U` opens **Attach image file**: type the path, and `Ctrl+Enter` attaches it.

A line like `![Screenshot 2026-10-13 at 07.02.09](uploading…)` goes in at the cursor, and becomes the
image's link once it's up; edit, move or delete it like any other text. The bar says
`Attached <file>  •  412 KB`, or `Attach failed:` and why, leaving what you wrote as it was. Images
go up to 10 MB. `Ctrl+Enter` won't post or add until every image is up.

The image is stored on the `a-team/attachments` branch of the team's repo, which a-team starts the
first time you attach one and never merges, and shows inline on GitHub. The agents look at it when
they read what you wrote, and a reply about it says what they saw. **In a public repo, attached
images are public.**

**Seeing a screenshot.** In a terminal that draws pictures (iTerm2, Ghostty, WezTerm, kitty or
Windows Terminal), the reader draws each image in the body and its comments as wide as the pane.
Put the cursor on one and press `Enter` to see it full-window; `Esc` closes it. In the Comment pane `Enter` starts a new line, so put the cursor on the image's line and pick
**View image** from Commands (`Ctrl+E`). In any other terminal, over a connection that doesn't pass
pictures on, or for an image your `gh` login can't read, the line stays as text and `Enter` opens
the image on GitHub. PNG, JPEG and GIF images are drawn; a WebP stays as its line. While you're
writing, the Comment pane and New idea's Description show the line, not the picture.

Over SSH the dashboard can't read your own machine's clipboard: pasting pastes text as before, and
the bar says to drop the file instead.

`Ctrl+Enter` adds it: the issue is opened as you, put on the team's board as an Idea and ranked if
you chose a Priority. Work reads again straight away, and the bar says `#<n> added to <team>'s
ideas`. If a step fails, the dialog stays open with what you wrote and says which step; `Ctrl+Enter`
tries again from there. `Esc` cancels, asking first if you've typed anything: `Ctrl+Enter` discards
it, and `Esc` keeps writing.

**From what you're reading.** Press `n` in a card's reader, or pick **New idea** from Commands
(`Ctrl+E`) there. **Team** starts on the card's team, and the **Description** on a link back to the
card. Select lines in the body first and they're quoted above the link, which goes to the comment
they came from (the first, if they span two). The new issue shows on the card's GitHub timeline as
"mentioned this", and closing the dialog leaves you in the reader where you were. While the ranks
show, `n` picks **None**; Commands still files an idea from there.
