# Dashboard

What each agent is doing, one pane per agent. `d` opens it, `w` goes back to [Work](work.md) and `o`
to [Overseer](overseer.md).
It shows every team you have, paused or not. `a-team dashboard [team...]` opens the app here, on
just the teams you name.

## A pane

The pane's title names the agent and says how it's doing:

| Icon | Means |
|---|---|
| `⚠` | The team is misconfigured and can't run. This wins over every other icon |
| `●` | Running now |
| `⏸` | Paused, or held by you |
| `✓` | Its last run finished cleanly |
| `✗` | Its last run failed. The pane's border, title and status row stay in the error colour until the next run clears it |
| `○` | It has never run |

Where a Nerd Font is in effect, the pane wears that font's glyph for each instead.

Under the title is how long the current run has been going, or when the role last ran and the
countdown to the dispatcher's next check. Then why the run was started, and then the run's log as
it happens: what the agent said, the tools it called, any errors, and how the run finished.

The dashboard checks each team when it starts and again whenever the team's file changes, the same
check **Settings → Teams** shows. If something stops the team running, all its panes wear `⚠` in
the error colour, their status row says what's wrong (as in `misconfigured: checkout
~/code/TuiCode/main isn't there: …`), and the message bar says which checks failed. Fix it in
**Settings → Teams** or in the file, and the `⚠` clears. A missing vision, docs page or label is only a note,
and changes nothing here.

The strip along the bottom is the dispatcher's recent decisions. Each run it starts names the
release it's on, as in `09:51 a-team lead: started 81834 on 0.1.7: #232 was approved`, and keeps
that release to its end, even if you upgrade a-team meanwhile.
A team that can't run on two passes in a row gets one line saying why, as in
`a-team: stopped: no GitHub App: run a-team app create a-team, then install it`, another only if
the reason changes, and `a-team: running again` once it can. A single failed pass, such as one
during `brew upgrade`, says nothing, though the team still sits that pass out.
A role whose check for work fails is the same: two passes in a row give one red line, as in
`a-team dev: triggers failed: gh: connection reset`, and `a-team dev: triggers working again` once
it passes.

Its title says what is driving the teams: the a-team the dispatcher runs and its version, with the
countdown to its next pass, as in `dispatcher · /opt/homebrew/bin/a-team 0.1.12 · next pass 1:12`.
It turns red when nothing will start: `dry run: nothing will actually start`, `stopped 14m ago`,
or `nothing installed · run: a-team install`. A pass that runs long, starting runs or waiting on
GitHub, holds the countdown at `next pass 0:00` rather than counting as stopped. A dispatcher installed before this title existed
shows plain `dispatcher` until you next run `a-team install`. `a-team status` opens with the same
line.

The strip is the last stop after the agents: `Tab` past the last agent, or `Down` from the bottom
row, selects it, and `Up` goes back. `Enter` expands it over the agents to show the whole log, and
the scroll keys below work on it as on a pane. `Esc` puts it back.

When a pass breaks before it can log anything, as with a syntax error, what the dispatcher printed
goes to its own output instead. If that is newer than the log's last line, the title ends `· last
pass failed` in red, and the expanded log ends with the last of that output under `── the
dispatcher's own output since then ──`.

Answered something a team was waiting on? **Run a dispatch pass now**, in the Commands palette
(`Ctrl+E`) or under **Agents**, has that dispatcher check for work straight away instead of at its
next pass. It starts only what that pass would have, within the same limits, and leaves the
countdown alone. The message bar says what it started, or `Pass done: nothing to start.`

## Reading a log

| Key | Does |
|---|---|
| `Tab`, arrows | Select an agent. The selected one wears `▶` |
| `Enter` | Expand the selected agent over the whole agent area, wide enough to read without scrolling |
| `Tab`, `Shift+Tab` | In the expanded view, read the next or previous agent without leaving it |
| `Esc` | Back to every agent |
| `b` | Open the selected agent's team's Project board on GitHub |
| `PgUp`, `PgDn`, `Home`, `End` | Scroll the selected log. Scrolling up stops it following new output until you press `End` |

## Copying from a log

In the expanded view one line of the log is highlighted, starting on the last. `Up` and `Down` move
it a line at a time, and `Shift+Up`, `Shift+Down` take in more. Moving it stops the log following
new output until you press `End`, and paging brings it with you.

