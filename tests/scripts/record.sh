#!/usr/bin/env bash
# The record: moving over from the mirror, keeping it in step, repairs.
# shellcheck source=tests/scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
# shellcheck disable=SC2034,SC2154 # helpers read what an area sets, and an area reads what lib.sh sets

case_ "the first read moves the board over from its mirror, Status and Priority, and later reads don't read the mirror again"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 },
  "wip": { "worktrees": 3 } }
JSON
gh_items 14 <<'ITEMS'
Ready 13 Something to start
In_review 14 Waiting on you
Done 15 Merged
ITEMS
run board demo list
same "exit" 0 "$STATUS"
same "moved over" '[[13,"Ready",null],[14,"In review","High"],[15,"Done",null]]' "$(jq -c 'map([.number, .status, .priority])' "$OUT")"
: >"$BIN/args"
A_TEAM_BOARD_READ_EVERY=3600 kept board demo list
same "the record's board" '[[13,"Ready",null],[14,"In review","High"],[15,"Done",null]]' "$(jq -c 'map([.number, .status, .priority])' "$OUT")"
same "mirror reads" 0 "$(grep -c 'projectV2(number' "$BIN/args")"
same "open items read" 1 "$(grep -c BoardRefresh "$BIN/args")"

case_ "a first read that can't sync the cards' reasons fails, rather than showing them without"
gh_items <<'ITEMS'
Pitched 106 Both gates are mine
ITEMS
run board demo list
touch "$BIN/offline"
A_TEAM_BOARD_READ_EVERY=3600 KEEP_BOARD=1 waiting
same "offline exit" 1 "$STATUS"
grep -q "can't read the cards' reasons from GitHub" "$ERR" || fail "offline: '$(cat "$ERR")'"
rm "$BIN/offline"
run board demo list
echo 10 >"$BIN/remaining"
A_TEAM_BOARD_READ_EVERY=3600 KEEP_BOARD=1 waiting
same "short of budget exit" 1 "$STATUS"
grep -q "too few GitHub API requests" "$ERR" || fail "short of budget: '$(cat "$ERR")'"
rm "$BIN/remaining"

case_ "after a sync, waiting reads every card and its reason from the record, with no call to GitHub"
gh_items <<'ITEMS'
Pitched 106 Both gates are mine
In_review 115 I can change any of the keys
ITEMS
gh_talk <<TALK
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 pr-body ${TODAY}T08:10:00Z demo-app[bot] Closes #115\n<!-- a-team:dev -->
TALK
run board demo sync
same "exit" 0 "$STATUS"
same "one call for the detail" 1 "$(grep -c defaultBranchRef "$BIN/args")"
same "main's CI in it" 0 "$(grep -c 'commits/HEAD/check-runs\|default_branch' "$BIN/args")"
: >"$CALLS"
KEEP_BOARD=1 waiting
same "exit" 0 "$STATUS"
same "api calls" 0 "$(grep -c '' <"$CALLS")"
cards='[[106,"you","awaiting your approval since 08:00"],[115,"you","awaiting your acceptance since 08:10"]]'
same "cards" "$cards" "$(jq -c 'map([.number, .turn, .reason])' "$OUT")"
same "synced" '[true,null]' "$(jq -c '[.synced != null, .failed]' "$OUT.synced")"
touch "$BIN/offline"
KEEP_BOARD=1 waiting
same "offline exit" 0 "$STATUS"
same "offline cards" "$cards" "$(jq -c 'map([.number, .turn, .reason])' "$OUT")"

case_ "a sync that can't reach GitHub keeps the cards as they were, and says when one last worked"
synced=$(jq -c .synced "$OUT.synced")
kept board demo sync
same "exit" 0 "$STATUS"
KEEP_BOARD=1 waiting
same "cards" "$cards" "$(jq -c 'map([.number, .turn, .reason])' "$OUT")"
same "last good sync" "$synced" "$(jq -c .synced "$OUT.synced")"
same "failed" true "$(jq '.failed != null' "$OUT.synced")"
rm "$BIN/offline"

case_ "a sync short of API budget leaves the detail as it was"
echo 10 >"$BIN/remaining"
gh_talk <<TALK
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
106 comment ${TODAY}T09:00:00Z reviewer Why now?
TALK
: >"$BIN/args"
kept board demo sync
same "exit" 0 "$STATUS"
same "no detail read" 0 "$(grep -c defaultBranchRef "$BIN/args")"
KEEP_BOARD=1 waiting
same "cards" "$cards" "$(jq -c 'map([.number, .turn, .reason])' "$OUT")"
same "stamp" "$synced" "$(jq -c .synced "$OUT.synced")"
rm "$BIN/remaining"

