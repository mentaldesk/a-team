#!/usr/bin/env bash
# Stuck runs.
# shellcheck source=tests/scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
# shellcheck disable=SC2034,SC2154 # helpers read what an area sets, and an area reads what lib.sh sets

end_run() { kill -9 "$1" 2>/dev/null; wait "$1" 2>/dev/null; }
# on_task <n> <dir under the role>: that run's task file.
on_task() { jq -n --argjson number "$1" '{$number, title: "Task"}' >"$A_TEAM_STATE/demo/$2/task"; }
# closed_item <n> <minutes ago>: #<n> Done on the board's record, since then.
closed_item() {
  history_db "INSERT OR REPLACE INTO board (team, item, node, status, since, content) VALUES ('demo', $1, 'n$1', 'Done', '$(ago "$2")', '{\"shut\":true}');" >/dev/null
}
day_of() { LC_ALL=C date -r $(($(date +%s) - $1 * 60)) '+%-d %b' 2>/dev/null || LC_ALL=C date -d "@$(($(date +%s) - $1 * 60))" '+%-d %b'; }
status_stuck() { A_TEAM_CONFIG="$CONFIG" bash "$DISPATCH/scripts/status.sh" | grep ': stuck: '; }
stuck_fixture() {
  fixture <<'JSON'
{ "repo": "mentaldesk/demo", "app": { "id": 7, "slug": "demo-app" }, "dispatch": { "enabled": true, "maxRuntime": 120 }, "wip": { "devs": 1 }, "roles": { "reviewer": true } }
JSON
  dev_dispatcher
  mkdir -p "$A_TEAM_STATE"
  : >"$A_TEAM_STATE/dispatch.log"
}

case_ "a run still alive on the pass after its kill is stuck, says so once each way, and clears when it goes"
stuck_fixture
jq -n '{reasons: [], creative: false, tasks: [{number: 8, title: "T", reasons: ["r"]}], ready: 9, readyCount: 1, chores: []}' >"$DEV_TRIGGERS"
live_run dev/runs/13 $((3 * 3600)) ignores
on_task 13 dev/runs/13
dispatch_dev
[ -e "$A_TEAM_STATE/demo/dev/stuck" ] && fail "stuck on the pass that sent the kill"
same "no status on the first pass" "" "$(status_stuck)"
dispatch_dev
dispatch_dev
same "one killed line" 1 "$(logged 'demo dev: killed run')"
same "one stuck line" 1 "$(logged "demo dev: run $FAKE is stuck: outlived its 2h limit")"
same "status" "demo dev: stuck: run $FAKE outlived its 2h limit by 1h · holds its only slot, 2 tasks waiting" "$(status_stuck)"
same "not cleared yet" 0 "$(logged 'no longer stuck')"
end_run "$FAKE"
same "gone from status before any pass" "" "$(status_stuck)"
dispatch_dev
dispatch_dev
same "one clear line" 1 "$(logged "demo dev: run $FAKE no longer stuck")"
[ -e "$A_TEAM_STATE/demo/dev/stuck" ] && fail "kept the marker"
[ -e "$A_TEAM_STATE/demo/dev/killed" ] && fail "kept the killed record"

case_ "a run that stops when told never warns"
stuck_fixture
live_run dev/runs/13 $((3 * 3600))
dispatch_dev
wait "$FAKE" 2>/dev/null
dispatch_dev
same "killed once" 1 "$(logged 'demo dev: killed run')"
same "no warning" 0 "$(logged 'stuck')"

