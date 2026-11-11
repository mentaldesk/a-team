#!/usr/bin/env bash
# What wakes a role.
# shellcheck source=tests/scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
# shellcheck disable=SC2034,SC2154 # helpers read what an area sets, and an area reads what lib.sh sets

case_ "with every worktree taken, the Dev isn't woken for a Ready task"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 },
  "wip": { "worktrees": 2 } }
JSON
gh_items <<'ITEMS'
In_review 12 Waiting on a pane nobody has built
In_review 14 Waiting on the reviewer
Ready 13 Something to start
Done 15 Merged with its blocked label left on
ITEMS
edit_item 15 '.labels.nodes = [{name: "a-team:dev"}, {name: "blocked"}]'
run board demo triggers dev
same "exit" 0 "$STATUS"
same "reasons" '[]' "$(jq -c .reasons "$OUT")"

case_ "a task blocked by another issue frees its worktree, and wip counts it apart"
edit_item 12 '.issueDependenciesSummary.blockedBy = 1'
run board demo triggers dev
same "exit" 0 "$STATUS"
same "reasons" '["Ready task available (e.g. #13) and a free worktree"]' "$(jq -c .reasons "$OUT")"
run board demo wip
same "exit" 0 "$STATUS"
same "dev" '{"Done":1,"In review":1,"blocked":1}' "$(jq -c .dev "$OUT")"

case_ "so does one the reviewer holds with the blocked label"
edit_item 12 '.issueDependenciesSummary.blockedBy = 0 | .labels.nodes += [{name: "blocked"}]'
run board demo triggers dev
same "exit" 0 "$STATUS"
same "reasons" '["Ready task available (e.g. #13) and a free worktree"]' "$(jq -c .reasons "$OUT")"

case_ "a draft PR whose CI is still running doesn't wake the Dev"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 },
  "wip": { "worktrees": 1 } }
JSON
gh_items <<'ITEMS'
In_progress 12 A task with its draft PR up
ITEMS
gh_pr 912 true
gh_runs <<'RUNS'
completed success 2025-09-19T09:00:00Z build
in_progress - - windows
RUNS
run board demo triggers dev
same "exit" 0 "$STATUS"
same "reasons" '[]' "$(jq -c .reasons "$OUT")"

case_ "a failed check wakes the Dev while the rest still run, and again once they've finished"
gh_runs <<'RUNS'
completed failure 2025-09-19T09:00:00Z build
in_progress - - windows
RUNS
run board demo triggers dev
same "exit" 0 "$STATUS"
same "reasons" '["CI failed on PR #912 at deadbee, other checks still running"]' "$(jq -c .reasons "$OUT")"
gh_runs <<'RUNS'
completed failure 2025-09-19T09:00:00Z build
completed success 2025-09-19T09:20:00Z windows
RUNS
run board demo triggers dev
same "exit" 0 "$STATUS"
same "reasons" '["CI failed on PR #912 at deadbee"]' "$(jq -c .reasons "$OUT")"

case_ "a task's PR says its head, so a new commit is new whatever the reason's words"
jq '(.. | objects | select(has("isDraft"))) += {mergeable: "CONFLICTING", headRefOid: "deadbee"}' "$PRS" >"$PRS.new" && mv "$PRS.new" "$PRS"
gh_runs <<'RUNS'
completed success 2025-09-19T09:00:00Z build
in_progress - - windows
RUNS
run board demo triggers dev
same "exit" 0 "$STATUS"
same "reasons" '["PR #912 conflicts with its base: merge the base branch into it and resolve"]' "$(jq -c .reasons "$OUT")"
same "head" '"deadbee"' "$(jq -c '.tasks[0].head' "$OUT")"
gh_pr 912 true

case_ "a draft PR that goes green wakes the Dev to mark it ready"
gh_runs <<'RUNS'
completed success 2025-09-19T09:00:00Z build
completed success 2025-09-19T09:20:00Z windows
RUNS
run board demo triggers dev
same "exit" 0 "$STATUS"
same "reasons" '["PR #912 is green but still a draft"]' "$(jq -c .reasons "$OUT")"

case_ "a PR GitHub hasn't linked to its task is still found, by what its body closes"
gh_pr 912 true 12
run board demo triggers dev
same "exit" 0 "$STATUS"
same "reasons" '["PR #912 is green but still a draft"]' "$(jq -c .reasons "$OUT")"
run board demo pr 12
same "exit" 0 "$STATUS"
same "pr" 912 "$(jq -c .number "$OUT")"
same "fields" '["headRefName","isDraft","mergeable","number","review","url"]' "$(jq -c keys "$OUT")"