case_ "once synced, a failed sync stops saying so"
kept board demo sync
KEEP_BOARD=1 waiting
same "failed" null "$(jq -c .failed "$OUT.synced")"

case_ "a new comment shows on its card after the next sync, and not before"
gh_talk <<TALK
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 pr-body ${TODAY}T08:10:00Z demo-app[bot] Closes #115\n<!-- a-team:dev -->
115 pr-comment ${TODAY}T09:00:00Z reviewer Can it say which key?
TALK
kept board demo sync
gh_talk <<TALK
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 pr-body ${TODAY}T08:10:00Z demo-app[bot] Closes #115\n<!-- a-team:dev -->
115 pr-comment ${TODAY}T09:00:00Z reviewer Can it say which key?
115 pr-review ${TODAY}T09:30:00Z reviewer And which team?
TALK
KEEP_BOARD=1 waiting
same "comment" '[115,"dev","answering your feedback since 09:00"]' "$(jq -c '.[1] | [.number, .turn, .reason]' "$OUT")"

case_ "so does a new review"
kept board demo sync
KEEP_BOARD=1 waiting
same "review" '[115,"dev","answering your feedback since 09:30"]' "$(jq -c '.[1] | [.number, .turn, .reason]' "$OUT")"

case_ "and a check going red, then green"
gh_talk <<TALK
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 pr-body ${TODAY}T08:10:00Z demo-app[bot] Closes #115\n<!-- a-team:dev -->
TALK
gh_runs <<RUNS
completed failure ${TODAY}T10:00:00Z build
RUNS
kept board demo sync
KEEP_BOARD=1 waiting
same "red" '[115,"dev","CI failing since 10:00"]' "$(jq -c '.[1] | [.number, .turn, .reason]' "$OUT")"
gh_runs <<RUNS
completed success ${TODAY}T10:30:00Z build
RUNS
kept board demo sync
KEEP_BOARD=1 waiting
same "green" '[115,"you","awaiting your acceptance since 08:10"]' "$(jq -c '.[1] | [.number, .turn, .reason]' "$OUT")"

case_ "the red main shows from the record, and goes after a sync once it's green"
gh_main_runs <<RUNS
completed failure ${TODAY}T10:00:00Z build
RUNS
kept board demo sync
KEEP_BOARD=1 waiting
same "red" '{"branch":"main","sha":"b8f5bdd0c0ffee","failing":["build"],"since":"b8f5bdd0c0ffee","fix":null}' "$(jq -c '.[-1].red' "$OUT")"
gh_main_runs <<RUNS
completed success ${TODAY}T10:30:00Z build
RUNS
KEEP_BOARD=1 waiting
same "still red until a sync" true "$(jq '.[-1] | has("red")' "$OUT")"
kept board demo sync
KEEP_BOARD=1 waiting
same "green" false "$(jq 'any(.[]; has("red"))' "$OUT")"

case_ "a card that reached its gate since the last sync has its reason straight away, without another call once it has"
gh_items <<'ITEMS'
Pitched 106 Both gates are mine
Ready 115 I can change any of the keys
ITEMS
sync_then_reach_gate() {
  gh_talk <<TALK
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
TALK
  run board demo sync
  sqlite3 "$A_TEAM_STATE/history.db" "UPDATE board SET status = 'In review' WHERE team = 'demo' AND item = 115;"
  gh_talk <<TALK
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 pr-body ${TODAY}T08:10:00Z demo-app[bot] Closes #115\n<!-- a-team:dev -->
115 pr-comment ${TODAY}T09:00:00Z reviewer Can it say which key?
TALK
}
sync_then_reach_gate
: >"$BIN/args"
KEEP_BOARD=1 waiting
same "exit" 0 "$STATUS"
same "reason" '[115,"dev","answering your feedback since 09:00"]' "$(jq -c '.[1] | [.number, .turn, .reason]' "$OUT")"
same "synced for it" 1 "$(grep -c defaultBranchRef "$BIN/args")"
: >"$BIN/args"
KEEP_BOARD=1 waiting
same "the next read" 0 "$(grep -c '' <"$BIN/args")"

case_ "a gap the sync can't fill keeps the cards, and offline only tries the once"
sync_then_reach_gate
touch "$BIN/offline"
KEEP_BOARD=1 waiting
same "exit" 0 "$STATUS"
same "cards" '[106,115]' "$(jq -c 'map(.number)' "$OUT")"
same "failed" true "$(jq '.failed != null' "$OUT.synced")"
: >"$BIN/args"
KEEP_BOARD=1 waiting
same "exit" 0 "$STATUS"
same "doesn't try again" 0 "$(grep -c '' <"$BIN/args")"
rm "$BIN/offline"
sync_then_reach_gate
echo 10 >"$BIN/remaining"
: >"$BIN/args"
KEEP_BOARD=1 waiting
same "short of budget exit" 0 "$STATUS"
same "short of budget, no detail read" 0 "$(grep -c defaultBranchRef "$BIN/args")"
rm "$BIN/remaining"

