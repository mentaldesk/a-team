#!/usr/bin/env bash
# A team with no mirror, red main and fixes.
# shellcheck source=tests/scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
# shellcheck disable=SC2034,SC2154 # helpers read what an area sets, and an area reads what lib.sh sets

case_ "a team with no mirror goes through a whole pitch and task cycle without a GitHub Project"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "mirror": false,
  "wip": { "worktrees": 3, "pitched": 3, "exploring": 3, "ideas": 3 } }
JSON
gh_items <<'ITEMS'
Pitched 30 A pitch
Ready 77 Its task
ITEMS
echo '{"number": 30, "node_id": "I_30", "body": "An idea"}' >"$ISSUE"
: >"$BIN/args"
run board demo add you 30 Idea
same "added" 0 "$STATUS"
kept board demo move lead 30 Exploring
kept board demo move lead 30 Pitched
kept board demo approve you 30
kept board demo move lead 30 Building
same "pitch" '"Building"' "$(KEEP_BOARD=1 A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board demo list | jq -c '.[] | select(.number == 30) | .status')"
echo 'A task' >"$WORK/task.md"
echo '{"id": 77, "number": 77, "node_id": "I_77", "body": "A task\n\n<!-- a-team:lead -->"}' >"$ISSUE"
kept board demo task lead 30 "Its task" "$WORK/task.md"
same "filed" 0 "$STATUS"
claimable
kept board demo claim dev
same "claimed" 77 "$(jq -c .number "$OUT")"
kept board demo move dev 77 "In review"
edit_item 77 '.state = "CLOSED" | .stateReason = "COMPLETED"'
kept board demo list
same "board" '[[30,"Building"],[77,"Done"]]' "$(jq -c 'map([.number, .status])' "$OUT")"
same "project calls" 0 "$(grep -ci projectv2 "$BIN/args")"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 },
  "wip": { "worktrees": 3 } }
JSON

# short_reads <times> <keep>: the next <times> board reads come back with only their first <keep> items.
short_reads() { echo "$1 $2" >"$BIN/short-reads"; }
SHORT='the board read came back incomplete (1 of 3 items), twice. Nothing was changed'

case_ "a board read that comes back short once is read again and carries on, filtered or not"
gh_items <<'ITEMS'
Ready 13 Something to start
In_review 14 Waiting on you
Done 15 Merged
ITEMS
run board demo list
complete=$(jq 'map(del(.since))' "$OUT")
short_reads 1 1
run board demo list
same "exit" 0 "$STATUS"
same "items" "$complete" "$(jq 'map(del(.since))' "$OUT")"
same "stderr" "" "$(cat "$ERR")"
short_reads 1 0
waiting
same "filtered exit" 0 "$STATUS"
same "filtered stderr" "" "$(cat "$ERR")"

case_ "a board read short twice, or empty with items to read, ends the command saying so, and changes nothing"
short_reads 2 1
run board demo list
failed "short twice"
one_line "short twice"
grep -qF "board.sh: $SHORT" "$ERR" || fail "short twice: '$(cat "$ERR")'"
same "stdout" "" "$(cat "$OUT")"
short_reads 2 0
waiting
failed "empty"
grep -qF 'incomplete (0 of 3 items), twice' "$ERR" || fail "empty: '$(cat "$ERR")'"

case_ "a board read gh fails on ends the command, rather than reading as an empty board"
touch "$BIN/read-fails"
run board demo list
rm "$BIN/read-fails"
failed "read fails"
same "stdout" "" "$(cat "$OUT")"

case_ "next and claim on a short board read claim nothing"
claimable
short_reads 2 1
run board demo next
failed "next"
same "next stdout" "" "$(cat "$OUT")"
grep -qF "$SHORT" "$ERR" || fail "next: '$(cat "$ERR")'"
short_reads 2 1
run board demo claim dev
failed "claim"
grep -qF "$SHORT" "$ERR" || fail "claim: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"

case_ "lead-next on a short board read uses up no turn"
mkdir -p "$A_TEAM_STATE/demo" && echo discover >"$A_TEAM_STATE/demo/lead-turn"
short_reads 2 1
run board demo lead-next
failed "lead-next"
grep -qF "$SHORT" "$ERR" || fail "lead-next: '$(cat "$ERR")'"
same "turn" discover "$(cat "$A_TEAM_STATE/demo/lead-turn")"

case_ "setup on a short board read drops no Status option"
short_reads 2 1
rm -f "$BIN/mutation.json"
run board demo setup
failed "setup"
grep -qF "$SHORT" "$ERR" || fail "setup: '$(cat "$ERR")'"
[ ! -e "$BIN/mutation.json" ] || fail "setup: Status options changed"

