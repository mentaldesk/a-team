# Work

Everything waiting on you, across every team. `w` opens it, and `d` goes to the Dashboard. The app
opens in whichever area you were in last, and on Work the first time.

Work's title says whether the queue is shrinking and how much you've accepted, across every team:

```
Work · 7 waiting on you (12 a week ago) · 23 accepted in 7 days
```

*Waiting on you* counts every card in Work, including those `m` hides. *Accepted* counts the task
PRs merged and the pitches accepted, whether from here or on GitHub. a-team counts from the day it's
installed: until it has a week's record the comparison is left out and *accepted* covers the days it
has, and with nothing recorded yet the title shows only what's waiting.

To compare the teams day by day, open **Trends** in Commands (`Ctrl+E`). It charts the last 14 days,
one line per team, for what was waiting on you, what you accepted each day, or the hours to accept:
the median time the tasks you accepted that day took from first entering Ready.
`Left`/`Right` switch between them. Days before a team's record began are left blank,
and so are days with nothing accepted when charting hours. Underneath, a table lists each team's
waiting now and a week ago, what you accepted in the last 7 days, what its runs cost in that time,
and for the tasks you accepted, their median **Cycle** and how much of it was **With you**, In
review. The **All** row at the bottom covers every team. A task that entered Ready before its team's
record began isn't counted in either. `Esc` closes it.

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
| Pitches | Pitches waiting for your approval, at [the first place you decide](how-a-team-works.md#where-you-decide), and the Customer lead's docs proposal, which you agree to by merging it on GitHub. |
| Questions | Tasks the Dev handed back to ask you something, and pitches the Lead can't go on with until you answer. |
| Review | Finished work you can accept now: a task whose PR is ready to merge, or the Customer lead's docs PR. |

Each column's heading counts its cards. Under the Review cards, a line sums up the work that's in
review but not ready for you yet, like `2 with the Dev: #12 CI failing · #14 still a draft`.

## A card

A card reads as the issue's number, then its title:

```
✓ ‹› #313  Help → Guide explains how a team works
  └ PR #320  Help → Guide explains how a team works
```

- **Whose move it is.** A green `✓` means it's yours. A dimmed `·` means an agent owes you an
  answer, and the card names the role in front of its title, like `#118  lead · ...`. It's theirs
  from the moment you comment on the issue or its PR until they reply.
- **What it is.** 💡 an Idea, `◇` a pitch, `‹›` a task, 📄 the Customer lead's docs PR. Triage
  and Pitches show the kind in their heading instead, so only a card of another kind wears its own.
- **Its Priority.** The issue number is coloured by Priority, in the colours GitHub gives them:
  green for Low, amber for Medium, red for High and pink for Urgent. An unranked card's number is
  plain.
- **The Lead's recommendation.** An Idea you haven't ranked shows the rank the Lead would give it
  after its number, like `#183  High? · ...`, and selecting it puts the Lead's one-line case on the
  status bar.
- **Its PR.** The pull request that closes a task hangs under its card as a row of its own.
- **Trouble.** A PR that's failing CI, conflicts with its base, is still running CI or is still a
  draft is the Dev's to fix, and the card says which, like `#124  dev · CI failing · ...`. A PR
  whose base just moved shows `resolving mergeable status` until GitHub has worked out whether it
  still merges.

In kitty, WezTerm and Ghostty, which bundle a Nerd Font, the icons are that font's glyphs instead,
and a PR row wears a pull request glyph in place of `PR`. No terminal says which font it has, so if
the icons come out wrong, pick *Nerd Font* or *Unicode* under **Settings → Dashboard**.

The line at the foot says why the card you're on is where it is, like
`#118 · lead · answering your feedback since 08:14`, and how long ago the cards were read.

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
| `F5` | Read what's waiting again |

To pick a team by name, open **Go to team…** from Commands (`Ctrl+E`) or the View menu.

The cards are read when you open Work, when you press `F5`, and by themselves every five minutes
while Work is in front. Coming back from the Dashboard puts you on the card you left, and reads again
only if an agent has started or finished a run, or five minutes have passed, since the last read.
That costs about 250 of the 5,000 GraphQL points an hour GitHub allows; the Dashboard reads nothing. If a read fails, the foot says so in red and the cards stay as they were.

## Reading a card

`Enter` opens the issue as it was written, then everything said since on it and on its PR, each
comment headed by who said it and when. On a card in Questions it shows just the question. `g`
opens it on GitHub, and `Esc` closes it.

Beside it, **History** lists what a-team has done to the card, newest first: each move, approval,
accept, comment, Priority, link and dependency, with when and who (`you`, `lead` or `dev`). What
you or anyone else does on GitHub directly (a merge, a close, a comment, a move on the Project board)
appears there too, within a read or two of Work, with someone else shown by their GitHub login. It
keeps history from the day it's installed. A comment you post from the reader appears there straight
away.
Each run the dispatcher started for the card is a line too: `running since 12:51` while it goes, then
how long it took and what it cost, like `run 38 min · $4.12`. One that didn't finish says why:
`stopped` by you, `killed` at the time limit, `error`, or `died` with no result, and no cost.
`Tab` moves between the body and History. `h` hides
History to give the body the full width, and shows it again; it stays as you left it until you
restart the app. On a terminal too narrow for both, the reader opens with History hidden.

**Copy from it.** The body and History each have a cursor. Move it with the arrows, `Home`, `End`,
`PgUp` and `PgDn` (`Ctrl+Home` and `Ctrl+End` for the top and the end), and hold `Shift` to select,
as in any editor. You can also drag with the mouse, or double-click a word, like an issue number or
a URL. `Ctrl+C` copies the selection, and `Esc` clears it.

## Answering from a card

- **Approve a pitch.** Reading a pitch in Pitches, press `a`. It's approved there and then, and
  the card leaves.
- **Reply.** Reading any card, press `c` to write a comment. It opens in a **Comment** pane on the
  left, with the body and History still beside it. `Tab` and `Shift+Tab` move between the panes,
  `Esc` in the comment goes back to the pane you came from, and `c` comes back to what you'd written.
  `Ctrl+Enter` posts it from any pane: it's posted as you, joins the end of what you're reading, and
  the agent picks it up on its next run. On a terminal too narrow for three panes, History hides
  while you write; `h` still shows it.
- **Quote what you're answering.** Select it in the body or History and press `q`. It goes into the
  comment where its cursor is, opening the comment if it isn't, as `> ` lines the agents read just
  as they read a quote reply on GitHub, and takes you to the comment below it to write your answer;
  `Esc` goes back for the next part. Closing the reader with something unposted in the comment asks
  before throwing it away.
- **Answer a question.** Read it in Questions, and reply with `c`.
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
- **Accept finished work.** On a card in Review, or reading one, press `a`. For a task, once you
  confirm, its PR is squash-merged and its branch deleted. The Customer lead's docs PR is merged
  the same way. If it isn't ready yet, `a` says why instead.

## Adding an idea

Press `n`, or pick **New idea** from the Cards menu or Commands (`Ctrl+E`). **Team** starts on the
lane you're in, or the first team from the Dashboard. Type a **Title**, and a **Description** if
you like; `Tab` moves between the fields. **Priority** starts on **None**, which puts the idea in
that team's Triage to rank later; pick another with `←`/`→` and it goes straight into the Lead's
queue.

`Ctrl+Enter` adds it: the issue is opened as you, put on the team's board as an Idea and ranked if
you chose a Priority. Work reads again straight away, and the bar says `#<n> added to <team>'s
ideas`. If a step fails, the dialog stays open with what you wrote and says which step; `Ctrl+Enter`
tries again from there. `Esc` cancels, asking first if you've typed anything.
