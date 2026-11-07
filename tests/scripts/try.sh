#!/usr/bin/env bash
# A-team try and a-team board clean.
# shellcheck source=tests/scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
# shellcheck disable=SC2034,SC2154 # helpers read what an area sets, and an area reads what lib.sh sets

case_ "try runs the team's command in a worktree at the PR's head, sandboxed, and clears up after"
try_fixture record
try_gh
run try demo 7
same "exit" 0 "$STATUS"
same "working directory" "$TRY_WORK/.try/7" "$(sed -n 1p "$TRY_WORK/ran")"
same "commit" "$TRY_SHA" "$(sed -n 2p "$TRY_WORK/ran")"
same "A_TEAM_STATE" "$TRY_WORK/.try/state/7" "$(sed -n 3p "$TRY_WORK/ran")"
same "A_TEAM_DRY_RUN" 1 "$(sed -n 4p "$TRY_WORK/ran")"
grep -q 'A_TEAM_DRY_RUN=1' "$OUT" || fail "sandbox: nothing about it in '$(cat "$OUT")'"
grep -q 'Running: record' "$OUT" || fail "run: nothing about what it ran in '$(cat "$OUT")'"
grep -q 'Merge #7 if it did what you wanted' "$OUT" || fail "merge: no invitation in '$(cat "$OUT")'"
same "worktrees left" "" "$(worktrees)"
[ ! -e "$TRY_WORK/.try/7" ] || fail "clean exit: the worktree is still there"
[ ! -e "$TRY_WORK/.try/state/7" ] || fail "clean exit: the sandbox state is still there"

case_ "no try command in the config drops you into a shell in the worktree instead"
try_fixture
try_gh
run try demo 7 <<IN
pwd >"$TRY_WORK/shell"
IN
same "exit" 0 "$STATUS"
same "working directory" "$TRY_WORK/.try/7" "$(cat "$TRY_WORK/shell")"
[ ! -e "$TRY_WORK/ran" ] || fail "shell: the team's command ran as well"
grep -q 'Ctrl+D' "$OUT" || fail "shell: nothing saying how to come back in '$(cat "$OUT")'"
grep -q 'try:demo#7' "$ERR" || fail "shell: the prompt doesn't name the PR"

case_ "a worktree you changed something in is kept, with the line that removes it"
try_fixture 'touch note.md'
try_gh
run try demo 7
same "exit" 0 "$STATUS"
[ -d "$TRY_WORK/.try/7" ] || fail "kept: the worktree was removed anyway"
grep -q 'you changed 1 file there' "$OUT" || fail "kept: nothing about what you changed in '$(cat "$OUT")'"
grep -q 'a-team try demo 7 --clean' "$OUT" || fail "kept: no --clean line in '$(cat "$OUT")'"

case_ "trying it again runs the worktree you changed as you left it, and says how to get a fresh one"
run try demo 7
same "exit" 0 "$STATUS"
[ -e "$TRY_WORK/.try/7/note.md" ] || fail "again: your change is gone"
grep -q 'as you left it, with 1 changed file' "$OUT" || fail "again: nothing about your change in '$(cat "$OUT")'"
grep -q 'For a fresh copy.*a-team try demo 7 --clean' "$OUT" || fail "again: no --clean line in '$(cat "$OUT")'"

case_ "once the PR has moved on, the worktree you changed is refused rather than run behind it"
cp "$TRY_BIN/pull.json" "$TRY_BIN/pull.was"
jq --arg sha "$(git -C "$CHECKOUT" rev-parse origin/main)" '.head.sha = $sha' "$TRY_BIN/pull.was" >"$TRY_BIN/pull.json"
rm -f "$TRY_WORK/ran"
A_TEAM_TRY_FAILED="$TRY_WORK/failed" run try demo 7
failed "moved on"
[ ! -e "$TRY_WORK/ran" ] || fail "moved on: it ran the old code"
[ -e "$TRY_WORK/.try/7/note.md" ] || fail "moved on: your change is gone"
grep -q "behind .*a-team try demo 7 --clean" "$TRY_WORK/failed" || fail "moved on: '$(cat "$TRY_WORK/failed")'"
mv "$TRY_BIN/pull.was" "$TRY_BIN/pull.json"