case_ "a task no PR closes, linked or not, has none"
gh_pr 912 true 13
run board demo triggers dev
same "exit" 0 "$STATUS"
same "reasons" '["#12 is In progress but has no PR: an earlier run didn'"'"'t finish"]' "$(jq -c .reasons "$OUT")"
run board demo pr 12
same "exit" 0 "$STATUS"
same "pr" null "$(cat "$OUT")"

case_ "triggers groups the Dev's reasons by task, and names the Ready task a run could claim"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 },
  "wip": { "worktrees": 3 } }
JSON
gh_items <<'ITEMS'
In_progress 12 A task with its draft PR up
Ready 13 Something to start
ITEMS
gh_pr 912 true
gh_runs <<'RUNS'
completed failure 2025-09-19T09:00:00Z build
RUNS
run board demo triggers dev
same "exit" 0 "$STATUS"
same "tasks" '[{"number":12,"title":"A task with its draft PR up","reasons":["CI failed on PR #912 at deadbee"]}]' \
  "$(jq -c .tasks "$OUT")"
same "ready" 13 "$(jq -c .ready "$OUT")"
same "chores" '[]' "$(jq -c .chores "$OUT")"
run board demo triggers lead
same "lead has no tasks" null "$(jq -c .tasks "$OUT")"

case_ "triggers starts the Lead for each Dependabot PR it hasn't commented on yet"
cat >"$UPDATES" <<'JSON'
[{"number": 440, "title": "Bump MentalDesk.Tui", "comments": []},
 {"number": 441, "title": "Bump Terminal.Gui", "comments": [
   {"author": {"login": "demo-app"}, "body": "Nothing to adopt.\n\n<!-- a-team:lead -->"}]},
 {"number": 442, "title": "Bump Spectre.Console", "comments": [
   {"author": {"login": "stranger"}, "body": "<!-- a-team:lead -->"}]}]
JSON
mkdir -p "$A_TEAM_STATE/demo/lead" && touch "$A_TEAM_STATE/demo/lead/seeded"
run board demo triggers lead
same "exit" 0 "$STATUS"
same "reasons" '["Dependabot opened PR #440 (Bump MentalDesk.Tui): see what the update brings","Dependabot opened PR #442 (Bump Spectre.Console): see what the update brings"]' \
  "$(jq -c .reasons "$OUT")"
same "items" '[440,442]' "$(jq -c .items "$OUT")"
echo '[]' >"$UPDATES"


case_ "claim moves the next Ready task to In progress and labels it before a run starts"
gh_items 14 <<'ITEMS'
Ready 13 Something to start
Ready 14 Something more pressing
ITEMS
claimable
run board demo claim dev
same "exit" 0 "$STATUS"
same "claimed" '{"number":14,"title":"Something more pressing"}' "$(cat "$OUT")"
same "claimed by the Dev" true "$(marks 14 | jq -c .dev)"
grep -q 'item=PVTI_14 .*option=OPT_progress' "$WRITES" || fail "claim: no move in '$(cat "$WRITES")'"

case_ "a task with no Priority takes its pitch's, so next passes over an older task under a lower pitch"
gh_items <<'ITEMS'
Building 10 A Low pitch
Building 11 An Urgent pitch
Ready 13 Under the Low pitch
Ready 14 Under the Urgent pitch
Ready 15 Under no pitch
ITEMS
edit_item 10 '.issueFieldValues.nodes = [{name: "Low", field: {name: "Priority"}}]'
edit_item 11 '.issueFieldValues.nodes = [{name: "Urgent", field: {name: "Priority"}}]'
edit_item 13 '.parent = {number: 10}'
edit_item 14 '.parent = {number: 11}'
claimable
run board demo next
same "next" 14 "$(jq .number "$OUT")"
run board demo list
same "priorities" '["Low","Urgent",null]' "$(jq -c 'map(select(.status == "Ready") | .priority)' "$OUT")"
same "the record keeps only what was set" "" \
  "$(sqlite3 "$A_TEAM_STATE/history.db" "SELECT priority FROM board WHERE team = 'demo' AND item = 14;")"

case_ "a task's own Priority wins over its pitch's"
edit_item 13 '.issueFieldValues.nodes = [{name: "High", field: {name: "Priority"}}]'
edit_item 11 '.issueFieldValues.nodes = [{name: "Medium", field: {name: "Priority"}}]'
run board demo next
same "next" 13 "$(jq .number "$OUT")"

