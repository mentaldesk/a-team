#!/usr/bin/env bash
# A state folder built to look weeks old, and the dispatcher, status and a pass's work over it.
# shellcheck source=tests/scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
# shellcheck disable=SC2034,SC2154 # helpers read what an area sets, and an area reads what lib.sh sets

FINISHED=300
WEEKS_AGO=$(jq -rn 'now - 21 * 86400 | strflocaltime("%Y%m%d%H%M.%S")')
THEN=$(ago $((21 * 1440)))
THEN_AT=$(($(date +%s) - 21 * 86400))

# A pid nothing has now: a process that has been waited for.
gone_pid() { sh -c 'exit 0' & local pid=$!; wait "$pid"; echo "$pid"; }

# A process no a-team started. Not this shell's child, so stopping it says nothing on stderr.
stranger() { (exec -a "$WORK/stranger" sleep "$FAKE_LIMIT") >/dev/null 2>&1 & echo $!; }

# stranger_ignoring_term <file>: notes each TERM in <file> and goes on, as the Simulator daemons did.
stranger_ignoring_term() {
  bash -c "trap 'echo TERM >>\"$1\"' TERM; while [ \$SECONDS -lt $FAKE_LIMIT ]; do sleep 0.1; done" >/dev/null 2>&1 &
  echo $!
}

# finished_runs <rows>: $FINISHED finished runs across the roles, each with its log and its row in <rows>. A Dev or
# Reviewer run has a folder, some of those a pid file and a release copy, and some rows haven't settled.
finished_runs() {
  local rows=$1 dead n role dir log result ended cost dirs=()
  dead=$(gone_pid)
  for role in lead customer reviewer dev; do dirs+=("$A_TEAM_STATE/demo/$role/logs"); done
  mkdir -p "${dirs[@]}"
  for n in $(seq "$FINISHED"); do
    case $((n % 5)) in
      0) role=lead ;;
      1) role=customer ;;
      2) role=reviewer ;;
      *) role=dev ;;
    esac
    log="$A_TEAM_STATE/demo/$role/logs/20261017T000000Z-$n.jsonl"
    result='{"type":"result","is_error":false,"total_cost_usd":0.5}'
    [ $((n % 12)) -ne 6 ] || result=''
    printf '{"type":"system"}\n%s\n' "$result" >"$log"
    ended="'$THEN'" cost=1.25
    [ $((n % 6)) -ne 0 ] || ended=NULL cost=NULL
    echo "INSERT INTO runs (team, role, pid, log, started, ended, cost) VALUES ('demo', '$role', $dead, '$log', '$THEN', $ended, $cost);" >>"$rows"
    case $role in
      lead | customer) continue ;;
    esac
    dir="$A_TEAM_STATE/demo/$role/runs/$n"
    mkdir -p "$dir"
    printf '{"number":%s,"title":"Task %s"}\n' "$n" "$n" >"$dir/task"
    echo "$THEN_AT" >"$dir/last-start"
    echo "Ready task #$n to build" >"$dir/last-reasons"
    ln -s "$log" "$dir/latest.jsonl"
    [ $((n % 3)) -ne 0 ] || echo "$dead" >"$dir/pid"
    [ $((n % 4)) -ne 0 ] || { mkdir "$dir/release"; echo 0.1.0 >"$dir/release/VERSION"; }
  done
  for role in lead customer reviewer dev; do
    dir="$A_TEAM_STATE/demo/$role"
    echo "$dead" >"$dir/pid"
    echo "$THEN_AT" >"$dir/last-start"
    mkdir "$dir/release"
  done
}

# old_run <role> <n> <pid> <rows>: a run long finished, whose pid <pid> has since been given to something else.
old_run() {
  local dir="$A_TEAM_STATE/demo/$1/runs/$2" log="$A_TEAM_STATE/demo/$1/logs/20261017T000000Z-$2.jsonl"
  mkdir -p "$dir/release"
  printf '{"number":%s,"title":"Task %s"}\n' "$2" "$2" >"$dir/task"
  echo "$THEN_AT" >"$dir/last-start"
  echo "$3" >"$dir/pid"
  : >"$log"
  echo "INSERT INTO runs (team, role, pid, log, started) VALUES ('demo', '$1', $3, '$log', '$THEN');" >>"$4"
}