case_ "a closed task warns only after 30 minutes, on its record's say, and a run inside its limit on an open one never"
stuck_fixture
jq -n '{reasons: [], creative: false, tasks: [], ready: 9, readyCount: 4, chores: []}' >"$DEV_TRIGGERS"
live_run dev/runs/516 3600
on_task 516 dev/runs/516
closed_item 516 10
dispatch_dev
same "inside the 30 minutes" 0 "$(logged 'stuck')"
same "a full role reads no triggers" 0 "$(grep -c 'triggers dev' "$TRIGGER_READS")"
history_db "INSERT OR REPLACE INTO board (team, item, node, status, since, content) VALUES ('demo', 516, 'n', 'In progress', '$(ago 300)', '{}');" >/dev/null
dispatch_dev
same "an open task, with work waiting" 0 "$(logged 'stuck')"
closed_item 516 45
dispatch_dev
day=$(day_of 45)
same "past them" 1 "$(logged "demo dev: run $FAKE is stuck: on #516, which closed $day")"
same "says it" "demo dev: stuck: run $FAKE is on #516, closed $day · holds its only slot, 4 tasks waiting" "$(status_stuck)"
rm -f "$DEV_TRIGGERS"
dispatch_dev
same "a failed triggers read leaves the count out" "demo dev: stuck: run $FAKE is on #516, closed $day · holds its only slot" "$(status_stuck)"
same "and logs nothing more" 1 "$(logged 'is stuck')"
same "or a triggers failure" 0 "$(logged 'triggers failed')"
end_run "$FAKE"

case_ "a run stuck on a closed task stays stuck as it crosses its limit, says so once each way"
stuck_fixture
jq -n '{reasons: [], creative: false, tasks: [], ready: 9, readyCount: 1, chores: []}' >"$DEV_TRIGGERS"
live_run dev/runs/516 3600 ignores
on_task 516 dev/runs/516
closed_item 516 45
dispatch_dev
day=$(day_of 45)
same "on the task" "demo dev: stuck: run $FAKE is on #516, closed $day · holds its only slot, 1 task waiting" "$(status_stuck)"
echo $(($(date +%s) - 3 * 3600)) >"$A_TEAM_STATE/demo/dev/runs/516/last-start"
dispatch_dev
same "the pass that sends the kill" "demo dev: stuck: run $FAKE is on #516, closed $day · holds its only slot, 1 task waiting" "$(status_stuck)"
same "killed" 1 "$(logged 'demo dev: killed run')"
dispatch_dev
same "the pass after it" "demo dev: stuck: run $FAKE outlived its 2h limit by 1h · holds its only slot, 1 task waiting" "$(status_stuck)"
same "one stuck line" 1 "$(logged 'is stuck')"
same "not cleared" 0 "$(logged 'no longer stuck')"
end_run "$FAKE"
dispatch_dev
same "one clear line" 1 "$(logged "demo dev: run $FAKE no longer stuck")"

case_ "a run that came to a card already closed gets its 30 minutes from when it started"
stuck_fixture
live_run lead 60
jq -n '{number: 40}' >"$A_TEAM_STATE/demo/lead/card"
closed_item 40 5000
dispatch_dev
same "no warning" 0 "$(logged 'stuck')"
end_run "$FAKE"

case_ "a Lead run on a closed card is stuck; one on no card only by its limit"
stuck_fixture
live_run lead 3600
jq -n '{number: 40}' >"$A_TEAM_STATE/demo/lead/card"
closed_item 40 45
jq -n '{reasons: ["one", "two"], creative: false}' >"$LEAD_TRIGGERS"
dispatch_dev
same "on the card" "demo lead: stuck: run $FAKE is on #40, closed $(day_of 45) · holds its only slot, 2 things waiting" "$(status_stuck)"
rm "$A_TEAM_STATE/demo/lead/card"
dispatch_dev
same "on no card" "" "$(status_stuck)"
same "cleared" 1 "$(logged 'no longer stuck')"
end_run "$FAKE"

case_ "a Reviewer's waiting PRs leave out the one it's on"
stuck_fixture
jq -n '{reasons: [], creative: false, tasks: [{number: 7, title: "T", reasons: []}, {number: 8, title: "T", reasons: []}, {number: 516, title: "T", reasons: []}], ready: null, chores: []}' >"$REVIEWER_TRIGGERS"
live_run reviewer/runs/516 3600
on_task 516 reviewer/runs/516
closed_item 516 45
dispatch_dev
same "says it" "demo reviewer: stuck: run $FAKE is on #516, closed $(day_of 45) · holds its only slot, 2 PRs waiting" "$(status_stuck)"
same "reads triggers once" 1 "$(grep -c 'triggers reviewer' "$TRIGGER_READS")"
end_run "$FAKE"