case_ "an Idea or a pitch under a pitch keeps its own Priority, and a Done pitch's still reaches its open task"
gh_items <<'ITEMS'
Building 10 An Urgent pitch
Building 11 A pitch under it
Idea 12 An Idea under it
Done 20 A finished pitch
Ready 13 Under the finished pitch
ITEMS
edit_item 10 '.issueFieldValues.nodes = [{name: "Urgent", field: {name: "Priority"}}]'
edit_item 20 '.issueFieldValues.nodes = [{name: "Low", field: {name: "Priority"}}]'
edit_item 11 '.parent = {number: 10}'
edit_item 12 '.parent = {number: 10}'
edit_item 13 '.parent = {number: 20}'
run board demo overview
same "priorities" '[[10,"Urgent"],[11,null],[12,null],[13,"Low"]]' "$(jq -c 'map(select(.number) | [.number, .priority])' "$OUT")"

case_ "claim refuses a task that's already claimed, and claims nothing"
gh_items <<'ITEMS'
In_progress 12 Taken by another run
Ready 13 Something to start
ITEMS
claimable
run board demo claim dev 12
failed "already claimed"
one_line "already claimed"
same "writes" "" "$(cat "$WRITES")"

case_ "claim refuses when every worktree is taken, and says null when nothing is Ready"
gh_items <<'ITEMS'
In_progress 11 One
In_progress 12 Two
In_progress 15 Three
Ready 13 Something to start
ITEMS
claimable
run board demo claim dev
failed "no worktree"
grep -q "no free worktree for #13" "$ERR" || fail "no worktree: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"
gh_items <<'ITEMS'
In_progress 12 Two
ITEMS
run board demo claim dev
same "exit" 0 "$STATUS"
same "nothing" null "$(cat "$OUT")"

case_ "another team's draft PR In progress takes no worktree, and next never offers it"
gh_items <<'ITEMS'
In_progress 11 One
In_progress 12 Two
In_progress 86 Make MenuLetters.Pick public
Ready 13 Something to start
ITEMS
edit_item 86 '.__typename = "PullRequest" | .labels.nodes = []'
claimable
run board demo wip
same "dev" '{"In progress":2,"blocked":0}' "$(jq -c .dev "$OUT")"
run board demo next
same "next" 13 "$(jq .number "$OUT")"
run board demo claim dev
same "exit" 0 "$STATUS"
same "claimed" '{"number":13,"title":"Something to start"}' "$(cat "$OUT")"

case_ "claim --dry-run prints the claim instead of making it"
gh_items <<'ITEMS'
Ready 13 Something to start
ITEMS
claimable
run board --dry-run demo claim dev
same "exit" 0 "$STATUS"
same "would claim" '{"number":13,"title":"Something to start"}' "$(cat "$OUT")"
grep -q "would mark #13 dev true" "$ERR" || fail "dry run: no mark in '$(cat "$ERR")'"
grep -q "would set #13 to 'In progress'" "$ERR" || fail "dry run: no move in '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"


# --- the Lead's scheduled run ---------------------------------------------------------------

# scheduled_agrees <label> <turn>: lead-next gives <turn>, and a scheduled check says there's work exactly when that isn't none.
scheduled_agrees() {
  run board demo triggers lead --scheduled
  same "$1: exit" 0 "$STATUS"
  local creative
  creative=$(jq .creative "$OUT")
  run board --dry-run demo lead-next
  same "$1: turn" "$2" "$(jq -r .turn "$OUT")"
  same "$1: creative" "$([ "$2" = none ] && echo false || echo true)" "$creative"
}

case_ "a scheduled check says the Lead has work exactly when lead-next would give it a turn"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 },
  "wip": { "pitched": 3, "exploring": 2, "ideas": 2 } }
JSON
lead_board() {
  gh_items "$@" <<'ITEMS'
Pitched 5 A pitch in front of me
Idea 7 A pitch for later
Idea 8 An Idea the Lead found
Idea 9 Another the Lead found
Idea 10 One the Lead passed over
ITEMS
  edit_item 5 '.labels.nodes = [{name: "pitch"}, {name: "theme: Consistently good UX"}]'
  edit_item 7 '.labels.nodes = [{name: "pitch"}, {name: "a-team:shelved"}, {name: "rank:low"}, {name: "theme: Consistently good UX"}]'
  edit_item 8 '.labels.nodes = [{name: "a-team:idea"}, {name: "rank:low"}]'
  edit_item 9 '.labels.nodes = [{name: "a-team:idea"}, {name: "rank:low"}]'
  edit_item 10 '.labels.nodes = [{name: "a-team:skipped"}, {name: "rank:low"}]'
  gh_thread <<THREAD
comment $(ago 60) reviewer 1 Not now.\n\n<!-- a-team:shelved until=$(weeks_on 2) priority=High -->
THREAD
}
lead_board 7
scheduled_agrees "shelved, skipped and unprioritised discoveries, queue full" none
lead_board 7 8
scheduled_agrees "a discovery given a Priority" pitch
lead_board 7
jq '.wip.ideas = 3' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
scheduled_agrees "room in the discovery queue" discover
jq '.wip.ideas = 2 | .wip.pitched = 1 | .wip.exploring = 1' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
lead_board 7 8
jq '.data.organization.projectV2.items.nodes += [.data.organization.projectV2.items.nodes[0]
  | .id = "PVTI_6" | .fieldValueByName.name = "Exploring" | .content.number = 6 | .content.id = "I_6"]' \
  "$ITEMS" >"$ITEMS.new" && mv "$ITEMS.new" "$ITEMS"