case_ "a truly empty board reads as empty, and other repos' items on a shared project count towards its total"
gh_items </dev/null
run board demo list
same "empty exit" 0 "$STATUS"
same "empty" "[]" "$(jq -c . "$OUT")"
same "empty stderr" "" "$(cat "$ERR")"
gh_items <<'ITEMS'
Ready 13 Something to start
Ready 14 Another team's task
ITEMS
edit_item 14 '.repository.nameWithOwner = "mentaldesk/other"'
run board demo list
same "shared exit" 0 "$STATUS"
same "shared" "[13]" "$(jq -c 'map(.number)' "$OUT")"
same "shared stderr" "" "$(cat "$ERR")"

case_ "next and claim take a fix first, even over an Urgent task, and in the extra worktree"
gh_items 14 <<'ITEMS'
In_progress 11 One
In_progress 12 Two
In_progress 15 Three
Ready 13 Something to start
Ready 14 Something pressing
Ready 16 Main builds again
ITEMS
edit_item 14 '.issueFieldValues.nodes = [{name: "Urgent", field: {name: "Priority"}}]'
edit_item 16 '.labels.nodes += [{name: "a-team:fix"}]'
claimable
run board demo next
same "next" 16 "$(jq -c .number "$OUT")"
run board demo triggers dev
same "ready" 16 "$(jq -c .ready "$OUT")"
jq -e '.reasons | index("Ready fix task #16 for the red default branch: take it first")' "$OUT" >/dev/null ||
  fail "triggers: no fix reason in '$(jq -c .reasons "$OUT")'"
run board demo claim dev
same "exit" 0 "$STATUS"
same "claimed" '{"number":16,"title":"Main builds again"}' "$(cat "$OUT")"

case_ "a task under an Urgent pitch takes the extra worktree too"
gh_items <<'ITEMS'
Building 10 An Urgent pitch
In_progress 11 One
In_progress 12 Two
In_progress 15 Three
Ready 13 Something to start
Ready 14 Something pressing
ITEMS
edit_item 10 '.issueFieldValues.nodes = [{name: "Urgent", field: {name: "Priority"}}]'
edit_item 14 '.parent = {number: 10}'
claimable
run board demo claim dev
same "exit" 0 "$STATUS"
same "claimed" '{"number":14,"title":"Something pressing"}' "$(cat "$OUT")"

case_ "a fix gets one extra worktree, not two"
gh_items <<'ITEMS'
In_progress 11 One
In_progress 12 Two
In_progress 15 Three
In_progress 17 Four
Ready 16 Main builds again
ITEMS
edit_item 16 '.labels.nodes += [{name: "a-team:fix"}]'
claimable
run board demo claim dev
failed "no extra worktree"
grep -q "no room In progress for #16" "$ERR" || fail "no worktree: '$(cat "$ERR")'"
run board demo triggers dev
same "ready" null "$(jq -c .ready "$OUT")"

case_ "a PR failing only what main fails starts no Dev run, and checks says main-red"
gh_items <<'ITEMS'
In_progress 12 A task with its draft PR up
ITEMS
gh_pr 912 true
gh_runs <<'RUNS'
completed failure 2025-09-19T09:00:00Z build
RUNS
gh_main_runs <<'RUNS'
completed failure 2025-09-19T08:00:00Z build
completed success 2025-09-19T08:00:00Z lint
RUNS
run board demo triggers dev
same "exit" 0 "$STATUS"
same "reasons" '[]' "$(jq -c .reasons "$OUT")"
run board demo checks 912
same "verdict" '"main-red"' "$(jq -c .verdict "$OUT")"

case_ "a fix's PR failing what main fails is still the Dev's to fix"
edit_item 12 '.labels.nodes += [{name: "a-team:fix"}]'
run board demo triggers dev
same "reasons" '["CI failed on PR #912 at deadbee"]' "$(jq -c .reasons "$OUT")"
run board demo checks 912
same "verdict" '"fail"' "$(jq -c .verdict "$OUT")"
edit_item 12 '.labels.nodes -= [{name: "a-team:fix"}]'

case_ "a PR failing a job main passes is the Dev's to fix"
gh_runs <<'RUNS'
completed failure 2025-09-19T09:00:00Z build
completed failure 2025-09-19T09:00:00Z lint
RUNS
run board demo triggers dev
same "reasons" '["CI failed on PR #912 at deadbee"]' "$(jq -c .reasons "$OUT")"
run board demo checks 912
same "verdict" '"fail"' "$(jq -c .verdict "$OUT")"

case_ "a red main starts the Lead until a fix is open"
mkdir -p "$A_TEAM_STATE/demo/lead" && touch "$A_TEAM_STATE/demo/lead/seeded"
gh_items <<'ITEMS'
Done 10 Something shipped
ITEMS
gh_main_runs <<'RUNS'
completed failure 2025-09-19T08:00:00Z build
RUNS
run board demo triggers lead
same "exit" 0 "$STATUS"
same "reasons" '["main is red since b8f5bdd: build failing. File a fix"]' "$(jq -c .reasons "$OUT")"
gh_items <<'ITEMS'
Ready 16 Main builds again
ITEMS
edit_item 16 '.labels.nodes += [{name: "a-team:fix"}]'
gh_main_runs <<'RUNS'
completed failure 2025-09-19T08:00:00Z build
RUNS
run board demo triggers lead
same "reasons with a fix open" '[]' "$(jq -c .reasons "$OUT")"

