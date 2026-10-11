# Overseer

Every team's board on one screen, with how long each card has sat where it is. `o` opens it, `w` goes
to [Work](work.md) and `d` to the [Dashboard](dashboard.md). It only shows: approving, accepting and
commenting stay in Work.

## Lanes and chips

Each team is a lane, in the order your teams are listed, under one row of column headings: Idea,
Exploring, Pitched, Approved, Building, Ready, In progress and In review. Done isn't shown.

A lane's title names the team and its roles that are running now, each with its animation playing
beside it, as in `a-team · Lead ✺ · Dev >`, or says `idle`. Where cards are past their column's limit, it counts them: `2 over`.

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
turns amber only once the card is past its column's limit. A card an agent is working on plays
its role's animation in place of its age:

| Role | Animation | Looks like |
|---|---|---|
| Dev | Decrypt: scrambled symbols lock into `</>`, `{;}`, `f()` or `0x1` | `ｱ#ﾘ` `</ｶ` `</>` in green |
| Lead | Thought to idea: thought bubbles rise and burst into a spark | `.oO` `  ✺` `. ✧` in amber |
| Customer lead | Typing: three dots bounce like someone typing | `⠶⠛⣤` `⣤⣤⣤` in blue |

A Lead run started for one card (feedback on it, or the Idea it's pitching) and a Customer lead run on
its docs PR animate on that card. A run working across the whole board animates in the lane's title
only. Within a column, the card that has waited longest is first. A column shows as many rows as
fit with every lane on screen, and at least three, and puts the rest behind a count, as in `+7`.

## Moving around

| Key | Does |
|---|---|
| Arrows | `←` and `→` go across columns, `↑` and `↓` along a column and on into the next lane |
| `Tab`, `Shift+Tab` | Go to the next or previous team's lane |
| `PgUp`, `PgDn` | Scroll the lanes a screen at a time, when they don't all fit |
| `Enter` | Opens or closes the details of the selected card. On a `+7`, shows the whole lane |
| `Esc` | Folds the lane back |
| `g` | Opens the card on GitHub |
| `b` | Opens the card's team's Project board on GitHub |
| `r` | Goes to the agent's session: the Dashboard, with the agent working on the card expanded |
| `F5` | Reads every board again |

The details pane gives the card's title, what it is, its Priority, team and column, how long it has
been there in full (`6d 2h 10m`), the column's limit if it has one, and why it's waiting where Work
says. For a pitch, it lists the open tasks under it, each with its column and age.

Overseer reads the boards when you open it and on `F5`, and the status bar says how long ago that
was. Ages and animations keep moving between reads.

## Time limits

**Settings → Overseer** has a field for each column. Put a number and a unit in one, as in `30m`,
`2h`, `3d` or `2w`, and a card that stays in that column longer has its age drawn in amber. A
blank field is no limit, and a column without one never highlights. Settings won't keep an entry it
can't read, and says which column it's in.
