#!/usr/bin/env bash
# Accept and finish.
# shellcheck source=tests/scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
# shellcheck disable=SC2034,SC2154 # helpers read what an area sets, and an area reads what lib.sh sets

case_ "accept squash-merges the task's PR and deletes its branch"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 } }
JSON
gh_items <<'ITEMS'
In_review 7 A task in front of me
In_progress 8 A task still being built
ITEMS
gh_pr 907 false
echo '{"head": {"sha": "deadbeefcafe", "ref": "feat/task", "repo": {"full_name": "mentaldesk/demo"}}}' >"$PULL"
run board demo accept you 7
same "exit" 0 "$STATUS"
same "said" "#7: merged PR #907" "$(cat "$OUT")"
same "writes" "PUT api -X PUT repos/mentaldesk/demo/pulls/907/merge -f merge_method=squash
DELETE api -X DELETE repos/mentaldesk/demo/git/refs/heads/feat/task" "$(cat "$WRITES")"

case_ "a branch on a fork is left where it is"
: >"$WRITES"
echo '{"head": {"sha": "deadbeefcafe", "ref": "feat/task", "repo": {"full_name": "someone/demo"}}}' >"$PULL"
run board demo accept you 7
same "exit" 0 "$STATUS"
same "writes" "PUT api -X PUT repos/mentaldesk/demo/pulls/907/merge -f merge_method=squash" "$(cat "$WRITES")"

case_ "a merge GitHub refuses says why in one line, and deletes nothing"
: >"$WRITES"
touch "$BIN/merge-fails"
run board demo accept you 7
failed "refused merge"
one_line "refused merge"
grep -q "can't merge PR #907 (gh: Pull Request is not mergeable (HTTP 405))" "$ERR" || fail "refused merge: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"
rm "$BIN/merge-fails"

case_ "--dry-run says what it would merge and merges nothing"
run board --dry-run demo accept you 7
same "exit" 0 "$STATUS"
same "writes" "" "$(cat "$WRITES")"
grep -q "would squash-merge PR #907" "$ERR" || fail "accept dry run: '$(cat "$ERR")'"

case_ "neither agent may accept a task: that gate is the reviewer's own"
for role in lead dev; do
  run board demo accept "$role" 7
  failed "$role accepting"
  one_line "$role accepting"
  grep -q "$role may not accept a task; accepting is the stakeholders' own gate" "$ERR" ||
    fail "$role accepting: '$(cat "$ERR")'"
done
same "writes" "" "$(cat "$WRITES")"

case_ "accept is only for a task In review with an open PR"
run board demo accept you 8
failed "accept In progress"
grep -q "is in 'In progress'" "$ERR" || fail "accept In progress: '$(cat "$ERR")'"
run board demo accept you 404
grep -q "#404 is not on the board" "$ERR" || fail "accept off the board: '$(cat "$ERR")'"
gh_pr
run board demo accept you 7
failed "accept with no PR"
grep -q "#7 has no open PR to merge" "$ERR" || fail "accept with no PR: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"

case_ "waiting counts a pitch In review's tasks, and how many are still open"
gh_items <<'ITEMS'
In_review 7 A validated pitch
In_review 8 A task in front of me
ITEMS
edit_item 7 '.labels.nodes = [{name: "pitch"}]'
gh_talk <<TALK
7 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
8 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
TALK
jq '.data.repository |= with_entries(.value.subIssuesSummary = {total: 3, completed: 1})' "$TALK" >"$TALK.new" &&
  mv "$TALK.new" "$TALK"
waiting
same "exit" 0 "$STATUS"
same "pitch tasks" '[3,2]' "$(jq -c '.[] | select(.number == 7) | [.tasks, .openTasks]' "$OUT")"
same "task tasks" '[null,null]' "$(jq -c '.[] | select(.number == 8) | [.tasks, .openTasks]' "$OUT")"

case_ "the agents' settings deny accept, beside approve"
grep -qF '"Bash(a-team board * accept you *)"' "$ROOT/settings/agents.json" || fail "no accept deny rule"

case_ "the agents' settings deny removing a label by hand, so blocked is cleared only through unblock"
grep -qF '"Bash(gh issue edit *--remove-label*)"' "$ROOT/settings/agents.json" || fail "no remove-label deny rule"

