#!/usr/bin/env bash
# Waiting: what reaches the stakeholder, and why.
# shellcheck source=tests/scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
# shellcheck disable=SC2034,SC2154 # helpers read what an area sets, and an area reads what lib.sh sets

case_ "waiting returns what's at a gate, and nothing else, in two calls"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 } }
JSON
gh_items <<'ITEMS'
Pitched 106 Both gates are mine
In_review 115 I can change any of the keys
Ready 128 Everything waiting on me
Done 99 Already merged
ITEMS
gh_talk <<TALK
106 body 2025-09-19T08:14:00Z reviewer The pitch\n<!-- a-team:lead -->
115 body ${TODAY}T08:14:00Z demo-app[bot] The task\n<!-- a-team:lead -->
TALK
waiting
same "exit" 0 "$STATUS"
same "numbers" '[106,115]' "$(jq -c '[.[].number]' "$OUT")"
same "statuses" '["Pitched","In review"]' "$(jq -c '[.[].status]' "$OUT")"
same "fields" '["number","pitch","priority","reason","status","team","title","turn","url"]' "$(jq -c '.[0] | keys' "$OUT")"
same "title" '"Both gates are mine"' "$(jq -c '.[0].title' "$OUT")"
same "url" '"https://github.com/mentaldesk/demo/issues/106"' "$(jq -c '.[0].url' "$OUT")"
same "team" '"demo"' "$(jq -c '.[0].team' "$OUT")"
same "api calls" 3 "$(grep -c '' <"$CALLS")"

# The read above began the record, so every read from here also catches up from GitHub: one call more.
case_ "a gate nobody has answered is the reviewer's, since the item was opened"
same "pitch turn" '"you"' "$(jq -c '.[0].turn' "$OUT")"
same "pitch reason" '"awaiting your approval since 19 Sep 08:14"' "$(jq -c '.[0].reason' "$OUT")"
same "task turn" '"you"' "$(jq -c '.[1].turn' "$OUT")"
same "task reason" '"awaiting your acceptance since 08:14"' "$(jq -c '.[1].reason' "$OUT")"

case_ "a reviewer comment since the role last spoke makes it the role's turn"
gh_talk <<TALK
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
106 comment ${TODAY}T09:30:00Z reviewer What about the second gate?
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 comment ${TODAY}T08:30:00Z demo-app[bot] Draft PR #9 is up.\n<!-- a-team:dev -->
115 comment ${TODAY}T10:15:00Z reviewer This one needs a test.
TALK
waiting
same "exit" 0 "$STATUS"
same "pitch turn" '"lead"' "$(jq -c '.[0].turn' "$OUT")"
same "pitch reason" '"answering your feedback since 09:30"' "$(jq -c '.[0].reason' "$OUT")"
same "task turn" '"dev"' "$(jq -c '.[1].turn' "$OUT")"
same "task reason" '"answering your feedback since 10:15"' "$(jq -c '.[1].reason' "$OUT")"

case_ "a comment a run has read and answered hands the gate back"
gh_talk <<TALK
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
106 comment+seen ${TODAY}T09:30:00Z reviewer What about the second gate?
106 comment ${TODAY}T11:00:00Z demo-app[bot] Redrafted.\n<!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 comment+seen ${TODAY}T10:15:00Z reviewer This one needs a test.
115 comment ${TODAY}T11:45:00Z demo-app[bot] Added one.\n<!-- a-team:dev -->
TALK
waiting
same "exit" 0 "$STATUS"
same "pitch turn" '"you"' "$(jq -c '.[0].turn' "$OUT")"
same "pitch reason" '"awaiting your approval since 11:00"' "$(jq -c '.[0].reason' "$OUT")"
same "task turn" '"you"' "$(jq -c '.[1].turn' "$OUT")"
same "task reason" '"awaiting your acceptance since 11:45"' "$(jq -c '.[1].reason' "$OUT")"

case_ "a comment from anyone but the reviewer is nobody's turn"
gh_talk <<TALK
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
106 comment ${TODAY}T09:30:00Z passer-by Have you considered doing it differently?
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 comment ${TODAY}T09:30:00Z passer-by This looks wrong to me.
TALK
waiting
same "exit" 0 "$STATUS"
same "turns" '["you","you"]' "$(jq -c '[.[].turn]' "$OUT")"

case_ "the reviewer answering on the task's PR rather than on the task is still the Dev's turn"
gh_talk <<TALK
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 comment ${TODAY}T08:30:00Z demo-app[bot] Draft PR #9 is up.\n<!-- a-team:dev -->
115 pr-body ${TODAY}T08:25:00Z demo-app[bot] Closes #115\n<!-- a-team:dev -->
115 pr-comment ${TODAY}T10:15:00Z reviewer This one needs a test.
TALK
: >"$CALLS"
waiting
same "exit" 0 "$STATUS"
same "task turn" '"dev"' "$(jq -c '.[1].turn' "$OUT")"
same "task reason" '"answering your feedback since 10:15"' "$(jq -c '.[1].reason' "$OUT")"
same "api calls" 4 "$(grep -c '' <"$CALLS")"

case_ "so it is when GitHub hasn't linked that PR to the task, and only its body closes it"
gh_talk false MERGEABLE unlinked <<TALK
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 pr-body ${TODAY}T08:25:00Z demo-app[bot] Closes #115\n<!-- a-team:dev -->
115 pr-comment ${TODAY}T10:15:00Z reviewer This one needs a test.
TALK
: >"$CALLS"
waiting
same "exit" 0 "$STATUS"
same "task turn" '"dev"' "$(jq -c '.[1].turn' "$OUT")"
same "task reason" '"answering your feedback since 10:15"' "$(jq -c '.[1].reason' "$OUT")"
same "api calls" 5 "$(grep -c '' <"$CALLS")"

case_ "but a PR the Dev didn't open isn't taken for the task's on its word"
gh_talk false MERGEABLE unlinked <<TALK
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 pr-body ${TODAY}T08:25:00Z passer-by Closes #115
115 pr-comment ${TODAY}T10:15:00Z reviewer This one needs a test.
TALK
waiting
same "exit" 0 "$STATUS"
same "task turn" '"you"' "$(jq -c '.[1].turn' "$OUT")"

case_ "a review and a line comment on that PR count as feedback too"
gh_talk <<TALK
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 pr-body ${TODAY}T08:25:00Z demo-app[bot] Closes #115\n<!-- a-team:dev -->
115 pr-review ${TODAY}T09:00:00Z reviewer Nearly there.
115 pr-line ${TODAY}T10:45:00Z reviewer This name reads oddly.
TALK
waiting
same "exit" 0 "$STATUS"
same "task turn" '"dev"' "$(jq -c '.[1].turn' "$OUT")"
same "task reason" '"answering your feedback since 10:45"' "$(jq -c '.[1].reason' "$OUT")"