# real_run <n> <started>: a Dev run going now, noting a TERM in $REAL_TERMS before it stops.
real_run() {
  local dir="$A_TEAM_STATE/demo/dev/runs/$1" log="$A_TEAM_STATE/demo/dev/logs/now-$1.jsonl" pid
  mkdir -p "$dir/release"
  bash -c "trap 'echo $1 >>\"$REAL_TERMS\"; exit' TERM; while [ \$SECONDS -lt $FAKE_LIMIT ]; do sleep 0.1; done" >/dev/null 2>&1 &
  pid=$!
  echo "$pid" >"$dir/pid"
  echo "$2" >"$dir/last-start"
  printf '{"number":%s,"title":"Task %s"}\n' "$1" "$1" >"$dir/task"
  : >"$log"
  history_db "INSERT INTO runs (team, role, pid, log, started) VALUES ('demo', 'dev', $pid, '$log', '$(ago 1)');" >/dev/null
  echo "$pid"
}

pids_in() { find "$A_TEAM_STATE/demo" -type f -name pid | sort; }
pids_named() { pids_in | sed "s|^$A_TEAM_STATE/demo/||" | paste -sd ' ' -; }
releases_named() { find "$A_TEAM_STATE/demo" -type d -name release -prune | sed "s|^$A_TEAM_STATE/demo/||" | sort | paste -sd ' ' -; }
pids_in_status() { A_TEAM_CONFIG="$CONFIG" bash "$DISPATCH/scripts/status.sh" | grep -oE '\(pid [0-9]+' | cut -d' ' -f2 | sort -n | paste -sd ' ' -; }
sorted() { printf '%s\n' "$@" | sort -n | paste -sd ' ' -; }

# commands_in_a_pass <trace>: what the dispatcher ran on a pass, each command one line of its trace.
commands_in_a_pass() { A_TEAM_CONFIG="$CONFIG" bash -x "$DISPATCH/scripts/dispatch.sh" 2>"$1"; grep -c '^+' "$1"; }
most_run() { sed -nE 's/^\++ ([^ ]+).*/\1/p' "$1" | sort | uniq -c | sort -rn | head -n 3 | awk '{print $2 " x" $1}' | paste -sd ',' - | sed 's/,/, /g'; }

fixture <<JSON
{ "repo": "mentaldesk/demo", "app": { "id": 7, "slug": "demo-app" }, "dispatch": { "enabled": true },
  "roles": { "reviewer": true, "customer": true }, "wip": { "devs": 3 }, "workdir": "$WORK" }
JSON
dev_dispatcher
LAUNCHED="$DISPATCH/launched"
: >"$LAUNCHED"
fake "$DISPATCH/bin/claude" <<SH
#!/usr/bin/env bash
echo "\$A_TEAM_RUN_TEAM \$A_TEAM_RUN_TASK" >>"$LAUNCHED"
exec -a "\$0" sleep $FAKE_LIMIT
SH
quiet_triggers() {
  echo '{"reasons": [], "creative": false}' >"$LEAD_TRIGGERS"
  jq -n '{reasons: [], creative: false, tasks: [], ready: null, chores: []}' | tee "$DEV_TRIGGERS" >"$REVIEWER_TRIGGERS"
}

case_ "a pass over an empty folder, to measure the aged one against"
quiet_triggers
AGED_STATE=$A_TEAM_STATE
A_TEAM_STATE="$WORK/empty-state"
commands_in_a_pass "$WORK/empty-1.trace" >/dev/null
NONE=$(commands_in_a_pass "$WORK/empty-2.trace")
[ "$NONE" -gt 0 ] || fail "couldn't count an empty folder's pass: '$(cat "$WORK/empty-2.trace")'"
A_TEAM_STATE=$AGED_STATE

