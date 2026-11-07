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

case_ "what a build generates doesn't make a checkout's core look out of date"
core_bin="$ROOT/core/bin/Debug/net10.0/a-team-core"
generated="$ROOT/core/obj/Release/net10.0/.core-test-generated.cs"
mkdir -p "$(dirname "$generated")"
: >"$generated"
touch -t 209901010000 "$generated"
stale=$(find "$ROOT/core" -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' -newer "$core_bin" -print -quit)
rm -f "$generated"
same "stale" "" "$stale"
grep -q "not -path '\*/obj/\*'" "$ROOT/bin/a-team" || fail "bin/a-team still counts generated sources"

case_ "only the status route writes a card's status"
writes=$(grep -nE 'SET status|INTO board \(|, status = ' "$ROOT"/scripts/*.sh "$ROOT"/core/*.cs | grep -v 'core/StatusRoute.cs' || true)
same "elsewhere" "" "$writes"

case_ "the board's format is the core's alone, and only a checkout's build can be told another"
same "in board.sh" "" "$(grep -nE 'BOARD_FORMAT|format_[a-z0-9]+\(\)' "$ROOT/scripts/board.sh" || true)"
same "the gate in board.sh" "core __format" "$(grep -n '__format' "$ROOT/scripts/board.sh" | cut -d: -f2-)"
same "read in a release" "" "$(awk '/^#if DEBUG/ { on = 1 } /^#else|^#endif/ { on = 0 } !on && /A_TEAM_BOARD_FORMAT/' "$ROOT"/core/*.cs)"

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
run board demo overview
same "overview exit" 0 "$STATUS"
grep -q "fallback overview: not ported yet" "$TRACE" || fail "overview: '$(cat "$TRACE")'"
grep -q "handled __items" "$TRACE" || fail "state: '$(cat "$TRACE")'"
: >"$TRACE"

case_ "the reads are the core's: none of them reaches board.sh"
core_fixture
gh_items <<'ITEMS'
Ready 8 A task
Building 7 A pitch
ITEMS
board_moved_over
for command in wip next today trend trends spending decisions "mine dev" "children 7" "body 8" "conversation 8" "history 8" \
  "moves 2026-10-11T09:00:00Z" "pr 8" "checks 8" "criteria 8 9" "feedback lead 8"; do
  read -ra words <<<"$command"
  KEEP_BOARD=1 run board demo "${words[@]}"
  same "$command exit" 0 "$STATUS"
  same "$command" "handled ${words[0]}" "$(traced)"
done

case_ "a read GitHub refuses says gh's own words, and exits as the shell did"
core_fixture
gh_items <<'ITEMS'
Ready 8 A task
Building 7 A pitch
ITEMS
board_moved_over
touch "$BIN/offline"
said="gh: error connecting to api.github.com"
refused_read() {
  KEEP_BOARD=1 run board demo "${@:4}"
  same "$4 exit" "$1" "$STATUS"
  same "$4 stdout" "$2" "$(cat "$OUT")"
  same "$4 stderr" "$3" "$(cat "$ERR")"
}
refused_read 1 '[]' "$said" children 7
refused_read 1 '' "$said" pr 8
refused_read 1 '' "$said"$'\n'"board.sh: could not read PR #8" checks 8
refused_read 1 '' "$said" criteria 8
refused_read 1 '' "$said"$'\n'"board.sh: can't read #8" criteria 8 9
refused_read 1 '' "$said" feedback lead 8
refused_read 1 '' "board.sh: can't read #8 ($said)" body 8
refused_read 1 '' "board.sh: can't read the conversation on #8 ($said)" conversation 8
rm "$BIN/offline"
: >"$TRACE"

case_ "pr still prints what jq made of a body gh failed with, as a PR number's read does"
core_fixture
gh_items <<'ITEMS'
In_review 8 A task
ITEMS
board_moved_over
refusing=$(mktemp -d "$WORK/refusing.XXXXXX")
fake "$refusing/gh" <<'SH'
echo '{"data": {"repository": {"issue": null, "pullRequests": {"nodes": []}}}}'
echo "gh: Could not resolve to an Issue with the number of 8." >&2
exit 1
SH
PATH="$refusing:$PATH" KEEP_BOARD=1 run board demo pr 8
same "exit" 1 "$STATUS"
same "stdout" null "$(cat "$OUT")"
same "stderr" "gh: Could not resolve to an Issue with the number of 8." "$(cat "$ERR")"
: >"$TRACE"

case_ "children stops at an error body gh prints, where jq could not read it as a list"
fake "$refusing/gh" <<'SH'
case " $* " in
  *sub_issues*) echo '{"message": "Not Found", "status": "404"}'; echo "gh: Not Found (HTTP 404)" >&2; exit 1 ;;
esac
exit 1
SH
PATH="$refusing:$PATH" KEEP_BOARD=1 run board demo children 8
same "exit" 5 "$STATUS"
same "stdout" "" "$(cat "$OUT")"
grep -qF "gh: Not Found (HTTP 404)" "$ERR" || fail "gh's words: '$(cat "$ERR")'"
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

case_ "check, setup and format are the core's: none of them reaches board.sh"
core_fixture
gh_items <<'ITEMS'
Ready 8 A task
ITEMS
board_moved_over
for command in check setup "setup --dry-run" format; do
  read -ra words <<<"$command"
  KEEP_BOARD=1 run board demo "${words[@]}"
  same "$command" "handled ${words[0]}" "$(traced)"
done
printf '{\n "repo": "mentaldesk/demo",\n}\n' >"$TEAM"
KEEP_BOARD=1 run board demo check
same "unreadable exit" 1 "$STATUS"
same "unreadable" "handled check" "$(traced)"

case_ "format says how it's used only once the gate has passed"
core_fixture
gh_items <<'ITEMS'
Ready 8 A task
ITEMS
board_moved_over
KEEP_BOARD=1 run board demo format now
same "exit" 1 "$STATUS"
same "words" "board.sh: usage: board.sh demo format" "$(cat "$ERR")"
sqlite3 "$A_TEAM_STATE/history.db" "UPDATE formats SET format = 99, version = '9.9.9' WHERE team = 'demo';"
KEEP_BOARD=1 run board demo format now
same "refused exit" 1 "$STATUS"
grep -q "the board is in format 99, from a-team 9.9.9" "$ERR" || fail "gate: '$(cat "$ERR")'"

case_ "a config whose project isn't an object is refused as board.sh refused it, and check reports it"
core_fixture
gh_items <<'ITEMS'
Ready 8 A task
ITEMS
echo '{ "repo": "mentaldesk/demo", "project": "mentaldesk/1" }' >"$TEAM"
run board demo wip
same "wip exit" 1 "$STATUS"
same "wip words" "board.sh: demo.json isn't a JSON object" "$(cat "$ERR")"
run board demo check
same "check exit" 1 "$STATUS"
same "check words" "problem  config    demo.json isn't a JSON object" "$(cat "$OUT")"

case_ "a record check can't read doesn't stop its other checks"
core_fixture
gh_items <<'ITEMS'
Ready 8 A task
ITEMS
echo "not a record" >"$A_TEAM_STATE/history.db"
run board demo check
grep -q '^problem  app ' "$OUT" || fail "check stopped: '$(cat "$OUT" "$ERR")'"
grep -q '^Error: ' "$ERR" || fail "said nothing: '$(cat "$ERR")'"

case_ "setup --dry-run after the command writes nothing, not even the format's stamp, and lists what the other dry run lists"
core_fixture
gh_items <<'ITEMS'
Ready 8 A task
ITEMS
board_moved_over
KEEP_BOARD=1 run board --dry-run demo setup
listed=$(cat "$OUT")
[ -n "$listed" ] || fail "nothing listed"
sqlite3 "$A_TEAM_STATE/history.db" "DELETE FROM formats;"
rm -f "$BIN/mutation.json"
: >"$WRITES"
KEEP_BOARD=1 run board demo setup --dry-run
same "exit" 0 "$STATUS"
same "listed" "$listed" "$(cat "$OUT")"
same "stderr" "" "$(cat "$ERR")"
same "writes" "" "$(cat "$WRITES")"
[ ! -e "$BIN/mutation.json" ] || fail "Status options changed"
same "stamp" "" "$(sqlite3 "$A_TEAM_STATE/history.db" "SELECT * FROM formats;")"

case_ "setup puts a label right only where it differs, and says what it changed"
core_fixture
gh_items <<'ITEMS'
Ready 8 A task
ITEMS
board_moved_over
LABELLED=$(mktemp -d "$WORK/bin.XXXXXX")
fake "$LABELLED/gh" <<SH
#!/usr/bin/env bash
case " \$* " in
  *" label list "*) [ ! -e "$LABELLED/list-fails" ] || { echo "gh: labels are down" >&2; exit 3; }; cat "$LABELLED/labels.json" ;;
  *" label edit "*) echo "\$*" >>"$WRITES" ;;
  *) exec "$BIN/gh" "\$@" ;;
esac
SH
labelled() { jq -n --arg color "$1" --arg description "$2" '[{name: "pitch", color: $color, description: $description}]' >"$LABELLED/labels.json"; }
setup_labels() {
  : >"$WRITES"
  PATH="$LABELLED:$PATH" KEEP_BOARD=1 run board "$@"
  same "$* exit" 0 "$STATUS"
}
right="An a-team pitch: Lead shapes it, reviewer approves it"
labelled 5319e7 "$right"
setup_labels demo setup
same "right said" "" "$(grep label "$OUT")"
same "right writes" "" "$(grep label "$WRITES" || true)"
labelled ffffff "$right"
setup_labels demo setup
same "colour said" "updated label pitch: colour" "$(grep label "$OUT")"
same "colour writes" "label edit pitch -R mentaldesk/demo --color 5319e7 --description $right" "$(grep label "$WRITES")"
labelled ffffff "Something else"
setup_labels --dry-run demo setup
same "both said" "(dry run) updated label pitch: colour and description" "$(grep label "$OUT")"
same "dry run writes" "" "$(grep label "$WRITES" || true)"
touch "$LABELLED/list-fails"
PATH="$LABELLED:$PATH" KEEP_BOARD=1 run board demo setup
same "unreadable exit" 3 "$STATUS"
same "unreadable words" "gh: labels are down" "$(cat "$ERR")"

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
