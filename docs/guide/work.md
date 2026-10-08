# Work

Everything waiting on you, across every team. `w` opens it, and `d` goes to the Dashboard. The app
opens in whichever area you were in last, and on Work the first time.

Each team has a lane, and each lane has up to four columns, left to right in the order work moves
through them. A column with nothing in it is hidden, and the one you're on takes half its lane.

## Columns

| Column | What it holds |
|---|---|
| Triage | Ideas and pitches with no Priority. Nothing here is pitched or approved until you give it one. |
| Pitches | Pitches waiting for your approval, at [the first place you decide](how-a-team-works.md#where-you-decide). |
| Questions | Tasks the Dev handed back to ask you something, and pitches the Lead can't go on with until you answer. |
| Review | Finished work you can accept now: a task whose PR is ready to merge. |

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
- **What it is.** 💡 an Idea, `◇` a pitch, `‹›` a task. Triage and Pitches show the kind in their
  heading instead, so only a card of another kind wears its own.
- **Its Priority.** The issue number is coloured by Priority, in the colours GitHub gives them:
  green for Low, amber for Medium, red for High and pink for Urgent. An unranked card's number is
  plain.
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
| Arrows | Move between cards and columns, and on into the lanes above and below |
| `Enter` | Read the card, and on a card in Triage, rank it |
| `g` | Open the card, or the PR row you're on, on GitHub |
| `p` | Read the card and set its Priority |
| `m` | Show only what's your move, or everything again. The foot says which, and it's kept for next time |
| `F5` | Read what's waiting again |

The cards are read when you open Work, when you press `F5`, and by themselves every five minutes
while Work is in front. Coming back from the Dashboard puts you on the card you left, and reads again
only if an agent has started or finished a run, or five minutes have passed, since the last read.
That costs about 250 of the 5,000 GraphQL points an hour GitHub allows; the Dashboard reads nothing. If a read fails, the foot says so in red and the cards stay as they were.

## Reading a card

`Enter` opens the issue as it was written, then everything said since on it and on its PR, each
comment headed by who said it and when. On a card in Questions it shows just the question. Arrows
and `PgUp`/`PgDn` scroll, `g` opens it on GitHub, and `Esc` closes it.

Beside it, **History** lists what a-team has done to the card, newest first: each move, approval,
accept, comment, Priority, link and dependency, with when and who (`you`, `lead` or `dev`). What
you or anyone else does on GitHub directly (a merge, a close, a comment, a move on the Project board)
appears there too, within a read or two of Work, with someone else shown by their GitHub login. It
keeps history from the day it's installed. A comment you post from the reader appears there straight
away.
`Tab` moves between the body and History, and the arrows scroll whichever has focus. `h` hides
History to give the body the full width, and shows it again; it stays as you left it until you
restart the app. On a terminal too narrow for both, the reader opens with History hidden.

## Answering from a card

- **Approve a pitch.** Reading a pitch in Pitches, press `a`. It's approved there and then, and
  the card leaves.
- **Reply.** Reading any card, press `c` to write a comment. It's posted as you, joins the end of
  what you're reading, and the agent picks it up on its next run.
- **Answer a question.** Read it in Questions, and reply with `c`.
- **Rank it.** `Enter` on a card in Triage, or `p` on any card, opens the reader with a row of
  ranks under it, **None** to **Urgent**, starting on the card's own. `←`/`→` or a rank's initial
  moves between them, and `Enter` sets it: the reader closes and the card moves to the column its
  new Priority puts it in. `Esc` closes without changing it. You can still scroll, comment with
  `c`, approve with `a` and open it with `g` while the ranks show; there, `h` picks **High**
  rather than hiding History.
- **Try it.** On a task with a PR, or reading one, press `t`. The terminal runs that PR's version,
  with its acceptance criteria printed first; quit it to come back. If you were reading the task,
  it opens again where you left it, ready for `a` or `c`, and says there if the try failed.
- **Accept finished work.** On a card in Review, or reading one, press `a`. For a task, once you
  confirm, its PR is squash-merged and its branch deleted. If it isn't ready yet, `a` says why
  instead.
