# Teams

Your teams are in **Settings → Teams**: `s` opens Settings, and **Teams** in Commands (`Ctrl+E`)
opens it on that page. `F1` in Settings opens this page.

## The Teams page

A row per team: its name, its repo, whether it's `working`, `paused` or has its `budget reached`,
what its runs have cost **Today**, since midnight, and its health.

| Key | Does |
|---|---|
| `p` | Pause the selected team, or start it working again. It happens straight away |
| `Enter` | Open the team's settings |
| `x` | Remove the team |
| `n` | Start a new team |

## Starting a team

`n` on the Teams page, or **New team** in Commands, opens the form for a new team. Fill it in and
press `Ctrl+Enter` (**Create**). A-Team writes the team's file, paused, then offers in turn to:

1. Clone the repo.
2. Give the team its GitHub App, and install it on the repo.
3. Create its Project.
4. Set its board up.
5. Write its vision with you.
6. Get to work.

Each step says what it will do and waits for `Ctrl+Enter`. `Esc` skips it, and `Backspace` goes back a
step. The last asks whether to get to work or keep the team paused: `↑`/`↓` choose, `Ctrl+Enter`
goes ahead with the one chosen, and `Esc` cancels the new team.

One part of the Project is yours to set up on GitHub: under its **Workflows**, turn on **Auto-add
to project** for the repo, and **Item added to project** with Status set to Idea. Without them, an
issue you or another team open on the repo never reaches Triage. Health notes it until both are on.

## Writing the vision

The Lead judges every pitch against the team's vision. **Write the vision with me** in Commands, for
the selected team, hands the terminal to Claude for about 20 minutes. It reads the repo first, then
asks you about eight questions one at a time, and opens the vision it writes from your answers as a
draft PR in Pitched, for you to approve by merging it. Quit Claude with `/exit` and press `Enter`
to come back. It rewrites a vision the team already has. From a terminal, it's `a-team vision <team>`.

Skip it and the Lead drafts one from the repo instead. That's a guess, so the team's Health notes
it until you replace it.

`gh` needs to be able to manage projects first: `gh auth refresh -s project`.

## Reviewing the vision

A vision goes stale as the team ships. **Vision reviews**, in the team's form, says when the Lead
proposes a refresh:

- **Manually**, the default: only when you run **Review the vision** in Commands.
- **Every N days, weeks or months** after the vision last changed.
- **After N% of its themes' issues close**: the issues its *Next themes* name. A vision that names
  none never comes due this way.

**Review the vision** in Commands starts one now for the selected team, whatever its setting. When
one's due, the dispatcher starts the Lead and says why, like `vision review due: 30 days since it
last changed`. The Lead opens a draft PR in Pitched, leading with what's happened since the vision
last changed. It rewrites *Next themes* only, and asks under **Needs your answer** about anything
else, so the card waits in Questions. While it's open, no other review starts. Close it without
merging, and the next waits for the vision to change, or for the setting to come due again counting
from the day you closed it.

Read it from its card in Work: `e` edits the proposed vision in your editor, `c` asks the Lead to
change it, and `a` accepts it ([Answering from a card](work.md#answering-from-a-card)). The Lead's
next run uses it.

## A team's settings

`Enter` on a team opens its form: repo, stakeholders, the other repos it contributes to, project, vision, workdir, skills, the command
`a-team try` runs, whether it's working or paused, when it releases, when the Lead
[reviews the vision](#reviewing-the-vision), which roles it runs, and its limits. Every team has a Lead and a Dev; tick **Customer lead** to add the
[Customer lead](how-a-team-works.md#the-customer-lead) (see [Turning on the Customer lead](#turning-on-the-customer-lead)),
and untick it to take it away again. Tick **Reviewer** to have the
[Reviewer](how-a-team-works.md#the-reviewer) review each task PR before it reaches you. **Stakeholders**, **Contributes to** and **Skills** open a list: type to
narrow it as in Commands, `Space` marks a name, and `Enter` keeps what's marked. Each
field says what it's for underneath. `Ctrl+Enter` (**Save**) saves them into the team's file, leaving everything else in it as it was, and
`Esc` (**Cancel**) cancels. `Enter` never saves.

**Workdir**, **Vision** and **First page** take typing, and `Enter` on one opens a picker:

- **Workdir** browses the folders on this machine, starting from the one typed in. Type to narrow the
  list, `Enter` goes into a folder, and into `../` at the top (or `Backspace`) to go up. **Use this folder** (`Ctrl+Enter`)
  puts the folder you're in into Workdir. A Workdir with no `main` checkout in it shows under
  **Problems** before you save.
- **Vision** and **First page** list the repo's Markdown files on its default branch. Type to narrow the list
  and `Enter` to pick one. Type a path that isn't there and `Enter` uses it anyway: the field then says
  the Lead or Customer lead will draft it.

**Contributes to** lists the other repos the Dev may open draft PRs on, picked from the ones the
team's GitHub App can reach. When a task needs a change in one of them first, like the style guide,
the Dev opens that draft PR itself and hands the task back with it linked, for you to review and
release. It looks after that PR as it does its own and marks it ready once it's green. Merging stays
yours. For a repo not listed, the Dev hands the task back saying the team doesn't contribute to it.

`Esc` in a picker leaves the field as it was. A new team's Workdir follows its Name, as
`~/code/<name>`, until you type or pick one of your own.

## Turning on the Customer lead

Ticking **Customer lead**, in a team's form or in **New team**, adds two rows under **Roles**:

- **User docs**: **Write new docs**, or **I already have docs**. It starts on **I already have docs**
  when the team's docs page is already in the repo, and on **Write new docs** otherwise.
- **First page**: the docs' first page, in the repo. With **Write new docs** it's filled with
  `docs/guide/index.md` unless the team already has one; keep it or type another. With
  **I already have docs**, `Enter` lists the repo's Markdown files to pick the first page from.

Saving with a first page that isn't in the repo yet says so, e.g. *The Customer lead will propose the docs at
docs/guide/index.md.* Its first PR proposes the docs there, and it writes them once you accept that proposal.
That page isn't counted under **Health** while it's waiting to be written. Untick **Customer lead** and
the rows go, but the first page is kept for when you tick it again.

## Pausing a team

A paused team picks up no new work, and a run already going finishes. `p` on the Teams page, or the
team's own form, pauses it and starts it again. To pause one role and not the other, use `h` on the
[Dashboard](dashboard.md#pausing-a-role).

## A daily budget

**Budget $/day**, under the form's limits, is the most a day's runs may cost at API prices. Once the
team's runs since midnight have cost that much, the dispatcher starts no new run for it until
midnight, and its row reads `budget reached`. A run already going finishes. The dispatcher's log
says so once a day, e.g. `tuicode: daily budget $40 reached ($41.37 today); no new runs until
midnight`. After midnight the team starts runs again by itself. 0, the default, means no budget.
The budget is whole dollars: a config that says `12.5` has none, and the log says why.

A run's cost counts once it has ended, so **Today** can pass the budget by what the last runs cost.

**Spending**, in [Work](work.md), shows what the money bought: each team's runs by theme and pitch.

## Removing a team

`x` asks first, and `Ctrl+Enter` removes it: A-Team forgets the team. Its repo, its board and
everything it has built are untouched, and its file is kept as `<team>.json.removed`. Rename it back
to `<team>.json` to bring the team back.

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
- **Overseer**: how long a card may stay in each column before [Overseer](overseer.md) highlights
  it.

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