case_ "no such task, or a record that can't be read, is no warning"
stuck_fixture
live_run dev/runs/516 3600
on_task 516 dev/runs/516
dispatch_dev
same "no such task" 0 "$(logged 'stuck')"
echo garbage >"$A_TEAM_STATE/history.db"
dispatch_dev 2>/dev/null
same "a failed read" 0 "$(logged 'is stuck')"
end_run "$FAKE"

case_ "a held or over-budget role still gets checked"
stuck_fixture
jq '.dispatch.hold = ["dev"]' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
live_run dev/runs/13 $((3 * 3600)) ignores
dispatch_dev
dispatch_dev
same "held" 1 "$(logged 'demo dev: run .* is stuck')"
end_run "$FAKE"
stuck_fixture
jq '.dispatch.budget = 1' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
history_db "INSERT INTO runs (team, role, pid, log, started, ended, cost) VALUES ('demo', 'lead', 1, 'x', '$(ago 5)', '$(ago 1)', 5);" >/dev/null
live_run dev/runs/13 $((3 * 3600)) ignores
dispatch_dev
dispatch_dev
same "over budget" 1 "$(logged 'demo dev: run .* is stuck')"
end_run "$FAKE"

case_ "status has a line for each of two stuck runs, oldest first, and counts the slots they hold"
stuck_fixture
jq '.wip.devs = 3' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
jq -n '{reasons: [], creative: false, tasks: [], ready: null, readyCount: 0, chores: []}' >"$DEV_TRIGGERS"
live_run dev/runs/13 $((5 * 3600)) ignores
first=$FAKE
live_run dev/runs/14 $((3 * 3600)) ignores
second=$FAKE
dispatch_dev
dispatch_dev
same "two lines" "demo dev: stuck: run $first outlived its 2h limit by 3h · holds 2 of 3 slots
demo dev: stuck: run $second outlived its 2h limit by 1h · holds 2 of 3 slots" "$(status_stuck)"
end_run "$first"
same "one goes" "demo dev: stuck: run $second outlived its 2h limit by 1h · holds 2 of 3 slots" "$(status_stuck)"
end_run "$second"

case_ "a marker shows nothing once its run has gone or its pid is another process's, paused team or not"
stuck_fixture
live_run dev/runs/13 $((3 * 3600)) ignores
dispatch_dev
dispatch_dev
same "stuck" 1 "$(status_stuck | grep -c .)"
jq '.dispatch.enabled = false' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
(exec -a "$WORK/stranger" sleep 300) >/dev/null 2>&1 &
stranger=$!
jq --argjson pid "$stranger" '.[0].pid = $pid' "$A_TEAM_STATE/demo/dev/stuck" >"$A_TEAM_STATE/demo/dev/stuck.new"
mv "$A_TEAM_STATE/demo/dev/stuck.new" "$A_TEAM_STATE/demo/dev/stuck"
echo "$stranger" >"$A_TEAM_STATE/demo/dev/runs/13/pid"
backdate "$A_TEAM_STATE/demo/dev/runs/13/pid"
same "a stranger on its pid" "" "$(status_stuck)"
end_run "$stranger"
echo "$FAKE" >"$A_TEAM_STATE/demo/dev/runs/13/pid"
jq --argjson pid "$FAKE" '.[0].pid = $pid' "$A_TEAM_STATE/demo/dev/stuck" >"$A_TEAM_STATE/demo/dev/stuck.new"
mv "$A_TEAM_STATE/demo/dev/stuck.new" "$A_TEAM_STATE/demo/dev/stuck"
same "back on the run" 1 "$(status_stuck | grep -c .)"
end_run "$FAKE"
same "its run gone, in a team no pass reaches" "" "$(status_stuck)"

case_ "--dry-run keeps the stuck state to its own dry- files"
stuck_fixture
live_run dev/runs/13 $((3 * 3600)) ignores
dispatch_dev --dry-run
dispatch_dev --dry-run
[ -s "$A_TEAM_STATE/demo/dev/dry-stuck" ] || fail "no dry-stuck"
[ -e "$A_TEAM_STATE/demo/dev/stuck" ] && fail "wrote the real marker"
[ -e "$A_TEAM_STATE/demo/dev/killed" ] && fail "wrote the real killed record"
same "status reads only the real one" "" "$(status_stuck)"
end_run "$FAKE"

finish_suite