case_ "overview and waiting read the record as it stands, and sync reads GitHub"
gh_items <<'ITEMS'
Ready 13 Something to start
ITEMS
run board demo list
: >"$BIN/args"
kept board demo overview
KEEP_BOARD=1 waiting
same "board reads" 0 "$(grep -c 'projectV2(number\|BoardRefresh' "$BIN/args")"
kept board demo sync
same "exit" 0 "$STATUS"
same "synced" 1 "$(grep -c 'projectV2(number' "$BIN/args")"

case_ "a dry run with no record yet, as in a try sandbox, moves the board over once"
rm -f "$A_TEAM_STATE/history.db" "$A_TEAM_STATE/dry-run.db"
run board --dry-run demo list
same "moved over" '[[13,"Ready"]]' "$(jq -c 'map([.number, .status])' "$OUT")"
: >"$BIN/args"
A_TEAM_BOARD_READ_EVERY=3600 kept board --dry-run demo overview
same "from its own record" '[[13,"Ready"]]' "$(jq -c 'map(select(.number) | [.number, .status])' "$OUT")"
same "mirror reads" 0 "$(grep -c 'projectV2(number' "$BIN/args")"
[ ! -e "$A_TEAM_STATE/history.db" ] || fail "dry run: recorded history"
rm -f "$A_TEAM_STATE/dry-run.db"

case_ "first reads side by side on a new record, as the dashboard makes in a try sandbox, all succeed"
for i in 1 2 3 4 5 6; do
  (OUT=$OUT.$i ERR=$ERR.$i KEEP_BOARD=1 run board --dry-run demo overview; echo "$STATUS" >"$OUT.$i.status") &
done
wait
for i in 1 2 3 4 5 6; do
  same "read $i" 0 "$(cat "$OUT.$i.status")" || cat "$ERR.$i" >&2
done
rm -f "$A_TEAM_STATE/dry-run.db"

case_ "a move is the record's, and the mirror shows it after: next, wip and claim all read the record"
gh_items <<'ITEMS'
Ready 13 Something to start
Ready 16 Something else
ITEMS
claimable
touch "$BIN/live"
run board demo claim dev
same "claimed" '{"number":13,"title":"Something to start"}' "$(cat "$OUT")"
grep -q 'item=PVTI_13 .*option=OPT_progress' "$WRITES" || fail "claim: mirror not written in '$(cat "$WRITES")'"
A_TEAM_BOARD_READ_EVERY=3600 kept board demo next
same "next" 16 "$(jq -c .number "$OUT")"
A_TEAM_BOARD_READ_EVERY=3600 kept board demo wip
same "wip" '{"In progress":1}' "$(jq -c .dev "$OUT" | jq -c 'del(.blocked)')"
A_TEAM_BOARD_READ_EVERY=3600 kept board demo claim dev 13
failed "claimed twice"

case_ "with GitHub out of quota a move still happens, the lane says how far behind the mirror is, and it catches up"
gh_items <<'ITEMS'
Ready 13 Something to start
ITEMS
claimable
touch "$BIN/mirror-fails"
run board demo claim dev
same "exit" 0 "$STATUS"
A_TEAM_BOARD_READ_EVERY=3600 kept board demo overview
same "card" '"In progress"' "$(jq -c '.[0].status' "$OUT")"
same "lag" '{"behind":1,"failed":"GitHub rate limit","retry":"'"$(date -u -r "$QUOTA_RESET" +%FT%TZ 2>/dev/null || date -u -d "@$QUOTA_RESET" +%FT%TZ)"'"}' "$(jq -c '.[] | select(.number == null) | .mirror' "$OUT")"
rm "$BIN/mirror-fails"
A_TEAM_BOARD_READ_EVERY=3600 kept board demo sync
A_TEAM_BOARD_READ_EVERY=3600 kept board demo overview
same "waits for the retry" 1 "$(jq -c '.[] | select(.number == null) | .mirror.behind' "$OUT")"
same "nothing written yet" "" "$(grep updateProjectV2ItemFieldValue "$WRITES")"
sqlite3 "$A_TEAM_STATE/history.db" "UPDATE boards SET retry = '2000-01-01T00:00:00Z';"
A_TEAM_BOARD_READ_EVERY=3600 kept board demo overview
same "not by overview" 1 "$(jq -c '.[] | select(.number == null) | .mirror.behind' "$OUT")"
A_TEAM_BOARD_READ_EVERY=3600 kept board demo sync
A_TEAM_BOARD_READ_EVERY=3600 kept board demo overview
same "caught up" '[]' "$(jq -c '[.[] | select(.number == null)]' "$OUT")"
grep -q 'item=PVTI_13 .*option=OPT_progress' "$WRITES" || fail "catch-up: mirror not written in '$(cat "$WRITES")'"

