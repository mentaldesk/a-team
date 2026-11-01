# Around the app

What works the same wherever you are: the Assistant, the app's keys and menus, copying, your Mac's
Cmd and Option keys, this Guide, and confirming what can't be undone.

## The Assistant

`Ctrl+G A`, **Go → Assistant** or **Assistant** in Commands opens a Claude Code conversation down the right of
whichever area you're in, with the cursor in its question box. Pressing or choosing it again closes it.
It stays open as you move between Work and Overseer.

Ask it anything about your teams: why a card has sat so long, what a team spent yesterday, why a
Dev hasn't run since lunch. It already knows how a-team works, where each team's config, logs and
run record are, and what you're looking at: the line above the box says what the next question is
about, like *on #499 in Work*, and your question's heading repeats it, so you never have to name the
card or role. With more than one team, the card's team is named too.

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

The keys come in TuiCode's families. `Ctrl+G` then a letter goes somewhere: `Ctrl+G W`
Work, `Ctrl+G O` Overseer, `Ctrl+G A` the Assistant, `Ctrl+G I` the panel for what's selected in
Overseer, `Ctrl+G T` a team and `Ctrl+G N` what's new. `Ctrl+T` then a letter turns something on or off: `Ctrl+T T` tool calls and
`Ctrl+T M` only your moves. Everyday commands are a Ctrl key: `Ctrl+E` Commands, `Ctrl+,` Settings,
`Ctrl+N` a new idea, `Ctrl+Q` quit, and `Ctrl+L` a new conversation in the Assistant. The keys for a
selected card or log line, like `a` to accept or `l` to copy, stay single letters.

Partway through a chord, the status bar shows what you've pressed so far, like `Ctrl+G…`. A key that
finishes no chord cancels it, says so on the status bar, and does nothing else; so does `Esc`.

`F1` shows the keys. *Everywhere*, on the left, lists the ones that work anywhere, under the menu
that holds them. Beside it, headed with where you are (Work, Overseer, a team's panel, a full-screen
log or the Assistant), are the ones for there. *Help → Show keys* and **Show keys** in Commands open it too, and
`Esc` or its *Esc Close* button closes it. *Press F1 for help*, at the right end of the status bar,
is the only key it names. The rest of it starts with where you are (Work's filter, or Overseer) and
when the board was last read, then says what's selected or going on. The app's version is in
*Help → About*.

A message on the status bar, like `#512 approved` or `Copied 3 lines`, stays for 5 seconds, or
until your next key if it answers one you just pressed. An error stays until you press a key, and
one that comes by itself, like a failed read, outlasts the keys you press in its first 5 seconds.
One that stops being true, like a team's failed check once it passes, comes off. Errors you
haven't seen count in `⚠ n`, just left of *Press F1 for help*. *Help → Messages…*, **Messages**
in Commands, or clicking `⚠ n` lists this session's messages, newest first, with their times;
`Ctrl+C` copies the selected one.

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
run that item. **Go** holds the screens and the Assistant, with the one you're on and a showing
Assistant marked `●`, then a team and what's new. `Ctrl+Q` quits the app.

## Copying and links

Over SSH, every copy in the app reaches your own computer's clipboard, sent through the
terminal, and the status bar ends `through the terminal`. In tmux that needs `set -g set-clipboard on`
in `~/.tmux.conf`, and in iTerm2 *Applications in terminal may access clipboard* (Settings → General
→ Selection).

Opening a link works the same way. Locally it opens in your browser and the status bar says
`Opened` and the address. Over SSH there's no browser to open it in, so the link is copied to your
own clipboard instead, with the same terminal setup, and the status bar says
`Copied <link> (no browser over SSH)`.

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
