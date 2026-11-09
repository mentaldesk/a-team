#!/usr/bin/env bash
# The dispatcher giving up on what its runs can't clear, and what frees it.
# shellcheck source=tests/scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
# shellcheck disable=SC2034,SC2154 # helpers read what an area sets, and an area reads what lib.sh sets

CI='CI failed on PR #912 at deadbee'
# aged <file> <minutes>: the last try recorded in <file> started that long ago.
aged() { read -r print _ tries <"$1"; echo "$print $(($(date +%s) - $2 * 60)) $tries" >"$1"; }
starts() { grep -c "$1" "$A_TEAM_STATE/dispatch.log"; }
tries_file() { echo "$A_TEAM_STATE/demo/dev/dry-tried/12"; }
lead_file() { echo "$A_TEAM_STATE/demo/lead/dry-fingerprint"; }
one_task() {
  jq -n --arg ci "${1:-$CI}" --arg head "${2:-}" '{reasons: [$ci], creative: false, tasks: [{number: 12, title: "Fix the pane", reasons: [$ci], head: (if $head == "" then null else $head end)}], ready: null, chores: []}' >"$DEV_TRIGGERS"
}
dispatcher() {
  fixture <<JSON
{ "repo": "mentaldesk/demo", "app": { "id": 7, "slug": "demo-app" }, "dispatch": { "enabled": true${1:+, $1} } }
JSON
  dev_dispatcher
  one_task
}

case_ "a stuck task gets three runs, 15 minutes and then an hour apart, and no fourth"
dispatcher
dispatch_dev --dry-run
same "first" 1 "$(starts 'would start: #12')"
dispatch_dev --dry-run
aged "$(tries_file)" 14
dispatch_dev --dry-run
same "not before 15 minutes" 1 "$(starts 'would start: #12')"
aged "$(tries_file)" 15
dispatch_dev --dry-run
same "second" 2 "$(starts 'would start: #12')"
aged "$(tries_file)" 59
dispatch_dev --dry-run
same "not before an hour" 2 "$(starts 'would start: #12')"
aged "$(tries_file)" 60
dispatch_dev --dry-run
same "third" 3 "$(starts 'would start: #12')"
same "no give up yet" "" "$(cat "$GIVE_UPS")"
aged "$(tries_file)" 600
dispatch_dev --dry-run
same "no fourth" 3 "$(starts 'would start: #12')"
same "gave up" "board --dry-run demo give-up dev 12 3 $(ls "$A_TEAM_STATE"/demo/dev/dry-give-up.* 2>/dev/null) as demo: $CI" "$(sed "s|$A_TEAM_STATE/demo/dev/dry-give-up\.[A-Za-z0-9]*|$(ls "$A_TEAM_STATE"/demo/dev/dry-give-up.* 2>/dev/null)|" "$GIVE_UPS")"
grep -q 'demo dev: gave up$' "$A_TEAM_STATE/dispatch.log" || fail "log: '$(cat "$A_TEAM_STATE/dispatch.log")'"

case_ "a dry run keeps saying what it would hold, and starts nothing"
dispatch_dev --dry-run
same "still no fourth" 3 "$(starts 'would start: #12')"
same "said again" 2 "$(grep -c give-up "$GIVE_UPS")"

case_ "new reasons start the count again"
one_task 'CI failed on PR #912 at cafe123'
dispatch_dev --dry-run
same "a run" 1 "$(starts 'would start: #12: CI failed on PR #912 at cafe123')"
same "from the first try" 1 "$(cut -d' ' -f3 "$(tries_file)")"

case_ "a new head starts the count again, with the same words and at once"
REVIEW='PR #912 has its review: act on it'
dispatcher
one_task "$REVIEW" deadbee
dispatch_dev --dry-run
aged "$(tries_file)" 15
dispatch_dev --dry-run
same "counted on one head" 2 "$(cut -d' ' -f3 "$(tries_file)")"
one_task "$REVIEW" cafe123
aged "$(tries_file)" 1
dispatch_dev --dry-run
same "a run at once" 3 "$(starts 'would start: #12')"
same "from the first try" 1 "$(cut -d' ' -f3 "$(tries_file)")"
same "the new head kept" cafe123 "$(cut -d' ' -f4 "$(tries_file)")"