case_ "the lane says GitHub unreachable only when it was, and otherwise gives GitHub's own reason"
mirror_says() {
  gh_items <<'ITEMS'
Ready 13 Something to start
ITEMS
  claimable
  printf '%s\n' "$1" >"$BIN/project-read-fails"
  run board demo claim dev
  same "exit" 0 "$STATUS"
  A_TEAM_BOARD_READ_EVERY=3600 kept board demo overview
  same "$1" "$2" "$(jq -r '.[] | select(.number == null) | .mirror.failed' "$OUT")"
  rm "$BIN/project-read-fails"
}
mirror_says "gh: Could not resolve to a ProjectV2 with the number 1." "GitHub refused: Could not resolve to a ProjectV2 with the number 1."
mirror_says "gh: Resource not accessible by integration" "GitHub refused: Resource not accessible by integration"
mirror_says "gh: Bad credentials (HTTP 401)" "GitHub refused: Bad credentials (HTTP 401)"
mirror_says "Could not resolve host: api.github.com" "GitHub unreachable"
mirror_says "error connecting to api.github.com" "GitHub unreachable"
mirror_says 'Post "https://api.github.com/graphql": dial tcp: lookup api.github.com: no such host' "GitHub unreachable"
mirror_says "gh: Post https://api.github.com/graphql: net/http: request canceled (Client.Timeout exceeded): timeout" "GitHub unreachable"
mirror_says "gh: Bad Gateway (HTTP 502)" "GitHub unreachable"
mirror_says 'Post "https://api.github.com/graphql": EOF' "GitHub unreachable"
mirror_says "gh: Could not resolve to an Organization with the login of 'geoff-network-timeout'." \
  "GitHub refused: Could not resolve to an Organization with the login of 'geoff-network-timeout'."

case_ "a project with no Status field refuses, and says so"
gh_items <<'ITEMS'
Ready 13 Something to start
ITEMS
claimable
jq '.data.organization.projectV2.fields.nodes = []' "$META" >"$META.new" && mv "$META.new" "$META"
run board demo claim dev
same "exit" 0 "$STATUS"
A_TEAM_BOARD_READ_EVERY=3600 kept board demo overview
same "reason" "GitHub refused: no single-select field 'Status' on mentaldesk project 1" \
  "$(jq -r '.[] | select(.number == null) | .mirror.failed' "$OUT")"

case_ "the board's own reasons are refusals, whatever the owner is called"
cp "$TEAM" "$WORK/team.saved"
jq '.project.owner = "acme-network-timeout"' "$WORK/team.saved" >"$TEAM"
gh_items <<'ITEMS'
Ready 13 Something to start
ITEMS
claimable
jq '.data.organization.projectV2.fields.nodes = []' "$META" >"$META.new" && mv "$META.new" "$META"
run board demo claim dev
same "exit" 0 "$STATUS"
A_TEAM_BOARD_READ_EVERY=3600 kept board demo overview
same "reason" "GitHub refused: no single-select field 'Status' on acme-network-timeout project 1" \
  "$(jq -r '.[] | select(.number == null) | .mirror.failed' "$OUT")"
mv "$WORK/team.saved" "$TEAM"

case_ "a sync on a project with a Theme of its own says so, and makes no field of that name"
gh_items <<'ITEMS'
Ready 13 Something to start
ITEMS
claimable
run board demo claim dev
same "exit" 0 "$STATUS"
sqlite3 "$A_TEAM_STATE/history.db" "UPDATE board SET marks = json_object('theme', 'Good UX') WHERE team = 'demo' AND item = 13;"
jq '.data.organization.projectV2.fields.nodes += [{id: "PVTSSF_theme", name: "Theme", dataType: "SINGLE_SELECT", options: []}]' "$META" >"$META.new" && mv "$META.new" "$META"
: >"$WRITES"
A_TEAM_BOARD_READ_EVERY=3600 kept board demo sync
grep -q '^FIELD ' "$WRITES" && fail "made a field: '$(cat "$WRITES")'"
A_TEAM_BOARD_READ_EVERY=3600 kept board demo overview
same "reason" "GitHub refused: mentaldesk project 1 has a Theme field that isn't text, so a-team can't copy to it: rename that field" \
  "$(jq -r '.[] | select(.number == null) | .mirror.failed' "$OUT")"