case_ "the Dev answering on the PR hands the task back"
gh_talk <<TALK
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 pr-body ${TODAY}T08:25:00Z demo-app[bot] Closes #115\n<!-- a-team:dev -->
115 pr-comment+seen ${TODAY}T10:15:00Z reviewer This one needs a test.
115 pr-comment ${TODAY}T11:45:00Z demo-app[bot] Added one.\n<!-- a-team:dev -->
TALK
waiting
same "exit" 0 "$STATUS"
same "task turn" '"you"' "$(jq -c '.[1].turn' "$OUT")"
same "task reason" '"awaiting your acceptance since 11:45"' "$(jq -c '.[1].reason' "$OUT")"

case_ "an In review item carries its open PR, and a Pitched one carries none"
gh_talk <<TALK
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 pr-body ${TODAY}T08:25:00Z demo-app[bot] Closes #115\n<!-- a-team:dev -->
TALK
waiting
same "exit" 0 "$STATUS"
same "pitch fields" '["number","pitch","priority","reason","status","team","title","turn","url"]' "$(jq -c '.[0] | keys' "$OUT")"
same "task fields" \
  '["base","branch","checks","conflicting","draft","number","pitch","pr","prUrl","priority","reason","status","team","title","turn","unready","url"]' \
  "$(jq -c '.[1] | keys' "$OUT")"
same "pitch flags" '[true,false]' "$(jq -c '[.[].pitch]' "$OUT")"
same "pr" 1015 "$(jq -c '.[1].pr' "$OUT")"
same "prUrl" '"https://github.com/mentaldesk/demo/pull/1015"' "$(jq -c '.[1].prUrl' "$OUT")"
same "branch" '"feat/task-115"' "$(jq -c '.[1].branch' "$OUT")"

case_ "a green, mergeable, ready PR with nothing unanswered stays the reviewer's"
same "checks" '"pass"' "$(jq -c '.[1].checks' "$OUT")"
same "conflicting" false "$(jq -c '.[1].conflicting' "$OUT")"
same "draft" false "$(jq -c '.[1].draft' "$OUT")"
same "base" '"main"' "$(jq -c '.[1].base' "$OUT")"
same "unready" '""' "$(jq -c '.[1].unready' "$OUT")"
same "task turn" '"you"' "$(jq -c '.[1].turn' "$OUT")"
same "task reason" '"awaiting your acceptance since 08:25"' "$(jq -c '.[1].reason' "$OUT")"

case_ "a PR whose CI is failing is the Dev's turn, since the run finished"
gh_runs <<RUNS
completed failure ${TODAY}T09:02:00Z build
RUNS
waiting
same "exit" 0 "$STATUS"
same "checks" '"fail"' "$(jq -c '.[1].checks' "$OUT")"
same "task turn" '"dev"' "$(jq -c '.[1].turn' "$OUT")"
same "task trouble" '"CI failing"' "$(jq -c '.[1].trouble' "$OUT")"
same "task reason" '"CI failing since 09:02"' "$(jq -c '.[1].reason' "$OUT")"

case_ "a PR failing only the jobs main fails reads main is red, and the red main rides along with no number"
gh_main_runs <<RUNS
completed failure ${TODAY}T08:50:00Z build
completed success ${TODAY}T08:50:00Z lint
RUNS
waiting
same "exit" 0 "$STATUS"
same "checks" '"main-red"' "$(jq -c '.[1].checks' "$OUT")"
same "task turn" '"dev"' "$(jq -c '.[1].turn' "$OUT")"
same "task trouble" '"main is red"' "$(jq -c '.[1].trouble' "$OUT")"
same "red" '{"team":"demo","red":{"branch":"main","sha":"b8f5bdd0c0ffee","failing":["build"],"since":"b8f5bdd0c0ffee","fix":null}}' \
  "$(jq -c 'last' "$OUT")"

case_ "the red main names its fix, and the fix's card says it's one"
edit_item 115 '.labels.nodes += [{name: "a-team:fix"}]'
waiting
same "exit" 0 "$STATUS"
same "fix" '{"number":115,"status":"In review"}' "$(jq -c 'last.red.fix' "$OUT")"
same "fix flag" '[null,true]' "$(jq -c '[.[0:2][].fix]' "$OUT")"
same "fix checks" '"fail"' "$(jq -c '.[1].checks' "$OUT")"
same "fix trouble" '"CI failing"' "$(jq -c '.[1].trouble' "$OUT")"
edit_item 115 '.labels.nodes -= [{name: "a-team:fix"}]'

case_ "red since stays the head it was first seen red on while later heads stay red"
jq '.check_runs |= map(.head_sha = "c0ffee1234")' "$MAIN_RUNS" >"$MAIN_RUNS.new" && mv "$MAIN_RUNS.new" "$MAIN_RUNS"
waiting
same "since" '"b8f5bdd0c0ffee"' "$(jq -c 'last.red.since' "$OUT")"
same "sha" '"c0ffee1234"' "$(jq -c 'last.red.sha' "$OUT")"

case_ "a PR failing a job main passes is its own failure"
gh_runs <<RUNS
completed failure ${TODAY}T09:02:00Z build
completed failure ${TODAY}T09:02:00Z lint
RUNS
waiting
same "exit" 0 "$STATUS"
same "checks" '"fail"' "$(jq -c '.[1].checks' "$OUT")"
same "task trouble" '"CI failing"' "$(jq -c '.[1].trouble' "$OUT")"

case_ "main still running, or with no CI at all, isn't red"
gh_runs <<RUNS
completed failure ${TODAY}T09:02:00Z build
RUNS
gh_main_runs <<RUNS
in_progress - - build
RUNS
waiting
same "checks" '"fail"' "$(jq -c '.[1].checks' "$OUT")"
same "no red" '[]' "$(jq -c 'map(select(.red))' "$OUT")"
[ ! -e "$A_TEAM_STATE/demo/red-since" ] || fail "green main: red-since left behind"
echo '{"check_runs": []}' >"$MAIN_RUNS"
waiting
same "checks, no CI on main" '"fail"' "$(jq -c '.[1].checks' "$OUT")"
same "no red, no CI on main" '[]' "$(jq -c 'map(select(.red))' "$OUT")"