case_ "the builder makes a folder weeks old"
mkdir -p "$A_TEAM_STATE"
ROWS="$WORK/rows.sql"
echo "BEGIN;" >"$ROWS"
finished_runs "$ROWS"
STRANGER_TERMS="$WORK/stranger-terms"
: >"$STRANGER_TERMS"
PLAIN=$(stranger)
IGNORES_DEV=$(stranger_ignoring_term "$STRANGER_TERMS")
IGNORES_REVIEWER=$(stranger_ignoring_term "$STRANGER_TERMS")
IGNORES_LEAD=$(stranger_ignoring_term "$STRANGER_TERMS")
old_run dev 2000 "$PLAIN" "$ROWS"
old_run dev 2001 "$IGNORES_DEV" "$ROWS"
old_run reviewer 2002 "$IGNORES_REVIEWER" "$ROWS"
echo "$PLAIN" >"$A_TEAM_STATE/demo/dev/pid"
echo "$IGNORES_LEAD" >"$A_TEAM_STATE/demo/lead/pid"
echo 'COMMIT;' >>"$ROWS"
history_db "SELECT 1;" >/dev/null
sqlite3 "$A_TEAM_STATE/history.db" <"$ROWS"
find "$A_TEAM_STATE/demo" -type f -exec touch -t "$WEEKS_AGO" {} +
OLD_PIDS=$(pids_in | grep -c '')
same "runs recorded" $((FINISHED + 3)) "$(sqlite3 "$A_TEAM_STATE/history.db" 'SELECT COUNT(*) FROM runs;')"
same "unsettled rows" $((FINISHED / 6 + 3)) "$(sqlite3 "$A_TEAM_STATE/history.db" 'SELECT COUNT(*) FROM runs WHERE ended IS NULL;')"
[ "$OLD_PIDS" -gt 50 ] || fail "only $OLD_PIDS old pid files"
REAL_TERMS="$WORK/real-terms"
: >"$REAL_TERMS"
OVER_LIMIT=$(real_run 3000 $(($(date +%s) - 3 * 3600)))
WITHIN_LIMIT=$(real_run 3001 "$(date +%s)")

case_ "a-team status lists only the real runs"
same "status listed a finished run or a stranger" "$(sorted "$OVER_LIMIT" "$WITHIN_LIMIT")" "$(pids_in_status)"

case_ "finished runs take no slots, and a new run starts"
jq -n '{reasons: [], creative: false, ready: 13, chores: [], tasks: []}' >"$DEV_TRIGGERS"
echo '{"number": 13, "title": "Task 13"}' >"$CLAIMED"
jq -n '{reasons: [], creative: false, ready: null, chores: [],
        tasks: [{number: 21, title: "Task 21", reasons: ["PR #921 is green and waiting for its review"]}]}' >"$REVIEWER_TRIGGERS"
echo '{"reasons": ["stakeholder feedback on #5"], "creative": false, "items": [5]}' >"$LEAD_TRIGGERS"
dispatch_dev
for role_started in 'dev: started .*#13' 'reviewer: started .*#21' 'lead: started'; do
  grep -q "demo $role_started" "$A_TEAM_STATE/dispatch.log" ||
    fail "finished runs took the slots: no '$role_started' in '$(cat "$A_TEAM_STATE/dispatch.log")'"
done
for _ in $(seq 50); do [ "$(grep -c '' "$LAUNCHED")" -ge 3 ] && break; sleep 0.1; done
same "runs that started" 3 "$(grep -c '' "$LAUNCHED")"
NEW_DEV=$(cat "$A_TEAM_STATE/demo/dev/runs/13/pid")
NEW_REVIEWER=$(cat "$A_TEAM_STATE/demo/reviewer/runs/21/pid")
NEW_LEAD=$(cat "$A_TEAM_STATE/demo/lead/pid")

