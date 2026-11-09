#!/usr/bin/env bash
# Runs the dispatcher starts for a task, and their limits.
# shellcheck source=tests/scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
# shellcheck disable=SC2034,SC2154 # helpers read what an area sets, and an area reads what lib.sh sets

case_ "a run for a task already in hand claims nothing new, and names its task"
A_TEAM_STATE_WAS=$A_TEAM_STATE
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "app": { "id": 7, "slug": "demo-app" }, "dispatch": { "enabled": true } }
JSON
dev_dispatcher
jq -n '{reasons: ["CI failed on PR #912 at deadbee", "Ready task available (e.g. #13) and a free worktree"], creative: false,
        tasks: [{number: 12, title: "Fix the pane", reasons: ["CI failed on PR #912 at deadbee"]}],
        ready: 13, chores: ["PR #900 has merged: clean up its worktree"]}' >"$DEV_TRIGGERS"
dispatch_dev --dry-run
grep -q 'demo dev: would start: #12: CI failed on PR #912 at deadbee;PR #900 has merged: clean up its worktree' \
  "$A_TEAM_STATE/dispatch.log" || fail "in hand: '$(cat "$A_TEAM_STATE/dispatch.log")'"
same "claims" "" "$(cat "$CLAIMS")"
same "task" '{"number":12,"title":"Fix the pane"}' "$(cat "$A_TEAM_STATE/demo/dev/dry-task")"

case_ "every pass settles the team's Dependabot PRs first, as the team's App, and logs what it merged"
same "bumps" "board --dry-run demo bumps as demo $DISPATCH/bin" "$(cat "$BUMPS")"
same "before free" "bumps" "$(head -n 1 "$FREES")"
: >"$A_TEAM_STATE/dispatch.log"
printf '(dry run) PR #535 (Bump x): merged\n' >"$BUMPED"
dispatch_dev --dry-run
grep -q 'demo: (dry run) PR #535 (Bump x): merged$' "$A_TEAM_STATE/dispatch.log" || fail "merged: '$(cat "$A_TEAM_STATE/dispatch.log")'"
: >"$BUMPED"

case_ "a merge GitHub keeps refusing is logged once, from the second pass"
echo "PR #535: can't merge PR #535 (HTTP 405)" >"$BUMPED.fails"
: >"$A_TEAM_STATE/dispatch.log"
dispatch_dev --dry-run
grep -q 'settling dependency bumps' "$A_TEAM_STATE/dispatch.log" && fail "first failure logged: '$(cat "$A_TEAM_STATE/dispatch.log")'"
dispatch_dev --dry-run
dispatch_dev --dry-run
same "logged once" 1 "$(grep -c "demo: settling dependency bumps failed: PR #535: can't merge PR #535 (HTTP 405)" "$A_TEAM_STATE/dispatch.log")"
rm -f "$BUMPED.fails"
dispatch_dev --dry-run
grep -q 'demo: settling dependency bumps working again' "$A_TEAM_STATE/dispatch.log" || fail "again: '$(cat "$A_TEAM_STATE/dispatch.log")'"
: >"$FREES"

case_ "every pass frees the tasks whose wait on another repo is met, as the team's App, and logs what it freed"
dispatch_dev --dry-run
same "frees" "bumps
board --dry-run demo free as demo $DISPATCH/bin" "$(cat "$FREES")"
: >"$FREES"
: >"$A_TEAM_STATE/dispatch.log"
printf '(dry run) #13: no longer waiting on mentaldesk/style#84\n' >"$FREED"
dispatch_dev --dry-run
grep -q 'demo: (dry run) #13: no longer waiting on mentaldesk/style#84$' "$A_TEAM_STATE/dispatch.log" ||
  fail "freed: '$(cat "$A_TEAM_STATE/dispatch.log")'"
: >"$FREED"

case_ "every pass brings the team's PRs behind their base up to date, as the team's App, and logs what it merged"
same "updates" "board --dry-run demo update as demo $DISPATCH/bin" "$(tail -n 1 "$UPDATES_RUN")"
: >"$UPDATES_RUN"
: >"$A_TEAM_STATE/dispatch.log"
printf '(dry run) PR #912: merged main into it\n' >"$UPDATED_SAID"
dispatch_dev --dry-run
grep -q 'demo: (dry run) PR #912: merged main into it$' "$A_TEAM_STATE/dispatch.log" ||
  fail "updated: '$(cat "$A_TEAM_STATE/dispatch.log")'"
: >"$UPDATED_SAID"

case_ "every pass moves the team's finished tasks, as the team's App, leaving the ones a Dev run has or a stakeholder stopped"
same "plain" "board --dry-run demo finished as demo $DISPATCH/bin" "$(tail -n 1 "$DISPATCH/finished")"
mkdir -p "$A_TEAM_STATE/demo/dev/runs/15" "$A_TEAM_STATE/demo/dev/runs/16"
sleep 30 &
live=$!
echo "$live" >"$A_TEAM_STATE/demo/dev/runs/15/pid"
echo '{"number": 15}' >"$A_TEAM_STATE/demo/dev/runs/15/task"
touch "$A_TEAM_STATE/demo/dev/runs/16/held"
dispatch_dev --dry-run
same "leaves" "board --dry-run demo finished 15 16 as demo $DISPATCH/bin" "$(tail -n 1 "$DISPATCH/finished")"
kill "$live" 2>/dev/null
wait "$live" 2>/dev/null
rm -rf "$A_TEAM_STATE/demo/dev/runs/15" "$A_TEAM_STATE/demo/dev/runs/16"
: >"$DISPATCH/finished"
jq '.dispatch.hold = ["dev"]' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
dispatch_dev --dry-run
same "dev held" "" "$(cat "$DISPATCH/finished")"
jq '.dispatch.hold = []' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"

case_ "the same reasons again wait, and the next task with something new gets the run"
jq '.tasks += [{number: 14, title: "Answer the review", reasons: ["stakeholder feedback on #914 (2025-09-19T09:00:00Z)"]}]' \
  "$DEV_TRIGGERS" >"$DEV_TRIGGERS.new" && mv "$DEV_TRIGGERS.new" "$DEV_TRIGGERS"
: >"$A_TEAM_STATE/dispatch.log"
dispatch_dev --dry-run
grep -q 'demo dev: would start: #14: stakeholder feedback on #914' "$A_TEAM_STATE/dispatch.log" ||
  fail "next task: '$(cat "$A_TEAM_STATE/dispatch.log")'"
: >"$A_TEAM_STATE/dispatch.log"
dispatch_dev --dry-run
grep -q 'claim dev' "$CLAIMS" || fail "with both waiting, the Ready task wasn't claimed"