case_ "an unanswered comment outranks the failing build it hasn't been answered with"
gh_talk <<TALK
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 pr-body ${TODAY}T08:25:00Z demo-app[bot] Closes #115\n<!-- a-team:dev -->
115 pr-comment ${TODAY}T10:15:00Z reviewer This one needs a test.
TALK
waiting
same "exit" 0 "$STATUS"
same "checks" '"fail"' "$(jq -c '.[1].checks' "$OUT")"
same "task turn" '"dev"' "$(jq -c '.[1].turn' "$OUT")"
same "task trouble" null "$(jq -c '.[1].trouble' "$OUT")"
same "still unready to merge" '"CI failing"' "$(jq -c '.[1].unready' "$OUT")"
same "task reason" '"answering your feedback since 10:15"' "$(jq -c '.[1].reason' "$OUT")"

case_ "a PR that conflicts with its base is the Dev's turn"
gh_runs <<RUNS
completed success ${TODAY}T09:00:00Z build
RUNS
gh_talk false CONFLICTING <<TALK
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 pr-body ${TODAY}T08:25:00Z demo-app[bot] Closes #115\n<!-- a-team:dev -->
TALK
waiting
same "exit" 0 "$STATUS"
same "conflicting" true "$(jq -c '.[1].conflicting' "$OUT")"
same "task turn" '"dev"' "$(jq -c '.[1].turn' "$OUT")"
same "task reason" '"conflicts with main"' "$(jq -c '.[1].reason' "$OUT")"

case_ "a green PR behind its base is the Dev's turn, not yours to accept"
gh_talk <<TALK
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 pr-body ${TODAY}T08:25:00Z demo-app[bot] Closes #115\n<!-- a-team:dev -->
TALK
jq '(.. | objects | select(has("isDraft"))) += {mergeStateStatus: "BEHIND"}' "$TALK" >"$TALK.new" && mv "$TALK.new" "$TALK"
waiting
same "exit" 0 "$STATUS"
same "unready" '"behind main"' "$(jq -c '.[1].unready' "$OUT")"
same "task turn" '"dev"' "$(jq -c '.[1].turn' "$OUT")"
same "task reason" '"behind main"' "$(jq -c '.[1].reason' "$OUT")"

case_ "a PR behind its base that also conflicts or fails CI says that first"
jq '(.. | objects | select(has("isDraft"))) += {mergeable: "CONFLICTING"}' "$TALK" >"$TALK.new" && mv "$TALK.new" "$TALK"
waiting
same "conflicting" '"conflicts with main"' "$(jq -c '.[1].unready' "$OUT")"
gh_runs <<RUNS
completed failure ${TODAY}T09:00:00Z build
RUNS
waiting
same "failing" '"CI failing"' "$(jq -c '.[1].unready' "$OUT")"
gh_runs <<RUNS
completed success ${TODAY}T09:00:00Z build
RUNS

case_ "a PR GitHub hasn't worked the conflict out for yet isn't ready to accept"
gh_talk false UNKNOWN <<TALK
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 pr-body ${TODAY}T08:25:00Z demo-app[bot] Closes #115\n<!-- a-team:dev -->
TALK
waiting
same "exit" 0 "$STATUS"
same "conflicting" false "$(jq -c '.[1].conflicting' "$OUT")"
same "unready" '"resolving mergeable status"' "$(jq -c '.[1].unready' "$OUT")"
same "task turn" '"dev"' "$(jq -c '.[1].turn' "$OUT")"
same "task reason" '"resolving mergeable status"' "$(jq -c '.[1].reason' "$OUT")"

case_ "a PR still in draft is the Dev's turn"
gh_talk true <<TALK
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 pr-body ${TODAY}T08:25:00Z demo-app[bot] Closes #115\n<!-- a-team:dev -->
TALK
waiting
same "exit" 0 "$STATUS"
same "draft" true "$(jq -c '.[1].draft' "$OUT")"
same "task turn" '"dev"' "$(jq -c '.[1].turn' "$OUT")"
same "task reason" '"still a draft"' "$(jq -c '.[1].reason' "$OUT")"

case_ "with a Reviewer, a green draft is the Reviewer's turn until its review is posted"
jq '.roles.reviewer = true' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
waiting
same "exit" 0 "$STATUS"
same "task turn" '"reviewer"' "$(jq -c '.[1].turn' "$OUT")"
same "task trouble" '"awaiting review"' "$(jq -c '.[1].trouble' "$OUT")"
same "task reason" '"awaiting review"' "$(jq -c '.[1].reason' "$OUT")"
jq '(.. | objects | select(has("isDraft"))) += {mergeStateStatus: "BEHIND"}' "$TALK" >"$TALK.new" && mv "$TALK.new" "$TALK"
waiting
same "behind turn" '"reviewer"' "$(jq -c '.[1].turn' "$OUT")"
same "behind trouble" '"awaiting review"' "$(jq -c '.[1].trouble' "$OUT")"
gh_runs <<RUNS
in_progress - - build
RUNS
waiting
same "running turn" '"dev"' "$(jq -c '.[1].turn' "$OUT")"
same "running trouble" '"CI running"' "$(jq -c '.[1].trouble' "$OUT")"
gh_runs <<RUNS
completed success ${TODAY}T09:00:00Z build
RUNS
gh_talk true <<TALK
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 pr-body ${TODAY}T08:25:00Z demo-app[bot] Closes #115\n<!-- a-team:dev -->
115 pr-comment+seen ${TODAY}T09:10:00Z demo-app[bot] Nothing needs changing.\n<!-- a-team:reviewer -->
TALK
waiting
same "reviewed turn" '"dev"' "$(jq -c '.[1].turn' "$OUT")"
same "reviewed trouble" '"still a draft"' "$(jq -c '.[1].trouble' "$OUT")"
jq 'del(.roles)' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"

case_ "a ready PR whose CI is still running, after a push to answer feedback, isn't your turn yet"
gh_talk <<TALK
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 pr-body ${TODAY}T08:25:00Z demo-app[bot] Closes #115\n<!-- a-team:dev -->
115 pr-comment+seen ${TODAY}T10:15:00Z reviewer This one needs a test.
115 pr-comment ${TODAY}T10:40:00Z demo-app[bot] Added one.\n<!-- a-team:dev -->
TALK
gh_runs <<RUNS
completed success ${TODAY}T10:45:00Z build
in_progress - - windows
RUNS
waiting
same "exit" 0 "$STATUS"
same "checks" '"pending"' "$(jq -c '.[1].checks' "$OUT")"
same "task turn" '"dev"' "$(jq -c '.[1].turn' "$OUT")"
same "task trouble" '"CI running"' "$(jq -c '.[1].trouble' "$OUT")"
same "task reason" '"CI running"' "$(jq -c '.[1].reason' "$OUT")"
gh_runs <<RUNS
completed success ${TODAY}T10:45:00Z build
completed success ${TODAY}T10:55:00Z windows
RUNS
waiting
same "exit" 0 "$STATUS"
same "task turn" '"you"' "$(jq -c '.[1].turn' "$OUT")"
same "task reason" '"awaiting your acceptance since 10:40"' "$(jq -c '.[1].reason' "$OUT")"