case_ "a count kept before heads were recorded carries on"
dispatcher
mkdir -p "$A_TEAM_STATE/demo/dev/dry-tried"
echo "$(shasum <<<"$CI" | cut -c1-12) $(($(date +%s) - 900)) 2" >"$(tries_file)"
one_task "$CI" deadbee
dispatch_dev --dry-run
same "not started over" 2 "$(cut -d' ' -f3 "$(tries_file)")"

case_ "CI on one commit settling, or starting again, is the same reason: the wait and the count carry on"
dispatcher
dispatch_dev --dry-run
one_task "$CI, other checks still running"
aged "$(tries_file)" 14
dispatch_dev --dry-run
same "no second run yet" 1 "$(starts 'would start: #12')"
aged "$(tries_file)" 15
dispatch_dev --dry-run
same "counted" 2 "$(cut -d' ' -f3 "$(tries_file)")"
one_task
aged "$(tries_file)" 60
dispatch_dev --dry-run
same "and on" 3 "$(cut -d' ' -f3 "$(tries_file)")"

case_ "a task that's held drops out, and the Dev carries on with the others"
dispatcher
jq -n --arg ci "$CI" '{reasons: [$ci, "PR #914 is green but still a draft"], creative: false, ready: null, chores: [],
  tasks: [{number: 12, title: "Stuck", reasons: [$ci]}, {number: 14, title: "Other", reasons: ["PR #914 is green but still a draft"]}]}' >"$DEV_TRIGGERS"
mkdir -p "$A_TEAM_STATE/demo/dev/dry-tried"
echo "$(shasum <<<"$CI" | cut -c1-12) $(($(date +%s) - 36000)) 3" >"$(tries_file)"
dispatch_dev --dry-run
same "held" 1 "$(grep -c 'give-up dev 12 3' "$GIVE_UPS")"
same "the other starts" 1 "$(starts 'would start: #14')"

case_ "a giving up that fails starts nothing and is logged once"
dispatcher
mkdir -p "$A_TEAM_STATE/demo/dev/dry-tried"
echo "$(shasum <<<"$CI" | cut -c1-12) $(($(date +%s) - 36000)) 3" >"$(tries_file)"
echo "#12 is not on the board" >"$GIVE_UPS.fails"
dispatch_dev --dry-run
dispatch_dev --dry-run
dispatch_dev --dry-run
same "logged once" 1 "$(starts "couldn't give up on #12: #12 is not on the board")"
same "no run" 0 "$(starts 'would start: #12')"

case_ "maxRuns can be set"
dispatcher '"maxRuns": 1'
dispatch_dev --dry-run
aged "$(tries_file)" 1
dispatch_dev --dry-run
same "one run" 1 "$(starts 'would start: #12')"
same "gave up after it" 1 "$(grep -c 'give-up dev 12 1' "$GIVE_UPS")"

case_ "a team with a shorter retry wait never waits longer than that"
dispatcher '"retryAfter": 5'
dispatch_dev --dry-run
aged "$(tries_file)" 5
dispatch_dev --dry-run
same "second" 2 "$(starts 'would start: #12')"
aged "$(tries_file)" 5
dispatch_dev --dry-run
same "third" 3 "$(starts 'would start: #12')"

case_ "a retry wait longer than an hour doesn't lengthen the steps"
dispatcher '"retryAfter": 240'
dispatch_dev --dry-run
aged "$(tries_file)" 15
dispatch_dev --dry-run
same "second at 15 minutes" 2 "$(starts 'would start: #12')"

case_ "a count kept before there was one reads as one run"
dispatcher
mkdir -p "$A_TEAM_STATE/demo/dev/dry-tried"
echo "$(shasum <<<"$CI" | cut -c1-12) $(($(date +%s) - 900))" >"$(tries_file)"
dispatch_dev --dry-run
same "second try" 2 "$(cut -d' ' -f3 "$(tries_file)")"

case_ "the Lead's scheduled run is never counted, however many times it repeats"
dispatcher '"creativeEvery": 0'
echo '{"reasons": [], "creative": true}' >"$LEAD_TRIGGERS"
for _ in 1 2 3 4 5; do
  dispatch_dev --dry-run
  aged "$(lead_file)" 61
done
same "five runs" 5 "$(starts 'demo lead: would start: it.s been a while')"
same "none counted" 0 "$(cut -d' ' -f3 "$(lead_file)")"
same "nothing held" "" "$(cat "$GIVE_UPS")"

case_ "the Lead's scheduled run keeps its flat wait"
: >"$A_TEAM_STATE/dispatch.log"
aged "$(lead_file)" 59
dispatch_dev --dry-run
same "too soon" 0 "$(starts 'demo lead: would start')"