case_ "only real runs get signals, and a process now holding an old run's pid is left alone"
for _ in $(seq 50); do [ -s "$REAL_TERMS" ] && break; sleep 0.1; done
same "the real run past its limit was stopped" 3000 "$(cat "$REAL_TERMS")"
kill -0 "$WITHIN_LIMIT" 2>/dev/null || fail "a real run within its limit was stopped"
quiet_triggers
dispatch_dev
same "killed in the log" 1 "$(grep -c 'killed run' "$A_TEAM_STATE/dispatch.log")"
kill -0 "$PLAIN" 2>/dev/null || fail "a stranger on an old run's pid was killed: '$(grep 'killed run' "$A_TEAM_STATE/dispatch.log")'"
[ -s "$STRANGER_TERMS" ] && fail "a stranger that ignores TERM was sent it $(grep -c '' "$STRANGER_TERMS") times"
for pid in "$IGNORES_DEV" "$IGNORES_REVIEWER" "$IGNORES_LEAD"; do
  kill -0 "$pid" 2>/dev/null || fail "stranger $pid is gone"
done
[ -e "$A_TEAM_STATE/demo/dev/stuck" ] && fail "a stranger was reported stuck: '$(cat "$A_TEAM_STATE/demo/dev/stuck")'"

case_ "old pid files and release copies are gone after a pass, and unsettled history rows settle"
same "pid files left" "dev/pid dev/runs/13/pid dev/runs/3001/pid lead/pid reviewer/pid reviewer/runs/21/pid" "$(pids_named)"
same "release copies left" "dev/runs/13/release dev/runs/3001/release lead/release reviewer/runs/21/release" "$(releases_named)"
same "rows still open" "$(sorted "$WITHIN_LIMIT" "$NEW_DEV" "$NEW_REVIEWER" "$NEW_LEAD")" \
  "$(sqlite3 "$A_TEAM_STATE/history.db" 'SELECT pid FROM runs WHERE ended IS NULL ORDER BY pid;' | paste -sd ' ' -)"
same "rows with a result keep its cost" $((FINISHED / 12)) "$(sqlite3 "$A_TEAM_STATE/history.db" 'SELECT COUNT(*) FROM runs WHERE cost = 0.5;')"
same "rows with no result died" $((FINISHED / 12 + 3)) "$(sqlite3 "$A_TEAM_STATE/history.db" "SELECT COUNT(*) FROM runs WHERE outcome = 'died';")"
same "the killed run says so" 1 "$(sqlite3 "$A_TEAM_STATE/history.db" "SELECT COUNT(*) FROM runs WHERE pid = $OVER_LIMIT AND outcome = 'killed';")"
same "a-team status" "$(sorted "$WITHIN_LIMIT" "$NEW_DEV" "$NEW_REVIEWER" "$NEW_LEAD")" "$(pids_in_status)"

case_ "once its runs end, a pass over it starts about as many commands as one over an empty folder"
kill "$WITHIN_LIMIT" "$NEW_DEV" "$NEW_REVIEWER" "$NEW_LEAD" 2>/dev/null
for _ in $(seq 50); do kill -0 "$WITHIN_LIMIT" 2>/dev/null || kill -0 "$NEW_DEV" 2>/dev/null || kill -0 "$NEW_REVIEWER" 2>/dev/null || kill -0 "$NEW_LEAD" 2>/dev/null || break; sleep 0.1; done
dispatch_dev
same "pid files after the last run" "" "$(pids_named)"
same "release copies after the last run" "" "$(releases_named)"
same "open rows after the last run" 0 "$(sqlite3 "$A_TEAM_STATE/history.db" 'SELECT COUNT(*) FROM runs WHERE ended IS NULL;')"
for pass in 1 2 3; do
  AGED=$(commands_in_a_pass "$WORK/aged-$pass.trace")
  [ "$AGED" -le $((NONE + 30)) ] ||
    fail "a pass's work grew with finished runs: $AGED commands on $FINISHED runs, $NONE on none; most run: $(most_run "$WORK/aged-$pass.trace")"
done
echo "  $AGED commands on $FINISHED runs, $NONE on none"
same "a-team status at the end" "" "$(pids_in_status)"

kill -9 "$PLAIN" "$IGNORES_DEV" "$IGNORES_REVIEWER" "$IGNORES_LEAD" 2>/dev/null

finish_suite