case_ "--clean removes it without running anything, and is happy when there's nothing to remove"
run try demo 7 --clean
same "exit" 0 "$STATUS"
same "worktrees left" "" "$(worktrees)"
[ ! -e "$TRY_WORK/.try/7" ] || fail "--clean: the worktree is still there"
[ ! -e "$TRY_WORK/.try/state/7" ] || fail "--clean: the sandbox state is still there"
run try demo 7 --clean
same "exit" 0 "$STATUS"
grep -q 'Nothing to remove' "$OUT" || fail "--clean twice: '$(cat "$OUT")'"

# A try of #7 left running, its `try` command staying until $TRY_WORK/go appears, so a test can act during it.
try_while_running() {
  try_fixture hold
  try_gh
  fake "$TRY_BIN/hold" <<SH
#!/usr/bin/env bash
touch "$TRY_WORK/started"
until [ -e "$TRY_WORK/go" ]; do [ "\$SECONDS" -lt "$FAKE_LIMIT" ] || exit; sleep 0.1; done
SH
  A_TEAM_CONFIG="$CONFIG" "$A_TEAM" try demo 7 >"$OUT.first" 2>&1 &
  TRY_FIRST=$!
  until [ -e "$TRY_WORK/started" ]; do sleep 0.1; done
}

case_ "a second try of a PR being tried refuses, says why in the terminal and for Work's bar, and leaves the first alone"
try_while_running
touch "$TRY_WORK/.try/state/7/theirs"
A_TEAM_TRY_FAILED="$TRY_WORK/failed" run try demo 7
same "exit" 1 "$STATUS"
same "terminal" "a-team try: #7 is already being tried (pid $TRY_FIRST): quit that one first" "$(cat "$ERR")"
same "for the bar" "#7 is already being tried (pid $TRY_FIRST): quit that one first" "$(cat "$TRY_WORK/failed")"
[ -e "$TRY_WORK/.try/7/file" ] || fail "second try: the first's worktree is gone"
[ -e "$TRY_WORK/.try/state/7/theirs" ] || fail "second try: the first's sandbox is gone"
same "first still running" "$TRY_FIRST" "$(cat "$TRY_WORK/.try/state/7.pid")"

case_ "--clean on a PR being tried refuses the same way"
run try demo 7 --clean
same "exit" 1 "$STATUS"
same "terminal" "a-team try: #7 is already being tried (pid $TRY_FIRST): quit that one first" "$(cat "$ERR")"
[ -e "$TRY_WORK/.try/7/file" ] || fail "--clean: the first's worktree is gone"
[ -e "$TRY_WORK/.try/state/7/theirs" ] || fail "--clean: the first's sandbox is gone"

case_ "the first try carries on after both, and clears up when it's quit"
touch "$TRY_WORK/go"
wait "$TRY_FIRST"
same "exit" 0 "$?"
[ ! -e "$TRY_WORK/.try/state/7.pid" ] || fail "quit: it's still marked as being tried"

case_ "after a try is killed, the next try of that PR starts normally"
try_while_running
kill -9 "$TRY_FIRST"
wait "$TRY_FIRST" 2>/dev/null
touch "$TRY_WORK/go"
rm -f "$TRY_WORK/started"
run try demo 7
same "exit" 0 "$STATUS"
[ -e "$TRY_WORK/started" ] || fail "killed: the next try didn't run"
[ ! -e "$TRY_WORK/.try/state/7.pid" ] || fail "killed: its record is still there"

case_ "a pid another process has since been given doesn't block a try or --clean"
try_fixture record
try_gh
mkdir -p "$TRY_WORK/.try/state"
echo $$ >"$TRY_WORK/.try/state/7.pid"
touch -t 200001010000 "$TRY_WORK/.try/state/7.pid"
run try demo 7 --clean
same "--clean exit" 0 "$STATUS"
echo $$ >"$TRY_WORK/.try/state/7.pid"
touch -t 200001010000 "$TRY_WORK/.try/state/7.pid"
run try demo 7
same "exit" 0 "$STATUS"
[ -e "$TRY_WORK/ran" ] || fail "reused pid: the try didn't run"