case_ "a sync on a project missing a field copies, and makes the field only once it has a value for it"
gh_items <<'ITEMS'
Ready 13 Something to start
ITEMS
claimable
: >"$WRITES"
run board demo claim dev
same "exit" 0 "$STATUS"
grep -q 'item=PVTI_13 .*option=OPT_progress' "$WRITES" || fail "status not copied: '$(cat "$WRITES")'"
grep -q '^FIELD ' "$WRITES" && fail "made a field with nothing to put in it: '$(cat "$WRITES")'"
sqlite3 "$A_TEAM_STATE/history.db" "UPDATE board SET marks = json_object('rank', 'High', 'theme', 'Good UX') WHERE team = 'demo' AND item = 13;"
jq '.data.organization.projectV2.fields.nodes += [{id: "PVTF_theme", name: "Theme", dataType: "TEXT"}]' "$META" >"$META.new" && mv "$META.new" "$META"
: >"$WRITES"
A_TEAM_BOARD_READ_EVERY=3600 kept board demo sync
same "only the one missing" 1 "$(grep -c '^FIELD ' "$WRITES")"
grep -q 'FIELD .*name=Lead rank' "$WRITES" || fail "no Lead rank field: '$(cat "$WRITES")'"
grep -q 'field=PVTF_new -f text=High' "$WRITES" || fail "rank not copied: '$(cat "$WRITES")'"
grep -q 'field=PVTF_theme -f text=Good UX' "$WRITES" || fail "theme not copied: '$(cat "$WRITES")'"
A_TEAM_BOARD_READ_EVERY=3600 kept board demo overview
same "lane" '[]' "$(jq -c '[.[] | select(.number == null)]' "$OUT")"

case_ "a catch-up that copied a batch stops saying it failed, though more items are still to come"
gh_items <<ITEMS
$(for i in $(seq 100 124); do echo "Ready $i Task $i"; done)
ITEMS
claimable
A_TEAM_BOARD_READ_EVERY=3600 run board demo wip
sqlite3 "$A_TEAM_STATE/history.db" "UPDATE board SET marks = json_object('rank', 'High') WHERE team = 'demo';
  UPDATE boards SET failed = 'GitHub unreachable', retry = '2000-01-01T00:00:00Z' WHERE team = 'demo';"
A_TEAM_BOARD_READ_EVERY=3600 kept board demo sync
A_TEAM_BOARD_READ_EVERY=3600 kept board demo overview
same "lag" '{"behind":5,"failed":null,"retry":null}' "$(jq -c '.[] | select(.number == null) | .mirror' "$OUT")"

case_ "a card moved on the mirror is put back, to and from every status, and its history says a change there isn't a move"
gh_items <<'ITEMS'
Pitched 620 A pitch in front of you
Done 621 A finished task
ITEMS
every_status
touch "$BIN/live"
run board demo list
for to in Idea Exploring Approved Building Ready "In progress" "In review" Done; do
  : >"$WRITES"
  drag 620 "$to"
  kept board demo list
  same "kept, from $to" '"Pitched"' "$(jq -c '.[] | select(.number == 620) | .status' "$OUT")"
  same "line, from $to" "board $to on GitHub isn't a move: put back to Pitched" "$(history_lines 620 | tail -n 1)"
  grep -q 'item=PVTI_620 .*option=OPT_pitched' "$WRITES" || fail "$to: not written back in '$(cat "$WRITES")'"
done
same "never yours" 0 "$(history_lines 620 | grep -c '^you')"
for from in Pitched Approved "In review" Ready Done; do
  jq --arg s "$from" '(.data.organization.projectV2.items.nodes[] | select(.id == "PVTI_621")).fieldValueByName = {name: $s}' "$ITEMS" >"$ITEMS.new" && mv "$ITEMS.new" "$ITEMS"
  sqlite3 "$A_TEAM_STATE/history.db" "UPDATE board SET status = '$from', mirrored_status = '$from' WHERE team = 'demo' AND item = 621;"
  drag 621 Idea
  kept board demo list
  same "kept at $from" "\"$from\"" "$(jq -c '.[] | select(.number == 621) | .status' "$OUT")"
  same "line at $from" "board Idea on GitHub isn't a move: put back to $from" "$(history_lines 621 | tail -n 1)"
done
: >"$WRITES"
lines=$(history_lines 620 | grep -c '')
kept board demo list
same "once" "" "$(cat "$WRITES")"
same "still once" "$lines" "$(history_lines 620 | grep -c '')"

