#!/usr/bin/env bash
# The compiled board: what it does itself gives what board.sh gives, and anything else is handed to board.sh.
# shellcheck source=tests/scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
# shellcheck disable=SC2034,SC2154 # helpers read what an area sets, and an area reads what lib.sh sets

CORE="$ROOT/core/bin/Debug/net10.0/a-team-core"
if ! command -v dotnet >/dev/null; then
  case_ "skipping the compiled board's cases: no dotnet here"
  finish_suite
fi
[ -x "$CORE" ] || fail "tests/scripts.sh should have built $CORE"

TRACE="$WORK/core-trace"
export A_TEAM_CORE_TRACE="$TRACE"
traced() { cat "$TRACE"; : >"$TRACE"; }
core_fixture() {
  fixture <<'JSON'
{ "repo": "mentaldesk/demo", "stakeholders": ["reviewer"], "project": { "owner": "mentaldesk", "number": 1 },
  "wip": { "pitched": 2, "exploring": 2, "worktrees": 2, "devs": 1, "ideas": 2, "readyFloor": 0 },
  "app": { "id": 7, "slug": "demo-app" } }
JSON
  A_TEAM_STATE=$(mktemp -d "$WORK/state.XXXXXX")
  export A_TEAM_STATE
}
# board_moved_over: board.sh makes a new record and stamps its format, in its first two commands. It's the core's after that.
board_moved_over() {
  run board demo list
  run board demo list
  : >"$TRACE"
}
# in_shell <args...>: the same command, on board.sh, against the same record.
in_shell() { A_TEAM_CONFIG="$CONFIG" bash "$ROOT/scripts/board.sh" "$@" >"$OUT.shell" 2>"$ERR.shell"; SHELL_STATUS=$?; }
events() { sqlite3 "$A_TEAM_STATE/history.db" "SELECT who, what FROM events WHERE team = 'demo' AND item = $1 ORDER BY id;"; }

case_ "the route says what board.sh's allowed says, for every role, status and target"
allowed_in_shell=$(sed -n '/^allowed() {/,/^}/p' "$ROOT/scripts/board.sh")
eval "$allowed_in_shell"
expected=$(
  for role in lead dev customer reviewer you; do
    for from in None Idea Exploring Pitched Approved Building Ready "In progress" "In review" Done; do
      for to in Idea Exploring Pitched Approved Building Ready "In progress" "In review" Done; do
        ! allowed "$role" "$from" "$to" || echo "$role:$from>$to"
      done
    done
  done | sort
)
same "route" "$expected" "$("$CORE" route | sort)"

case_ "the record it makes is the record board.sh makes"
schema_of() { sqlite3 "$1" .schema | tr -s ' \n' ' ' | sed 's/; /;\n/g' | sort; }
core_fixture
gh_items <<'ITEMS'
Ready 8 A task
ITEMS
board_moved_over
KEEP_BOARD=1 run board demo list
same "handled" "handled list" "$(traced)"
real=$(mktemp -d "$WORK/state.XXXXXX")
A_TEAM_STATE="$real" A_TEAM_CONFIG="$CONFIG" bash "$ROOT/scripts/board.sh" demo list >/dev/null 2>&1
same "schema" "$(schema_of "$real/history.db")" "$(schema_of "$A_TEAM_STATE/history.db")"

case_ "it knows the board format board.sh does"
same "format" "$(sed -n 's/^BOARD_FORMAT=//p' "$ROOT/scripts/board.sh")" "$(sed -n 's/.*const int Format = \([0-9]*\);.*/\1/p' "$ROOT/core/Board.cs")"

case_ "list prints what board.sh prints, character for character"
core_fixture
gh_items <<'ITEMS'
Idea 2 An "idea" with a quote and ünïcode
Exploring 7 A pitch
Pitched 9 Another pitch
Ready 8 A task
In_progress 11 Claimed
In_review 12 In review
Done 13 Finished
ITEMS
board_moved_over
KEEP_BOARD=1 run board demo list
same "exit" 0 "$STATUS"
same "handled" "handled list" "$(traced)"
cp "$OUT" "$OUT.core"
in_shell demo list
same "shell exit" 0 "$SHELL_STATUS"
same "same output" "$(cat "$OUT.shell")" "$(cat "$OUT.core")"
KEEP_BOARD=1 run board demo list Ready "In review"
same "filtered exit" 0 "$STATUS"
cp "$OUT" "$OUT.core"
in_shell demo list Ready "In review"
same "same filtered" "$(cat "$OUT.shell")" "$(cat "$OUT.core")"
same "filtered" "[8,12]" "$(jq -c 'map(.number)' "$OUT.core")"