case_ "the same reasons run again once the board has moved format, since that's what stopped the last run"
: >"$A_TEAM_STATE/dispatch.log"
dispatch_dev --dry-run
grep -q 'demo dev: would start' "$A_TEAM_STATE/dispatch.log" && fail "before the move: '$(cat "$A_TEAM_STATE/dispatch.log")'"
history_db "INSERT INTO formats (team, format, version, moved) VALUES ('demo', 2, '0.4.0', '$(date -u +%FT%TZ)');"
dispatch_dev --dry-run
grep -q 'demo dev: would start: #12' "$A_TEAM_STATE/dispatch.log" || fail "after the move: '$(cat "$A_TEAM_STATE/dispatch.log")'"

case_ "with nothing in hand, a Ready task is claimed before the run starts"
dev_dispatcher
jq -n '{reasons: ["Ready task available (e.g. #13) and a free worktree"], creative: false, tasks: [], ready: 13, chores: []}' \
  >"$DEV_TRIGGERS"
echo '{"number": 13, "title": "Something to start"}' >"$CLAIMED"
dispatch_dev --dry-run
same "claims" "board --dry-run demo claim dev" "$(cat "$CLAIMS")"
grep -q 'demo dev: would start: #13: Ready task #13 to build' "$A_TEAM_STATE/dispatch.log" ||
  fail "claimed: '$(cat "$A_TEAM_STATE/dispatch.log")'"
same "why" "Ready task #13 to build" "$(cat "$A_TEAM_STATE/demo/dev/dry-last-reasons")"

case_ "a claim that's refused starts nothing, and says why"
sed -i.bak 's|^  \*" claim dev "\*).*|  *" claim dev "*) echo "board.sh: no free worktree for #13" >\&2; exit 1 ;;|' "$DISPATCH/bin/a-team.body"
: >"$A_TEAM_STATE/dispatch.log"
dispatch_dev --dry-run
grep -q 'demo dev: claim failed: board.sh: no free worktree for #13' "$A_TEAM_STATE/dispatch.log" ||
  fail "refused: '$(cat "$A_TEAM_STATE/dispatch.log")'"
grep -q 'would start' "$A_TEAM_STATE/dispatch.log" && fail "refused: a run started"
sed -i.bak 's|^  \*" claim dev "\*).*|  *" claim dev "*) echo "board.sh: the board read came back incomplete (100 of 358 items), twice. Nothing was changed: end this run, and the next one reads the board again." >\&2; exit 1 ;;|' "$DISPATCH/bin/a-team.body"
: >"$A_TEAM_STATE/dispatch.log"
dispatch_dev --dry-run
same "short claim" "demo dev: board read incomplete (100 of 358 items), twice; trying again next check" \
  "$(cut -d' ' -f2- "$A_TEAM_STATE/dispatch.log")"

case_ "a Reviewer run is for one waiting PR at a time, named in the log, and claims nothing"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "app": { "id": 7, "slug": "demo-app" }, "dispatch": { "enabled": true },
  "roles": { "reviewer": true } }
JSON
dev_dispatcher
jq -n '{reasons: [], creative: false, tasks: [], ready: null, chores: []}' >"$DEV_TRIGGERS"
jq -n '{reasons: ["PR #912 is green and waiting for its review", "PR #914 is green and waiting for its review"],
        creative: false, ready: 13, chores: [],
        tasks: [{number: 12, title: "Fix the pane", reasons: ["PR #912 is green and waiting for its review"]},
                {number: 14, title: "Name the column", reasons: ["PR #914 is green and waiting for its review"]}]}' \
  >"$REVIEWER_TRIGGERS"
dispatch_dev --dry-run
grep -q 'demo reviewer: would start: #12: PR #912 is green and waiting for its review$' "$A_TEAM_STATE/dispatch.log" ||
  fail "reviewer: '$(cat "$A_TEAM_STATE/dispatch.log")'"
same "one run" 1 "$(grep -c 'demo reviewer: would start' "$A_TEAM_STATE/dispatch.log")"
same "task" '{"number":12,"title":"Fix the pane"}' "$(cat "$A_TEAM_STATE/demo/reviewer/dry-task")"
same "claims" "" "$(cat "$CLAIMS")"
: >"$A_TEAM_STATE/dispatch.log"
dispatch_dev --dry-run
grep -q 'demo reviewer: would start: #14: PR #914 is green and waiting for its review$' "$A_TEAM_STATE/dispatch.log" ||
  fail "next pass: '$(cat "$A_TEAM_STATE/dispatch.log")'"
same "next task" '{"number":14,"title":"Name the column"}' "$(cat "$A_TEAM_STATE/demo/reviewer/dry-task")"
: >"$A_TEAM_STATE/dispatch.log"
dispatch_dev --dry-run
same "both tried" "" "$(cat "$A_TEAM_STATE/dispatch.log")"
same "claims" "" "$(cat "$CLAIMS")"

case_ "a task stopped for the Reviewer is passed over, and the next one gets its run"
dev_dispatcher
jq -n '{reasons: [], creative: false, tasks: [], ready: null, chores: []}' >"$DEV_TRIGGERS"
jq -n '{reasons: ["PR #912 is green and waiting for its review", "PR #914 is green and waiting for its review"],
        creative: false, ready: null, chores: [],
        tasks: [{number: 12, title: "Fix the pane", reasons: ["PR #912 is green and waiting for its review"]},
                {number: 14, title: "Name the column", reasons: ["PR #914 is green and waiting for its review"]}]}' \
  >"$REVIEWER_TRIGGERS"
A_TEAM_CONFIG="$CONFIG" bash "$DISPATCH/scripts/pause.sh" stop demo reviewer 12 >"$OUT"
[ -e "$A_TEAM_STATE/demo/reviewer/runs/12/held" ] || fail "stop: nothing held under reviewer/runs/12"
dispatch_dev --dry-run
grep -q 'demo reviewer: would start: #14: ' "$A_TEAM_STATE/dispatch.log" ||
  fail "held: '$(cat "$A_TEAM_STATE/dispatch.log")'"
grep -q '#12' "$A_TEAM_STATE/dispatch.log" && fail "held: #12 was started"
same "claims" "" "$(cat "$CLAIMS")"