case_ "a try of the default branch refuses while one is running, naming the branch"
try_fixture hold
try_gh
fake "$TRY_BIN/hold" <<SH
#!/usr/bin/env bash
touch "$TRY_WORK/started"
until [ -e "$TRY_WORK/go" ]; do [ "\$SECONDS" -lt "$FAKE_LIMIT" ] || exit; sleep 0.1; done
SH
A_TEAM_CONFIG="$CONFIG" "$A_TEAM" try demo >"$OUT.first" 2>&1 &
TRY_FIRST=$!
until [ -e "$TRY_WORK/started" ]; do sleep 0.1; done
run try demo
same "terminal" "a-team try: main is already being tried (pid $TRY_FIRST): quit that one first" "$(cat "$ERR")"
touch "$TRY_WORK/go"
wait "$TRY_FIRST"

# --- a-team board clean -----------------------------------------------------------------------
# A checkout from try_fixture, and a gh answering `pr list --head <branch>` with what clean_pr gave that branch.
clean_world() {
  try_fixture
  CLEAN_BIN=$(mktemp -d "$WORK/cleanbin.XXXXXX")
  fake "$CLEAN_BIN/gh" <<SH
#!/usr/bin/env bash
[ ! -e "$CLEAN_BIN/fails" ] || { echo "gh: HTTP 502" >&2; exit 1; }
while [ \$# -gt 0 ]; do
  case "\$1" in
    --head) branch=\$2; shift ;;
    -R) repo=\$2; shift ;;
  esac
  shift
done
echo "\$repo" >>"$CLEAN_BIN/repos"
cat "$CLEAN_BIN/\${branch//\//_}.json" 2>/dev/null || echo '[]'
SH
  CLEAN_PATH=$CLEAN_BIN:$PATH
}