case_ "a task with no PR yet waits on the reviewer since the task was opened"
gh_talk <<TALK
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
TALK
waiting
same "exit" 0 "$STATUS"
same "task turn" '"you"' "$(jq -c '.[1].turn' "$OUT")"
same "task reason" '"awaiting your acceptance since 08:00"' "$(jq -c '.[1].reason' "$OUT")"

case_ "a team with nothing at a gate waits on nothing, and asks nobody whose turn it is"
gh_items <<'ITEMS'
Ready 128 Everything waiting on me
ITEMS
waiting
same "exit" 0 "$STATUS"
same "items" '[]' "$(jq -c . "$OUT")"
same "api calls" 4 "$(grep -c '' <"$CALLS")"

case_ "a gated item carries the Priority the cards colour its number by"
gh_items 106 <<'ITEMS'
Pitched 106 Both gates are mine
In_review 115 I can change any of the keys
ITEMS
gh_talk <<TALK
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
TALK
waiting
same "exit" 0 "$STATUS"
same "priorities" '["High",null]' "$(jq -c '[.[].priority]' "$OUT")"

case_ "a task with no Priority of its own carries its pitch's onto its card in Work"
edit_item 115 '.parent = {number: 106}'
waiting
same "exit" 0 "$STATUS"
same "priorities" '["High","High"]' "$(jq -c '[.[].priority]' "$OUT")"

case_ "the Ideas with no Priority are waiting on the reviewer to rank them"
gh_items 26 <<'ITEMS'
Pitched 106 Both gates are mine
Idea 6 The agents can't say what they'd change
Idea 26 A pitch I've shelved
In_review 115 I can change any of the keys
Ready 128 Everything waiting on me
ITEMS
gh_talk <<TALK
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
TALK
waiting
same "exit" 0 "$STATUS"
same "numbers" '[106,115,6]' "$(jq -c '[.[].number]' "$OUT")"
same "idea status" '"Idea"' "$(jq -c '.[2].status' "$OUT")"
same "idea turn" '"you"' "$(jq -c '.[2].turn' "$OUT")"
same "idea reason" '"waiting to be ranked"' "$(jq -c '.[2].reason' "$OUT")"
same "idea fields" '["number","reason","status","team","title","turn","url"]' "$(jq -c '.[2] | keys' "$OUT")"
same "idea url" '"https://github.com/mentaldesk/demo/issues/6"' "$(jq -c '.[2].url' "$OUT")"
same "api calls" 4 "$(grep -c '' <"$CALLS")"

case_ "a team whose every Idea is ranked has none of them waiting"
gh_items 6 26 <<'ITEMS'
Idea 6 The agents can't say what they'd change
Idea 26 A pitch I've shelved
ITEMS
waiting
same "exit" 0 "$STATUS"
same "items" '[]' "$(jq -c . "$OUT")"
same "api calls" 4 "$(grep -c '' <"$CALLS")"

case_ "a pitch that needs the reviewer's answer comes with that section as its question"
gh_items <<'ITEMS'
Pitched 257 Devs or worktrees
Pitched 258 Only assumed
Pitched 259 Heading only
Pitched 260 Says none
Pitched 261 Old format
In_review 262 A finished pitch
ITEMS
edit_item 262 '.labels.nodes = [{name: "pitch"}]'
gh_talk <<TALK
257 body ${TODAY}T08:00:00Z demo-app[bot] ## Proposal\n\nIt.\n\n## Assumed\n\n1. **Squash.** Always.\n\n## Needs your answer\n\n1. **Devs or Worktrees?** I can't pick.\n\n---\n\n## Original idea\n\n> Hi\n\n<!-- a-team:lead -->
258 body ${TODAY}T08:00:00Z demo-app[bot] ## Assumed\n\n1. **Squash.** Always.\n\n<!-- a-team:lead -->
259 body ${TODAY}T08:00:00Z demo-app[bot] ## Needs your answer\n\n## Original idea\n\n> Hi\n<!-- a-team:lead -->
260 body ${TODAY}T08:00:00Z demo-app[bot] ## Needs your answer\n\nNone.\n\n<!-- a-team:lead -->
261 body ${TODAY}T08:00:00Z demo-app[bot] ## Open questions\n\n1. **Which?** I'd pick one.\n<!-- a-team:lead -->
262 body ${TODAY}T08:00:00Z demo-app[bot] ## Needs your answer\n\n1. **Which?** Still open.\n<!-- a-team:lead -->
TALK
waiting
same "exit" 0 "$STATUS"
same "question" '"## Needs your answer\n\n1. **Devs or Worktrees?** I can'"'"'t pick."' "$(jq -c '.[0].question' "$OUT")"
same "turn" '"you"' "$(jq -c '.[0].turn' "$OUT")"
same "reason" '"asked you since 08:00"' "$(jq -c '.[0].reason' "$OUT")"
same "the others" '[[258,null],[259,null],[260,null],[261,null],[262,null]]' "$(jq -c '[.[1:][] | [.number, .question]]' "$OUT")"
same "their reasons" '"awaiting your approval since 08:00"' "$(jq -c '.[1].reason' "$OUT")"

case_ "once the reviewer answers a pitch's question, it's the Lead's turn and still asks it"
gh_talk <<TALK
257 body ${TODAY}T08:00:00Z demo-app[bot] ## Needs your answer\n\n1. **Devs or Worktrees?** I can't pick.\n<!-- a-team:lead -->
257 comment ${TODAY}T10:50:00Z reviewer Devs.
TALK
waiting
same "exit" 0 "$STATUS"
same "turn" '"lead"' "$(jq -c '.[0].turn' "$OUT")"
same "reason" '"answering your feedback since 10:50"' "$(jq -c '.[0].reason' "$OUT")"
same "question" '"## Needs your answer\n\n1. **Devs or Worktrees?** I can'"'"'t pick."' "$(jq -c '.[0].question' "$OUT")"