case_ "when the Lead gives up on reasons that name items, each is held, once"
dispatcher
ROOM='#31 was approved: break it down'
jq -n --arg r "$ROOM" '{reasons: [$r, "stakeholder feedback on #32 (x)"], creative: false, items: [31, 32, 31]}' >"$LEAD_TRIGGERS"
dispatch_dev --dry-run
aged "$(lead_file)" 15
dispatch_dev --dry-run
aged "$(lead_file)" 60
dispatch_dev --dry-run
aged "$(lead_file)" 60
dispatch_dev --dry-run
same "runs" 3 "$(starts 'demo lead: would start')"
same "items" "31
32" "$(sed -n 's/.*give-up lead \([0-9]*\) 3 .*/\1/p' "$GIVE_UPS")"
[ ! -e "$A_TEAM_STATE/demo/lead/dry-gave-up" ] || fail "a lane note where items were held"

case_ "when the Lead gives up on a reason with no item, the role says so until the reasons change"
dispatcher
jq -n '{reasons: ["room in Pitched for a drafted pitch"], creative: false, items: []}' >"$LEAD_TRIGGERS"
dispatch_dev --dry-run
aged "$(lead_file)" 15
dispatch_dev --dry-run
aged "$(lead_file)" 60
dispatch_dev --dry-run
aged "$(lead_file)" 60
dispatch_dev --dry-run
dispatch_dev --dry-run
same "runs" 3 "$(starts 'demo lead: would start')"
same "note" "3
room in Pitched for a drafted pitch" "$(cat "$A_TEAM_STATE/demo/lead/dry-gave-up")"
same "logged once" 1 "$(starts 'demo lead: gave up after 3 runs')"
same "nothing held" "" "$(cat "$GIVE_UPS")"
jq -n '{reasons: ["something else"], creative: false, items: []}' >"$LEAD_TRIGGERS"
dispatch_dev --dry-run
same "a new reason runs" 1 "$(starts 'demo lead: would start: something else')"
[ ! -e "$A_TEAM_STATE/demo/lead/dry-gave-up" ] || fail "the note outlived its reasons"

case_ "a Lead that gave up on a reason with no item still gets its scheduled runs"
dispatcher
jq -n '{reasons: ["room in Pitched for a drafted pitch"], creative: true, items: []}' >"$LEAD_TRIGGERS"
dispatch_dev --dry-run
aged "$(lead_file)" 15
dispatch_dev --dry-run
aged "$(lead_file)" 60
dispatch_dev --dry-run
aged "$(lead_file)" 60
dispatch_dev --dry-run
same "not due yet" 0 "$(starts 'demo lead: would start: it.s been a while')"
echo $(($(date +%s) - 181 * 60)) >"$A_TEAM_STATE/demo/lead/dry-last-start"
dispatch_dev --dry-run
same "due" 1 "$(starts 'demo lead: would start: it.s been a while')"
same "the stuck reason doesn't run" 3 "$(starts 'demo lead: would start: room in Pitched')"
same "still given up" 3 "$(head -n 1 "$A_TEAM_STATE/demo/lead/dry-gave-up")"
same "still counted" 3 "$(cut -d' ' -f3 "$(lead_file)")"
dispatch_dev --dry-run
same "not again until it's due" 1 "$(starts 'demo lead: would start: it.s been a while')"
same "still no fourth" 3 "$(starts 'demo lead: would start: room in Pitched')"

case_ "reasons that went away and came back start from nothing for the Lead"
dispatcher
jq -n '{reasons: ["room in Pitched for a drafted pitch"], creative: false, items: []}' >"$LEAD_TRIGGERS"
dispatch_dev --dry-run
aged "$(lead_file)" 15
dispatch_dev --dry-run
echo '{"reasons": [], "creative": false}' >"$LEAD_TRIGGERS"
dispatch_dev --dry-run
same "forgotten" 0 "$(cut -d' ' -f3 "$(lead_file)")"

# give-up and free, through the board.
heads() {
  fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 } }
JSON
  gh_items <<'ITEMS'
In_progress 12 The stuck task
Pitched 31 The stuck pitch
In_review 40 A PR on the board
ITEMS
  edit_item 12 '.labels.nodes = [{name: "a-team:dev"}]'
  edit_item 31 '.labels.nodes = [{name: "pitch"}]'
  gh_talk <<TALK
