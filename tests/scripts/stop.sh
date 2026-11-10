#!/usr/bin/env bash
# Stop, resume, and the dispatcher's passes.
# shellcheck source=tests/scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
# shellcheck disable=SC2034,SC2154 # helpers read what an area sets, and an area reads what lib.sh sets

case_ "stop ends the run once, holds the role, and lists what it left claimed"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 },
  "dispatch": { "enabled": true } }
JSON
gh_items <<'ITEMS'
In_progress 12 A task left half done
Ready 13 A task nobody has claimed
ITEMS
fake_run
history_db "INSERT INTO runs (team, role, pid, log, started) VALUES ('demo', 'dev', $RUN_PID, 'a.jsonl', '2026-10-07T09:00:00Z');"
run stop demo dev
wait "$RUN_PID"
same "exit" 0 "$STATUS"
same "signals" 1 "$(grep -c TERM "$SIGNALS")"
same "recorded" "dev - 0 - stopped" "$(runs_recorded)"
same "hold" '["dev"]' "$(held)"
same "enabled" true "$(enabled)"
grep -q "stopped demo dev's run ($RUN_PID)" "$OUT" || fail "stop: no word of the run in '$(cat "$OUT")'"
grep -q '#12 A task left half done' "$OUT" || fail "stop: #12 isn't listed as left claimed"
grep -q '#13' "$OUT" && fail "stop: #13 was never claimed"

case_ "a dispatcher pass skips the held role, and starts its sibling"
APP=$(mktemp -d "$WORK/dispatch.XXXXXX")
mkdir -p "$APP/bin" "$APP/scripts"
cp "$ROOT"/scripts/*.sh "$APP/scripts/"
fake "$APP/bin/a-team" <<'SH'
#!/usr/bin/env bash
if [ "$1" = token ] && [ "${NOT_INSTALLED:-}" = 2 ]; then
  echo "a-team token: other" >&2
  exit 1
fi
if [ "$1" = token ] && [ -n "${NOT_INSTALLED:-}" ]; then
  echo "a-team token: app 7 isn't installed on mentaldesk/$2: install it at https://github.com/apps/demo-app/installations/new" >&2
  exit 1
fi
[ "$1" = board ] && [ "${!#}" = format ] && [ -n "${BOARD_AHEAD:-}" ] && {
  echo "board.sh: the board is in format 2, from a-team 0.4.0, and this is 0.3.0: brew upgrade a-team" >&2
  exit 1
}
[ "$1" = board ] && { [ "${!#}" = free ] || [ "${!#}" = update ] || [ "${!#}" = bumps ] || [ "${!#}" = finished ]; } && exit 0
[ "$1" = board ] && [ "${!#}" = adopt ] && { echo "$*" >>"${ADOPTED:-/dev/null}"; exit 0; }
[ "$1" = board ] && [ "${!#}" = sync ] && { echo "$*" >>"${SYNCS:-/dev/null}"; exit 0; }
echo '{"reasons": ["work to do"], "creative": false}'
SH
A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/dispatch.sh" --dry-run
grep -q 'demo lead: would start' "$A_TEAM_STATE/dispatch.log" || fail "dispatch: the lead wasn't started"
grep -q 'demo dev' "$A_TEAM_STATE/dispatch.log" && fail "dispatch: the held dev was started"

case_ "a dispatcher pass adopts each team's upstream PRs, the dry run's in dry run"
ADOPTED="$WORK/adopted" A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/dispatch.sh" --dry-run
same "adopt" "board --dry-run demo adopt" "$(cat "$WORK/adopted")"

case_ "a dispatcher pass syncs each team's board, the cards' detail with it"
SYNCS="$WORK/syncs" A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/dispatch.sh" --dry-run
same "sync" "board --dry-run demo sync" "$(cat "$WORK/syncs")"

case_ "a dispatcher behind the board's format stops the team, saying why once, until it's upgraded"
: >"$A_TEAM_STATE/dispatch.log"
rm -f "$A_TEAM_STATE"/demo/*/dry-*
for _ in 1 2 3; do BOARD_AHEAD=1 A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/dispatch.sh" --dry-run; done
grep -q 'demo lead\|demo dev' "$A_TEAM_STATE/dispatch.log" && fail "behind: demo was started: '$(cat "$A_TEAM_STATE/dispatch.log")'"
same "stopped once" 1 "$(grep -c 'demo: stopped: the board is in format 2, from a-team 0.4.0, and this is 0.3.0: brew upgrade a-team$' "$A_TEAM_STATE/dispatch.log")"
A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/dispatch.sh" --dry-run
same "running again" 1 "$(grep -c 'demo: running again' "$A_TEAM_STATE/dispatch.log")"