case_ "a Needs your answer heading inside a code block isn't a question, and doesn't end one"
# #261 shows the new pitch format in a mockup; 264 adds a real section above it.
PITCH_261=$(awk 'NR > 1 { printf "\\n" } { printf "%s", $0 }' "$ROOT/tests/fixtures/pitch-261.md")
gh_items <<'ITEMS'
Pitched 263 Only a mockup
Pitched 264 A real one and a mockup
Pitched 265 A question with a code block
Pitched 266 A tilde fence
ITEMS
gh_talk <<TALK
263 body ${TODAY}T08:00:00Z demo-app[bot] $PITCH_261
264 body ${TODAY}T08:00:00Z demo-app[bot] ## Needs your answer\n\n1. **Which?** Real.\n\n$PITCH_261
265 body ${TODAY}T08:00:00Z demo-app[bot] ## Needs your answer\n\n1. **Which?** Like:\n\n   \`\`\`\n   # not a heading\n   \`\`\`\n\n2. **And?** More.\n\n## Original idea\n\n> Hi\n<!-- a-team:lead -->
266 body ${TODAY}T08:00:00Z demo-app[bot] ~~~~\n## Needs your answer\n~~~\n1. Still fenced.\n~~~~\n<!-- a-team:lead -->
TALK
waiting
same "exit" 0 "$STATUS"
same "questions" '[[263,null],[264,"## Needs your answer\n\n1. **Which?** Real."],[265,"## Needs your answer\n\n1. **Which?** Like:\n\n   ```\n   # not a heading\n   ```\n\n2. **And?** More."],[266,null]]' \
  "$(jq -c '[.[] | [.number, .question]]' "$OUT")"

case_ "everything at a gate of process.md's, or held for the reviewer, comes out of waiting"
statuses=$(sed -n 's/^| \([A-Z][a-z]*\( [a-z]*\)\{0,1\}\)\( ⛔\)\{0,1\} | .*/\1/p' "$ROOT/process.md" | grep -vx -e Status -e Done)
gates=$(sed -n 's/^| \([A-Za-z ]*\) ⛔ | .*/\1/p' "$ROOT/process.md" | jq -R . | jq -sc .)
same "process.md's gates" '["Pitched","In review"]' "$gates"
gh_items < <(awk '{ status = $0; gsub(/ /, "_", status); print status, 300 + NR, $0 }' <<<"$statuses")
edit_item "$((300 + $(grep -nx Ready <<<"$statuses" | cut -d: -f1)))" '.labels.nodes = [{name: "blocked"}]'
yours=$(jq -c --argjson gates "$gates" '[.data.organization.projectV2.items.nodes[]
  | select((.fieldValueByName.name | IN($gates[])) or any(.content.labels.nodes[]; .name == "blocked")) | .content.number]' "$ITEMS")
same "one at each gate, and one held" 3 "$(jq length <<<"$yours")"
waiting
same "exit" 0 "$STATUS"
same "all in waiting" '[]' "$(jq -c --argjson yours "$yours" '$yours - [.[].number]' "$OUT")"

case_ "every held task waits on the reviewer, and one the Dev handed back with a question brings it"
gh_items <<'ITEMS'
In_review 115 I can change any of the keys
Ready 192 I can reply to a pitch
Ready 195 Held by me
Ready 196 Waits on another task
ITEMS
edit_item 192 '.labels.nodes = [{name: "a-team:dev"}, {name: "blocked"}]'
edit_item 195 '.labels.nodes = [{name: "blocked"}]'
edit_item 196 '.issueDependenciesSummary.blockedBy = 1'
gh_talk <<TALK
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
192 body ${TODAY}T07:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
192 comment ${TODAY}T08:23:06Z demo-app[bot] Which marker should it post?\n\n<!-- a-team:dev -->
192 labeled ${TODAY}T08:23:14Z demo-app[bot] blocked
195 body ${TODAY}T07:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
195 comment ${TODAY}T07:05:00Z demo-app[bot] Split off from #194.\n<!-- a-team:lead -->
195 labeled ${TODAY}T09:00:00Z reviewer blocked
TALK
: >"$CALLS"
waiting
same "exit" 0 "$STATUS"
same "numbers" '[115,192,195]' "$(jq -c '[.[].number]' "$OUT")"
same "status" '"Ready"' "$(jq -c '.[1].status' "$OUT")"
same "turn" '"you"' "$(jq -c '.[1].turn' "$OUT")"
same "reason" '"asked you since 08:23"' "$(jq -c '.[1].reason' "$OUT")"
same "question" '"Which marker should it post?"' "$(jq -c '.[1].question' "$OUT")"
same "held by" '"dev"' "$(jq -c '.[1].heldBy' "$OUT")"
same "api calls" 4 "$(grep -c '' <"$CALLS")"

case_ "a held task with no ask from either agent since it was labelled is held for no reason given"
same "turn" '"you"' "$(jq -c '.[2].turn' "$OUT")"
same "reason" '"held, no reason given since 09:00"' "$(jq -c '.[2].reason' "$OUT")"
same "no question" '[null,null]' "$(jq -c '.[2] | [.question, .heldBy]' "$OUT")"

case_ "a task the Lead held waits on the reviewer, with the Lead's latest ask as its question"
gh_talk <<TALK
195 body ${TODAY}T07:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
195 comment ${TODAY}T07:05:00Z demo-app[bot] Split off from #194.\n<!-- a-team:lead -->
195 comment ${TODAY}T08:58:00Z demo-app[bot] Holding this: it needs a GPG key.\n<!-- a-team:lead -->
195 labeled ${TODAY}T09:00:00Z demo-app[bot] blocked
195 comment ${TODAY}T09:10:00Z demo-app[bot] Holding this until you have a GPG key, and the SDK.\n\n<!-- a-team:lead -->
TALK
waiting
same "exit" 0 "$STATUS"
same "held" '{"turn":"you","reason":"held by the Lead since 09:10","question":"Holding this until you have a GPG key, and the SDK.","heldBy":"lead"}' \
  "$(jq -c '.[] | select(.number == 195) | {turn, reason, question, heldBy}' "$OUT")"
same "no unread in waiting" 'null' "$(jq -c '.[] | select(.number == 195) | .unread' "$OUT")"
run board demo triggers dev
same "no Dev run for a Lead hold" '[]' "$(jq -c .reasons "$OUT")"
run board --dry-run demo unblock lead 195
failed "the Lead clearing its hold before a reply"
grep -q "no stakeholder has replied to lead's ask" "$ERR" || fail "lead before a reply: '$(cat "$ERR")'"