case_ "accept closes a validated pitch as done once every one of its tasks is closed"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 } }
JSON
gh_items <<'ITEMS'
In_review 7 A validated pitch
Building 8 A pitch still being built
ITEMS
edit_item 7 '.labels.nodes = [{name: "pitch"}]'
edit_item 8 '.labels.nodes = [{name: "pitch"}]'
echo '[{"number": 11, "state": "closed"}, {"number": 12, "state": "closed"}]' >"$SUBS"
run board demo accept you 7
same "exit" 0 "$STATUS"
same "said" "#7: closed as done" "$(cat "$OUT")"
same "writes" "CLOSE issue close 7 -R mentaldesk/demo --reason completed" "$(cat "$WRITES")"

case_ "--dry-run says it would close the pitch and closes nothing"
: >"$WRITES"
run board --dry-run demo accept you 7
same "exit" 0 "$STATUS"
same "writes" "" "$(cat "$WRITES")"
grep -q "would close #7 as completed" "$ERR" || fail "accept pitch dry run: '$(cat "$ERR")'"

case_ "a close GitHub refuses says why in one line"
touch "$BIN/close-fails"
run board demo accept you 7
failed "refused close"
one_line "refused close"
grep -q "can't close #7 (gh: Resource not accessible by integration (HTTP 403))" "$ERR" ||
  fail "refused close: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"
rm "$BIN/close-fails"

case_ "a pitch with a task still open is refused, saying how many, and stays open"
echo '[{"number": 11, "state": "closed"}, {"number": 12, "state": "open"}, {"number": 13, "state": "open"}]' >"$SUBS"
run board demo accept you 7
failed "open tasks"
one_line "open tasks"
grep -q "#7 has 2 open tasks" "$ERR" || fail "open tasks: '$(cat "$ERR")'"
echo '[{"number": 11, "state": "closed"}, {"number": 12, "state": "open"}]' >"$SUBS"
run board demo accept you 7
grep -q "#7 has 1 open task$" "$ERR" || fail "one open task: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"

case_ "a pitch is accepted only In review"
echo '[]' >"$SUBS"
run board demo accept you 8
failed "accept a pitch Building"
grep -q "only a pitch In review can be accepted (#8 is in 'Building')" "$ERR" ||
  fail "accept a pitch Building: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"

case_ "finish closes a pitch in Building once every one of its tasks is closed, saying what the Lead tried"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 } }
JSON
gh_items <<'ITEMS'
Building 7 A pitch whose tasks have all merged
In_review 8 A pitch waiting on acceptance
Ready 9 A task
ITEMS
edit_item 8 '.labels.nodes = [{name: "pitch"}]'
echo '[{"number": 11, "state": "closed"}, {"number": 12, "state": "closed"}]' >"$SUBS"
echo "Tried both cases on main: they work." >"$WORK/checked"
run board demo finish lead 7 "$WORK/checked"
same "exit" 0 "$STATUS"
same "said" "#7: closed as done" "$(cat "$OUT")"
same "writes" "CLOSE issue close 7 -R mentaldesk/demo --reason completed" "$(cat "$WRITES")"
same "posted" "Tried both cases on main: they work." "$(head -1 "$POSTED")"
same "marker" "<!-- a-team:lead -->" "$(tail -1 "$POSTED")"

case_ "finish --dry-run says it would close the pitch and comment, and does neither"
: >"$WRITES"
: >"$POSTED"
run board --dry-run demo finish lead 7 "$WORK/checked"
same "exit" 0 "$STATUS"
same "writes" "" "$(cat "$WRITES")"
same "posted" "" "$(cat "$POSTED")"
grep -q "would close #7 as completed" "$ERR" || fail "finish dry run: no close in '$(cat "$ERR")'"
grep -q "would comment on #7" "$ERR" || fail "finish dry run: no comment in '$(cat "$ERR")'"

case_ "finish is the Lead's alone, for a pitch in Building with tasks, all of them closed"
run board demo finish dev 7 "$WORK/checked"
failed "finish as dev"
grep -q "only lead may close a pitch as done" "$ERR" || fail "finish as dev: '$(cat "$ERR")'"
run board demo finish lead 8 "$WORK/checked"
failed "finish In review"
grep -q "only a pitch in Building can be closed as done (#8 is in 'In review')" "$ERR" ||
  fail "finish In review: '$(cat "$ERR")'"