case_ "a trigger check that fails once and then passes says nothing, and the role sits that pass out"
dev_dispatcher
jq -n '{reasons: [], creative: false, tasks: [], ready: null, chores: []}' >"$DEV_TRIGGERS"
cp "$CONFIG/teams/demo.json" "$CONFIG/teams/other.json"
FAILS="$DISPATCH/fails"
FAILING="  *\" demo triggers dev\"*) [ -s $FAILS ] && { cat $FAILS >&2; exit 1; }; cat $DEV_TRIGGERS ;;"
FAILING=$FAILING perl -i -pe 'print "$ENV{FAILING}\n" if /^  \*" triggers dev"\*\)/' "$DISPATCH/bin/a-team.body"
echo "gh: connection reset" >"$FAILS"
echo '{"number": 13, "title": "Something to start"}' >"$CLAIMED"
jq '.ready = 13 | .reasons = ["Ready task available (e.g. #13) and a free worktree"]' "$DEV_TRIGGERS" >"$DISPATCH/ready.json"
cp "$DISPATCH/ready.json" "$DEV_TRIGGERS"
dispatch_dev --dry-run
grep -q 'demo dev' "$A_TEAM_STATE/dispatch.log" && fail "blip: '$(cat "$A_TEAM_STATE/dispatch.log")'"
grep -q 'other dev: would start' "$A_TEAM_STATE/dispatch.log" || fail "blip: other wasn't started"
same "claims" "board --dry-run other claim dev" "$(cat "$CLAIMS")"
: >"$FAILS"
: >"$A_TEAM_STATE/dispatch.log"
jq '.ready = null | .reasons = []' "$DEV_TRIGGERS" >"$DEV_TRIGGERS.new" && mv "$DEV_TRIGGERS.new" "$DEV_TRIGGERS"
dispatch_dev --dry-run
same "after a blip" "" "$(cat "$A_TEAM_STATE/dispatch.log")"
echo "gh: connection reset" >"$FAILS"
dispatch_dev --dry-run
: >"$FAILS"
dispatch_dev --dry-run
same "two failures not in a row" "" "$(cat "$A_TEAM_STATE/dispatch.log")"

case_ "two failed trigger checks in a row say so once, again when the reason changes, and once working again"
echo "gh: connection reset" >"$FAILS"
dispatch_dev --dry-run
dispatch_dev --dry-run
dispatch_dev --dry-run
same "failed once" "demo dev: triggers failed: gh: connection reset" "$(cut -d' ' -f2- "$A_TEAM_STATE/dispatch.log")"
echo "gh: API rate limit exceeded" >"$FAILS"
dispatch_dev --dry-run
dispatch_dev --dry-run
same "new reason" 1 "$(grep -c 'demo dev: triggers failed: gh: API rate limit exceeded$' "$A_TEAM_STATE/dispatch.log")"
grep -q 'other \|demo lead\|demo customer' "$A_TEAM_STATE/dispatch.log" && fail "another role: '$(cat "$A_TEAM_STATE/dispatch.log")'"
: >"$FAILS"
dispatch_dev --dry-run
dispatch_dev --dry-run
same "working again" 1 "$(grep -c 'demo dev: triggers working again$' "$A_TEAM_STATE/dispatch.log")"
same "lines" 3 "$(grep -c . "$A_TEAM_STATE/dispatch.log")"

case_ "a board read short twice is logged at once in one line, and the role tries again on its next check"
: >"$A_TEAM_STATE/dispatch.log"
echo "board.sh: the board read came back incomplete (100 of 358 items), twice. Nothing was changed: end this run, and the next one reads the board again." >"$FAILS"
dispatch_dev --dry-run
same "logged" "demo dev: board read incomplete (100 of 358 items), twice; trying again next check" \
  "$(grep 'demo dev' "$A_TEAM_STATE/dispatch.log" | cut -d' ' -f2-)"
: >"$FAILS"
: >"$A_TEAM_STATE/dispatch.log"
cp "$DISPATCH/ready.json" "$DEV_TRIGGERS"
dispatch_dev --dry-run
grep -q 'demo dev: would start: #13' "$A_TEAM_STATE/dispatch.log" || fail "next check: '$(cat "$A_TEAM_STATE/dispatch.log")'"
rm "$CONFIG/teams/other.json"

case_ "merged worktrees alone start no Dev run"
dev_dispatcher
jq -n '{reasons: ["PR #900 has merged: clean up its worktree"], creative: false, tasks: [], ready: null,
        chores: ["PR #900 has merged: clean up its worktree"]}' >"$DEV_TRIGGERS"
dispatch_dev --dry-run
grep -q 'demo dev' "$A_TEAM_STATE/dispatch.log" 2>/dev/null && fail "chores: '$(cat "$A_TEAM_STATE/dispatch.log")'"

case_ "the run's task reaches its prompt, its environment and the state the dashboard reads"
WORKDIR=$(mktemp -d "$WORK/workdir.XXXXXX")
jq --arg w "$WORKDIR" '.workdir = $w' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
jq -n '{reasons: [], creative: false, tasks: [], ready: 13, chores: []}' >"$DEV_TRIGGERS"
echo '{"number": 13, "title": "Something to start"}' >"$CLAIMED"
LAUNCHED="$DISPATCH/launched"
fake "$DISPATCH/bin/claude" <<SH
#!/usr/bin/env bash
{ echo "task: \$A_TEAM_RUN_TASK"; printf '%s\n' "\$2"; } >"$LAUNCHED"
SH
dispatch_dev
for _ in $(seq 50); do [ -s "$LAUNCHED" ] && break; sleep 0.1; done
same "claims" "board demo claim dev" "$(cat "$CLAIMS")"
same "env" "task: 13" "$(sed -n 1p "$LAUNCHED")"
grep -q '^This run is for #13 Something to start, and only that task.$' "$LAUNCHED" ||
  fail "prompt: '$(cat "$LAUNCHED")'"
same "state" '{"number":13,"title":"Something to start"}' "$(cat "$A_TEAM_STATE/demo/dev/task")"

case_ "a workdir that isn't there starts nothing, and says so rather than recording a start"
STATE_DIR="$A_TEAM_STATE/demo/dev"
rm -rf "$STATE_DIR"
: >"$A_TEAM_STATE/dispatch.log"
: >"$LAUNCHED"
jq --arg w "$WORK/nope" '.workdir = $w' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
echo '{"number": 14, "title": "Something else"}' >"$CLAIMED"
dispatch_dev
grep -qF "demo dev: cannot start: workdir $WORK/nope: no such directory" "$A_TEAM_STATE/dispatch.log" ||
  fail "cannot start: '$(cat "$A_TEAM_STATE/dispatch.log")'"
grep -q 'demo dev: started' "$A_TEAM_STATE/dispatch.log" && fail "logged a start: '$(cat "$A_TEAM_STATE/dispatch.log")'"
for file in last-start last-reasons pid latest.jsonl; do
  [ -e "$STATE_DIR/$file" ] || [ -L "$STATE_DIR/$file" ] && fail "wrote $file"
done
[ -s "$LAUNCHED" ] && fail "claude was started"
jq --arg w "$WORKDIR" '.workdir = $w' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"