scheduled_agrees "Pitched and Exploring full, with a prioritised Idea" none
jq '.wip.pitched = 3 | .wip.exploring = 2' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"

case_ "only a scheduled check asks after each Idea: the check every pass makes no call per Idea, and says no scheduled run"
lead_board
edit_item 8 '.labels.nodes = [{name: "rank:low"}]'
: >"$BIN/args"
run board demo triggers lead
same "exit" 0 "$STATUS"
same "creative" false "$(jq .creative "$OUT")"
grep -q 'repos/mentaldesk/demo/issues/8 ' "$BIN/args" && fail "an Idea read every pass: '$(cat "$BIN/args")'"
run board demo triggers lead --scheduled
grep -q 'repos/mentaldesk/demo/issues/8 ' "$BIN/args" || fail "the scheduled check didn't ask: '$(cat "$BIN/args")'"

case_ "an Idea with no recommended rank starts the Lead, once"
history_db "DELETE FROM runs; DELETE FROM run_items;"
lead_board
edit_item 9 '.labels.nodes = [{name: "a-team:idea"}]'
since_ago() {
  jq --argjson n "$1" --arg at "$(ago "$2")" '(.data.organization.projectV2.items.nodes[] | select(.content.number == $n)
    .fieldValueByName.updatedAt) = $at' "$ITEMS" >"$ITEMS.new" && mv "$ITEMS.new" "$ITEMS"
}
since_ago 9 60
run board demo triggers lead
same "exit" 0 "$STATUS"
same "reasons" '["Idea #9 has no recommended rank: recommend one"]' "$(jq -c .reasons "$OUT")"
same "items" '[9]' "$(jq -c .items "$OUT")"
history_db "INSERT INTO runs (team, role, pid, log, started) VALUES ('demo', 'lead', 1, 'a', '$(ago 0)');
  INSERT INTO run_items (run, item) VALUES ((SELECT MAX(id) FROM runs), 9);"
run board demo triggers lead
same "after its run" '[]' "$(jq -c .reasons "$OUT")"

case_ "a Building pitch with no tasks starts the Lead once, and one with open tasks doesn't"
history_db "DELETE FROM runs; DELETE FROM run_items;"
gh_items <<'ITEMS'
Building 21 A pitch another one built
ITEMS
edit_item 21 '.labels.nodes = [{name: "pitch"}, {name: "theme: Consistently good UX"}]'
since_ago 21 60
run board demo triggers lead
same "exit" 0 "$STATUS"
same "reasons" '["#21 is Building with no tasks: check whether another pitch already built it"]' "$(jq -c .reasons "$OUT")"
same "items" '[21]' "$(jq -c .items "$OUT")"
history_db "INSERT INTO runs (team, role, pid, log, started) VALUES ('demo', 'lead', 1, 'a', '$(ago 0)');
  INSERT INTO run_items (run, item) VALUES ((SELECT MAX(id) FROM runs), 21);"
run board demo triggers lead
same "after its run" '[]' "$(jq -c .reasons "$OUT")"
history_db "DELETE FROM runs; DELETE FROM run_items;"
echo '[{"state": "open"}]' >"$SUBS"
run board demo triggers lead
same "with a task" '[]' "$(jq -c .reasons "$OUT")"

case_ "a pitch with no theme starts the Lead, and so does a register that isn't seeded"
gh_items <<'ITEMS'
Pitched 5 A pitch in front of me
ITEMS
run board demo triggers lead
same "unthemed" '["pitches with no theme (#5): give each one"]' "$(jq -c .reasons "$OUT")"
edit_item 5 '.labels.nodes = [{name: "pitch"}, {name: "theme: Consistently good UX"}]'
rm -f "$A_TEAM_STATE/demo/lead/seeded"
run board demo triggers lead
same "unseeded" "[\"the decision register isn't seeded: seed it\"]" "$(jq -c .reasons "$OUT")"
touch "$A_TEAM_STATE/demo/lead/seeded"
run board demo triggers lead
same "neither" '[]' "$(jq -c .reasons "$OUT")"

finish_suite