case_ "a dispatcher pass skips a team with no app key, says it stopped only on the second pass, and runs the others"
jq 'del(.app) | .dispatch.hold = []' "$TEAM" >"$CONFIG/teams/bare.json"
: >"$A_TEAM_STATE/dispatch.log"
rm -f "$A_TEAM_STATE"/demo/*/dry-*
A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/dispatch.sh" --dry-run
grep -q 'bare lead\|bare dev' "$A_TEAM_STATE/dispatch.log" && fail "no app: bare was started"
grep -q 'demo lead: would start' "$A_TEAM_STATE/dispatch.log" || fail "no app: demo wasn't started"
grep -q 'bare: stopped' "$A_TEAM_STATE/dispatch.log" && fail "no app: stopped after one pass"
A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/status.sh" >"$OUT"
grep -q '^bare: stopped' "$OUT" && fail "no app: status says stopped after one pass"
A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/dispatch.sh" --dry-run
grep -q 'bare lead\|bare dev' "$A_TEAM_STATE/dispatch.log" && fail "no app: bare was started on the second pass"
same "stopped once" 1 "$(grep -c 'bare: stopped: no GitHub App' "$A_TEAM_STATE/dispatch.log")"
A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/status.sh" >"$OUT"
grep -q '^bare: stopped: no GitHub App: run a-team app create bare, then install it$' "$OUT" ||
  fail "no app: status says '$(cat "$OUT")'"
grep -q '^demo: stopped' "$OUT" && fail "no app: demo shows as stopped"
A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/dispatch.sh" --dry-run
same "same reason, still once" 1 "$(grep -c 'bare: stopped' "$A_TEAM_STATE/dispatch.log")"
rm "$CONFIG/teams/bare.json"

case_ "a team whose check fails once and then passes says nothing"
: >"$A_TEAM_STATE/dispatch.log"
NOT_INSTALLED=1 A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/dispatch.sh" --dry-run
grep -q 'demo lead' "$A_TEAM_STATE/dispatch.log" && fail "blip: demo was started"
A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/dispatch.sh" --dry-run
grep -q 'demo: ' "$A_TEAM_STATE/dispatch.log" && fail "blip: '$(cat "$A_TEAM_STATE/dispatch.log")'"
A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/dispatch.sh" --dry-run
NOT_INSTALLED=1 A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/dispatch.sh" --dry-run
grep -q 'demo: stopped' "$A_TEAM_STATE/dispatch.log" && fail "blip: two failures that weren't in a row"
A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/dispatch.sh" --dry-run

case_ "a team whose App isn't installed says it stopped, again when the reason changes, and running again once it is"
: >"$A_TEAM_STATE/dispatch.log"
NOT_INSTALLED=1 A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/dispatch.sh" --dry-run
NOT_INSTALLED=1 A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/dispatch.sh" --dry-run
grep -q "demo: stopped: app 7 isn't installed on mentaldesk/demo" "$A_TEAM_STATE/dispatch.log" ||
  fail "not installed: '$(cat "$A_TEAM_STATE/dispatch.log")'"
grep -q 'demo lead' "$A_TEAM_STATE/dispatch.log" && fail "not installed: demo was started"
A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/status.sh" >"$OUT"
grep -q "^demo: stopped: app 7 isn't installed" "$OUT" || fail "not installed: status says '$(cat "$OUT")'"
NOT_INSTALLED=2 A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/dispatch.sh" --dry-run
same "new reason" 1 "$(grep -c 'demo: stopped: other' "$A_TEAM_STATE/dispatch.log")"
same "stopped twice in all" 2 "$(grep -c 'demo: stopped' "$A_TEAM_STATE/dispatch.log")"
A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/dispatch.sh" --dry-run
same "running again" 1 "$(grep -c 'demo: running again$' "$A_TEAM_STATE/dispatch.log")"
A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/status.sh" >"$OUT"
grep -q '^demo: stopped' "$OUT" && fail "installed: demo still shows as stopped"
A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/dispatch.sh" --dry-run
same "running again once" 1 "$(grep -c 'demo: running again' "$A_TEAM_STATE/dispatch.log")"

case_ "a team moved aside to <name>.json.removed is neither listed nor dispatched"
jq '.dispatch.hold = []' "$TEAM" >"$CONFIG/teams/gone.json.removed"
: >"$A_TEAM_STATE/dispatch.log"
A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/dispatch.sh" --dry-run
grep -q 'gone' "$A_TEAM_STATE/dispatch.log" && fail "removed: gone was dispatched"
run teams
grep -q 'gone' "$OUT" && fail "removed: teams lists '$(cat "$OUT")'"
grep -q '^demo ' "$OUT" || fail "removed: teams dropped demo: '$(cat "$OUT")'"
rm "$CONFIG/teams/gone.json.removed"

case_ "resume <team> <role> lets it go and leaves dispatch.enabled alone"
jq '.dispatch.enabled = false' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
run resume demo dev
same "exit" 0 "$STATUS"
same "hold" '[]' "$(held)"
same "enabled" false "$(enabled)"

case_ "stop with no run going still holds, and says there was nothing to stop"
true &
dead=$!
wait "$dead"
echo "$dead" >"$A_TEAM_STATE/demo/dev/pid"
gh_items </dev/null
run stop demo dev
same "exit" 0 "$STATUS"
same "hold" '["dev"]' "$(held)"
grep -q 'no run to stop' "$OUT" || fail "dead pid: '$(cat "$OUT")'"
grep -q 'left nothing claimed' "$OUT" || fail "nothing claimed: '$(cat "$OUT")'"

case_ "stopping it again doesn't hold it twice"
run stop demo dev
same "hold" '["dev"]' "$(held)"

case_ "stop leaves alone a process that has since been given the run's pid"
(exec -a "$WORK/stranger" sleep 30) >/dev/null 2>&1 &
stranger=$!
echo "$stranger" >"$A_TEAM_STATE/demo/dev/pid"
backdate "$A_TEAM_STATE/demo/dev/pid"
run stop demo dev
same "exit" 0 "$STATUS"
grep -q 'no run to stop' "$OUT" || fail "stranger: '$(cat "$OUT")'"
kill -0 "$stranger" 2>/dev/null || fail "stop killed it"
kill "$stranger"

case_ "stop --dry-run names the run it would stop, and neither stops nor holds it"
run resume demo dev
fake_run
run stop --dry-run demo dev
same "exit" 0 "$STATUS"
same "hold" '[]' "$(held)"
grep -q "would stop run $RUN_PID" "$OUT" || fail "dry run: '$(cat "$OUT")'"
kill -0 "$RUN_PID" 2>/dev/null || fail "dry run: the run was stopped"

if [ "$(id -u)" != 0 ]; then
  case_ "a config stop can't write is refused, and the run goes on"
  chmod 444 "$TEAM"
  run stop demo dev
  failed "unwritable"
  grep -q "$TEAM" "$ERR" || fail "unwritable: the file isn't named in '$(cat "$ERR")'"
  kill -0 "$RUN_PID" 2>/dev/null || fail "unwritable: the run was stopped anyway"
  same "signals" 0 "$(grep -c TERM "$SIGNALS")"
  chmod 644 "$TEAM"
fi
kill "$RUN_PID" 2>/dev/null
wait "$RUN_PID" 2>/dev/null

case_ "stop needs a role it knows"
run stop demo
failed "no role"
run stop demo tester
failed "unknown role"

case_ "pause, stop, resume and attach take the Customer lead only on a team that has it on"
for cmd in "pause demo customer" "stop demo customer" "resume demo customer" "attach demo customer"; do
  # shellcheck disable=SC2086 # one argument per word
  run $cmd
  failed "$cmd while off"
  grep -q "demo has no Customer lead" "$ERR" || fail "$cmd while off: '$(cat "$ERR")'"
done
jq '.roles.customer = true' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
run pause demo customer
same "exit" 0 "$STATUS"
same "hold" '["customer"]' "$(held)"
A_TEAM_CONFIG="$CONFIG" bash "$ROOT/scripts/status.sh" >"$OUT"
grep -q '^demo customer: never run$' "$OUT" || fail "status: '$(cat "$OUT")'"
run resume demo customer
same "hold" '[]' "$(held)"
jq '.roles.reviewer = true' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
for role in customer reviewer; do
  run pause demo "$role"
  same "exit with both on" 0 "$STATUS"
  same "hold with both on" "[\"$role\"]" "$(held)"
  run resume demo "$role"
  same "hold with both on" '[]' "$(held)"
  run attach demo "$role"
  grep -q "demo $role has no run with a session" "$ERR" || fail "attach $role with both on: '$(cat "$ERR")'"
done
jq 'del(.roles)' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
A_TEAM_CONFIG="$CONFIG" bash "$ROOT/scripts/status.sh" >"$OUT"
grep -q 'customer' "$OUT" && fail "status while off: '$(cat "$OUT")'"

case_ "pause <team> <role> holds the role and lets its run finish"
jq '.dispatch.enabled = true' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
fake_run
run pause demo dev
same "exit" 0 "$STATUS"
same "hold" '["dev"]' "$(held)"
same "enabled" true "$(enabled)"
grep -q 'a-team resume demo dev' "$OUT" || fail "pause role: '$(cat "$OUT")'"
kill -0 "$RUN_PID" 2>/dev/null || fail "pause role: the run was stopped"
same "signals" 0 "$(grep -c TERM "$SIGNALS")"
kill "$RUN_PID" 2>/dev/null
wait "$RUN_PID" 2>/dev/null

case_ "a dispatcher pass skips the role pause held"
A_TEAM_STATE_WAS=$A_TEAM_STATE
A_TEAM_STATE=$(mktemp -d "$WORK/state.XXXXXX")
A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/dispatch.sh" --dry-run
grep -q 'demo lead: would start' "$A_TEAM_STATE/dispatch.log" || fail "dispatch: the lead wasn't started"
grep -q 'demo dev' "$A_TEAM_STATE/dispatch.log" && fail "dispatch: the paused dev was started"
A_TEAM_STATE=$A_TEAM_STATE_WAS

finish_suite