case_ "only a team with the Customer lead on asks it for work, and its run gets its own rules too"
dev_dispatcher
jq -n '{reasons: [], creative: false, tasks: [], ready: null, chores: []}' >"$DEV_TRIGGERS"
jq -n '{reasons: ["pitch #5 is done: check the docs cover what it shipped"], creative: false}' >"$CUSTOMER_TRIGGERS"
printf '#!/usr/bin/env bash\n' | fake "$DISPATCH/bin/claude"
dispatch_dev --dry-run
same "asked while off" "" "$(cat "$CLAIMS")"
jq '.roles.customer = true' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
dispatch_dev
same "asked" "board demo triggers customer --sweep" "$(cat "$CLAIMS")"
grep -qE 'demo customer: started [0-9]+ on 0\.1\.7: pitch #5 is done' "$A_TEAM_STATE/dispatch.log" ||
  fail "customer: '$(cat "$A_TEAM_STATE/dispatch.log")'"
[ -e "$A_TEAM_STATE/demo/customer/card" ] && fail "no card: recorded '$(cat "$A_TEAM_STATE/demo/customer/card")'"
SETTINGS="$A_TEAM_STATE/demo/customer/release/settings.json"
jq -e '.permissions.deny | index("Edit(**/*.cs)") and index("Bash(gh pr merge *)")' "$SETTINGS" >/dev/null ||
  fail "customer settings: '$(jq -c .permissions.deny "$SETTINGS")'"
jq -e '.permissions.allow | index("Bash(gh pr edit *)")' "$SETTINGS" >/dev/null ||
  fail "customer allow: '$(jq -c .permissions.allow "$SETTINGS")'"
jq -e '.permissions | (.allow | index("Bash(gh issue create *)")) and (.deny | index("Bash(gh issue create *)") | not)' \
  "$SETTINGS" >/dev/null ||
  fail "customer can't file an Idea: '$(jq -c .permissions "$SETTINGS")'"

case_ "an audit run that ends without recording the audit is retried later, not every pass"
for _ in $(seq 50); do kill -0 "$(cat "$A_TEAM_STATE/demo/customer/pid")" 2>/dev/null || break; sleep 0.1; done
jq -n '{reasons: ["weekly docs audit: check the docs as a whole"], creative: false}' >"$CUSTOMER_TRIGGERS"
: >"$A_TEAM_STATE/dispatch.log"
dispatch_dev --dry-run
dispatch_dev --dry-run
same "runs" 1 "$(grep -c 'demo customer: would start: weekly docs audit' "$A_TEAM_STATE/dispatch.log")"

case_ "a run for one card records it for Overseer, and the next run without one clears it"
jq -n '{reasons: ["stakeholder feedback on docs PR #9 (2025-09-19T09:00:00Z)"], creative: false, items: [9], card: 9}' \
  >"$CUSTOMER_TRIGGERS"
dispatch_dev --dry-run
same "card" '{"number":9}' "$(cat "$A_TEAM_STATE/demo/customer/dry-card")"
jq -n '{reasons: ["pitch #6 is done: check the docs cover what it shipped"], creative: false, items: [], card: null}' \
  >"$CUSTOMER_TRIGGERS"
dispatch_dev --dry-run
[ -e "$A_TEAM_STATE/demo/customer/dry-card" ] && fail "kept '$(cat "$A_TEAM_STATE/demo/customer/dry-card")'"
jq 'del(.roles)' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"

# Claims hand out the tasks queued in $QUEUE one at a time, then refuse for want of a worktree;
# claude stays up until killed, recording the task each run is for in $LAUNCHED.
queued_dispatcher() {
  dev_dispatcher
  QUEUE="$DISPATCH/queue" LAUNCHED="$DISPATCH/launched"
  : >"$QUEUE"
  : >"$LAUNCHED"
  fake "$DISPATCH/bin/a-team" <<SH
#!/usr/bin/env bash
case " \$* " in
  *" claim dev "*)
    echo "\$*" >>"$CLAIMS"
    n=\$(head -n 1 "$QUEUE")
    [ -n "\$n" ] || { echo "board.sh: no free worktree for #99 (3 in use, wip.worktrees is 3)" >&2; exit 1; }
    tail -n +2 "$QUEUE" >"$QUEUE.rest" && mv "$QUEUE.rest" "$QUEUE"
    echo "{\"number\": \$n, \"title\": \"Task \$n\"}" ;;
  *" triggers dev"*) cat "$DEV_TRIGGERS" ;;
  *" triggers lead"*) echo '{"reasons": [], "creative": false}' ;;
  *" task-prompt "*) echo "Run one shift." ;;
  *" version "*) cat "\$(dirname "\$0")/../VERSION" ;;
esac
SH
  fake "$DISPATCH/bin/claude" <<SH
#!/usr/bin/env bash
echo "\$A_TEAM_RUN_TASK" >>"$LAUNCHED"
exec -a "\$0" sleep 60
SH
}
launched() { for _ in $(seq 50); do [ "$(grep -c '' "$LAUNCHED")" -ge "$1" ] && break; sleep 0.1; done; }
stop_runs() { for pid in "$A_TEAM_STATE"/demo/dev/runs/*/pid; do kill "$(cat "$pid")" 2>/dev/null; done; }

case_ "with devs at 3 and three Ready tasks, one pass starts three runs, each on its own task with its own state"
queued_dispatcher
devs 3
printf '13\n14\n15\n16\n' >"$QUEUE"
jq -n '{reasons: [], creative: false, tasks: [], ready: 13, chores: ["PR #900 has merged: clean up its worktree"]}' >"$DEV_TRIGGERS"
dispatch_dev
launched 3
same "claims" 3 "$(grep -c '' "$CLAIMS")"
same "launched" "13 14 15" "$(sort "$LAUNCHED" | paste -sd ' ' -)"
for n in 13 14 15; do
  alive "$n" || fail "#$n: its run isn't going"
  same "#$n task" "{\"number\":$n,\"title\":\"Task $n\"}" "$(cat "$A_TEAM_STATE/demo/dev/runs/$n/task")"
  same "#$n log" "$n" "$(readlink "$A_TEAM_STATE/demo/dev/runs/$n/latest.jsonl" | sed 's/.*-\([0-9]*\)\.jsonl$/\1/')"
done
same "pids" 3 "$(cat "$A_TEAM_STATE"/demo/dev/runs/*/pid | sort -u | grep -c '')"
same "chores ride with the first run" "Ready task #13 to build
PR #900 has merged: clean up its worktree" "$(cat "$A_TEAM_STATE/demo/dev/runs/13/last-reasons")"
same "the others' reasons" "Ready task #14 to build" "$(cat "$A_TEAM_STATE/demo/dev/runs/14/last-reasons")"
same "the role follows the latest" '{"number":15,"title":"Task 15"}' "$(cat "$A_TEAM_STATE/demo/dev/task")"

