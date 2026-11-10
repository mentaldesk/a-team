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

case_ "a task says its pitch's Priority, and whether its own is that one"
gh_items <<'ITEMS'
Building 10 A Medium pitch
Ready 13 Following it
Ready 14 Ranked itself
Ready 15 Under no pitch
ITEMS
edit_item 10 '.issueFieldValues.nodes = [{name: "Medium", field: {name: "Priority"}}]'
edit_item 13 '.parent = {number: 10}'
edit_item 14 '.parent = {number: 10} | .issueFieldValues.nodes = [{name: "High", field: {name: "Priority"}}]'
run board demo overview
same "overview" '[[10,"Medium",null,null],[13,"Medium","Medium",true],[14,"High","Medium",null],[15,null,null,null]]' \
  "$(jq -c 'map(select(.number) | [.number, .priority, .pitchPriority, .inherited])' "$OUT")"
run board demo list
same "list" '[[13,"Medium",true],[14,"Medium",null]]' \
  "$(jq -c 'map(select(.pitchPriority) | [.number, .pitchPriority, .inherited])' "$OUT")"

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

finish_suite
