# Dashboard

What each agent is doing, one pane per agent. `d` opens it, and `w` goes back to [Work](work.md).
It shows every team you have, paused or not. `a-team dashboard [team...]` opens the app here, on
just the teams you name.

## A pane

The pane's title names the agent and says how it's doing:

| Icon | Means |
|---|---|
| `●` | Running now |
| `⏸` | Paused, or held by you |
| `✓` | Its last run finished cleanly |
| `✗` | Its last run failed. The pane's border, title and status row stay in the error colour until the next run clears it |
| `○` | It has never run |

Where a Nerd Font is in effect, the pane wears that font's glyph for each instead.

Under the title is how long the current run has been going, or when the role last ran and the
countdown to the dispatcher's next check. Then why the run was started, and then the run's log as
it happens: what the agent said, the tools it called, any errors, and how the run finished.

The strip along the bottom is the dispatcher's recent decisions. Each run it starts names the
release it's on, as in `09:51 a-team lead: started 81834 on 0.1.7: #232 was approved`, and keeps
that release to its end, even if you upgrade a-team meanwhile.

## Reading a log

| Key | Does |
|---|---|
| `Tab`, arrows | Select an agent. The selected one wears `▶` |
| `Enter` | Expand the selected agent over the whole agent area, wide enough to read without scrolling |
| `Tab`, `Shift+Tab` | In the expanded view, read the next or previous agent without leaving it |
| `Esc` | Back to every agent |
| `PgUp`, `PgDn`, `Home`, `End` | Scroll the selected log. Scrolling up stops it following new output until you press `End` |

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

From a shell, `a-team stop <team> <role>` stops a run and holds the role, and
`a-team attach <team> <role>` steps into its last run.

While any of these is going, a line at the foot of the window says so. If one fails, the line says
why, in red.

## Commands and keys

`Ctrl+E` opens Commands: everything the app can do, with the key for each. Type to narrow the list,
`Enter` to run one. Every key on this page is one of those commands, so anything you can press you
can also run by name, and you can change its key under
[Settings → Keyboard Shortcuts](teams.md#the-rest-of-settings).

`F1` lists the few keys worth knowing first, and `q` quits the app.