case_ "status lists each live Dev run with its task and how long it has run"
A_TEAM_CONFIG="$CONFIG" bash "$DISPATCH/scripts/status.sh" >"$OUT"
for n in 13 14 15; do
  grep -q "^  #$n Task $n: running 0h00m (pid $(cat "$A_TEAM_STATE/demo/dev/runs/$n/pid"))$" "$OUT" ||
    fail "status: no line for #$n in '$(cat "$OUT")'"
done

case_ "with every Dev slot taken, a pass claims nothing"
dispatch_dev
same "claims" 3 "$(grep -c '' "$CLAIMS")"

case_ "a run for a task in hand starts while the others build, and never a second run for a task already going"
devs 4
jq -n '{reasons: [], creative: false, ready: null, chores: [],
        tasks: [{number: 14, title: "Task 14", reasons: ["stakeholder feedback on #914 (2025-09-19T09:00:00Z)"]},
                {number: 20, title: "Task 20", reasons: ["stakeholder feedback on #920 (2025-09-19T09:00:00Z)"]}]}' >"$DEV_TRIGGERS"
dispatch_dev
launched 4
same "launched" "13 14 15 20" "$(sort -n "$LAUNCHED" | paste -sd ' ' -)"
alive 20 || fail "#20: its run isn't going"

case_ "an over-long run is killed, and the others are left alone"
echo $(($(date +%s) - 3 * 3600)) >"$A_TEAM_STATE/demo/dev/runs/13/last-start"
dispatch_dev
for _ in $(seq 50); do alive 13 || break; sleep 0.1; done
alive 13 && fail "the over-long run is still going"
for n in 14 15 20; do alive "$n" || fail "#$n was stopped with it"; done
grep -q "demo dev: killed run .* after 180 minutes" "$A_TEAM_STATE/dispatch.log" || fail "kill: '$(cat "$A_TEAM_STATE/dispatch.log")'"
same "killed, until a pass sees it end" "dev 13 0 - killed" "$(runs_recorded | grep ' 13 ')"

case_ "a held Dev starts nothing new, and its runs go on"
jq '.dispatch.hold = ["dev"]' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
printf '30\n' >"$QUEUE"
jq -n '{reasons: [], creative: false, tasks: [], ready: 30, chores: []}' >"$DEV_TRIGGERS"
dispatch_dev
same "claims" 3 "$(grep -c '' "$CLAIMS")"
for n in 14 15 20; do alive "$n" || fail "#$n stopped when the Dev was held"; done
same "killed, once it ended, with no cost" "dev 13 1 - killed" "$(runs_recorded | grep ' 13 ')"
same "the others still running" "dev 14 0 - -
dev 15 0 - -
dev 20 0 - -" "$(runs_recorded | grep -v ' 13 ')"
stop_runs


case_ "status with no Dev run going, and with one"
A_TEAM_CONFIG="$CONFIG" bash "$DISPATCH/scripts/status.sh" >"$OUT"
grep -q "^demo dev: idle, last started" "$OUT" || fail "none: '$(cat "$OUT")'"
jq '.dispatch.hold = []' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
devs 1
dispatch_dev
launched 5
A_TEAM_CONFIG="$CONFIG" bash "$DISPATCH/scripts/status.sh" >"$OUT"
grep -q "^demo dev: running$" "$OUT" || fail "one: '$(cat "$OUT")'"
same "one run" 1 "$(grep -c '^  #30 Task 30: running 0h00m' "$OUT")"
stop_runs

case_ "a process given a finished run's pid since takes no slot, isn't killed or in status, and the run's pid file and record end"
(exec -a "$WORK/stranger" sleep 30) >/dev/null 2>&1 &
stranger=$!
echo "$stranger" >"$A_TEAM_STATE/demo/dev/runs/14/pid"
echo $(($(date +%s) - 3 * 3600)) >"$A_TEAM_STATE/demo/dev/runs/14/last-start"
: >"$DISPATCH/old.jsonl"
backdate "$A_TEAM_STATE/demo/dev/runs/14/pid" "$DISPATCH/old.jsonl"
history_db "INSERT INTO runs (team, role, pid, log, started) VALUES ('demo', 'dev', $stranger, '$DISPATCH/old.jsonl', '$(ago 60)');"
printf '31\n' >"$QUEUE"
jq -n '{reasons: [], creative: false, tasks: [], ready: 31, chores: []}' >"$DEV_TRIGGERS"
: >"$A_TEAM_STATE/dispatch.log"
dispatch_dev
launched 6
same "launched" 31 "$(tail -n 1 "$LAUNCHED")"
kill -0 "$stranger" 2>/dev/null || fail "the dispatcher killed it"
grep -q "killed run $stranger" "$A_TEAM_STATE/dispatch.log" && fail "kill: '$(cat "$A_TEAM_STATE/dispatch.log")'"
[ -e "$A_TEAM_STATE/demo/dev/runs/14/pid" ] && fail "kept the finished run's pid file"
[ -e "$A_TEAM_STATE/demo/dev/runs/31/pid" ] || fail "removed the new run's pid file"
same "its run" "dev - 1 - died" "$(runs_recorded | grep '^dev - ')"
A_TEAM_CONFIG="$CONFIG" bash "$DISPATCH/scripts/status.sh" >"$OUT"
grep -q "pid $stranger" "$OUT" && fail "status: '$(cat "$OUT")'"
kill "$stranger"
stop_runs

case_ "a dry-run dispatch records no runs"
dev_dispatcher
jq '.project = {owner: "mentaldesk", number: 1}' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
jq -n '{reasons: [], creative: false, tasks: [], ready: 13, chores: []}' >"$DEV_TRIGGERS"
echo '{"number": 13, "title": "Something to start"}' >"$CLAIMED"
dispatch_dev --dry-run
grep -q 'would start' "$A_TEAM_STATE/dispatch.log" || fail "dry run: nothing would start"
[ -e "$A_TEAM_STATE/history.db" ] && fail "dry run: recorded '$(runs_recorded)'"

case_ "a dry-run dispatch says why a vision review is due, and starts no run for it"
for reason in "vision review due: 11 of the 14 issues its themes name are closed" "vision review due: 30 days since it last changed"; do
  : >"$A_TEAM_STATE/dispatch.log"
  jq -n --arg why "$reason" '{reasons: [$why], creative: false, items: [], card: null}' >"$LEAD_TRIGGERS"
  dispatch_dev --dry-run
  grep -q "demo lead: would start: $reason\$" "$A_TEAM_STATE/dispatch.log" || fail "lead: '$(cat "$A_TEAM_STATE/dispatch.log")'"
  [ -e "$A_TEAM_STATE/demo/lead/pid" ] && fail "dry run: a lead run started"
done
echo '{"reasons": [], "creative": false}' >"$LEAD_TRIGGERS"