12 body ${TODAY}T07:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
31 body ${TODAY}T07:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
40 body ${TODAY}T07:00:00Z demo-app[bot] The PR\n<!-- a-team:dev -->
TALK
  printf '%s\n' "$CI" 'a reason about #99' >"$WORK/reasons"
}

case_ "--dry-run shows the hold on a task, a pitch and a PR, and changes nothing"
heads
for n in 12 31 40; do
  run board --dry-run demo give-up dev "$n" 3 "$WORK/reasons"
  same "exit" 0 "$STATUS"
  grep -q 'mark #'"$n"' held' "$ERR" || fail "no mark on #$n: '$(cat "$ERR")'"
  grep -q 'a-team:dispatch' "$ERR" || fail "no marker on #$n: '$(cat "$ERR")'"
  same "posted" "" "$(cat "$POSTED")"
done

case_ "giving up holds an In progress task and says why on it"
heads
run board demo give-up dev 12 3 "$WORK/reasons"
same "exit" 0 "$STATUS"
same "held by" '"dispatcher"' "$(marks 12 | jq -c .held.by)"
same "why" "\"gave up after 3 runs: $CI · a reason about #99\"" "$(marks 12 | jq -c .held.why)"
same "kept its status" '"In progress"' "$(marks 12 | jq -c .held.status)"
grep -q 'Gave up after 3 runs' "$POSTED" || fail "comment: '$(cat "$POSTED")'"
grep -q 'Nothing will start for it until you reply here' "$POSTED" || fail "comment: '$(cat "$POSTED")'"
same "marker last" '<!-- a-team:dispatch -->' "$(sed '/^$/d' "$POSTED" | tail -n 1)"
same "history" "held · gave up after 3 runs" "$(A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board demo history 12 | jq -r '.events[0].what')"

case_ "a role's run can't give up on an item"
A_TEAM_RUN_STARTED=${TODAY}T09:00:00Z run board demo give-up dev 12 3 "$WORK/reasons"
failed "inside a run"
grep -q 'only the dispatcher' "$ERR" || fail "refusal: '$(cat "$ERR")'"

case_ "only the reasons naming an item go on it, where any do"
heads
printf '%s\n' '#31 was approved: break it down' 'stakeholder feedback on #32 (x)' >"$WORK/both"
run board demo give-up lead 31 3 "$WORK/both"
same "why" '"gave up after 3 runs: #31 was approved: break it down"' "$(marks 31 | jq -c .held.why)"

case_ "the comment gives each run's time, its last line and what they cost"
heads
for i in 1 2 3; do
  jq -nc --arg r "Doing things
<!-- a-team:dev --> attempt $i ended" '{type: "result", result: $r}' >"$WORK/run$i.jsonl"
done
KEEP_BOARD=1
run board demo list >/dev/null
history_db "INSERT INTO runs (team, role, pid, log, started, ended, cost) VALUES
  ('demo', 'dev', 1, '$WORK/run1.jsonl', '2026-10-04T10:44:00Z', '2026-10-04T10:50:00Z', 1.25),
  ('demo', 'dev', 2, '$WORK/run2.jsonl', '2026-10-04T11:00:00Z', '2026-10-04T11:05:00Z', 1.00),
  ('demo', 'dev', 3, '$WORK/dead.jsonl', '2026-10-04T11:30:00Z', '2026-10-04T11:31:00Z', NULL),
  ('demo', 'dev', 4, '$WORK/run3.jsonl', '2026-10-04T12:02:00Z', '2026-10-04T12:10:00Z', 0.06),
  ('demo', 'dev', 5, '$WORK/stopped.jsonl', '2026-10-04T12:30:00Z', '2026-10-04T12:31:00Z', 0.50),
  ('demo', 'dev', 6, '$WORK/limit.jsonl', '2026-10-04T12:40:00Z', '2026-10-04T12:41:00Z', 0.25);
  UPDATE runs SET outcome = 'died' WHERE pid = 3;
  UPDATE runs SET outcome = 'stopped' WHERE pid = 5;
  UPDATE runs SET outcome = 'error' WHERE pid = 6;
  INSERT INTO run_items (run, item) SELECT id, 12 FROM runs WHERE team = 'demo';" >/dev/null