run board demo finish lead 9 "$WORK/checked"
failed "finish a task"
grep -q "#9 is not a pitch" "$ERR" || fail "finish a task: '$(cat "$ERR")'"
echo '[{"number": 11, "state": "closed"}, {"number": 12, "state": "open"}]' >"$SUBS"
run board demo finish lead 7 "$WORK/checked"
failed "finish with a task open"
one_line "finish with a task open"
grep -q "#7 has 1 open task$" "$ERR" || fail "finish with a task open: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"
same "posted" "" "$(cat "$POSTED")"

case_ "finish closes a pitch with no tasks once it names the merged PRs that built it"
echo '[]' >"$SUBS"
echo '{"merged": true}' >"$PULL"
: >"$WRITES"
: >"$POSTED"
echo "Built by #532. Tried it on main: it works." >"$WORK/built"
run board demo finish lead 7 "$WORK/built" 532 '#540'
same "exit" 0 "$STATUS"
same "said" "#7: closed as done (built by #532, #540)" "$(cat "$OUT")"
same "writes" "CLOSE issue close 7 -R mentaldesk/demo --reason completed" "$(cat "$WRITES")"
same "posted" "Built by #532. Tried it on main: it works." "$(head -1 "$POSTED")"

case_ "finish refuses a pitch with no tasks unless every PR it names is merged here, and changes nothing"
: >"$WRITES"
rm -f "$A_TEAM_STATE/history.db"
: >"$POSTED"
run board demo finish lead 7 "$WORK/built"
failed "finish naming no PR"
one_line "finish naming no PR"
grep -q "#7 has no tasks: name the merged PRs that built it" "$ERR" || fail "finish naming no PR: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"
echo '{"merged": false}' >"$PULL"
run board demo finish lead 7 "$WORK/built" 540
failed "finish naming an unmerged PR"
grep -q "#540 isn't merged, so it can't have built #7" "$ERR" || fail "finish naming an unmerged PR: '$(cat "$ERR")'"
echo '{"merged": true}' >"$PULL"
touch "$BIN/not-a-pull"
run board demo finish lead 7 "$WORK/built" 513
failed "finish naming an issue"
grep -q "#513 isn't merged, so it can't have built #7" "$ERR" || fail "finish naming an issue: '$(cat "$ERR")'"
rm "$BIN/not-a-pull"
touch "$BIN/pull-fails"
run board demo finish lead 7 "$WORK/built" 532
failed "finish when GitHub fails"
grep -q "can't tell whether #532 is merged (gh: API rate limit exceeded (HTTP 403))" "$ERR" ||
  fail "finish when GitHub fails: '$(cat "$ERR")'"
rm "$BIN/pull-fails"
run board demo finish lead 7 "$WORK/built" mentaldesk/style#5
failed "finish naming a PR elsewhere"
grep -q "mentaldesk/style#5 isn't merged" "$ERR" || fail "finish naming a PR elsewhere: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"
same "posted" "" "$(cat "$POSTED")"
same "finish refused: recorded" "" "$(sqlite3 "$A_TEAM_STATE/history.db" 'SELECT what FROM events' 2>/dev/null)"

case_ "finish judges a pitch with tasks on its tasks alone, whatever PRs it names"
echo '[{"number": 11, "state": "closed"}, {"number": 12, "state": "open"}]' >"$SUBS"
run board demo finish lead 7 "$WORK/built" 532
failed "finish with a task open and a merged PR"
grep -q "#7 has 1 open task$" "$ERR" || fail "finish with a task open and a merged PR: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"
echo '[{"number": 11, "state": "closed"}]' >"$SUBS"
echo '{"merged": false}' >"$PULL"
run board demo finish lead 7 "$WORK/built" 540
same "exit" 0 "$STATUS"
same "said" "#7: closed as done" "$(cat "$OUT")"
same "writes" "CLOSE issue close 7 -R mentaldesk/demo --reason completed" "$(cat "$WRITES")"
: >"$WRITES"
: >"$POSTED"

case_ "the Lead closes a finished pitch itself rather than moving it to In review"
run board demo move lead 7 "In review"
failed "Building to In review"
grep -q "lead may not move #7 from 'Building' to 'In review'" "$ERR" ||
  fail "Building to In review: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"

case_ "the agents' settings deny approving as you, beside priority"
grep -qF '"Bash(a-team board * approve you *)"' "$ROOT/settings/agents.json" || fail "no approve deny rule"

finish_suite