case_ "a reply on a task the Lead held is the Lead's to read, and starts a Lead run naming it"
gh_talk <<TALK
195 body ${TODAY}T07:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
195 labeled ${TODAY}T09:00:00Z demo-app[bot] blocked
195 comment ${TODAY}T09:10:00Z demo-app[bot] Reply here once the SDK is in.\n\n<!-- a-team:lead -->
195 comment ${TODAY}T09:20:00Z reviewer Installed.
TALK
waiting
same "exit" 0 "$STATUS"
same "held" '{"turn":"lead","reason":"reading your answer since 09:20","heldBy":"lead"}' \
  "$(jq -c '.[] | select(.number == 195) | {turn, reason, heldBy}' "$OUT")"
mkdir -p "$A_TEAM_STATE/demo/lead"
touch "$A_TEAM_STATE/demo/lead/holds-reviewed"
run board demo triggers lead
same "exit" 0 "$STATUS"
same "reasons" "[\"stakeholder answered the Lead's hold on #195 (${TODAY}T09:20:00Z)\"]" "$(jq -c '[.reasons[] | select(contains("hold"))]' "$OUT")"
same "items" "[195]" "$(jq -c .items "$OUT")"

case_ "the Lead may clear a hold it set once a stakeholder has replied"
gh_thread <<THREAD
body ${TODAY}T07:00:00Z demo-app[bot] 0 The task\n<!-- a-team:lead -->
comment ${TODAY}T09:10:00Z demo-app[bot] 0 Reply here once the SDK is in.\n<!-- a-team:lead -->
comment ${TODAY}T09:20:00Z reviewer 0 Installed.
THREAD
: >"$WRITES"
: >"$ACKED"
A_TEAM_RUN_STARTED=${TODAY}T23:59:59Z run board demo unblock lead 195
same "exit" 0 "$STATUS"
same "writes" "" "$(cat "$WRITES")"
same "held" null "$(marks 195 | jq -c .held)"
same "acked" "IC_2" "$(cat "$ACKED")"
same "recorded" '"hold answered"' "$(A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board demo history 195 | jq -c '.events[0].what')"
run board demo unblock dev 195
failed "the Dev clearing the Lead's hold"
grep -q "lead held it" "$ERR" || fail "dev on a Lead hold: '$(cat "$ERR")'"

case_ "the Lead may clear a hold it set once the task waits on another repo instead, reply or not"
gh_talk <<TALK
195 body ${TODAY}T07:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
195 labeled ${TODAY}T09:00:00Z demo-app[bot] blocked
195 comment ${TODAY}T09:10:00Z demo-app[bot] Remove blocked once tui-style-guide#44 is released.\n<!-- a-team:lead -->
TALK
edit_item 195 '.labels.nodes = [{name: "blocked"}, {name: "a-team:upstream"}]'
: >"$WRITES"
run board demo unblock lead 195
same "exit" 0 "$STATUS"
same "writes" "" "$(cat "$WRITES")"
same "held" null "$(marks 195 | jq -c .held)"
same "recorded" '"hold now waits on another repo"' "$(A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board demo history 195 | jq -c '.events[0].what')"
edit_item 195 '.labels.nodes = [{name: "blocked"}]'

case_ "the Lead's first run after holds clear themselves is asked to turn its holds on another repo into dependencies, once"
rm -f "$A_TEAM_STATE/demo/lead/holds-reviewed"
run board --dry-run demo triggers lead
same "dry-run leaves it to ask" false "$([ -f "$A_TEAM_STATE/demo/lead/holds-reviewed" ] && echo true || echo false)"
run board demo triggers lead
same "exit" 0 "$STATUS"
same "reasons" '["tasks you held before holds cleared themselves (#195): make those waiting on another repo dependencies"]' \
  "$(jq -c '[.reasons[] | select(startswith("tasks you held"))]' "$OUT")"
run board demo triggers lead
same "only once" '[]' "$(jq -c '[.reasons[] | select(startswith("tasks you held"))]' "$OUT")"


case_ "once the reviewer replies to the question, it's the Dev's turn"
gh_talk <<TALK
192 body ${TODAY}T07:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
192 comment ${TODAY}T08:23:06Z demo-app[bot] Which marker should it post?\n<!-- a-team:dev -->
192 labeled ${TODAY}T08:23:14Z demo-app[bot] blocked
192 comment+seen ${TODAY}T10:50:00Z reviewer The Dev's own.
TALK
waiting
same "exit" 0 "$STATUS"
same "turn" '"dev"' "$(jq -c '.[] | select(.number == 192) | .turn' "$OUT")"
same "reason" '"reading your answer since 10:50"' "$(jq -c '.[] | select(.number == 192) | .reason' "$OUT")"

case_ "a Dev comment long before the task was labelled blocked is no question, but the task is still held"
gh_talk <<TALK
192 body ${TODAY}T07:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
192 comment ${TODAY}T07:10:00Z demo-app[bot] Draft PR #9 is up.\n<!-- a-team:dev -->
192 labeled ${TODAY}T09:00:00Z reviewer blocked
TALK
waiting
same "exit" 0 "$STATUS"
same "numbers" '[115,192,195]' "$(jq -c '[.[].number]' "$OUT")"
same "no question" '[null,null]' "$(jq -c '.[1] | [.question, .heldBy]' "$OUT")"
same "reason" '"held, no reason given since 09:00"' "$(jq -c '.[1].reason' "$OUT")"

case_ "a reply to the Dev's question starts a Dev run, and a held task's comment doesn't"
gh_talk <<TALK
192 body ${TODAY}T07:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
192 comment ${TODAY}T08:23:06Z demo-app[bot] Which marker should it post?\n<!-- a-team:dev -->
192 labeled ${TODAY}T08:23:14Z demo-app[bot] blocked
192 comment ${TODAY}T10:50:00Z reviewer The Dev's own.
195 body ${TODAY}T07:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
195 labeled ${TODAY}T09:00:00Z reviewer blocked
195 comment ${TODAY}T09:30:00Z reviewer Not yet, I'm holding this.
TALK
run board demo triggers dev
same "exit" 0 "$STATUS"
same "reasons" "[\"stakeholder answered the Dev's question on #192 (${TODAY}T10:50:00Z)\"]" "$(jq -c .reasons "$OUT")"
waiting
same "no unread in waiting" 'null' "$(jq -c '.[] | select(.number == 192) | .unread' "$OUT")"

