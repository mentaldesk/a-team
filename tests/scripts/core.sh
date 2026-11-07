#!/usr/bin/env bash
# The compiled board: what it owns, it does itself, and only it writes a card's status.
# shellcheck source=tests/scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
# shellcheck disable=SC2034,SC2154 # helpers read what an area sets, and an area reads what lib.sh sets

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
# board_moved_over: the first commands make a new record and stamp its format.
board_moved_over() {
  run board demo list
  run board demo list
  : >"$TRACE"
}
events() { sqlite3 "$A_TEAM_STATE/history.db" "SELECT who, what FROM events WHERE team = 'demo' AND item = $1 ORDER BY id;"; }
card() { sqlite3 "$A_TEAM_STATE/history.db" "SELECT status, mirror, mirrored_status, mirrored_priority, marks FROM board WHERE team = 'demo' AND item = $1;"; }

case_ "only the status route writes a card's status"
writes=$(grep -nE 'SET status|INTO board \(|, status = ' "$ROOT"/scripts/*.sh "$ROOT"/core/*.cs | grep -v 'core/StatusRoute.cs' || true)
same "elsewhere" "" "$writes"

case_ "it knows the board format board.sh does"
same "format" "$(sed -n 's/^BOARD_FORMAT=//p' "$ROOT/scripts/board.sh")" "$(sed -n 's/.*const int DefaultFormat = \([0-9]*\);.*/\1/p' "$ROOT/core/Board.cs")"

case_ "list prints each card the way the app and the roles read it"
core_fixture
gh_items <<'ITEMS'
Idea 2 An "idea" with a quote and ünïcode
Ready 8 A task
Done 13 Finished
ITEMS
board_moved_over
KEEP_BOARD=1 run board demo list
same "exit" 0 "$STATUS"
same "handled" "handled list" "$(traced)"
same "pretty" '  {' "$(sed -n 2p "$OUT")"
same "fields" 'id,number,node,type,title,url,labels,marks,blockedBy,priority,since,parent,status' "$(jq -r '.[0] | keys_unsorted | join(",")' "$OUT")"
same "title" 'An "idea" with a quote and ünïcode' "$(jq -r '.[0].title' "$OUT")"
KEEP_BOARD=1 run board demo list Ready "Done"
same "filtered" "[8,13]" "$(jq -c 'map(.number)' "$OUT")"

case_ "a move reaches the mirror where the mirror has the status, and is kept to push where it hasn't"
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
  if [ "$option" = with ]; then
    same "$option record" 'In progress|PVTI_8|In progress|none|{"dev":true}' "$(card 8)"
  else
    same "$option record" 'In progress|PVTI_8|Ready|none|{"dev":true}' "$(card 8)"
    grep -q "no 'In progress' option" <<<"$(sqlite3 "$A_TEAM_STATE/history.db" "SELECT failed FROM boards WHERE team = 'demo';")" || fail "why it's behind"
  fi
done

case_ "the route's refusals are in the board's own words"
core_fixture
gh_items <<'ITEMS'
Ready 8 A task
Exploring 7 A pitch
Done 13 Finished
ITEMS
board_moved_over
refused() {
  # shellcheck disable=SC2086 # the words are the command's arguments
  KEEP_BOARD=1 run board demo move $1
  same "$1 exit" 1 "$STATUS"
  same "$1 words" "board.sh: $2" "$(cat "$ERR")"
}
refused "dev 8 Done" "dev may not move #8 from 'Ready' to 'Done'"
refused "dev 99 Ready" "#99 is not on the board (use add)"
refused "nobody 8 Ready" "unknown role 'nobody' (lead | dev | customer | reviewer)"
refused "dev 8 Nowhere" "unknown status 'Nowhere'"
refused "reviewer 8 Ready" "reviewer may not move #8 from 'Ready' to 'Ready'"
refused "dev 7 Pitched" "dev may not move #7 from 'Exploring' to 'Pitched'"
refused "lead 13 Idea" "lead may not move #13 from 'Done' to 'Idea'"

case_ "whatever isn't ported runs in board.sh, which comes back to the core for the board's state"
core_fixture
gh_items <<'ITEMS'
Ready 8 A task
ITEMS
run board demo wip
same "wip exit" 0 "$STATUS"
grep -q "fallback wip: not ported yet" "$TRACE" || fail "wip: '$(cat "$TRACE")'"
grep -q "handled __items" "$TRACE" || fail "state: '$(cat "$TRACE")'"
: >"$TRACE"

case_ "a dry run is the core's own: it says what it would do, and changes nothing"
core_fixture
gh_items <<'ITEMS'
Ready 8 A task
ITEMS
board_moved_over
before=$(card 8)
own_temp=$(mktemp -d "$WORK/temp.XXXXXX")
TMPDIR=$own_temp KEEP_BOARD=1 run board --dry-run demo move dev 8 "In progress"
same "exit" 0 "$STATUS"
same "handled" "handled move" "$(traced)"
same "said" "(dry run) #8: Ready -> In progress" "$(cat "$OUT")"
grep -q "dry-run: would set #8 to 'In progress'" "$ERR" || fail "would: '$(cat "$ERR")'"
same "record" "$before" "$(card 8)"
same "history" "" "$(events 8)"
same "copies left behind" "" "$(find "$own_temp" -name 'a-team-history.*')"

case_ "a swap in Pitched is refused unless a draft displaces it"
core_fixture
gh_items <<'ITEMS'
Pitched 7 A pitch
ITEMS
board_moved_over
KEEP_BOARD=1 run board demo move lead 7 Exploring
same "handled" "handled move" "$(traced)"
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
