# Overseer

Every team's board on one screen, with how long each card has sat where it is. `Ctrl+G O` opens it, `Ctrl+G W`
goes to [Work](work.md) and `Ctrl+G D` to the [Dashboard](dashboard.md). It only shows: approving, accepting and
commenting stay in Work.

## Lanes and chips

Each team is a lane, in the order your teams are listed, under one row of column headings: Idea,
Exploring, Pitched, Approved, Building, Ready, In progress and In review. Done isn't shown.

A lane's title names the team and its roles that are running now, each with its animation playing
beside it, as in `a-team · Lead ✺ · Dev >`, or says `idle`. Where cards are past their column's limit, it counts them: `2 over`.
Where the team's GitHub Project hasn't caught up with its board yet, it says how far behind it is and why, as in
`mirror 3 moves behind (GitHub rate limit, retry 11:20)`. The board itself is never behind: once GitHub is back,
the Project catches up and the note goes.

Each card is a chip: a mark for what it is, its number, and how long it has been in that column, in
the largest unit, as in `◆404  6d`.

| Mark | Is |
|---|---|
| `◆` | A pitch |
| `●` | A task |
| `✎` | The Customer lead's docs PR |
| `○` | One of your own items |

Where a Nerd Font is in effect, a chip wears the icon [Work](work.md) gives the card
instead: an idea, a pitch, a task or the docs PR.

A chip's mark and number are drawn in its Priority's colour. Its age is in the theme's own colour, and
turns amber only once the card is past its column's limit. A card another task is waiting on, the one
[Work](work.md) tags `⛓ frees`, wears an amber `⚠` between its number and its age, as in `●81 ⚠6d`. A card that's
blocked for any reason, waiting on another task, on another repo or on your answer, wears `⊘` there (the Nerd Font
blocked icon), as in `●82 ⊘2d`. A card an agent is working on plays
its role's animation in place of its age:

| Role | Animation | Looks like |
|---|---|---|
| Dev | Decrypt: scrambled symbols lock into `</>`, `{;}`, `f()` or `0x1` | `ｱ#ﾘ` `</ｶ` `</>` in green |
| Lead | Thought to idea: thought bubbles rise and burst into a spark | `.oO` `  ✺` `. ✧` in amber |
| Customer lead | Typing: three dots bounce like someone typing | `⠶⠛⣤` `⣤⣤⣤` in blue |
| Reviewer | Magnifier: a lens works along one line of a diff, then back along the next | `⌕-+` `+⌕+` `+-⌕` in violet |

A role with no animation of its own plays a dim spinner, `⠋⠙⠹`, in the theme's colour.

A Lead run started for one card (feedback on it, or the Idea it's pitching), a Customer lead run on
its docs PR and a Reviewer run on a task's PR animate on that card. A run working across the whole
board animates in the lane's title only. Within a column, the card that has waited longest is first.
A column shows as many rows as fit with every lane on screen, and at least three, and puts the rest
behind a count, as in `+7`.

## Moving around

| Key | Does |
|---|---|
| Arrows | `←` and `→` go across columns, `↑` and `↓` along a column and on into the next lane |
| `Tab`, `Shift+Tab` | Go to the next or previous team's lane |
| `PgUp`, `PgDn` | Scroll the lanes a screen at a time, when they don't all fit |
| `Enter` | Opens the session of the agent on the selected card full-screen. On a `+7`, shows the whole lane |
| `Esc` | Folds the lane back |
| `i` | Interrupts the agent on the selected card and puts you in its conversation |
| `g` | Opens the card on GitHub |
| `b` | Opens the card's team's Project board on GitHub |
| `r` | Goes to the agent's session: the Dashboard, with the agent working on the card expanded |
| `r` | Reads again the boards that couldn't be read, when the card has no agent on it |
| `F5` | Reads every board again |

## The card's panel

At 140 columns or wider, the selected card has a panel on the right, about two fifths of the
window. Narrower, its details sit at the foot of the lanes instead, without the session.

The panel gives the card's title, what it is, its Priority, team and column, how long it has
been there in full (`6d 2h 10m`), the column's limit if it has one, and why it's waiting where Work
says. For a task waiting on a change in another repo, that's how far the change has got:
`Waiting on tui-style-guide#84: merged, not released`. A task already in review says so on a line
of its own: `Also waits on tui-style-guide#100 · in review, yours`. A task held for you says who held it and why, as Work's Blocked card does: `Blocked: held by the Lead · …`. For a card another task waits on, it gives the
chain, as on Work's status bar: `a-team #499 waits on this · then MentalDesk.Tui's release`. For a pitch, it lists the open tasks under it,
each with its column and age.

Below the details is the session of the agent on the card, following live as it works: who's running,
for how long and why, as in `Dev · running 14:02 · why: claimed #602`, then the tail of its log. With
nothing running on the card, it shows the last run there was on it and when, as in
`Dev · last ran 3h12m ago`, or says no agent has run on it yet. A Lead or Customer lead run is found only
while it's that role's latest. The panel's last line lists the keys that work on the card. Moving the
selection switches the panel to the new card straight away.

`Enter` opens the session full-screen, as an expanded pane on the [Dashboard](dashboard.md) does:
`↑` and `↓` select lines, `Shift` with them extends the selection, `l` copies the selected lines and
`a` the whole log, `e` opens it in your editor, `Ctrl+T T` shows tool calls in full, and `PgUp`,
`PgDn`, `Home` and `End` scroll. `Esc` comes back to the same card.

`i` interrupts the agent on the card and puts you in its conversation, as on the Dashboard; a run too
new to have one is stopped instead, once you've said so. Quitting the conversation brings you back to
the same card. A run you stopped says `held` in the panel, and `i` lets it start again.

With the [Assistant](dashboard.md#the-assistant) open too, the panel has two tabs, *Details* and *Assistant*.
`Ctrl+G I` puts *Details* in front with focus in it, `Ctrl+G A` does the same for the Assistant, and
`F6` moves focus between the lanes and the panel. Closing the Assistant leaves *Details*. Below 140
columns the Assistant stays on the right on its own.

Overseer reads the boards when you open it and on `F5`, and the status bar says how long ago the
selected card's board was read. Ages and animations keep moving between reads. Each lane is read on
its own, and says so in the lane while it reads, when its board has nothing on it, or when it
couldn't be read, with `r retry` under the reason; the other lanes keep their cards. A board that
came back with items missing says `Board read incomplete (<got> of <total> items)` instead of
showing some of its cards.

## Time limits

**Settings → Overseer** has a field for each column. Put a number and a unit in one, as in `30m`,
`2h`, `3d` or `2w`, and a card that stays in that column longer has its age drawn in amber. A
blank field is no limit, and a column without one never highlights. Settings won't keep an entry it
can't read, and says which column it's in.