case_ "a run that ends with a result records how long it took, its cost and whether it erred"
# claude waits for $DISPATCH/finish, then prints $DISPATCH/result, if there is one, as its log's last line.
fake "$DISPATCH/bin/claude" <<SH
#!/usr/bin/env bash
until [ -e "$DISPATCH/finish" ]; do [ "\$SECONDS" -lt "$FAKE_LIMIT" ] || exit; sleep 0.1; done
cat "$DISPATCH/result" 2>/dev/null
SH
run_gone() { for _ in $(seq 50); do kill -0 "$(cat "$A_TEAM_STATE/demo/dev/runs/$1/pid")" 2>/dev/null || return; sleep 0.1; done; }
dispatch_dev
same "running" "dev 13 0 - -" "$(runs_recorded)"
A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board demo history 13 >"$OUT"
same "history while running" '{"who":"dev","what":"run","run":{"ended":null,"cost":null,"outcome":null}}' \
  "$(jq -c '.events[0] | del(.at)' "$OUT")"
echo '{"type":"result","subtype":"success","is_error":false,"total_cost_usd":4.12}' >"$DISPATCH/result"
touch "$DISPATCH/finish"
run_gone 13
jq -n '{reasons: [], creative: false, tasks: [], ready: null, chores: []}' >"$DEV_TRIGGERS"
dispatch_dev
same "finished" "dev 13 1 4.12 -" "$(runs_recorded)"

case_ "a retried run is a second line, and one that dies without a result has no cost"
rm "$DISPATCH/result"
jq -n '{reasons: [], creative: false, ready: null, chores: [], tasks: [{number: 13, title: "Something to start", reasons: ["CI failed on PR #913 at deadbee"]}]}' >"$DEV_TRIGGERS"
dispatch_dev
run_gone 13
jq -n '{reasons: [], creative: false, tasks: [], ready: null, chores: []}' >"$DEV_TRIGGERS"
dispatch_dev
same "two runs" "dev 13 1 4.12 -
dev 13 1 - died" "$(runs_recorded)"
A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board demo history 13 >"$OUT"
same "history" "died -" "$(jq -r '[.events[] | .run.outcome // "-"] | join(" ")' "$OUT")"

case_ "an errored run says so"
echo '{"type":"result","subtype":"error_during_execution","is_error":true,"total_cost_usd":0.5}' >"$DISPATCH/result"
jq -n '{reasons: [], creative: false, ready: null, chores: [], tasks: [{number: 14, title: "Another", reasons: ["CI failed on PR #914 at deadbee"]}]}' >"$DEV_TRIGGERS"
dispatch_dev
run_gone 14
jq -n '{reasons: [], creative: false, tasks: [], ready: null, chores: []}' >"$DEV_TRIGGERS"
dispatch_dev
same "errored" "dev 14 1 0.5 error" "$(runs_recorded | grep ' 14 ')"

case_ "a Lead run is recorded on each item it was started for, and a discovery run on none"
sed -i.bak "s|^  \*\" triggers lead\"\*).*|  *\" triggers lead\"*) cat \"$DISPATCH/lead.json\" ;;|" "$DISPATCH/bin/a-team.body"
jq -n '{reasons: ["stakeholder feedback on #7 (2026-10-07T09:00:00Z)", "#9 was approved: break it down"], creative: false, items: [7, 9]}' \
  >"$DISPATCH/lead.json"
history_db "DELETE FROM runs; DELETE FROM run_items;"
rm "$DISPATCH/result"
dispatch_dev
for _ in $(seq 50); do kill -0 "$(cat "$A_TEAM_STATE/demo/lead/pid")" 2>/dev/null || break; sleep 0.1; done
jq -n '{reasons: [], creative: true, items: []}' >"$DISPATCH/lead.json"
jq '.dispatch.creativeEvery = 0' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
dispatch_dev
same "lead runs" "lead 7 1 - died
lead 9 1 - died
lead - 0 - -" "$(runs_recorded | sed -n '1,3p')"
for _ in $(seq 50); do kill -0 "$(cat "$A_TEAM_STATE/demo/lead/pid")" 2>/dev/null || break; sleep 0.1; done
jq 'del(.dispatch.creativeEvery)' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"

case_ "today's cost is the team's runs that ended since local midnight, and a running or costless run counts nothing"
CONFIG_WAS=$CONFIG TEAM_WAS=$TEAM
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "app": { "id": 7, "slug": "demo-app" }, "dispatch": { "enabled": true },
  "project": { "owner": "mentaldesk", "number": 1 } }
JSON
dev_dispatcher
jq -n '{reasons: [], creative: false, tasks: [], ready: 13, chores: []}' >"$DEV_TRIGGERS"
echo '{"number": 13, "title": "Something to start"}' >"$CLAIMED"
midnight=$(sqlite3 :memory: "SELECT strftime('%s', 'now', 'localtime', 'start of day', 'utc')")
history_db "INSERT INTO runs (team, role, pid, log, started, ended, cost) VALUES
  ('demo', 'dev', 1, 'a', '$(at -7200)', '$(at -1)', 5),
  ('demo', 'dev', 2, 'b', '$(at -60)', '$(at 0)', 2.996),
  ('demo', 'lead', 3, 'c', '$(at 1)', '$(at 2)', 1.004),
  ('demo', 'dev', 4, 'd', '$(at 3)', '$(at 4)', NULL),
  ('demo', 'dev', 5, 'e', '$(at 5)', NULL, NULL),
  ('other', 'dev', 6, 'f', '$(at 5)', '$(at 6)', 100);"
same "no budget" '{"cost":4,"budget":0,"reached":false}' "$(today)"

case_ "with no budget, or under it, the dispatcher starts runs as before"
dispatch_dev --dry-run
grep -q 'demo dev: would start: #13' "$A_TEAM_STATE/dispatch.log" || fail "no budget: '$(cat "$A_TEAM_STATE/dispatch.log")'"
jq '.dispatch.budget = 5' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
same "under" '{"cost":4,"budget":5,"reached":false}' "$(today)"
: >"$A_TEAM_STATE/dispatch.log"
rm -rf "$A_TEAM_STATE/demo/dev/dry-tried"
dispatch_dev --dry-run
grep -q 'demo dev: would start: #13' "$A_TEAM_STATE/dispatch.log" || fail "under: '$(cat "$A_TEAM_STATE/dispatch.log")'"

