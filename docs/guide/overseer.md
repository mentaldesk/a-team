# Overseer

Every team's board on one screen, with how long each card has sat where it is, and what each team's agents
are doing. `Ctrl+G O` opens it and `Ctrl+G W` goes to [Work](work.md). Approving, accepting and commenting stay in
Work; here you read the agents' sessions, interrupt a run and pause a role. `a-team dashboard [team...]` opens the
app here, on just the teams you name.

## Lanes and chips

Each team is a lane, in the order your teams are listed, under one row of column headings: Idea,
Exploring, Pitched, Approved, Building, Ready, In progress and In review. Done isn't shown.

A lane's title names the team and its roles that are running now, each with its animation playing
beside it, as in `a-team · Lead ✺ · Dev >`, or says `idle`. A [stuck role](#a-stuck-role) is named on its own
instead of as running, as in `a-team · Dev ⚠ stuck · Reviewer ⚠ stuck`. Where cards are past their column's limit, it counts them: `2 over`.
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

A chip's mark and number are drawn in its Priority's colour, which for a task with none of its own is its
pitch's. Its age is in the theme's own colour, and
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
board animates in the lane's title only. Within a column, the highest Priority is first, and among equals the
card that has waited longest.
A column shows as many rows as fit with every lane on screen, and at least three, and puts the rest
behind a count, as in `+7`.

## Moving around

| Key | Does |
|---|---|
| Arrows | `←` and `→` go across columns, `↑` and `↓` along a column. `↑` from a lane's top row selects its heading, and `↓` from its last row goes on to the next lane's |
| `Tab`, `Shift+Tab` | Go to the next or previous team's lane |
| `PgUp`, `PgDn` | Scroll the lanes a screen at a time, when they don't all fit |
| `Enter` | Opens the session of the agent on the selected card full-screen, or the chosen role's latest on a heading. On a `+7`, shows the whole lane |
| `Esc` | Folds the lane back |
| `i` | Interrupts the agent on the selected card, or the chosen role on a heading, and puts you in its conversation |
| `h` | On a heading, pauses the chosen role, or lets it start again |
| `g` | Opens the card on GitHub |
| `b` | Opens the team's mirror, its Project board on GitHub, if it has one |
| `r` | Reads again the boards that couldn't be read |
| `F5` | Reads every board again |

## Reading the boards

Overseer reads the boards when you open it and on `F5`, and the status bar says how long ago the
selected card's board was read. Ages and animations keep moving between reads. Each lane is read on
its own, and says so in the lane while it reads, when its board has nothing on it, or when it
couldn't be read, with `r retry` under the reason; the other lanes keep their cards. A board that
came back with items missing says `Board read incomplete (<got> of <total> items)` instead of
showing some of its cards.

## The card's panel

At 140 columns or wider, the selected card has a panel on the right, about two fifths of the
window. Narrower, its details sit at the foot of the lanes instead, without the session.

The panel gives the card's title, what it is, its Priority, team and column, how long it has
been there in full (`6d 2h 10m`), the column's limit if it has one, then
[what a-team keeps about it](how-a-team-works.md#what-a-card-says-about-an-item), like the Lead's rank
or a shelve, and why it's waiting where Work says. For a task waiting on a change in another repo, that's how far the change has got:
`Waiting on tui-style-guide#84: merged, not released`. A task already in review says so on a line
of its own: `Also waits on tui-style-guide#100 · in review, yours`. A task held for you says who held it and why, as Work's Blocked card does: `Blocked: held by the Lead · …`. For a card another task waits on, it gives the
chain, as on Work's status bar: `a-team #499 waits on this · then MentalDesk.Tui's release`. For a pitch, it lists the open tasks under it,
each with its column and age.

Below the details is the session of the agent on the card, following live as it works: who's running,
for how long and why, as in `Dev · running 14:02 · why: claimed #602`, then the tail of its log. With
nothing running on the card, it shows the last run there was on it and when, as in
`Dev · last ran 3h12m ago`, or says no agent has run on it yet. The panel's last line lists the keys that work on the card. Moving the
selection switches the panel to the new card straight away.

`Enter` opens the session full-screen, as [Reading a session](#reading-a-session) describes. `Esc`
comes back to the same card.

`i` interrupts the agent on the card, as [Interrupting a run](#interrupting-a-run) describes. Quitting
its conversation brings you back to the same card. A run you stopped says `held` in the panel, and `i`
lets it start again.

With the [Assistant](app.md#the-assistant) open too, the panel has two tabs, *Details* and *Assistant*.
`Ctrl+G I` puts *Details* in front with focus in it, `Ctrl+G A` does the same for the Assistant, and
`F6` moves focus between the lanes and the panel. Closing the Assistant leaves *Details*. Below 140
columns the Assistant stays on the right on its own.

## The team's panel

`↑` from a lane's top row selects the lane's heading, and the panel shows the team instead of a card.
Below 140 columns it sits at the foot of the lanes. It has a line per role, each with an icon for how
it's doing:

| Icon | Means |
|---|---|
| `⚠` | The team is misconfigured and can't run, which wins over every other icon, or the role is [stuck](#a-stuck-role), as its line says |
| `●` | Running now |
| `⏸` | Paused, or held by you |
| `✓` | Its last run finished cleanly |
| `✗` | Its last run failed, or was cut short |
| `○` | It has never run |

Where a Nerd Font is in effect, each wears that font's glyph instead.

Then the role's name and what it's doing: `running 14:02 · #602` with the cards it's on, or
`idle · ran 12m ago · next check 3:41`, adding `failed` or `cut short` where its last run didn't
finish cleanly. Where the dispatcher isn't checking, the line says so instead of counting down:
`dispatcher hasn't run`, `checking now` while a pass runs long, or `dispatcher not running`. A role you
paused says `paused · ran 12m ago`, or `paused · stopped by you 3m ago` if you stopped its run, and
one whose team is paused says `team paused`.

The app checks each team when it starts and again whenever the team's file changes, the same check
**Teams** shows. If something stops the team running, every role wears `⚠`, the panel
says what's wrong above them, as in `misconfigured: checkout ~/code/TuiCode/main isn't there: …`,
and the status bar says which checks failed. Fix it in **Teams** or in the file, and the
`⚠` clears. A missing vision, docs page or label is only a note, and changes nothing here.

The keys act on the chosen role, the first until you choose another. `F6` or `Ctrl+G I` puts focus
in the panel, where `↑` and `↓` choose a role, and `F6` goes back to the lanes. `Enter` opens the
role's latest session full-screen, as [Reading a session](#reading-a-session) describes, and `Esc`
comes back to the team. `h` pauses the role or lets it start again ([Pausing a role](#pausing-a-role)),
and `i` interrupts it ([Interrupting a run](#interrupting-a-run)). The panel's last line lists the keys
that work now.

### A stuck role

A role is stuck when the dispatcher counts a run as live that isn't doing its work: the run is still alive on the
pass after it was stopped for reaching its time limit, or its task or PR closed or went Done more than 30 minutes
ago. A run that stops when told, or that closes its own card and finishes up, never warns, and neither does a
long run with work waiting behind it. A Lead or Customer lead run that isn't on a card can only be stuck by its time limit.

The role's line wears `⚠` and says what's wrong, which run, and what's waiting on its slot:

```
⚠ Dev       stuck · run 67118 outlived its 2h limit by 19d · holds 1 of 3 slots
⚠ Reviewer  stuck · run 51200 is on #516, closed 29 Oct · holds its only slot, 3 PRs waiting
```

Where the panel is too narrow for the line, it says it in full again below the roles. Where a role has more than one stuck run, the line describes the oldest and adds `+1 more`. `a-team status` prints
a line for each, as in `a-team reviewer: stuck: run 51200 is on #516, closed 29 Oct · holds its only slot, 3 PRs waiting`.

It only reports: the dispatcher starts nothing in its place and frees no slot, so the run is yours to look into.
`Enter`, `h` and `i` work on its line as on any other. There's nothing to dismiss: once the run has gone, the
warning clears by itself, from the title, the panel and `a-team status`, even if the team is paused or the dispatcher is stopped.

## Reading a session

A session full-screen shows the run's log as it happens: what the agent said, the tools it called,
any errors, and how the run finished. One line is highlighted, starting on the last. `↑` and `↓` move
it a line at a time, and `Shift+↑`, `Shift+↓` take in more. Moving it stops the log following new
output until you press `End`, and `PgUp`, `PgDn`, `Home` and `End` scroll, bringing it with you.

`l` copies the highlighted lines and `a` the whole log, each line in full: the whole command or error,
not the clipped row on screen. A folded run of tool calls copies the one it shows. The status bar
says how much went. With the [Mac keys](app.md#mac-keys) installed, `Cmd+C` copies as `l` does. Over
SSH, copies reach your own clipboard as [Copying and links](app.md#copying-and-links) says.

A run of tool calls draws as a single row, so the agent's own words aren't pushed off the top.
`Ctrl+T T` shows every call, and `Ctrl+T T` again folds them back up. To start every session with its
calls showing, tick *Show tool calls in full* under [Settings → Dashboard](teams.md#settings).

The panel keeps only the last 500 lines. `e` opens the whole session as text in your editor: every
line from the start, each command and error in full. It uses `$VISUAL`, or `$EDITOR` if that isn't
set, or `less` if neither is; set `EDITOR` to `tuicode` to read it there. Quit the editor and you're
back in the session where you left it. If the editor can't start or exits with an error, the status
bar says so.

## Pausing a role

`h` on a team's panel pauses the chosen role. A run already going finishes, its line reading
`running <time> · paused`, and the dispatcher starts no new one. The same key, now
*Let this role start again*, lets it start again. The other roles carry on either way.

It's the same as `a-team pause <team> <role>` and `a-team resume <team> <role>`.

## Interrupting a run

`i` interrupts the agent on the selected card, or the chosen role on a team's panel, and puts you in
its run: the app steps aside for the run's own conversation, with everything it had worked out, so
you can see what went wrong and steer it. When you quit that conversation you're back on the same card
or team, and the role is free to start again.

A run too new to have a conversation yet is just stopped, and the role held. A Dev or Reviewer run
on a task asks first, naming its task, and `Ctrl+Enter` stops it; its task stays where it is on the
board and no run starts on it until you let it start again, while new tasks still start. On a held
role or run the same key reads *Let it start again*. A Dev on several cards at once is interrupted
from the card you want stopped.

From a shell, `a-team stop <team> <role>` stops a run and holds the role, and
`a-team attach <team> <role>` steps into its last run. Add a task number to either, and to
`a-team resume`, to act on that one Dev run alone.

While any of these is going, a line at the foot of the window says so. If one fails, the line says
why, in red. Pausing and interrupting never make you wait: they queue behind anything else being
written to the board, as in [Work](work.md#while-its-being-written).

## The dispatcher's log

**View → Dispatcher log** opens the dispatcher's whole log full-screen, from any area, and `Esc` comes
back. Each run it starts names the release it's on, as in `09:51 a-team lead: started 81834 on
0.1.7: #232 was approved`, and keeps that release to its end, even if you upgrade a-team meanwhile, unless the upgrade moves the
board's format, as below.
A team that can't run on two passes in a row gets one line saying why, as in
`a-team: stopped: no GitHub App: run a-team app create a-team, then install it`, another only if
the reason changes, and `a-team: running again` once it can. A single failed pass, such as one
during `brew upgrade`, says nothing, though the team still sits that pass out.
If the board is in a newer format than the a-team the dispatcher runs, as after a downgrade, every team
stops the same way, as in `a-team: stopped: the board is in format 4, from a-team 0.2.17, and this is
0.2.11: brew upgrade a-team`. The message bar says the same, each lane's title says `stopped`, and in the
panel of a lane's heading each role wears `⚠` above the same line until you upgrade. A release that moves the
board's format moves it on the dispatcher's next pass. A run still on the release before stops at its
next board command with a line saying so, and the next pass carries on from the board, so there's
nothing for you to do.
A role whose check for work fails is the same: two passes in a row give one red line, as in
`a-team dev: triggers failed: gh: connection reset`, and `a-team dev: triggers working again` once
it passes. A dependency bump it merges has its own line, as in
`a-team: PR #653 (Bump MentalDesk.Tui from 0.5.2 to 0.6.0): merged`. A [stuck run](#a-stuck-role) gets one red line when it starts, as in
`a-team reviewer: run 51200 is stuck: on #516, which closed 29 Oct`, and one when it clears, `a-team reviewer: run 51200
no longer stuck`, and a run past its limit gets one `killed run` line however many passes it stays alive. `PgUp`, `PgDn`, `Home` and `End` scroll it.

Its title says what is driving the teams: the a-team the dispatcher runs and its version, with the
countdown to its next pass, as in `dispatcher · /opt/homebrew/bin/a-team 0.1.12 · next pass 1:12`.
It turns red when nothing will start: `dry run: nothing will actually start`, `stopped 14m ago`,
or `nothing installed · run: a-team install`. A pass that runs long, starting runs or waiting on
GitHub, holds the countdown at `next pass 0:00` rather than counting as stopped. A dispatcher installed
before this title existed shows plain `dispatcher` until you next run `a-team install`. `a-team status`
opens with the same line.

When a pass breaks before it can log anything, as with a syntax error, what the dispatcher printed
goes to its own output instead. If that is newer than the log's last line, the status bar says
`dispatcher: last pass failed` with its last line, in red, until a later pass logs again. The title
ends `· last pass failed`, and the log ends with the last of that output under `── the dispatcher's
own output since then ──`.

Answered something a team was waiting on? **Run a dispatch pass now**, in the Commands palette
(`Ctrl+E`) or under **Agents**, has that dispatcher check for work straight away instead of at its
next pass. It starts only what that pass would have, within the same limits, and leaves the
countdown alone. The status bar says what it started, or `Pass done: nothing to start.`

## Time limits

**Settings → Overseer** has a field for each column. Put a number and a unit in one, as in `30m`,
`2h`, `3d` or `2w`, and a card that stays in that column longer has its age drawn in amber. A
blank field is no limit, and a column without one never highlights. Settings won't keep an entry it
can't read: **Keep** opens this page instead, turns the name of every such column red and moves you to the
first, and the foot says which column you're on, how to write a limit, and how many more there are.
