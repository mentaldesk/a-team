#!/usr/bin/env bash
# Board commands that get no answer stop in bounded time, and say on what.
# shellcheck source=tests/scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
# shellcheck disable=SC2034,SC2154 # helpers read what an area sets, and an area reads what lib.sh sets

app_fakes
STALL=$(mktemp -d "$WORK/stall.XXXXXX")
for tool in gh git curl stall; do
  fake "$STALL/$tool" <<SH
#!/usr/bin/env bash
[ "\$(basename "\$0")" != gh ] || [ -z "\${STALL_ON:-}" ] || case "\$*" in *"\$STALL_ON"*) ;; *) exec "\$REAL_GH" "\$@" ;; esac
while [ \$SECONDS -lt $FAKE_LIMIT ]; do sleep 0.1; done
SH
done

limits_fixture() {
  CHECKOUT=$(mktemp -d "$WORK/checkout.XXXXXX")
  fixture <<JSON
{ "repo": "mentaldesk/demo", "stakeholders": ["reviewer"], "project": { "owner": "mentaldesk", "number": 1 },
  "checkout": "$CHECKOUT", "app": { "id": 7, "slug": "demo-app" } }
JSON
  A_TEAM_STATE=$(mktemp -d "$WORK/state.XXXXXX")
  gh_items <<'ITEMS'
Ready 8 A task
ITEMS
  run board demo list
  same "fixture" 0 "$STATUS"
}

# stalled <seconds> <args...>: runs a board command with one tool that never answers on PATH ahead, and how long it took.
stalled() {
  local limit=$1 started
  shift
  started=$SECONDS
  PATH="$STALL:$PATH" A_TEAM_CALL_LIMIT=$limit run board "$@"
  TOOK=$((SECONDS - started))
}

said_stuck() {
  same "exit" 124 "$STATUS"
  same "last line" "$1" "$(tail -n 1 "$ERR")"
  same "dispatch log" "demo${2:+ $2}: $1" "$(tail -n 1 "$A_TEAM_STATE/dispatch.log" | cut -d' ' -f2-)"
  [ "$TOOK" -le 6 ] || fail "took ${TOOK}s"
}

no_stall_left() {
  local left
  left=$(pgrep -f "$STALL/$1" || true)
  same "$1 stopped" "" "$left"
}

case_ "a gh that never answers is stopped at its limit, and only it"
limits_fixture
"$STALL/stall" &
other=$!
stalled 1 demo list
said_stuck "board stuck: gh api graphql RepoIssues (team demo, list) gave no answer in 1 second, stopping"
no_stall_left gh
kill -0 "$other" 2>/dev/null || fail "stopped a process it didn't start"
kill "$other"
wait "$other" 2>/dev/null

case_ "a git that never answers is stopped the same way"
limits_fixture
stalled 1 demo clean dev
said_stuck "board stuck: git worktree (team demo, clean) gave no answer in 1 second, stopping"
no_stall_left git

case_ "minting the App's token that never answers is stopped the same way, its own children with it"
limits_fixture
rm -f "$A_TEAM_STATE/demo/token.json"
NO_TOKEN=1 stalled 1 demo mine dev
said_stuck "board stuck: a-team token demo (team demo, mine) gave no answer in 1 second, stopping" dev
no_stall_left curl

case_ "a run's line in the dispatch log names its role"
limits_fixture
A_TEAM_RUN_ROLE=lead stalled 1 demo list
said_stuck "board stuck: gh api graphql RepoIssues (team demo, list) gave no answer in 1 second, stopping" lead

case_ "a comment whose post never answers leaves nothing in the record"
limits_fixture
echo "Hello" >"$WORK/limits-reply"
before=$(sqlite3 "$A_TEAM_STATE/history.db" "SELECT (SELECT COUNT(*) FROM events) || '|' || (SELECT COUNT(*) FROM comments_said);")
STALL_ON=comments REAL_GH="$BIN/gh" KEEP_BOARD=1 stalled 1 demo comment dev 8 "$WORK/limits-reply"
said_stuck "board stuck: gh api repos/mentaldesk/demo/issues/8/comments (team demo, comment) gave no answer in 1 second, stopping" dev
same "record" "$before" "$(sqlite3 "$A_TEAM_STATE/history.db" "SELECT (SELECT COUNT(*) FROM events) || '|' || (SELECT COUNT(*) FROM comments_said);")"
same "card" "Ready" "$(sqlite3 "$A_TEAM_STATE/history.db" "SELECT status FROM board WHERE team = 'demo' AND item = 8;")"

case_ "a command waiting past its deadline on no program of its own stops, saying on what"
limits_fixture
"$STALL/stall" &
holder=$!
sqlite3 "$A_TEAM_STATE/history.db" "UPDATE formats SET format = 1 WHERE team = 'demo'; INSERT INTO format_moves (team, pid) VALUES ('demo', $holder);"
started=$SECONDS
A_TEAM_COMMAND_LIMIT=2 KEEP_BOARD=1 run board demo format
TOOK=$((SECONDS - started))
said_stuck "board stuck: another a-team (pid $holder) moving the board forward (team demo, format) was still waiting after 2 seconds, stopping"
kill -0 "$holder" 2>/dev/null || fail "stopped the a-team it waited on"
kill "$holder"
wait "$holder" 2>/dev/null

finish_suite