printf '%s\n' "$CI" >"$WORK/reasons"
run board demo give-up dev 12 3 "$WORK/reasons"
unset KEEP_BOARD
grep -q 'Why: CI failed on PR #912 at deadbee' "$POSTED" || fail "why: '$(cat "$POSTED")'"
grep -q 'Runs: 10:44 · 11:00 · 12:02 UTC (\$2.31)' "$POSTED" || fail "runs: '$(cat "$POSTED")'"
grep -q '^- 12:02: " a-team:dev --> attempt 3 ended"' "$POSTED" || fail "last line: '$(cat "$POSTED")'"
same "marker is the dispatcher's alone" 1 "$(grep -c '<!-- a-team:' "$POSTED")"

case_ "a held item waits on the stakeholder in Blocked, once, saying who held it"
heads
printf '%s\n' "$CI" >"$WORK/reasons"
run board demo give-up dev 40 3 "$WORK/reasons"
KEEP_BOARD=1
gh_talk <<TALK
40 body ${TODAY}T07:00:00Z demo-app[bot] The PR\n<!-- a-team:dev -->
40 comment $(ago 0) demo-app[bot] Gave up after 3 runs.\n\n<!-- a-team:dispatch -->
TALK
waiting
unset KEEP_BOARD
same "once" 1 "$(jq -c '[.[] | select(.number == 40)] | length' "$OUT")"
same "held" "{\"turn\":\"you\",\"heldBy\":\"dispatcher\",\"reason\":\"gave up after 3 runs: $CI\",\"question\":\"Gave up after 3 runs.\"}" \
  "$(jq -c '.[] | select(.number == 40) | {turn, heldBy, reason, question}' "$OUT")"

case_ "a held task keeps its PR, and the conversation gives the comment without its marker"
heads
printf '%s\n' "$CI" >"$WORK/reasons"
run board demo give-up dev 12 3 "$WORK/reasons"
KEEP_BOARD=1
gh_talk <<TALK
12 body ${TODAY}T07:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
12 comment $(ago 0) demo-app[bot] Gave up after 3 runs.\n\n<!-- a-team:dispatch -->
12 pr-body ${TODAY}T07:30:00Z demo-app[bot] Closes #12\n<!-- a-team:dev -->
TALK
waiting
same "PR" '{"pr":912,"branch":"feat/task-12","base":"main"}' "$(jq -c '.[] | select(.number == 12) | {pr, branch, base}' "$OUT")"
run board demo conversation 12
unset KEEP_BOARD
same "who" dispatcher "$(jq -r '.[] | select(.body | startswith("Gave up")) | .who' "$OUT")"
same "no marker" "Gave up after 3 runs." "$(jq -r '.[] | select(.body | startswith("Gave up")) | .body' "$OUT")"

case_ "a held item drops out of triggers"
heads
run board demo give-up dev 31 3 "$WORK/reasons"
KEEP_BOARD=1
sqlite3 "$A_TEAM_STATE/history.db" "UPDATE board SET status = 'Approved' WHERE team = 'demo' AND item = 31;"
run board demo triggers lead
unset KEEP_BOARD
same "exit" 0 "$STATUS"
same "no reason for it" '[]' "$(jq -c '[.reasons[] | select(contains("#31"))]' "$OUT")"
same "no item" '[]' "$(jq -c '[.items[] | select(. == 31)]' "$OUT")"

case_ "a stakeholder's reply frees it; a comment from anyone else doesn't"
heads
run board demo give-up dev 12 3 "$WORK/reasons"
KEEP_BOARD=1
HELD_AT=$(marks 12 | jq -r .held.at)
LATER=$(date -u -d "$HELD_AT + 1 minute" +%FT%TZ 2>/dev/null || date -u -j -v+1M -f %FT%TZ "$HELD_AT" +%FT%TZ)
gh_talk <<TALK
12 body ${TODAY}T07:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
12 comment ${LATER} passer-by Have you tried turning it off?
12 comment ${LATER} demo-app[bot] Still looking.\n\n<!-- a-team:dev -->
TALK
run board demo free
same "exit" 0 "$STATUS"
same "still held" '"dispatcher"' "$(marks 12 | jq -c .held.by)"
gh_talk <<TALK
12 body ${TODAY}T07:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
12 comment ${LATER} reviewer Try again now.
TALK
run board --dry-run demo free
same "dry run frees nothing" '"dispatcher"' "$(marks 12 | jq -c .held.by)"
grep -q "clear #12's held" "$ERR" || fail "dry run: '$(cat "$ERR")'"
run board demo free
unset KEEP_BOARD
same "freed" null "$(marks 12 | jq -c .held)"
grep -q '#12: freed, a stakeholder replied' "$OUT" || fail "said: '$(cat "$OUT")'"