case_ "a card the mirror can't put back yet says so once, and goes back when it can"
gh_items <<'ITEMS'
Ready 622 A task
ITEMS
every_status
touch "$BIN/live" "$BIN/mirror-fails"
run board demo list
drag 622 Idea
kept board demo list
kept board demo list
same "kept" '"Ready"' "$(jq -c '.[0].status' "$OUT")"
same "once" 1 "$(history_lines 622 | grep -c 'put back to Ready')"
rm -f "$BIN/mirror-fails"
sqlite3 "$A_TEAM_STATE/history.db" "UPDATE boards SET retry = NULL WHERE team = 'demo';"
kept board demo list
grep -q 'item=PVTI_622 .*option=OPT_ready' "$WRITES" || fail "not written back in '$(cat "$WRITES")'"

case_ "a role's move isn't read back from a mirror read taken before it, and a later change there is put back"
gh_items <<'ITEMS'
Ready 13 Something to start
ITEMS
claimable
run board demo list
A_TEAM_BOARD_READ_EVERY=3600 kept board demo claim dev
A_TEAM_BOARD_READ_EVERY=3600 kept board demo list
same "kept" '"In progress"' "$(jq -c '.[0].status' "$OUT")"
same "no line" 0 "$(history_lines 13 | grep -c "isn't a move")"
drag 13 Ready
A_TEAM_BOARD_READ_EVERY=3600 kept board demo list
same "put back" '"In progress"' "$(jq -c '.[0].status' "$OUT")"
same "line" "board Ready on GitHub isn't a move: put back to In progress" "$(history_lines 13 | tail -n 1)"

case_ "auto-add's Idea on a task the board just put in Ready is put back to Ready, on the Project too"
gh_items <<'ITEMS'
Building 623 A pitch
ITEMS
claimable
every_status
touch "$BIN/live"
run board demo list
echo 'A task' >"$WORK/task.md"
echo '{"id": 77, "number": 77, "node_id": "I_77", "body": "A task\n\n<!-- a-team:lead -->"}' >"$ISSUE"
kept board demo task lead 623 "Its task" "$WORK/task.md"
same "filed" 0 "$STATUS"
add_card 77 Idea "Its task"
: >"$WRITES"
kept board demo list
same "stays Ready" '[[623,"Building"],[77,"Ready"]]' "$(statuses)"
same "line" "board Idea on GitHub isn't a move: put back to Ready" "$(history_lines 77 | tail -n 1)"
grep -q 'item=PVTI_77 .*option=OPT_ready' "$WRITES" || fail "not written back in '$(cat "$WRITES")'"
kept board demo list
same "the Project agrees" '{"name":"Ready"}' "$(jq -c '.data.organization.projectV2.items.nodes[] | select(.id == "PVTI_77").fieldValueByName' "$ITEMS")"

case_ "an issue the Project adds that the record doesn't have reaches Triage, with a Status or still waiting for one"
gh_items <<'ITEMS'
Building 624 A pitch
ITEMS
every_status
touch "$BIN/live"
run board demo list
add_card 625 Idea "Opened on GitHub"
add_card 626 null "Opened on GitHub, its Status still coming"
kept board demo list
same "with one" '"Idea"' "$(jq -c '.[] | select(.number == 625) | .status' "$OUT")"
same "without" '"None"' "$(jq -c '.[] | select(.number == 626) | .status' "$OUT")"
jq '(.data.organization.projectV2.items.nodes[] | select(.id == "PVTI_626")).fieldValueByName = {name: "Idea", updatedAt: (now + 1 | todate)}' "$ITEMS" >"$ITEMS.new" && mv "$ITEMS.new" "$ITEMS"
kept board demo list
same "once it has one" '"Idea"' "$(jq -c '.[] | select(.number == 626) | .status' "$OUT")"
same "nobody's move" 0 "$(history_lines 626 | grep -c '')"