case_ "at or over its budget, a team starts nothing, and the log says so once a day"
for budget in 4 3; do
  jq --argjson budget "$budget" '.dispatch.budget = $budget' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
  same "reached at $budget" true "$(today | jq .reached)"
  rm -f "$A_TEAM_STATE/demo/dry-budget-said"
  : >"$A_TEAM_STATE/dispatch.log"
  rm -rf "$A_TEAM_STATE/demo/dev/dry-tried" "$A_TEAM_STATE/demo/lead/dry-fingerprint"
  dispatch_dev --dry-run
  dispatch_dev --dry-run
  grep -q 'would start:' "$A_TEAM_STATE/dispatch.log" && fail "at $budget: '$(cat "$A_TEAM_STATE/dispatch.log")'"
  same "said once at $budget" "demo: daily budget \$$budget reached (\$4.00 today); would start no new runs until midnight" \
    "$(cut -d' ' -f2- "$A_TEAM_STATE/dispatch.log")"
done

case_ "a budget reached yesterday says so again today"
echo 2000-01-01 >"$A_TEAM_STATE/demo/dry-budget-said"
: >"$A_TEAM_STATE/dispatch.log"
dispatch_dev --dry-run
same "said again" 1 "$(grep -c 'daily budget' "$A_TEAM_STATE/dispatch.log")"
same "today's date" "$(date +%F)" "$(cat "$A_TEAM_STATE/demo/dry-budget-said")"

case_ "a budget that isn't a whole number holds nothing back, and the log says so once"
jq '.dispatch.budget = 3.5' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
same "reads as none" '{"cost":4,"budget":0,"reached":false}' "$(today)"
: >"$A_TEAM_STATE/dispatch.log"
for _ in 1 2 3; do
  rm -rf "$A_TEAM_STATE/demo/dev/dry-tried"
  dispatch_dev --dry-run
done
same "starts runs" 3 "$(grep -c 'demo dev: would start: #13' "$A_TEAM_STATE/dispatch.log")"
same "said once" "demo: daily budget ignored: \$3.5 isn't a whole number of dollars" \
  "$(grep 'budget' "$A_TEAM_STATE/dispatch.log" | cut -d' ' -f2-)"
jq '.dispatch.budget = 5' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
: >"$A_TEAM_STATE/dispatch.log"
dispatch_dev --dry-run
same "in use again" "demo: daily budget in use again" "$(grep 'budget' "$A_TEAM_STATE/dispatch.log" | cut -d' ' -f2-)"

case_ "a history the budget can't be read from holds nothing back, and the log says so once"
mv "$A_TEAM_STATE/history.db" "$A_TEAM_STATE/history.db.was"
echo "not a database" >"$A_TEAM_STATE/history.db"
: >"$A_TEAM_STATE/dispatch.log"
for _ in 1 2 3; do
  rm -rf "$A_TEAM_STATE/demo/dev/dry-tried"
  dispatch_dev --dry-run
done
same "starts runs" 3 "$(grep -c 'demo dev: would start: #13' "$A_TEAM_STATE/dispatch.log")"
same "said once" 1 "$(grep -c "demo: daily budget ignored: couldn't read today's cost: " "$A_TEAM_STATE/dispatch.log")"
mv "$A_TEAM_STATE/history.db.was" "$A_TEAM_STATE/history.db"
CONFIG=$CONFIG_WAS TEAM=$TEAM_WAS

case_ "devs stops at the worktrees limit quietly, and a missing devs reads as 1"
queued_dispatcher
devs 5
printf '13\n14\n' >"$QUEUE"
jq -n '{reasons: [], creative: false, tasks: [], ready: 13, chores: []}' >"$DEV_TRIGGERS"
dispatch_dev --dry-run
same "would start" 2 "$(grep -c 'demo dev: would start' "$A_TEAM_STATE/dispatch.log")"
grep -q 'claim failed' "$A_TEAM_STATE/dispatch.log" && fail "limit: '$(cat "$A_TEAM_STATE/dispatch.log")'"
jq 'del(.wip)' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
printf '13\n14\n' >"$QUEUE"
: >"$A_TEAM_STATE/dispatch.log"
dispatch_dev --dry-run
same "without devs" 1 "$(grep -c 'demo dev: would start' "$A_TEAM_STATE/dispatch.log")"