case_ "fix opens a Ready task marked as the fix, with no pitch, and prints its number"
gh_items <<'ITEMS'
Done 10 Something shipped
ITEMS
gh_main_runs <<'RUNS'
completed failure 2025-09-19T08:00:00Z build
RUNS
claimable
printf 'build fails since #880 and #881 merged.\n' >"$WORK/fix"
run board demo fix dev "Main builds again" "$WORK/fix"
failed "dev files a fix"
run board demo fix lead "Main builds again" "$WORK/fix"
same "exit" 0 "$STATUS"
same "number" 77 "$(cat "$OUT")"
grep -q 'CREATE issue create -R mentaldesk/demo --title Main builds again --body-file' "$WRITES" ||
  fail "fix: no issue in '$(cat "$WRITES")'"
grep -q 'label' "$WRITES" && fail "fix: labelled in '$(cat "$WRITES")'"
same "fix" true "$(marks 77 | jq -c .fix)"
grep -q 'option=OPT_ready' "$WRITES" || fail "fix: not put in Ready in '$(cat "$WRITES")'"
grep -q 'sub_issues' "$WRITES" && fail "fix: made a sub-issue in '$(cat "$WRITES")'"
grep -qF '<!-- a-team:lead -->' "$POSTED" || fail "fix: no marker in '$(cat "$POSTED")'"

case_ "fix is refused while another fix is open"
gh_items <<'ITEMS'
Ready 16 Main builds again
ITEMS
gh_main_runs <<'RUNS'
completed failure 2025-09-19T08:00:00Z build
RUNS
edit_item 16 '.labels.nodes += [{name: "a-team:fix"}]'
run board demo fix lead "Main builds again" "$WORK/fix"
failed "second fix"
grep -q "#16 is already the fix" "$ERR" || fail "second fix: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"

case_ "fix is refused while main is green"
gh_items <<'ITEMS'
Done 10 Something shipped
ITEMS
echo '{"check_runs": []}' >"$MAIN_RUNS"
run board demo fix lead "Main builds again" "$WORK/fix"
failed "green main"
grep -q "isn't red" "$ERR" || fail "green main: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"
run board demo triggers lead
same "no reason while green" '[]' "$(jq -c .reasons "$OUT")"

case_ "a run bound to a task can hand it back, and can't touch another"
gh_items <<'ITEMS'
In_progress 12 This run's task
In_progress 16 Another run's task
ITEMS
claimable
gh_pr
echo 'Which marker?' >"$WORK/question"
A_TEAM_RUN_TASK=12 run board demo move dev 12 Ready
same "hand back" 0 "$STATUS"
A_TEAM_RUN_TASK=12 run board demo depends dev 12 16 "both rewrite the same view"
same "defer" 0 "$STATUS"
A_TEAM_RUN_TASK=12 run board demo comment dev 12 "$WORK/question"
same "ask" 0 "$STATUS"
: >"$WRITES"
: >"$POSTED"
A_TEAM_RUN_TASK=12 run board demo move dev 16 Ready
failed "other task"
grep -q "this run is for #12: leave #16 to a run of its own" "$ERR" || fail "other task: '$(cat "$ERR")'"
A_TEAM_RUN_TASK=12 run board demo comment dev 16 "$WORK/question"
failed "other comment"
A_TEAM_RUN_TASK=12 run board demo depends dev 16 12 "the other way round"
failed "other depends"
same "writes" "" "$(cat "$WRITES")"
same "posted" "" "$(cat "$POSTED")"
gh_pr 912 true
A_TEAM_RUN_TASK=12 run board demo comment dev 912 "$WORK/question"
same "its own PR" 0 "$STATUS"

case_ "handing a task back labelled blocked records the question, for Trends' With you"
rm -f "$A_TEAM_STATE/history.db"
gh_items <<'ITEMS'
In_progress 12 A task with a question
In_progress 16 A task that can't be finished
ITEMS
claimable
edit_item 12 '.labels.nodes += [{name: "blocked"}]'
A_TEAM_RUN_TASK=12 run board demo move dev 12 Ready
same "exit" 0 "$STATUS"
same "recorded" '["handed back with a question","In progress → Ready"]' \
  "$(A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board demo history 12 | jq -c '[.events[].what]')"
A_TEAM_RUN_TASK=16 run board demo move dev 16 Ready
same "unlabelled" '["In progress → Ready"]' "$(A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board demo history 16 | jq -c '[.events[].what]')"

finish_suite