case_ "a closed issue or merged PR is Done whatever Status it had, and a reopened one comes back to its last move"
gh_items <<'ITEMS'
In_progress 627 Closed and left In progress
In_review 628 A PR
Ready 629 A task
Idea 630 Never moved
ITEMS
claimable
every_status
touch "$BIN/live"
run board demo list
edit_item 627 '.state = "CLOSED" | .stateReason = "COMPLETED"'
kept board demo list
sqlite3 "$A_TEAM_STATE/history.db" "UPDATE board SET status = 'In progress' WHERE team = 'demo' AND item = 627;"
edit_item 628 '.__typename = "PullRequest" | .state = "MERGED" | .author = {login: "someone"}'
kept board demo list
same "closed, even a record that knew" '"Done"' "$(jq -c '.[] | select(.number == 627) | .status' "$OUT")"
same "merged" '"Done"' "$(jq -c '.[] | select(.number == 628) | .status' "$OUT")"
kept board demo claim dev 629
edit_item 629 '.state = "CLOSED" | .stateReason = "NOT_PLANNED"'
edit_item 630 '.state = "CLOSED" | .stateReason = "COMPLETED"'
kept board demo list
same "closed" '[[629,"Done"],[630,"Done"]]' "$(jq -c 'map(select(.number >= 629) | [.number, .status])' "$OUT")"
edit_item 629 '.state = "OPEN" | .stateReason = "REOPENED"'
edit_item 630 '.state = "OPEN" | .stateReason = "REOPENED"'
: >"$WRITES"
kept board demo list
same "reopened" '[[629,"In progress"],[630,"Idea"]]' "$(jq -c 'map(select(.number >= 629) | [.number, .status])' "$OUT")"
same "line" "board Done → In progress · reopened" "$(history_lines 629 | tail -n 1)"
grep -q 'item=PVTI_629 .*option=OPT_progress' "$WRITES" || fail "not written back in '$(cat "$WRITES")'"
kept board demo list
same "stays" '"In progress"' "$(jq -c '.[] | select(.number == 629) | .status' "$OUT")"
same "once" 1 "$(history_lines 629 | grep -c reopened)"

case_ "a reopened task that closed In review comes back Ready, since its PR has gone, and a reopened PR stays In review"
gh_items <<'ITEMS'
Ready 641 Accepted, then reopened
In_review 642 A PR closed unmerged
ITEMS
claimable
every_status
touch "$BIN/live"
run board demo list
kept board demo claim dev 641
kept board demo move dev 641 "In review"
sqlite3 "$A_TEAM_STATE/history.db" "INSERT INTO events (team, item, at, who, what) VALUES ('demo', 642, '2026-11-03T09:38:00Z', 'dev', 'In progress → In review');"
edit_item 641 '.state = "CLOSED" | .stateReason = "COMPLETED"'
edit_item 642 '.__typename = "PullRequest" | .state = "CLOSED" | .author = {login: "someone"}'
kept board demo list
same "closed" '[[641,"Done"],[642,"Done"]]' "$(statuses)"
edit_item 641 '.state = "OPEN" | .stateReason = "REOPENED"'
edit_item 642 '.state = "OPEN"'
kept board demo list
same "reopened" '[[641,"Ready"],[642,"In review"]]' "$(statuses)"
same "line" "board Done → Ready · reopened" "$(history_lines 641 | tail -n 1)"
kept board demo claim dev 641
kept board demo list
same "work restarts" '"In progress"' "$(jq -c '.[] | select(.number == 641) | .status' "$OUT")"

case_ "the repair puts a card back at its last recorded move, once, and leaves alone a card with none"
gh_items <<'ITEMS'
Idea 631 Set back
Pitched 632 At its last move
Exploring 633 Never moved
Ready 634 Filed, and set back
Done 635 Finished
Idea 637 Set back and since closed
Idea 639 Moved late in the UTC day
ITEMS
every_status
touch "$BIN/live"
run board demo list
edit_item 637 '.state = "CLOSED" | .stateReason = "COMPLETED"'
kept board demo list
sqlite3 "$A_TEAM_STATE/history.db" "
  INSERT INTO events (team, item, at, who, what) VALUES
    ('demo', 631, '2026-11-02T09:00:00Z', 'lead', 'Pitched → Approved'),
    ('demo', 631, '2026-11-03T09:38:00Z', 'lead', 'Approved → Building'),
    ('demo', 631, '2026-11-04T05:40:53Z', 'you', 'moved to Building on GitHub, set back: Idea → Building is the team''s move, not a stakeholder''s'),
    ('demo', 632, '2026-11-03T09:38:00Z', 'lead', 'Exploring → Pitched'),
    ('demo', 634, '2026-11-03T09:38:00Z', 'lead', 'filed as Ready under #631'),
    ('demo', 635, '2026-11-03T09:38:00Z', 'dev', 'In progress → In review'),
    ('demo', 637, '2026-11-03T09:38:00Z', 'lead', 'Approved → Building'),
    ('demo', 639, '2026-11-03T14:38:00Z', 'lead', 'Approved → Building');
  UPDATE board SET status = 'Idea' WHERE team = 'demo' AND item = 634;
  UPDATE boards SET repaired = NULL WHERE team = 'demo';"