case_ "stop ends every Dev run going"
queued_dispatcher
devs 2
printf '13\n14\n' >"$QUEUE"
jq -n '{reasons: [], creative: false, tasks: [], ready: 13, chores: []}' >"$DEV_TRIGGERS"
dispatch_dev
launched 2
PIDS=$(cat "$A_TEAM_STATE"/demo/dev/runs/*/pid | sort -n | paste -sd ' ' -)
A_TEAM_CONFIG="$CONFIG" bash "$DISPATCH/scripts/pause.sh" stop demo dev >"$OUT" 2>&1
for _ in $(seq 50); do alive 13 || alive 14 || break; sleep 0.1; done
alive 13 && fail "stop: #13 is still going"
alive 14 && fail "stop: #14 is still going"
grep -q "stopped demo dev's run (${PIDS// /, })" "$OUT" || fail "stop: '$(cat "$OUT")'"

case_ "stop <team> dev <task> ends only that task's run, and holds that task but not the role"
jq '.dispatch.hold = []' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
queued_dispatcher
devs 2
printf '13\n14\n' >"$QUEUE"
jq -n '{reasons: [], creative: false, tasks: [], ready: 13, chores: []}' >"$DEV_TRIGGERS"
dispatch_dev
launched 2
A_TEAM_CONFIG="$CONFIG" bash "$DISPATCH/scripts/pause.sh" stop demo dev 13 >"$OUT" 2>&1
for _ in $(seq 50); do alive 13 || break; sleep 0.1; done
alive 13 && fail "stop by task: #13 is still going"
alive 14 || fail "stop by task: #14 was stopped too"
[ -f "$A_TEAM_STATE/demo/dev/runs/13/held" ] || fail "stop by task: #13 isn't held"
same "hold" '[]' "$(jq -c .dispatch.hold "$TEAM")"
grep -q "stopped demo dev's run on #13" "$OUT" || fail "stop by task: '$(cat "$OUT")'"

case_ "a pass starts no run on a task stopped by task, and still starts a new one"
printf '15\n' >"$QUEUE"
: >"$LAUNCHED"
jq -n '{reasons: [], creative: false, tasks: [{number: 13, title: "Task 13", reasons: ["CI failed on PR #913"]}], ready: 15,
        chores: []}' >"$DEV_TRIGGERS"
dispatch_dev
launched 1
same "launched" "15" "$(cat "$LAUNCHED")"

case_ "resume <team> dev <task> lets that task start again"
A_TEAM_CONFIG="$CONFIG" bash "$DISPATCH/scripts/pause.sh" resume demo dev 13 >"$OUT" 2>&1
[ -f "$A_TEAM_STATE/demo/dev/runs/13/held" ] && fail "resume by task: #13 is still held"
devs 3
: >"$LAUNCHED"
dispatch_dev
launched 1
same "launched" "13" "$(cat "$LAUNCHED")"
stop_runs

case_ "stop by task needs a Dev task number"
A_TEAM_CONFIG="$CONFIG" bash "$DISPATCH/scripts/pause.sh" stop demo lead 13 >"$OUT" 2>&1 && fail "lead task: accepted"
A_TEAM_CONFIG="$CONFIG" bash "$DISPATCH/scripts/pause.sh" stop demo dev x >"$OUT" 2>&1 && fail "bad task: accepted"

# claude records where its a-team is, its settings, and its version before and after $GO appears.
pinned_claude() {
  SEEN=$(mktemp -d "$WORK/seen.XXXXXX") GO="$WORK/go.$RANDOM"
  fake "$DISPATCH/bin/claude" <<SH
#!/usr/bin/env bash
seen="$SEEN/\${A_TEAM_RUN_TASK:-lead}"
while [ \$# -gt 0 ]; do [ "\$1" = --settings ] && echo "\$2" >"\$seen.settings"; shift; done
command -v a-team >"\$seen.bin"
a-team version >"\$seen.before"
until [ -e "$GO" ]; do [ "\$SECONDS" -lt "$FAKE_LIMIT" ] || exit; sleep 0.1; done
a-team version >"\$seen.after"
echo "\${A_TEAM_RUN_TASK:-lead}" >>"$LAUNCHED"
exec -a "\$0" sleep 60
SH
}

case_ "a fake whose suite has gone stops waiting at its limit"
FAKE_LIMIT=1 pinned_claude
PATH="$DISPATCH/bin:$PATH" bash "$DISPATCH/bin/claude" >/dev/null 2>&1 &
waiting=$!
for _ in $(seq 50); do kill -0 "$waiting" 2>/dev/null || break; sleep 0.1; done
kill -0 "$waiting" 2>/dev/null && fail "still waiting"
kill -9 "$waiting" 2>/dev/null
wait "$waiting" 2>/dev/null
[ -e "$SEEN/lead.before" ] || fail "never started"
[ -e "$SEEN/lead.after" ] && fail "carried on as if told to"

case_ "each Dev run keeps the release it started on, even once an upgrade removes it"
jq '.dispatch.hold = []' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
queued_dispatcher
pinned_claude
devs 2
jq -n '{reasons: [], creative: false, tasks: [], ready: 13, chores: []}' >"$DEV_TRIGGERS"
printf '13\n' >"$QUEUE"
dispatch_dev
seen 13.before >/dev/null
echo 0.1.8 >"$DISPATCH/VERSION"
printf '14\n' >"$QUEUE"
dispatch_dev
RUNS="$A_TEAM_STATE/demo/dev/runs"
same "#13 a-team" "$RUNS/13/release/bin/a-team" "$(seen 13.bin)"
same "#14 a-team" "$RUNS/14/release/bin/a-team" "$(seen 14.bin)"
same "#13 copy" 0.1.7 "$(cat "$RUNS/13/release/VERSION")"
same "#14 copy" 0.1.8 "$(cat "$RUNS/14/release/VERSION")"
same "#13 settings" "$RUNS/13/release/settings.json" "$(seen 13.settings)"
grep -qF "\"Edit(/$RUNS/13/release/**)\"" "$RUNS/13/release/settings.json" ||
  fail "settings: '$(grep Edit "$RUNS/13/release/settings.json")'"
grep -qF "$DISPATCH/" "$RUNS/13/release/settings.json" && fail "settings name the original release"
grep -qE "demo dev: started [0-9]+ on 0\.1\.7: #13: " "$A_TEAM_STATE/dispatch.log" || fail "log: '$(cat "$A_TEAM_STATE/dispatch.log")'"
grep -qE "demo dev: started [0-9]+ on 0\.1\.8: #14: " "$A_TEAM_STATE/dispatch.log" || fail "log: '$(cat "$A_TEAM_STATE/dispatch.log")'"
mv "$DISPATCH" "$DISPATCH.gone"
touch "$GO"
same "#13 after the upgrade" 0.1.7 "$(seen 13.after)"
same "#14 after the upgrade" 0.1.8 "$(seen 14.after)"
mv "$DISPATCH.gone" "$DISPATCH"

case_ "a finished run's copy is removed, and a live run's is left alone"
kill "$(cat "$RUNS/13/pid")"
for _ in $(seq 50); do alive 13 || break; sleep 0.1; done
dispatch_dev
[ -e "$RUNS/13/release" ] && fail "#13's copy is still there"
[ -x "$RUNS/14/release/bin/a-team" ] || fail "#14's copy was removed while it ran"
stop_runs

case_ "the Lead's run keeps its copy in the role's folder"
sed -i.bak 's|^  \*" triggers lead"\*).*|  *" triggers lead"*) echo '"'"'{"reasons": ["#232 was approved"], "creative": false}'"'"' ;;|' "$DISPATCH/bin/a-team.body"
jq -n '{reasons: [], creative: false, tasks: [], ready: null, chores: []}' >"$DEV_TRIGGERS"
dispatch_dev
same "lead a-team" "$A_TEAM_STATE/demo/lead/release/bin/a-team" "$(seen lead.bin)"
same "lead settings" "$A_TEAM_STATE/demo/lead/release/settings.json" "$(seen lead.settings)"
kill "$(cat "$A_TEAM_STATE/demo/lead/pid")"

case_ "a dry run copies no release"
dev_dispatcher
jq -n '{reasons: [], creative: false, tasks: [], ready: 13, chores: []}' >"$DEV_TRIGGERS"
echo '{"number": 13, "title": "Something to start"}' >"$CLAIMED"
dispatch_dev --dry-run
grep -q 'demo dev: would start' "$A_TEAM_STATE/dispatch.log" || fail "dry run: '$(cat "$A_TEAM_STATE/dispatch.log")'"
same "copies" "" "$(find "$A_TEAM_STATE" -name release)"
A_TEAM_STATE=$A_TEAM_STATE_WAS

case_ "pause --dry-run <team> <role> says it would hold the role, and doesn't"
run resume demo dev
run pause --dry-run demo dev
same "exit" 0 "$STATUS"
same "hold" '[]' "$(held)"
grep -q "would add dev to dispatch.hold" "$OUT" || fail "dry run: '$(cat "$OUT")'"

case_ "pause needs a role it knows"
run pause demo tester
failed "unknown role"
same "hold" '[]' "$(held)"

case_ "stop and the per-role resume are in the usage text"
run help
grep -q '^  stop ' "$OUT" || fail "usage: no stop line"
grep -q '^  resume \[--dry-run\] <team> \[<role>\]' "$OUT" || fail "usage: resume takes no role"
grep -q '^  pause \[--dry-run\] <team> \[<role>\]' "$OUT" || fail "usage: pause takes no role"

finish_suite