case_ "once a run has read the reply, it starts no more runs"
gh_talk <<TALK
192 body ${TODAY}T07:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
192 comment ${TODAY}T08:23:06Z demo-app[bot] Which marker should it post?\n<!-- a-team:dev -->
192 labeled ${TODAY}T08:23:14Z demo-app[bot] blocked
192 comment+seen ${TODAY}T10:50:00Z reviewer The Dev's own.
TALK
run board demo triggers dev
same "exit" 0 "$STATUS"
same "reasons" '[]' "$(jq -c .reasons "$OUT")"

case_ "the Dev asking again makes the question the reviewer's once more"
gh_talk <<TALK
192 body ${TODAY}T07:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
192 comment ${TODAY}T08:23:06Z demo-app[bot] Which marker should it post?\n<!-- a-team:dev -->
192 labeled ${TODAY}T08:23:14Z demo-app[bot] blocked
192 comment+seen ${TODAY}T10:50:00Z reviewer I don't understand the question.
192 comment ${TODAY}T11:00:00Z demo-app[bot] Put another way: the Lead's marker or the Dev's?\n<!-- a-team:dev -->
TALK
waiting
same "turn" '"you"' "$(jq -c '.[] | select(.number == 192) | .turn' "$OUT")"
same "reason" '"asked you since 11:00"' "$(jq -c '.[] | select(.number == 192) | .reason' "$OUT")"
run board --dry-run demo unblock dev 192
failed "unblock before a reply"
grep -q "no stakeholder has replied" "$ERR" || fail "unblock before a reply: '$(cat "$ERR")'"

case_ "the Dev may clear blocked once the reviewer has replied, and reads the reply once"
gh_talk <<TALK
192 body ${TODAY}T07:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
192 comment ${TODAY}T08:23:06Z demo-app[bot] Which marker should it post?\n<!-- a-team:dev -->
192 labeled ${TODAY}T08:23:14Z demo-app[bot] blocked
192 comment ${TODAY}T10:50:00Z reviewer The Dev's own.
TALK
gh_thread <<THREAD
body ${TODAY}T07:00:00Z demo-app[bot] 0 The task\n<!-- a-team:lead -->
comment ${TODAY}T08:23:06Z demo-app[bot] 0 Which marker should it post?\n<!-- a-team:dev -->
comment ${TODAY}T10:50:00Z reviewer 0 The Dev's own.
THREAD
: >"$WRITES"
: >"$ACKED"
run board --dry-run demo unblock dev 192
same "dry-run exit" 0 "$STATUS"
same "dry-run said" "(dry run) #192: no longer blocked" "$(cat "$OUT")"
same "dry-run writes" "" "$(cat "$WRITES")"
same "dry-run acked" "" "$(cat "$ACKED")"
A_TEAM_RUN_STARTED=${TODAY}T23:59:59Z run board demo unblock dev 192
same "exit" 0 "$STATUS"
same "writes" "" "$(cat "$WRITES")"
same "held" null "$(marks 192 | jq -c .held)"
same "acked" "IC_2" "$(cat "$ACKED")"
same "recorded" '"question answered"' "$(A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board demo history 192 | jq -c '.events[0].what')"

case_ "the Dev may not clear blocked on a task the reviewer holds, nor may the Lead"
gh_talk <<TALK
195 body ${TODAY}T07:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
195 labeled ${TODAY}T09:00:00Z reviewer blocked
195 comment ${TODAY}T09:30:00Z reviewer Not yet, I'm holding this.
TALK
: >"$WRITES"
run board demo unblock dev 195
failed "held task"
grep -q "a stakeholder is holding it" "$ERR" || fail "held task: '$(cat "$ERR")'"
run board demo unblock lead 195
failed "lead on a stakeholder's hold"
grep -q "a stakeholder is holding it" "$ERR" || fail "lead on a stakeholder's hold: '$(cat "$ERR")'"
run board demo unblock lead 192
failed "lead"
run board demo unblock dev 115
failed "a task in review"
same "writes" "" "$(cat "$WRITES")"

case_ "you can hold a Ready task with a reason, which the card in waiting says"
gh_talk <<TALK
196 body ${TODAY}T07:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
TALK
echo "wait until I've talked to the designer" >"$WORK/why"
: >"$WRITES"
: >"$POSTED"
run board demo hold you 196 "$WORK/why"
same "exit" 0 "$STATUS"
same "posted" "wait until I've talked to the designer" "$(cat "$POSTED")"
same "writes" "" "$(cat "$WRITES")"
same "held by" '"you"' "$(marks 196 | jq -c .held.by)"
same "recorded" '"held"' "$(A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board demo history 196 | jq -c '.events[0].what')"
edit_item 196 '.labels.nodes = [{name: "blocked"}]'
gh_talk <<TALK
196 body ${TODAY}T07:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
196 comment ${TODAY}T11:00:00Z reviewer wait until I've talked to the designer
196 labeled ${TODAY}T11:00:02Z reviewer blocked
TALK
waiting
same "held by you" '{"turn":"you","reason":"held by you since 11:00","question":"wait until I'"'"'ve talked to the designer","heldBy":"you"}' \
  "$(jq -c '.[] | select(.number == 196) | {turn, reason, question, heldBy}' "$OUT")"

case_ "the Lead commenting on a task you held doesn't make the hold its own"
gh_talk <<TALK
196 body ${TODAY}T07:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
196 comment ${TODAY}T11:00:00Z reviewer wait until I've talked to the designer
196 labeled ${TODAY}T11:00:02Z reviewer blocked
196 comment ${TODAY}T12:00:00Z demo-app[bot] Blocked by #11: the picker first.\n<!-- a-team:lead -->
TALK
waiting
same "still yours" '"you"' "$(jq -c '.[] | select(.number == 196) | .heldBy' "$OUT")"
run board demo unblock lead 196
failed "the Lead clearing your hold"
grep -q "a stakeholder is holding it" "$ERR" || fail "lead on your hold: '$(cat "$ERR")'"

case_ "a Lead comment just before you hold a task doesn't make the hold the Lead's"
gh_talk <<TALK
195 body ${TODAY}T07:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
195 comment ${TODAY}T10:55:00Z demo-app[bot] Waits on tui-style-guide#44 being released.\n<!-- a-team:lead -->
195 comment ${TODAY}T11:00:00Z reviewer wait until I've talked to the designer
195 labeled ${TODAY}T11:00:02Z reviewer blocked
TALK
edit_item 195 '.labels.nodes = [{name: "blocked"}, {name: "a-team:upstream"}]'
waiting
same "held by you" '{"turn":"you","reason":"held by you since 11:00","heldBy":"you"}' \
  "$(jq -c '.[] | select(.number == 195) | {turn, reason, heldBy}' "$OUT")"