case_ "a move leaves the record and the mirror as board.sh would, whether GitHub takes it or not"
card() { sqlite3 "$A_TEAM_STATE/history.db" "SELECT status, mirror, mirrored_status, mirrored_priority, marks FROM board WHERE team = 'demo' AND item = $1; SELECT failed FROM boards WHERE team = 'demo';"; }
with_option() { jq '(.. | objects | select(.name? == "Status") | .options) += [{id: "O_progress", name: "In progress"}]' "$META" >"$META.new" && mv "$META.new" "$META"; }
for option in without with; do
  core_fixture
  gh_items <<'ITEMS'
Ready 8 A task
Exploring 7 A pitch
ITEMS
  [ "$option" = without ] || with_option
  board_moved_over
  KEEP_BOARD=1 run board demo move dev 8 "In progress"
  same "$option exit" 0 "$STATUS"
  same "$option said" "#8: Ready -> In progress" "$(cat "$OUT")"
  same "$option handled" "handled move" "$(traced)"
  same "$option marks" '{"dev":true}' "$(marks 8)"
  same "$option history" "dev|Ready → In progress" "$(events 8)"
  by_core=$(card 8)
  core_calls=$(grep -c . "$CALLS")

  core_fixture
  gh_items <<'ITEMS'
Ready 8 A task
Exploring 7 A pitch
ITEMS
  [ "$option" = without ] || with_option
  board_moved_over
  with_token
  in_shell demo move dev 8 "In progress"
  same "$option shell exit" 0 "$SHELL_STATUS"
  same "$option record" "$(card 8)" "$by_core"
  same "$option calls to GitHub" "$(grep -c . "$CALLS")" "$core_calls"
done
same "reached the mirror" "In progress|PVTI_8|In progress|none|{\"dev\":true}" "$(echo "$by_core" | head -1)"

case_ "what the route refuses, it refuses in board.sh's words"
core_fixture
gh_items <<'ITEMS'
Ready 8 A task
Exploring 7 A pitch
Done 13 Finished
ITEMS
board_moved_over
for refused in "dev 8 Done" "dev 99 Ready" "nobody 8 Ready" "dev 8 Nowhere" "reviewer 8 Ready" "dev 7 Pitched" "lead 13 Idea"; do
  # shellcheck disable=SC2086 # the words are the command's arguments
  KEEP_BOARD=1 run board demo move $refused
  core_status=$STATUS
  cp "$ERR" "$ERR.core"
  # shellcheck disable=SC2086 # the words are the command's arguments
  in_shell demo move $refused
  same "$refused exit" "$SHELL_STATUS" "$core_status"
  same "$refused words" "$(cat "$ERR.shell")" "$(cat "$ERR.core")"
done

case_ "whatever isn't ported is handed to board.sh with the same arguments"
core_fixture
gh_items <<'ITEMS'
Ready 8 A task
ITEMS
run board demo wip
same "wip exit" 0 "$STATUS"
same "wip handed over" "fallback wip: not ported yet" "$(tail -1 "$TRACE")"
: >"$TRACE"
run board --dry-run demo move dev 8 "In progress"
same "dry run exit" 0 "$STATUS"
grep -q "a dry run" "$TRACE" || fail "dry run: '$(cat "$TRACE")'"
grep -q "(dry run) #8: Ready -> In progress" "$OUT" || fail "dry run said: '$(cat "$OUT")'"

case_ "a swap in Pitched is still lead-next's, so board.sh decides it"
core_fixture
gh_items <<'ITEMS'
Pitched 7 A pitch
ITEMS
board_moved_over
KEEP_BOARD=1 run board demo move lead 7 Exploring
grep -q "fallback move: a swap in Pitched" "$TRACE" || fail "swap: '$(cat "$TRACE")'"
grep -q "may move #7 out of Pitched only when" "$ERR" || fail "swap said: '$(cat "$ERR")'"

case_ "a mirror GitHub won't write to fails the push and not the move"
core_fixture
gh_items <<'ITEMS'
Ready 8 A task
ITEMS
board_moved_over
touch "$BIN/read-fails"
KEEP_BOARD=1 run board demo move dev 8 "In progress"
rm -f "$BIN/read-fails"
same "exit" 0 "$STATUS"
same "handled" "handled move" "$(traced)"

finish_suite