`l` copies the highlighted lines and `L` the whole log, each line in full: the whole command or error,
not the clipped row on screen. A folded run of tool calls copies the one it shows. The status bar
says how much went. With the [Mac keys](#mac-keys) installed, `Cmd+C` copies as `l` does.

Over SSH, every copy in the dashboard reaches your own computer's clipboard, sent through the
terminal, and the status bar ends `through the terminal`. In tmux that needs `set -g set-clipboard on`
in `~/.tmux.conf`, and in iTerm2 *Applications in terminal may access clipboard* (Settings → General
→ Selection).

## Reading the whole session

The pane keeps only the last 500 lines. `e`, in the expanded view, opens the whole session as text
in your editor: every line from the start, each command and error in full. It uses `$VISUAL`, or
`$EDITOR` if that isn't set, or `less` if neither is; set `EDITOR` to `tuicode` to read it there.
Quit the editor and you're back in the pane where you left it. If the editor can't start or exits
with an error, the status bar says so.

## Tool calls

A run of tool calls draws as a single row, so the agent's own words aren't pushed off the top. `t`
shows every call in the selected pane, and its title says `[tool calls]`; `t` again folds them back
up. To start every pane with its calls showing, tick *Show tool calls in full* under
[Settings → Dashboard](teams.md#the-rest-of-settings).

## Pausing a role

`h` pauses the selected agent's role. A run already going finishes, its pane reading
`running <time> · held`, and the dispatcher starts no new one. The same key, now
*Let this role start again*, lets it start again. The other roles carry on either way.

It's the same as `a-team pause <team> <role>` and `a-team resume <team> <role>`.

## Interrupting a run

`i` interrupts the selected agent's run and puts you in it: the app steps aside for the run's own
conversation, with everything it had worked out, so you can see what went wrong and steer it. When
you quit that conversation you're back on the Dashboard, and the role is free to start again.

A run too new to have a conversation yet is just stopped, and the role held. On a held role the same
key reads *Let it start again*.

With several Dev runs going, `i` acts on the run the Dev pane shows, and the others keep going.
Stopping one asks first, naming its task, and `Ctrl+Enter` stops it. Its task stays where it is on
the board and no run starts on it until you let it start again, while new tasks still start. `h` still pauses the whole role.

From a shell, `a-team stop <team> <role>` stops a run and holds the role, and
`a-team attach <team> <role>` steps into its last run. Add a task number to either, and to
`a-team resume`, to act on that one Dev run alone.

While any of these is going, a line at the foot of the window says so. If one fails, the line says
why, in red. Pausing and interrupting never make you wait: they queue behind anything else being
written to the board, as in [Work](work.md#while-its-being-written).

## The Assistant

**View → Assistant**, or **Assistant** in Commands, opens a Claude Code conversation down the right of
whichever area you're in, with the cursor in its question box. Choosing it again closes it. It has
no key of its own; give it one under [Settings → Keyboard Shortcuts](teams.md#the-rest-of-settings).
It stays open as you move between Work, Overseer and the Dashboard.

Ask it anything about your teams: why a card has sat so long, what a team spent yesterday, why a
Dev hasn't run since lunch. It already knows how a-team works, where each team's config, logs and
run record are, and what you're looking at: the line above the box says what the next question is
about, like *on #499 in Work*, and your question's heading repeats it, so you never have to name the
card or agent. With more than one team, the card's team is named too.

The answer streams in, with a `▸` line for each thing it looks at. It runs in Claude Code's Auto
mode, so it reads and runs commands without asking unless Auto's check flags one. Then it asks you,
in a dialog titled *Claude Code wants to run a command*:
`Ctrl+Enter` allows that one command, and `Esc` denies it. Deny starts focused. To change anything it
goes through the board as you do, so it can't make a move you couldn't.

`Enter` sends, `Esc` stops the answer and keeps what has arrived, and `Ctrl+L` starts a new
conversation. `F6` moves between the Assistant and the area beside it. The last conversation is
there again the next time you open the app. If Claude Code can't start or isn't signed in, the pane
says so in a line, with the command that signs you in.

## Commands and keys

`F1` shows the keys. *Everywhere*, on the left, lists the ones that work anywhere, under the menu
that holds them. Beside it, headed with where you are (Work, Overseer, the agent grid, an expanded
log or the Assistant), are the ones for there. *Help → Show keys* and **Show keys** in Commands open it too, and
`Esc` or its *Esc Close* button closes it. *Press F1 for help*, at the right end of the status bar,
is the only key it names. The rest of it starts with where you are (Work's filter, or Overseer) and
when the board was last read, then says what's selected or going on. The app's version is in
*Help → About*.

In a dialog, what you can do to it is a row of buttons at its foot, each with its key first, like
*Ctrl+Enter Add* or *Esc Cancel*; click one or press its key. `F1` there shows that dialog's keys,
headed with its title, including the ones with no button, like scrolling, selecting and copying.
`Esc` closes it and puts you back where you were, a half-written comment and its cursor included.

`Ctrl+E` opens Commands: everything the app can do where you are, with the key for each. Type to
narrow the list. Letters that start words match first, so `gt` finds **Go to team…**, then names
that hold the letters anywhere, best match first; `Enter` runs the one at the top. Every key on
this page is one of those commands, so anything you can press you can also run by name, and you
can change its key under [Settings → Keyboard Shortcuts](teams.md#the-rest-of-settings).

The menus list everything, wherever you are. An item that can't run here is dimmed, with its key
beside it, and does nothing. With a menu open, each item's underlined letter is its own: press it to
run that item. `q` quits the app.

## Mac keys

A Mac terminal keeps `Cmd` to itself, so out of the box `Cmd+←` and the rest do nothing in the app.
In iTerm2, kitty or WezTerm, **Settings → Terminal** fixes that: it names the terminal you're in and
whether the dashboard's Mac keys are *Not installed*, *Installed* or *Out of date*. **Install** adds
them, **Update** brings them up to date after an a-team upgrade changes them, and **Remove** takes
them away. In kitty and WezTerm the page then shows the one line to add to your terminal's config,
and the file it goes in, with **Copy** beside it. After adding it, reload kitty's config with
`Ctrl+Cmd+,` (kitty reads it only then), or in WezTerm reopen the dashboard.

Once installed, in every text box:

| Keys | Does |
|---|---|
| `Cmd+←` / `Cmd+→` | Start / end of the line |
| `Cmd+↑` / `Cmd+↓` | Start / end of the text |
| `Option+←` / `Option+→` | Previous / next word |
| Any of those with `Shift` | Select as it moves |
| `Cmd+Backspace` / `Option+Backspace` | Delete to the start of the line / the previous word |
| `Cmd+A`, `Cmd+C`, `Cmd+X`, `Cmd+Z` | Select all, copy, cut, undo |

They apply only while the dashboard is in front: your shell, TuiCode and anything else in the same
terminal keep their own keys, and TuiCode's own Mac keys are a separate install that removing these
leaves alone. Ghostty, Alacritty and Terminal.app can't change keys for one app, so there the page
says so; `Option+←`/`→` and `fn+←`/`→` work in them without it.

## The Guide

**Help → Guide**, or **Guide** in Commands, opens these pages on the one for where you are. Beside
the page, **Contents** lists every page with its sections under it, the one you're reading open and
the section you're at selected. `↑`/`↓` move, `→`/`←` open and close a page, and `Enter` opens that
page at that section. **Index** lists every section by name, its page under it: type to narrow it,
so `pau` finds *Pausing a role* and *Pausing a team*, and `Enter` opens the one selected.
`Ctrl+Shift+C` goes to Contents and `Ctrl+Shift+I` to Index from anywhere in the Guide. In a page,
`Tab` moves to the next link, `Enter` follows it, `Backspace` goes back, and `Esc` closes the Guide.

## Confirming

Approving a pitch, accepting work, stopping a run, removing a team and throwing away something you've written all ask
first. `Ctrl+Enter` goes ahead, `Esc` cancels, and `Enter` does nothing, so a stray one can't.
`Tab` or `←`/`→` move between the buttons and `Space` presses one; the `Esc` button starts focused. In a
terminal that can't tell `Ctrl+Enter` from `Enter`, such as macOS Terminal, `Tab` to the action's
button and press `Space`.