# clean_pr <branch> <number> <state> <sha> [<marker role, or "" for none>] [<jq to apply to it>]: another PR on <branch>, oldest first.
clean_pr() {
  local file="$CLEAN_BIN/${1//\//_}.json"
  [ -e "$file" ] || echo '[]' >"$file"
  jq --argjson number "$2" --arg state "$3" --arg sha "$4" --arg role "${5-dev}" "
    [{number: \$number, state: \$state, headRefOid: \$sha, isCrossRepository: false, author: {login: \"app/demo-app\"},
      body: (if \$role == \"\" then \"By hand\" else \"Does it\n<!-- a-team:\(\$role) -->\" end)} | ${6:-.}] + ." "$file" >"$file.new" &&
    mv "$file.new" "$file"
}

# clean_tree <slug> <branch>: a worktree on a new <branch> from origin/main; its HEAD in TREE_HEAD.
clean_tree() {
  git -C "$CHECKOUT" worktree add -q "$TRY_WORK/$1" -b "$2" origin/main 2>/dev/null
  TREE_HEAD=$(git -C "$TRY_WORK/$1" rev-parse HEAD)
}


case_ "clean removes a merged PR's worktree and branch, at its last commit with no tracked changes, untracked files and all"
clean_world
CLEAN_ROLE=dev
clean_tree finished feat/finished
clean_pr feat/finished 11 MERGED "$TREE_HEAD"
touch "$TRY_WORK/finished/build-output"
clean demo
same "exit" 0 "$STATUS"
[ ! -e "$TRY_WORK/finished" ] || fail "merged and clean: the worktree is still there"
same "branches" "" "$(branches)"
grep -q 'Removed .*finished: #11 has merged' "$OUT" || fail "merged and clean: '$(cat "$OUT")'"
same "asked" mentaldesk/demo "$(cat "$CLEAN_BIN/repos")"

case_ "clean leaves a worktree whose HEAD is past the PR's last commit, and says why"
clean_world
clean_tree past feat/past
clean_pr feat/past 12 MERGED "$TREE_HEAD"
git -C "$TRY_WORK/past" -c user.email=t@example.com -c user.name=T commit -q --allow-empty -m newer
past=$(git -C "$TRY_WORK/past" rev-parse HEAD)
clean demo
same "exit" 0 "$STATUS"
[ -d "$TRY_WORK/past" ] || fail "past: it was removed"
same "branches" feat/past "$(branches)"
grep -q "Left .*past: its HEAD ${past:0:7} isn't #12's last commit (${TREE_HEAD:0:7})" "$OUT" || fail "past: '$(cat "$OUT")'"

case_ "clean leaves a worktree with changed tracked files"
clean_world
clean_tree edited feat/edited
clean_pr feat/edited 13 MERGED "$TREE_HEAD"
echo edit >"$TRY_WORK/edited/file"
clean demo
[ -d "$TRY_WORK/edited" ] || fail "edited: it was removed"
same "edit kept" edit "$(cat "$TRY_WORK/edited/file")"
grep -q 'Left .*edited: it has 1 changed tracked file' "$OUT" || fail "edited: '$(cat "$OUT")'"

case_ "clean leaves an unmerged PR's worktree, open or closed"
clean_world
clean_tree opened feat/opened
clean_pr feat/opened 14 OPEN "$TREE_HEAD"
clean_tree closed feat/closed
clean_pr feat/closed 15 CLOSED "$TREE_HEAD"
clean demo
[ -d "$TRY_WORK/opened" ] || fail "open: it was removed"
[ -d "$TRY_WORK/closed" ] || fail "closed: it was removed"
grep -q 'Left .*opened: #14 is open' "$OUT" || fail "open: '$(cat "$OUT")'"
grep -q 'Left .*closed: #15 was closed without merging' "$OUT" || fail "closed: '$(cat "$OUT")'"

case_ "on a reused branch only the newest PR counts, and a fork's PR of the same name doesn't"
clean_world
clean_tree reused feat/reused
clean_pr feat/reused 16 MERGED "$TREE_HEAD"
clean_pr feat/reused 17 OPEN "$TREE_HEAD"
clean_pr feat/reused 18 MERGED "$TREE_HEAD" dev '.isCrossRepository = true'
clean demo
[ -d "$TRY_WORK/reused" ] || fail "reused: it was removed"
grep -q 'Left .*reused: #17 is open' "$OUT" || fail "reused: '$(cat "$OUT")'"

case_ "clean touches only the role's own: not the stakeholder's, the Lead's, the other role's, or one with no branch"
clean_world
clean_tree mine-by-hand feat/by-hand
clean_pr feat/by-hand 19 MERGED "$TREE_HEAD" ''
clean_tree vision a-team/vision
clean_pr a-team/vision 20 MERGED "$TREE_HEAD" lead
clean_tree docs docs/customer-lead
clean_pr docs/customer-lead 21 MERGED "$TREE_HEAD" customer
git -C "$CHECKOUT" worktree add -q --detach "$TRY_WORK/detached" origin/main
clean demo
same "exit" 0 "$STATUS"
same "said" "No worktrees of yours to clean up." "$(cat "$OUT")"
for tree in mine-by-hand vision docs detached; do [ -d "$TRY_WORK/$tree" ] || fail "not dev's: $tree was removed"; done
[ -d "$CHECKOUT" ] || fail "not dev's: the checkout was removed"

case_ "clean customer removes its finished worktree, and leaves every other branch to the Dev"
CLEAN_ROLE=customer
clean_tree dev-work feat/dev-work
clean_pr feat/dev-work 22 MERGED "$TREE_HEAD"
clean demo
[ ! -e "$TRY_WORK/docs" ] || fail "customer: its merged worktree is still there"
[ -d "$TRY_WORK/dev-work" ] || fail "customer: it removed the Dev's"
grep -q 'Removed .*docs: #21 has merged' "$OUT" || fail "customer: '$(cat "$OUT")'"

case_ "clean customer names newer work on its branch, whether the newest PR has merged or there's none"
clean_world
clean_tree docs docs/customer-lead
clean_pr docs/customer-lead 23 MERGED "$TREE_HEAD" customer
git -C "$TRY_WORK/docs" -c user.email=t@example.com -c user.name=T commit -q --allow-empty -m newer
clean_tree proposal docs/customer-lead-proposal
clean demo
[ -d "$TRY_WORK/docs" ] || fail "newer: it was removed"
[ -d "$TRY_WORK/proposal" ] || fail "no PR: it was removed"
grep -q "Left .*docs: its HEAD .* isn't #23's last commit" "$OUT" || fail "newer: '$(cat "$OUT")'"
grep -q "Left .*proposal: there's no PR on docs/customer-lead-proposal" "$OUT" || fail "no PR: '$(cat "$OUT")'"

case_ "clean customer names a closed PR on its branch, and the role says how to start again from it"
clean_world
clean_tree docs docs/customer-lead
clean_pr docs/customer-lead 27 CLOSED "$TREE_HEAD" customer
clean demo
[ -d "$TRY_WORK/docs" ] || fail "closed: it was removed"
grep -q 'Left .*docs: #27 was closed without merging' "$OUT" || fail "closed: '$(cat "$OUT")'"
grep -qF 'closed without merging' "$ROOT/roles/customer.md" || fail "closed: the role doesn't mention it"
grep -qF 'git merge -s ours --no-commit origin/' "$ROOT/roles/customer.md" || fail "closed: the role doesn't say how to start again"
grep -qF 'closed without merging' "$ROOT/tasks/customer.md" || fail "closed: the prompt doesn't allow it"

case_ "clean treats Dependabot's merged PR as the Dev's"
clean_world
CLEAN_ROLE=dev
clean_tree bump dependabot/nuget/x
clean_pr dependabot/nuget/x 24 MERGED "$TREE_HEAD" '' '.author.login = "app/dependabot"'
clean demo
[ ! -e "$TRY_WORK/bump" ] || fail "Dependabot: it's still there"

case_ "clean takes the Dev's worktrees of a contributed-to repo too, asking that repo, and never the clone"
clean_world
git clone -q "file://$TRY_ORIGIN" "$TRY_WORK/upstream-clone"
mkdir "$TRY_WORK/upstream"
mv "$TRY_WORK/upstream-clone" "$TRY_WORK/upstream/lib"
jq '.contributesTo = ["other/lib"]' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
git -C "$TRY_WORK/upstream/lib" worktree add -q "$TRY_WORK/upstream/lib-change" -b feat/lib-change origin/main 2>/dev/null
clean_pr feat/lib-change 25 MERGED "$(git -C "$TRY_WORK/upstream/lib" rev-parse origin/main)"
clean_pr main 3 MERGED "$(git -C "$TRY_WORK/upstream/lib" rev-parse origin/main)"
clean demo
[ ! -e "$TRY_WORK/upstream/lib-change" ] || fail "upstream: its worktree is still there"
[ -d "$TRY_WORK/upstream/lib" ] || fail "upstream: the clone was removed"
same "asked" other/lib "$(sort -u "$CLEAN_BIN/repos")"

case_ "clean leaves a worktree when it can't read the PRs, or this shell is in it"
clean_world
clean_tree here feat/here
clean_pr feat/here 26 MERGED "$TREE_HEAD"
touch "$CLEAN_BIN/fails"
clean demo
[ -d "$TRY_WORK/here" ] || fail "can't read: it was removed"
grep -q "Left .*here: couldn't read the PRs on feat/here (gh: HTTP 502)" "$OUT" || fail "can't read: '$(cat "$OUT")'"
rm "$CLEAN_BIN/fails"
(cd "$TRY_WORK/here" && clean demo)
[ -d "$TRY_WORK/here" ] || fail "in it: it was removed"
grep -q 'Left .*here: this shell is in it' "$OUT" || fail "in it: '$(cat "$OUT")'"

case_ "clean --dry-run says what it would remove and removes nothing"
PATH=$CLEAN_PATH run board --dry-run demo clean dev
[ -d "$TRY_WORK/here" ] || fail "dry run: it was removed"
same "branches" feat/here "$(branches)"
grep -q 'would remove the worktree' "$ERR" || fail "dry run: '$(cat "$ERR")'"
grep -q '^(dry run) Removed' "$OUT" || fail "dry run: '$(cat "$OUT")'"

case_ "only the Dev and the Customer lead clean up"
for role in lead reviewer you; do
  PATH=$CLEAN_PATH run board demo clean "$role"
  failed "$role"
  grep -q 'only dev and customer clean up worktrees' "$ERR" || fail "$role: '$(cat "$ERR")'"
done
[ -d "$TRY_WORK/here" ] || fail "other role: it was removed"

case_ "the Dev and the Customer lead are told to clean up, allowed to, and can't remove a worktree any other way"
for role in dev customer; do
  grep -qF "a-team board {{team}} clean $role" "$ROOT/roles/$role.md" || fail "$role: not in its role"
  grep -qF "a-team board {{team}} clean $role" "$ROOT/tasks/$role.md" || fail "$role: not in its prompt"
  grep -qF '"Bash(git worktree remove *)"' "$ROOT/settings/$role.json" || fail "$role: no deny rule"
  grep -q 'wrap-up' "$ROOT/roles/$role.md" "$ROOT/tasks/$role.md" && fail "$role: still told to use wrap-up"
done

case_ "a worktree left behind with nothing changed in it is replaced by a fresh one"
try_fixture record
try_gh
git -C "$CHECKOUT" fetch -q origin pull/7/head
git -C "$CHECKOUT" worktree add -q --detach "$TRY_WORK/.try/7" origin/main
run try demo 7
same "exit" 0 "$STATUS"
same "commit" "$TRY_SHA" "$(sed -n 2p "$TRY_WORK/ran")"
same "worktrees left" "" "$(worktrees)"
mkdir -p "$TRY_WORK/.try/7"
run try demo 7
same "not a worktree" 0 "$STATUS"
same "not a worktree: commit" "$TRY_SHA" "$(sed -n 2p "$TRY_WORK/ran")"

case_ "a PR that isn't there is refused in one line, leaving nothing behind"
try_fixture record
try_gh
run try demo 404
failed "no such PR"
one_line "no such PR"
[ ! -e "$TRY_WORK/.try" ] || fail "no such PR: something was left in .try"

case_ "so is one that's already closed"
try_gh closed
run try demo 7
failed "closed PR"
one_line "closed PR"
[ ! -e "$TRY_WORK/.try" ] || fail "closed PR: something was left in .try"

case_ "a PR from somewhere else asks first, and n stops it before anything is fetched"
try_gh open someone-else/demo
run try demo 7 <<<n
failed "fork"
grep -q 'someone-else/demo' "$OUT" || fail "fork: it doesn't say whose branch it is"
[ ! -e "$TRY_WORK/.try" ] || fail "fork: something was left in .try"
git -C "$CHECKOUT" cat-file -e "$TRY_SHA" 2>/dev/null && fail "fork: it fetched the PR anyway"

case_ "with A_TEAM_TRY_FAILED set, why it failed goes in that file too"
A_TEAM_TRY_FAILED="$TRY_WORK/failed" run try demo 404
failed "failure file"
same "failure file" "no PR #404 on mentaldesk/demo" "$(cat "$TRY_WORK/failed")"
try_fixture false
try_gh
A_TEAM_TRY_FAILED="$TRY_WORK/failed" run try demo 7
failed "command failed"
same "command failure file" "false exited 1" "$(cat "$TRY_WORK/failed")"

case_ "a PR that closes an issue shows what's left for you, then what the team checked, in order, before running"
try_fixture record
try_gh
try_closes 92 "$(printf '%s\r\n' '## Context' '' 'Why.' '' '## Acceptance criteria' '' \
  '- [ ] A grid cell never goes below' '      5 rows' '- [x] Moving the selection scrolls' \
  '  - [ ] and the strip never does' '- [x] The foot says so' '' '## Tests' '' '- [ ] not this one')"
jq --arg body "$(printf '%s\n' 'Closes #92' '' '## Checked by the team' '' '- `screen` Moving the selection scrolls' \
  '- `test` The foot says so' '' '<!-- a-team:dev -->')" '. + {body: $body}' "$TRY_BIN/pull.json" >"$TRY_BIN/pull.new"
mv "$TRY_BIN/pull.new" "$TRY_BIN/pull.json"
run try demo 7
same "exit" 0 "$STATUS"
[ -e "$TRY_WORK/ran" ] || fail "criteria: the team's command didn't run"
sed -n 2p "$OUT" | grep -q "at ${TRY_SHA:0:7} (closes #92)$" || fail "criteria: second line '$(sed -n 2p "$OUT")'"
same "checklist" "Left for you (2):
    [ ] A grid cell never goes below 5 rows
      [ ] and the strip never does
Checked by the team (2), pictures on the PR:
    [x] Moving the selection scrolls
    [x] The foot says so" "$(sed -n '4,9p' "$OUT")"
same "then the sandbox" "" "$(sed -n 10p "$OUT")"
grep -q 'not this one' "$OUT" && fail "criteria: it ran on past the section in '$(cat "$OUT")'"

case_ "with nothing recorded on its PR, every criterion is left for you, as the issue has it"
try_fixture record
try_gh
try_closes 92 "$(printf '%s\n' '## Acceptance criteria' '- [ ] One' '- [x] Two')"
run try demo 7
same "checklist" "Left for you (2):
    [ ] One
    [x] Two" "$(sed -n '4,6p' "$OUT")"
grep -q 'Checked by the team' "$OUT" && fail "nothing recorded: '$(cat "$OUT")'"

case_ "an issue with no acceptance criteria, or a PR closing nothing, shows no checklist and runs"
try_fixture record
try_gh
try_closes 92 "Just some words."
run try demo 7
same "exit" 0 "$STATUS"
[ -e "$TRY_WORK/ran" ] || fail "no criteria: the team's command didn't run"
grep -q 'Left for you' "$OUT" && fail "no criteria: a checklist in '$(cat "$OUT")'"
same "stderr" "" "$(cat "$ERR")"
try_gh
run try demo 7
same "exit" 0 "$STATUS"
grep -q 'closes\|Left for you' "$OUT" && fail "closes nothing: '$(cat "$OUT")'"
same "stderr" "" "$(cat "$ERR")"

case_ "an issue that won't read is one line, and try runs anyway"
try_fixture record
try_gh
try_closes fail
run try demo 7
same "exit" 0 "$STATUS"
[ -e "$TRY_WORK/ran" ] || fail "unreadable: the team's command didn't run"
same "lines about it" 1 "$(grep -c 'Could not read' "$OUT")"
grep -q 'Left for you' "$OUT" && fail "unreadable: a checklist in '$(cat "$OUT")'"

case_ "with no PR, there's no checklist"
try_fixture record
try_gh
try_closes 92 "$(printf '%s\n' '## Acceptance criteria' '- [ ] something')"
run try demo
same "exit" 0 "$STATUS"
grep -q 'Left for you' "$OUT" && fail "no PR: a checklist in '$(cat "$OUT")'"

case_ "with no PR, try runs origin's default branch as it is now, not the checkout's stale copy"
try_fixture record
try_gh
land main
run try demo
same "exit" 0 "$STATUS"
same "working directory" "$TRY_WORK/.try/main" "$(sed -n 1p "$TRY_WORK/ran")"
same "commit" "$TRY_LANDED" "$(sed -n 2p "$TRY_WORK/ran")"
same "A_TEAM_STATE" "$TRY_WORK/.try/state/main" "$(sed -n 3p "$TRY_WORK/ran")"
same "A_TEAM_DRY_RUN" 1 "$(sed -n 4p "$TRY_WORK/ran")"
same "first line" "Fetching mentaldesk/demo main…" "$(sed -n 1p "$OUT")"
sed -n 2p "$OUT" | grep -q "at main ${TRY_LANDED:0:7}$" || fail "no PR: second line '$(sed -n 2p "$OUT")'"
grep -q '#' "$OUT" && fail "no PR: it talks about a PR in '$(cat "$OUT")'"
same "worktrees left" "" "$(worktrees)"
[ ! -e "$TRY_WORK/.try/main" ] || fail "no PR: the worktree is still there"
[ ! -e "$TRY_WORK/.try/state/main" ] || fail "no PR: the sandbox state is still there"

case_ "running it again after a merge gives you the newer commit"
land main
run try demo
same "exit" 0 "$STATUS"
same "commit" "$TRY_LANDED" "$(sed -n 2p "$TRY_WORK/ran")"

case_ "the default branch is whatever origin's HEAD names, not main"
land trunk
git -C "$TRY_ORIGIN" symbolic-ref HEAD refs/heads/trunk
run try demo
same "exit" 0 "$STATUS"
same "commit" "$TRY_LANDED" "$(sed -n 2p "$TRY_WORK/ran")"
same "first line" "Fetching mentaldesk/demo trunk…" "$(sed -n 1p "$OUT")"

case_ "with no PR, a shell names the branch, a change keeps the worktree, and --clean removes it"
try_fixture
run try demo <<IN
pwd >"$TRY_WORK/shell"
touch note.md
IN
same "exit" 0 "$STATUS"
same "working directory" "$TRY_WORK/.try/main" "$(cat "$TRY_WORK/shell")"
grep -q 'try:demo@main' "$ERR" || fail "shell: the prompt doesn't name the branch"
[ -d "$TRY_WORK/.try/main" ] || fail "kept: the worktree was removed anyway"
grep -q 'a-team try demo --clean' "$OUT" || fail "kept: no --clean line in '$(cat "$OUT")'"
run try demo --clean
same "exit" 0 "$STATUS"
same "worktrees left" "" "$(worktrees)"
[ ! -e "$TRY_WORK/.try/main" ] || fail "--clean: the worktree is still there"
[ ! -e "$TRY_WORK/.try/state/main" ] || fail "--clean: the sandbox state is still there"

case_ "an unknown team, or no team at all, is refused with the usage"
run try nobody 7
failed "unknown team"
grep -q "^usage: a-team try" "$ERR" || fail "unknown team: no usage in '$(cat "$ERR")'"
run try
failed "no team"
grep -q "^usage: a-team try" "$ERR" || fail "no team: no usage in '$(cat "$ERR")'"

case_ "try is in the usage text, and the example config carries the optional try key"
run help
grep -q '^  try <team> \[<pr>\]' "$OUT" || fail "usage: no try line with an optional PR"
same "example try" '"./bin/a-team dashboard"' "$(jq -c .try "$ROOT/examples/team.json")"

case_ "a-team with no command opens the app, and dashboard opens it on the Dashboard"
APP=$(mktemp -d "$WORK/app.XXXXXX")
mkdir -p "$APP/bin" "$APP/scripts" "$APP/libexec"
cp "$ROOT/bin/a-team" "$APP/bin/"
cp "$ROOT"/scripts/*.sh "$APP/scripts/"
fake "$APP/libexec/ateam" <<SH
#!/usr/bin/env bash
printf '%s\n' "\$@" >"$WORK/app-args"
SH

"$APP/bin/a-team" >"$OUT" 2>"$ERR"
STATUS=$?
same "exit" 0 "$STATUS"
same "arguments" "" "$(cat "$WORK/app-args")"

"$APP/bin/a-team" dashboard tuicode >"$OUT" 2>"$ERR"
STATUS=$?
same "exit" 0 "$STATUS"
same "arguments" "--area dashboard tuicode" "$(tr '\n' ' ' <"$WORK/app-args" | sed 's/ $//')"


export A_TEAM_STATE
A_TEAM_STATE=$(mktemp -d "$WORK/state.XXXXXX")

finish_suite
