# Teams

Your teams are in **Settings → Teams**: `s` opens Settings, and **Teams** in Commands (`Ctrl+E`)
opens it on that page. `F1` in Settings opens this page.

## The Teams page

A row per team: its name, its repo, whether it's `working` or `paused`, and its health.

| Key | Does |
|---|---|
| `p` | Pause the selected team, or start it working again. It happens straight away |
| `Enter` | Open the team's settings |
| `x` | Remove the team |
| `n` | Start a new team |

## Starting a team

`n` on the Teams page, or **New team** in Commands, opens the form for a new team. Fill it in and
press `Enter`. A-Team writes the team's file, paused, then offers in turn to:

1. Clone the repo.
2. Give the team its GitHub App, and install it on the repo.
3. Create its Project.
4. Set its board up.
5. Get to work.

Each step says what it will do and waits for `Enter`, and `Esc` skips it. The last asks whether to
get to work or keep the team paused, and there `Esc` cancels the new team.

`gh` needs to be able to manage projects first: `gh auth refresh -s project`.

## A team's settings

`Enter` on a team opens its form: repo, stakeholders, project, vision, workdir, skills, the command
`a-team try` runs, whether it's working or paused, and its limits. Each field says what it's for
underneath. `Enter` saves them into the team's file, leaving everything else in it as it was, and
`Esc` cancels.

## Pausing a team

A paused team picks up no new work, and a run already going finishes. `p` on the Teams page, or the
team's own form, pauses it and starts it again. To pause one role and not the other, use `h` on the
[Dashboard](dashboard.md#pausing-a-role).

## Removing a team

`x` asks first, then A-Team forgets the team. Its repo, its board and everything it has built are
untouched, and its file is kept as `<team>.json.removed`. Rename it back to `<team>.json` to bring the
team back.

## When a team can't run

The **Health** column reads `checking…` while A-Team checks each team, then `ok` or how many problems
it found.

- **Problems**: open the team, and its form lists them at the top, one a line, each naming what it's
  about and what's wrong. A board that's missing a Status option or a label can be set
  up again with `F12` (*Repair*).
- **`can't read this file`**: the team's file isn't valid. Select it, and the foot of the page names
  the line that's wrong. Fix it in the file; the team can't be started or paused until you do.

`a-team board <team> check` reports the same problems from a shell.

## The rest of Settings

Settings is a page at a time: the pages down the left, the one you picked on the right. `Tab` goes
into the page and back out. `Ctrl+Enter` keeps what you picked, on any page, and `Esc` throws it
away.

- **Theme**: Midnight, Daylight, Turbo Pascal or Modern Borland, the same four as TuiCode.
- **Keyboard Shortcuts**: a row per command and the key that runs it. `Enter` on a row takes the
  next key you press, and a key another command already holds is refused, naming the one that holds
  it. `Delete` takes the key off. Type to narrow the list.
- **Dashboard**: whether panes start with every tool call showing, and which icons the panes and
  cards wear. *Automatic* names what it decided for the terminal you're in, and decides again every
  time the app starts. *Nerd Font* and *Unicode* are drawn in their own glyphs, so pick the one that
  isn't boxes. The app previews each behind Settings as you move.

What you keep is written to `~/.config/a-team/dashboard.json`, and is what the app comes up in next
time.

### Keys by hand

Keys can be set in that file too, which is the way out of a key your terminal or multiplexer
swallows. A `keys` object maps a command's id, the ones Commands lists, to a key name, spelled as
Terminal.Gui spells it (`PageUp`, not the `PgUp` Settings shortens it to):

```json
{ "theme": "Midnight", "keys": { "settings": "Ctrl+,", "log.toolCalls": "d" } }
```

A command named there is reached by that key instead of the one it ships with, and only by that key.
An id nothing is registered under, a name that isn't a key, and a key another command already holds
are each ignored on their own: that command keeps the key it had, and the rest of the file still
applies.