run board demo triggers lead
same "no Lead run" '[]' "$(jq -c '[.reasons[] | select(contains("#195"))]' "$OUT")"
: >"$WRITES"
run board demo unblock lead 195
failed "the Lead clearing your hold on an upstream task"
grep -q "a stakeholder is holding it" "$ERR" || fail "lead on your upstream hold: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"
edit_item 195 '.labels.nodes = [{name: "blocked"}]'

case_ "a hold needs a reason, a Ready task, and you"
: >"$WRITES"
printf '  \n' >"$WORK/blank"
run board demo hold you 192 "$WORK/blank"
failed "no reason"
grep -q "say why" "$ERR" || fail "no reason: '$(cat "$ERR")'"
run board demo hold you 115 "$WORK/why"
failed "not Ready"
run board demo hold lead 192 "$WORK/why"
failed "lead"
run board demo hold you 196 "$WORK/why"
failed "already held"
same "writes" "" "$(cat "$WRITES")"

case_ "you can release any hold, an agent's included, but no agent can release one this way"
: >"$WRITES"
run board demo release you 192
same "exit" 0 "$STATUS"
same "writes" "" "$(cat "$WRITES")"
same "held" null "$(marks 192 | jq -c .held)"
same "recorded" '"hold released"' "$(A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board demo history 192 | jq -c '.events[0].what')"
: >"$WRITES"
run board demo release dev 195
failed "dev"
run board demo release you 115
failed "not held"
same "writes" "" "$(cat "$WRITES")"

case_ "the Lead asks for a repo secret and the Dev for an environment's: each waits on you, by name and destination"
gh_items <<'ITEMS'
In_progress 301 The Dev's task
Ready 302 The Lead's task
Ready 303 A plain Ready task
ITEMS
edit_item 301 '.labels.nodes = [{name: "a-team:dev"}]'
printf 'A classic PAT with public_repo, for the fork.\n' >"$WORK/why-secret"
: >"$WRITES"
: >"$POSTED"
run board demo secret lead 302 WINGET_TOKEN repo "$WORK/why-secret"
export KEEP_BOARD=1
same "exit" 0 "$STATUS"
same "marked" '{"name":"WINGET_TOKEN","environment":"","by":"lead","why":"A classic PAT with public_repo, for the fork."}' "$(marks 302 | jq -c .secret)"
same "held by the Lead" '"lead"' "$(marks 302 | jq -c .held.by)"
grep -q 'WINGET_TOKEN.*a repo secret of mentaldesk/demo' "$POSTED" || fail "the ask names it and where: '$(cat "$POSTED")'"
grep -q 'A classic PAT with public_repo, for the fork.' "$POSTED" || fail "the ask says why: '$(cat "$POSTED")'"
grep -q '<!-- a-team:lead -->' "$POSTED" || fail "the ask says who: '$(cat "$POSTED")'"
same "recorded" '"held · asks for WINGET_TOKEN"' "$(A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board demo history 302 | jq -c '.events[0].what')"
run board demo secret dev 301 PACKAGES_TOKEN release "$WORK/why-secret"
same "exit" 0 "$STATUS"
same "marked" '{"name":"PACKAGES_TOKEN","environment":"release","by":"dev"}' "$(marks 301 | jq -c '.secret | {name, environment, by}')"
run board demo move dev 301 Ready
same "exit" 0 "$STATUS"
gh_talk <<TALK
301 comment ${TODAY}T08:00:00Z demo-app[bot] Needs it.\n\n<!-- a-team:dev -->
302 comment ${TODAY}T08:00:00Z demo-app[bot] Needs it.\n\n<!-- a-team:lead -->
TALK
waiting
same "exit" 0 "$STATUS"
same "the Lead's" '{"name":"WINGET_TOKEN","environment":"","by":"lead","why":"A classic PAT with public_repo, for the fork.","repo":"mentaldesk/demo"}' \
  "$(jq -c '.[] | select(.number == 302) | .secret' "$OUT")"
same "the Dev's" '{"name":"PACKAGES_TOKEN","environment":"release","repo":"mentaldesk/demo"}' \
  "$(jq -c '.[] | select(.number == 301) | .secret | {name, environment, repo}' "$OUT")"
same "facts say what it needs" '"held · needs WINGET_TOKEN"' "$(jq -c '.[] | select(.number == 302) | .facts' "$OUT")"

case_ "only the Lead on a Ready task, and the Dev on its own, can ask for a secret, for a name GitHub would take"
: >"$WRITES"
for role in you customer reviewer; do
  run board demo secret "$role" 303 WINGET_TOKEN repo "$WORK/why-secret"
  failed "$role"
  grep -q "only the Lead or the Dev" "$ERR" || fail "$role: '$(cat "$ERR")'"
done
run board demo secret dev 303 WINGET_TOKEN repo "$WORK/why-secret"
failed "the Dev on a task that isn't its own"
for name in 1TOKEN "A-B" GITHUB_TOKEN github_token; do
  run board demo secret lead 303 "$name" repo "$WORK/why-secret"
  failed "the name $name"
done
run board demo secret lead 303 TOKEN '--bad' "$WORK/why-secret"
failed "an environment that reads as a flag"
printf '  \n' >"$WORK/blank"
run board demo secret lead 303 TOKEN repo "$WORK/blank"
failed "no reason"
run board demo secret lead 302 OTHER repo "$WORK/why-secret"
failed "already held"
same "nothing posted" "" "$(cat "$WRITES")"
same "no ask" 'null' "$(marks 303 | jq -c .secret)"

case_ "a given secret frees the task and records only its name, and only you can say so"
: >"$WRITES"
run board demo gave lead 302
failed "the Lead"
run board demo gave dev 302
failed "the Dev"
run board demo gave you 303
failed "a task asking for none"
A_TEAM_RUN_TEAM=demo run board demo gave you 302
failed "in a team's run"
run board demo gave you 302
same "exit" 0 "$STATUS"
same "held" 'null' "$(marks 302 | jq -c .held)"
same "secret" 'null' "$(marks 302 | jq -c .secret)"
same "recorded" '{"who":"you","what":"gave WINGET_TOKEN"}' "$(A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board demo history 302 | jq -c '.events[0] | {who, what}')"
same "no comment" "" "$(cat "$WRITES")"
run board demo gave you 302
failed "given twice"

case_ "releasing a task asking for a secret takes the ask off too"
run board demo release you 301
same "exit" 0 "$STATUS"
same "secret" 'null' "$(marks 301 | jq -c .secret)"
unset KEEP_BOARD

finish_suite