: >"$WRITES"
TZ=NZT-13 kept board demo list
same "repaired" '[[631,"Building"],[632,"Pitched"],[633,"Exploring"],[634,"Ready"],[635,"Done"],[637,"Done"],[639,"Building"]]' "$(statuses)"
same "local day" "board Idea → Building · repaired to its last move (lead, 4 Nov)" "$(history_lines 639 | tail -n 1)"
same "line" "board Idea → Building · repaired to its last move (lead, 3 Nov)" "$(history_lines 631 | tail -n 1)"
same "filed" "board Idea → Ready · repaired to its last move (lead, 3 Nov)" "$(history_lines 634 | tail -n 1)"
same "the rest say nothing" 0 "$(for n in 632 633 635 637; do history_lines "$n"; done | grep -c repaired)"
grep -q 'item=PVTI_631 .*option=OPT_building' "$WRITES" || fail "not on the Project in '$(cat "$WRITES")'"
sqlite3 "$A_TEAM_STATE/history.db" "UPDATE board SET status = 'Idea' WHERE team = 'demo' AND item = 631;"
kept board demo list
same "a second pass changes nothing" '"Idea"' "$(jq -c '.[] | select(.number == 631) | .status' "$OUT")"
same "recorded once" 1 "$(history_lines 631 | grep -c repaired)"

case_ "a repair that can't read a card changes nothing, and the next pass finishes it, once"
gh_items <<'ITEMS'
Idea 651 Set back
Idea 652 Set back, with a move that can't be read
ITEMS
every_status
touch "$BIN/live"
run board demo list
sqlite3 "$A_TEAM_STATE/history.db" "
  INSERT INTO events (team, item, at, who, what) VALUES
    ('demo', 651, '2026-11-03T09:38:00Z', 'lead', 'Approved → Building'),
    ('demo', 652, 'sometime', 'lead', 'Approved → Building');
  UPDATE boards SET repaired = NULL WHERE team = 'demo';"
kept board demo list
same "nothing moved" '[[651,"Idea"],[652,"Idea"]]' "$(statuses)"
same "nothing said" 0 "$(history_lines 651 | grep -c repaired)"
same "still to do" 1 "$(sqlite3 "$A_TEAM_STATE/history.db" "SELECT COUNT(*) FROM boards WHERE team = 'demo' AND repaired IS NULL;")"
sqlite3 "$A_TEAM_STATE/history.db" "UPDATE events SET at = '2026-11-03T09:38:00Z' WHERE team = 'demo' AND item = 652 AND at = 'sometime';"
A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board demo list >/dev/null 2>&1 &
A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board demo list >/dev/null 2>&1 &
wait
kept board demo list
same "finished" '[[651,"Building"],[652,"Building"]]' "$(statuses)"
same "recorded once, even by two at once" 2 "$(for n in 651 652; do history_lines "$n"; done | grep -c repaired)"
same "done" 0 "$(sqlite3 "$A_TEAM_STATE/history.db" "SELECT COUNT(*) FROM boards WHERE team = 'demo' AND repaired IS NULL;")"

case_ "a board new to a-team has nothing to repair: its first read is taken as it stands"
gh_items <<'ITEMS'
Idea 638 Moved long ago
ITEMS
sqlite3 "$A_TEAM_STATE/history.db" "INSERT INTO events (team, item, at, who, what) VALUES ('demo', 638, '2026-11-03T09:38:00Z', 'lead', 'Approved → Building');"
run board demo list
same "as it stands" '[[638,"Idea"]]' "$(statuses)"

case_ "an item whose card was taken off the mirror is put back on it with its next move"
gh_items <<'ITEMS'
Ready 13 Something to start
ITEMS
claimable
run board demo list
echo '[13]' >"$BIN/off-mirror"
A_TEAM_BOARD_READ_EVERY=3600 kept board demo claim dev
grep -q '^ADD .*content=I_13' "$WRITES" || fail "not added back in '$(cat "$WRITES")'"
grep -q 'item=PVTI_new .*option=OPT_progress' "$WRITES" || fail "not moved on the new card in '$(cat "$WRITES")'"

case_ "a Priority set on the mirror is read back as the stakeholder's, and one set here is written there"
gh_items <<'ITEMS'
Idea 23 An idea
Idea 24 Another
ITEMS
run board demo list
edit_item 23 '.issueFieldValues.nodes = [{name: "Urgent", field: {name: "Priority"}}]'
kept board demo list
same "read back" '"Urgent"' "$(jq -c '.[] | select(.number == 23) | .priority' "$OUT")"
same "history" "you Priority set to Urgent" "$(history_lines 23)"
kept board demo priority you 24 Low
grep -q 'issue=I_24 .*option=OP_low' "$WRITES" || fail "priority: not mirrored in '$(cat "$WRITES")'"

finish_suite