case_ "a comment from before it was held doesn't free it"
heads
run board demo give-up dev 12 3 "$WORK/reasons"
KEEP_BOARD=1
gh_talk <<TALK
12 comment ${TODAY}T00:00:01Z reviewer An old thought.
TALK
run board demo free
unset KEEP_BOARD
same "still held" '"dispatcher"' "$(marks 12 | jq -c .held.by)"

case_ "the stakeholder's release frees it, whatever its status"
heads
run board demo give-up dev 12 3 "$WORK/reasons"
KEEP_BOARD=1
run board demo release you 12
same "exit" 0 "$STATUS"
same "freed" null "$(marks 12 | jq -c .held)"
unset KEEP_BOARD

case_ "the Dev can't clear a hold the dispatcher set"
heads
run board demo give-up dev 12 3 "$WORK/reasons"
KEEP_BOARD=1
run board demo unblock dev 12
unset KEEP_BOARD
failed "dev unblock"
grep -q 'dispatcher held it' "$ERR" || fail "refusal: '$(cat "$ERR")'"

case_ "what says who may hold a task says the dispatcher may"
grep -q 'The dispatcher may hold' "$ROOT/process.md" || fail "process.md"
grep -q 'dispatcher held' "$ROOT/roles/dev.md" || fail "dev.md"
grep -q 'the dispatcher held' "$ROOT/roles/lead.md" || fail "lead.md"

case_ "a run that died isn't counted and is retried at once; one that finished waits and counts"
dispatcher
WORKDIR=$(mktemp -d "$WORK/workdir.XXXXXX")
jq --arg w "$WORKDIR" '.workdir = $w' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
fake "$DISPATCH/bin/claude" <<SH
#!/usr/bin/env bash
cat "$DISPATCH/result" 2>/dev/null
SH
TRIED="$A_TEAM_STATE/demo/dev/tried/12"
ended() { for _ in $(seq 50); do kill -0 "$(cat "$A_TEAM_STATE/demo/dev/runs/12/pid")" 2>/dev/null || return; sleep 0.1; done; }
dispatch_dev
ended
same "first try" 1 "$(cut -d' ' -f3 "$TRIED")"
dispatch_dev
ended
same "retried" 2 "$(starts 'started .*#12')"
same "not counted" 1 "$(cut -d' ' -f3 "$TRIED")"
echo '{"type":"result","is_error":false,"total_cost_usd":0.5}' >"$DISPATCH/result"
dispatch_dev
ended
same "and another, since that one died too" 3 "$(starts 'started .*#12')"
dispatch_dev
same "this one finished, so it waits" 3 "$(starts 'started .*#12')"
same "still one try" 1 "$(cut -d' ' -f3 "$TRIED")"
aged "$TRIED" 15
dispatch_dev
ended
same "counted" 2 "$(cut -d' ' -f3 "$TRIED")"

case_ "once it's held, the count goes, so what frees it runs at once"
aged "$TRIED" 60
dispatch_dev
ended
same "third try" 3 "$(cut -d' ' -f3 "$TRIED")"
: >"$GIVE_UPS"
dispatch_dev
same "given up" 1 "$(grep -c 'give-up dev 12 3' "$GIVE_UPS")"
[ ! -e "$TRIED" ] || fail "the count outlived the hold"
started=$(starts 'started .*#12')
dispatch_dev
ended
same "the same reasons, freed, run on the next pass" $((started + 1)) "$(starts 'started .*#12')"
same "from the first try" 1 "$(cut -d' ' -f3 "$TRIED")"

case_ "a run that ended in an error isn't counted, and waits as it does today"
echo '{"type":"result","is_error":true,"total_cost_usd":0}' >"$DISPATCH/result"
: >"$GIVE_UPS"
aged "$TRIED" 15
dispatch_dev
ended
same "the run before it counted" 2 "$(cut -d' ' -f3 "$TRIED")"
started=$(starts 'started .*#12')
aged "$TRIED" 59
dispatch_dev
same "waits" "$started" "$(starts 'started .*#12')"
for _ in 1 2 3; do
  aged "$TRIED" 60
  dispatch_dev
  ended
done
same "runs again each hour" $((started + 3)) "$(starts 'started .*#12')"
same "not counted" 2 "$(cut -d' ' -f3 "$TRIED")"
same "never given up on" "" "$(cat "$GIVE_UPS")"

finish_suite
