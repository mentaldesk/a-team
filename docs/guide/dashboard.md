# Dashboard

What each agent is doing, one pane per agent. `d` opens it, and `w` goes back to [Work](work.md).
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
check **Settings → Teams** shows. If something stops the team running, both its panes wear `⚠` in
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
says how much went.

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
*Let this role start again*, lets it start again. The other role carries on either way.

It's the same as `a-team pause <team> <role>` and `a-team resume <team> <role>`.

## Interrupting a run

`i` interrupts the selected agent's run and puts you in it: the app steps aside for the run's own
conversation, with everything it had worked out, so you can see what went wrong and steer it. When
you quit that conversation you're back on the Dashboard, and the role is free to start again.

A run too new to have a conversation yet is just stopped, and the role held. On a held role the same
key reads *Let it start again*.

With several Dev runs going, `i` acts on the run the Dev pane shows, and the others keep going.
Stopping one asks first, naming its task. Its task stays where it is on the board and no run starts
on it until you let it start again, while new tasks still start. `h` still pauses the whole role.

From a shell, `a-team stop <team> <role>` stops a run and holds the role, and
`a-team attach <team> <role>` steps into its last run. Add a task number to either, and to
`a-team resume`, to act on that one Dev run alone.

While any of these is going, a line at the foot of the window says so. If one fails, the line says
why, in red.

## Commands and keys

`Ctrl+E` opens Commands: everything the app can do, with the key for each. Type to narrow the list,
`Enter` to run one. Every key on this page is one of those commands, so anything you can press you
can also run by name, and you can change its key under
[Settings → Keyboard Shortcuts](teams.md#the-rest-of-settings).

`F1` lists the few keys worth knowing first, and `q` quits the app.
