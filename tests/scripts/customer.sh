#!/usr/bin/env bash
# The Customer lead, bumps and updates, and vision review.
# shellcheck source=tests/scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
# shellcheck disable=SC2034,SC2154 # helpers read what an area sets, and an area reads what lib.sh sets

case_ "the Customer lead never triggers for a team that hasn't turned it on"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "stakeholders": ["reviewer"], "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 } }
JSON
gh_items <<'ITEMS'
Done 5 A pitch accepted long ago
ITEMS
accepted 5
run board demo triggers customer
same "exit" 0 "$STATUS"
same "reasons" '[]' "$(jq -c .reasons "$OUT")"
[ -e "$A_TEAM_STATE/demo/customer/covered" ] && fail "off: it started counting what's covered"

case_ "turned on, it audits the docs straight away"
customer_on
run board demo triggers customer
same "first pass" '["weekly docs audit: check the docs as a whole"]' "$(jq -c .reasons "$OUT")"
run board demo audited customer
same "exit" 0 "$STATUS"
same "said" "docs audited: the next audit is in a week" "$(cat "$OUT")"

case_ "turned on, it starts from the pitches done after that, one reason each, and never for a shelved one"
run board demo triggers customer
same "first pass" '[]' "$(jq -c .reasons "$OUT")"
same "creative" false "$(jq -c .creative "$OUT")"
gh_items <<'ITEMS'
Done 5 A pitch accepted long ago
Done 6 A pitch just accepted
Done 8 A pitch closed as not planned
In_review 7 A pitch being validated
ITEMS
accepted 5
accepted 6
accepted 8 NOT_PLANNED
edit_item 7 '.labels.nodes = [{name: "pitch"}]'
run board demo triggers customer
same "exit" 0 "$STATUS"
same "reasons" '["pitch #6 is done: check the docs cover what it shipped"]' "$(jq -c .reasons "$OUT")"
same "api calls" 2 "$(grep -c '' <"$CALLS")"

case_ "covered stops a pitch triggering again, and goes in its history"
run board demo covered customer 6
same "exit" 0 "$STATUS"
same "said" "#6: docs checked" "$(cat "$OUT")"
run board demo triggers customer
same "reasons" '[]' "$(jq -c .reasons "$OUT")"
run board demo history 6
same "history" '"customer docs checked"' "$(jq -c '.events[0] | "\(.who) \(.what)"' "$OUT")"

case_ "covered is the Customer lead's alone, and only for a done pitch"
run board demo covered dev 6
failed "dev covering"
grep -q "only customer checks the docs" "$ERR" || fail "dev covering: '$(cat "$ERR")'"
run board demo covered customer 7
failed "covering a pitch In review"
grep -q "#7 isn't a done pitch" "$ERR" || fail "covering In review: '$(cat "$ERR")'"

case_ "the audit comes round again a week after the last one finished, and not before"
AUDITED="$A_TEAM_STATE/demo/customer/audited"
echo $(($(date +%s) - 7 * 24 * 60 * 60 + 60)) >"$AUDITED"
run board demo triggers customer
same "six days on" '[]' "$(jq -c .reasons "$OUT")"
echo $(($(date +%s) - 7 * 24 * 60 * 60)) >"$AUDITED"
run board demo triggers customer
same "a week on" '["weekly docs audit: check the docs as a whole"]' "$(jq -c .reasons "$OUT")"
run board demo triggers customer
same "until it's audited" '["weekly docs audit: check the docs as a whole"]' "$(jq -c .reasons "$OUT")"
run board demo audited customer
run board demo triggers customer
same "audited" '[]' "$(jq -c .reasons "$OUT")"

case_ "audited is the Customer lead's alone, and --dry-run records nothing"
echo 0 >"$AUDITED"
run board --dry-run demo audited customer
same "said" "(dry run) docs audited: the next audit is in a week" "$(cat "$OUT")"
same "unchanged" 0 "$(cat "$AUDITED")"
run board demo audited dev
failed "dev auditing"
grep -q "only customer audits the docs" "$ERR" || fail "dev auditing: '$(cat "$ERR")'"
jq 'del(.roles)' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
run board demo triggers customer
same "off" '[]' "$(jq -c .reasons "$OUT")"
run board demo audited customer
failed "auditing while off"
customer_on
echo $(($(date +%s))) >"$AUDITED"

case_ "the Customer lead adds its own open docs PR to In review, marked as its"
gh_items <<'ITEMS'
In_progress 7 A task being built
In_review 9 Docs: what's changed since 28 Sep
ITEMS
docs_pr 9
jq '(.data.organization.projectV2.items.nodes[] | select(.content.number == 9)) |= (.fieldValueByName = null)' \
  "$ITEMS" >"$ITEMS.new" && mv "$ITEMS.new" "$ITEMS"
jq '.data.organization.projectV2.fields.nodes[0].options += [{id: "OPT_review", name: "In review"}, {id: "OPT_done", name: "Done"}]' \
  "$META" >"$META.new" && mv "$META.new" "$META"
jq -n '{node_id: "PR_9", number: 9, state: "open", pull_request: {},
        body: "- Help → Guide covers the Customer lead (#48)\n\n<!-- a-team:customer -->"}' >"$ISSUE"
run board demo add customer 9 "In review"
same "exit" 0 "$STATUS"
same "the Customer lead's" true "$(marks 9 | jq -c .customer)"
grep -q 'item=PVTI_9 .*option=OPT_review' "$WRITES" || fail "add: no move in '$(cat "$WRITES")'"

case_ "it may add nothing but its own open docs PR, and only one at a time"
: >"$WRITES"
jq '.body = "Closes #7\n\n<!-- a-team:dev -->"' "$ISSUE" >"$ISSUE.new" && mv "$ISSUE.new" "$ISSUE"
run board demo add customer 9 "In review"
failed "someone else's PR"
grep -q "customer may only add its own open docs PR (#9 isn't one)" "$ERR" || fail "someone else's PR: '$(cat "$ERR")'"
gh_items <<'ITEMS'
In_review 9 Docs: what's changed since 28 Sep
In_review 10 Docs: what's changed since 1 Oct
ITEMS
docs_pr 9
jq -n '{node_id: "PR_10", number: 10, state: "open", pull_request: {}, body: "<!-- a-team:customer -->"}' >"$ISSUE"
run board demo add customer 10 "In review"
failed "a second docs PR"
grep -q "customer's docs PR #9 is still open: add to it rather than opening another" "$ERR" ||
  fail "a second docs PR: '$(cat "$ERR")'"
run board demo add customer 10 Ready
failed "adding to Ready"
grep -q "customer may not add items as 'Ready'" "$ERR" || fail "adding to Ready: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"

case_ "the Customer lead can't move pitches or tasks, change what a task waits on, or clear a gate"
gh_items <<'ITEMS'
In_progress 7 A task being built
Approved 11 A pitch to break down
In_review 9 Docs: what's changed since 28 Sep
ITEMS
docs_pr 9
for args in "move customer 7 Ready" "move customer 11 Building" "approve customer 11" "accept customer 9" \
  "priority customer 7 High" "depends customer 7 11 why" "claim customer" "skip customer 7 $WORK/none"; do
  # shellcheck disable=SC2086 # one argument per word
  run board demo $args
  failed "customer: $args"
done
same "writes" "" "$(cat "$WRITES")"

case_ "the Customer lead comments only on its docs PR, with its own marker"
printf "Added the Customer lead to Help → Guide." >"$WORK/said"
gh_thread pull <<'THREAD'
body 2025-09-19T08:00:00Z demo-app[bot] 0 Docs\n<!-- a-team:customer -->
THREAD
run board demo comment customer 9 "$WORK/said"
same "exit" 0 "$STATUS"
same "posted" "Added the Customer lead to Help → Guide.

<!-- a-team:customer -->" "$(cat "$POSTED")"
: >"$POSTED"
run board demo comment customer 7 "$WORK/said"
failed "commenting on a task"
grep -q "customer only touches its own docs PR (#7 isn't it)" "$ERR" || fail "commenting on a task: '$(cat "$ERR")'"
same "posted" "" "$(cat "$POSTED")"

case_ "accepting the docs PR squash-merges it, deletes its branch and moves it to Done"
: >"$WRITES"
jq '.data.organization.projectV2.fields.nodes[0].options += [{id: "OPT_done", name: "Done"}]' "$META" >"$META.new" && mv "$META.new" "$META"
echo '{"head": {"sha": "deadbeefcafe", "ref": "docs/customer-lead", "repo": {"full_name": "mentaldesk/demo"}}}' >"$PULL"
run board demo accept you 9
same "exit" 0 "$STATUS"
same "said" "#9: merged docs PR #9" "$(cat "$OUT")"
grep -q 'PUT api -X PUT repos/mentaldesk/demo/pulls/9/merge -f merge_method=squash' "$WRITES" || fail "merge: '$(cat "$WRITES")'"
grep -q 'DELETE api -X DELETE repos/mentaldesk/demo/git/refs/heads/docs/customer-lead' "$WRITES" || fail "branch: '$(cat "$WRITES")'"
grep -q 'item=PVTI_9 .*option=OPT_done' "$WRITES" || fail "Done: '$(cat "$WRITES")'"

case_ "waiting shows the docs PR as the Customer lead's, with itself as the PR to merge"
gh_talk <<TALK
9 body ${TODAY}T08:14:00Z demo-app[bot] Docs\n<!-- a-team:customer -->
TALK
jq '.data.repository.x9 += {url: "https://github.com/mentaldesk/demo/pull/9", isDraft: false, mergeable: "MERGEABLE",
      baseRefName: "main"} | del(.data.repository.x9.closedByPullRequestsReferences)' "$TALK" >"$TALK.new" &&
  mv "$TALK.new" "$TALK"
waiting
same "exit" 0 "$STATUS"
same "docs PR" '{"number":9,"role":"customer","pr":9,"turn":"you","unready":""}' \
  "$(jq -c '.[] | select(.number == 9) | {number, role, pr, turn, unready}' "$OUT")"

case_ "conversation tells the Customer lead's words apart from the Lead's and the Dev's"
gh_talk <<TALK
9 body ${TODAY}T08:14:00Z demo-app[bot] Docs\n<!-- a-team:customer -->
9 comment ${TODAY}T08:20:00Z demo-app[bot] Covered #48\n<!-- a-team:customer -->
TALK
run board demo conversation 9
same "who" '[{"who":"customer","body":"Covered #48"}]' "$(jq -c 'map({who, body})' "$OUT")"

reviewable() {
  jq '.data.organization.projectV2.fields.nodes[0].options += [{id: "OPT_review", name: "In review"}, {id: "OPT_done", name: "Done"}]' \
    "$META" >"$META.new" && mv "$META.new" "$META"
}

case_ "the Lead may no longer add a Dependabot PR to In review"
gh_items <<'ITEMS'
In_progress 7 A task being built
ITEMS
reviewable
jq -n '{node_id: "PR_535", number: 535, state: "open", pull_request: {}, user: {login: "dependabot[bot]"},
        body: "Bumps MentalDesk.Tui from 0.3.8 to 0.3.9."}' >"$ISSUE"
jq -n '[{user: {login: "demo-app[bot]"}, body: "Nothing new to adopt.\n\n<!-- a-team:lead -->"}]' >"$THREAD"
run board demo add lead 535 "In review"
failed "after the Lead's comment"
grep -q "lead may not add items as 'In review'" "$ERR" || fail "refusal: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"

case_ "waiting shows a Dependabot PR In review as yours, with itself as the PR to merge"
gh_items <<'ITEMS'
In_review 535 Bump MentalDesk.Tui from 0.3.8 to 0.3.9
ITEMS
dependabot_pr 535
dependabot_talk() {
  gh_talk <<TALK
535 body ${TODAY}T08:00:00Z dependabot[bot] Bumps MentalDesk.Tui.
535 comment ${TODAY}T08:05:00Z demo-app[bot] Nothing new to adopt.\n<!-- a-team:lead -->
535 comment ${TODAY}T08:10:00Z reviewer Looks fine.
TALK
  jq '.data.repository.x535 += {url: "https://github.com/mentaldesk/demo/pull/535", isDraft: false, mergeable: "MERGEABLE",
        baseRefName: "main", headRefName: "dependabot/nuget/MentalDesk.Tui-0.3.9"}
      | del(.data.repository.x535.closedByPullRequestsReferences)' "$TALK" >"$TALK.new" && mv "$TALK.new" "$TALK"
}
dependabot_talk
waiting
same "exit" 0 "$STATUS"
same "Dependabot PR" '{"number":535,"dependency":true,"pr":535,"turn":"you","unready":"","reason":"awaiting your acceptance since 08:05"}' \
  "$(jq -c '.[] | select(.number == 535) | {number, dependency, pr, turn, unready, reason}' "$OUT")"

case_ "a Dependabot PR In review whose CI fails is the Dev's"
gh_runs <<'RUNS'
completed failure 2025-09-19T09:00:00Z build
RUNS
waiting
same "failing" '{"turn":"dev","trouble":"CI failing","unready":"CI failing"}' \
  "$(jq -c '.[] | select(.number == 535) | {turn, trouble, unready}' "$OUT")"

case_ "accepting a Dependabot PR squash-merges it and moves it to Done"
reviewable
: >"$WRITES"
echo '{"head": {"sha": "deadbeefcafe", "ref": "dependabot/nuget/MentalDesk.Tui-0.3.9", "repo": {"full_name": "mentaldesk/demo"}}}' >"$PULL"
run board demo accept you 535
same "exit" 0 "$STATUS"
same "said" "#535: merged PR #535" "$(cat "$OUT")"
grep -q 'PUT api -X PUT repos/mentaldesk/demo/pulls/535/merge -f merge_method=squash' "$WRITES" || fail "merge: '$(cat "$WRITES")'"
grep -q 'item=PVTI_535 .*option=OPT_done' "$WRITES" || fail "Done: '$(cat "$WRITES")'"

case_ "a Dependabot PR is accepted only from In review"
gh_items <<'ITEMS'
Idea 535 Bump MentalDesk.Tui from 0.3.8 to 0.3.9
ITEMS
dependabot_pr 535
run board demo accept you 535
failed "from Idea"
grep -q "only a PR In review can be accepted (#535 is in 'Idea')" "$ERR" || fail "from Idea: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"

# `bump_updates <n> [<update>]`: Dependabot's open PR #<n>, as `gh pr list` gives it, ready to merge unless a jq <update> changes it.
bump_updates() {
  jq -n --argjson n "$1" '[{id: "PR_\($n)", number: $n, title: "Bump MentalDesk.Tui from 0.3.8 to 0.3.9",
      author: {login: "app/dependabot"}, isDraft: false, mergeable: "MERGEABLE", headRefOid: "deadbeefcafe",
      comments: [{author: {login: "demo-app"}, body: "Nothing new to adopt.\\n\\n<!-- a-team:lead -->"}]}] | map('"${2:-.}"')' >"$UPDATES"
}
# `bump_board <status>`: Dependabot's PR #535 on the board, with an ordinary task In progress beside it.
bump_board() {
  gh_items <<ITEMS
In_progress 7 A task being built
$1 535 Bump MentalDesk.Tui from 0.3.8 to 0.3.9
ITEMS
  dependabot_pr 535
  reviewable
  claimable
  gh_pr
  jq -n '{number: 535, url: "https://github.com/mentaldesk/demo/pull/535", isDraft: false,
          headRefName: "dependabot/nuget/MentalDesk.Tui-0.3.9", mergeable: "MERGEABLE", mergeStateStatus: "CLEAN"}' >"$BUMP_VIEW"
  echo '{"head": {"sha": "deadbeefcafe", "ref": "dependabot/nuget/MentalDesk.Tui-0.3.9", "repo": {"full_name": "mentaldesk/demo"}}}' >"$PULL"
  gh_main_runs </dev/null
  gh_runs <<'RUNS'
completed success 2025-09-19T09:00:00Z build
RUNS
}
history_json() { A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board demo history "$1"; }

case_ "a green Dependabot PR the Lead has commented on merges with its branch deleted, and history says the dispatcher did it"
gh_items <<'ITEMS'
In_progress 7 A task being built
ITEMS
bump_updates 535
reviewable
echo '{"head": {"sha": "deadbeefcafe", "ref": "dependabot/nuget/MentalDesk.Tui-0.3.9", "repo": {"full_name": "mentaldesk/demo"}}}' >"$PULL"
gh_runs <<'RUNS'
completed success 2025-09-19T09:00:00Z build
RUNS
: >"$WRITES"
run board demo bumps
same "exit" 0 "$STATUS"
same "said" "PR #535 (Bump MentalDesk.Tui from 0.3.8 to 0.3.9): merged" "$(cat "$OUT")"
grep -q 'PUT api -X PUT repos/mentaldesk/demo/pulls/535/merge -f merge_method=squash -f sha=deadbeefcafe' "$WRITES" || fail "merge: '$(cat "$WRITES")'"
grep -q 'DELETE api -X DELETE repos/mentaldesk/demo/git/refs/heads/dependabot/nuget/MentalDesk.Tui-0.3.9' "$WRITES" || fail "branch: '$(cat "$WRITES")'"
grep -q 'ADD ' "$WRITES" && fail "merge: it came onto the board in '$(cat "$WRITES")'"
same "history" '{"who":"dispatcher","what":"merged · PR #535"}' "$(history_json 535 | jq -c '.events[0] | {who, what}')"
waiting
same "never in Work" '' "$(jq -c '.[] | select(.number == 535) | .number' "$OUT")"

case_ "the merge check refuses every other PR"
while IFS='|' read -r why update; do
  : >"$WRITES"
  bump_updates 535 "$update"
  run board demo bumps
  same "$why: exit" 0 "$STATUS"
  same "$why: said" "" "$(cat "$OUT")"
  same "$why: writes" "" "$(cat "$WRITES")"
done <<'CASES'
not Dependabot's|.author.login = "someone"
conflicting|.mergeable = "CONFLICTING"
not known to merge yet|.mergeable = "UNKNOWN"
a draft|.isDraft = true
no Lead comment|.comments = []
another role's comment|.comments[0].body = "Done.\n\n<!-- a-team:dev -->"
a stranger's Lead marker|.comments[0].author.login = "stranger"
CASES

case_ "the merge check refuses a PR that isn't green"
bump_updates 535
for runs in "in_progress - - build" "completed success 2025-09-19T09:00:00Z build
in_progress - - lint" ""; do
  : >"$WRITES"
  gh_runs <<<"$runs"
  run board demo bumps
  same "exit" 0 "$STATUS"
  same "writes" "" "$(cat "$WRITES")"
done

case_ "a merge GitHub refuses fails the pass, and says which PR"
gh_runs <<'RUNS'
completed success 2025-09-19T09:00:00Z build
RUNS
touch "$BIN/merge-fails"
run board demo bumps
failed "merge refused"
grep -q "PR #535: can't merge PR #535" "$ERR" || fail "refused: '$(cat "$ERR")'"
rm -f "$BIN/merge-fails"

case_ "a PR pushed to since its checks were read isn't merged or counted, and the next pass retries"
rm -rf "$A_TEAM_STATE/demo/bump-refusals"
touch "$BIN/merge-moved"
: >"$WRITES"
run board demo bumps
same "exit" 0 "$STATUS"
same "said" "" "$(cat "$OUT")"
same "writes" "" "$(cat "$WRITES")"
[ ! -e "$A_TEAM_STATE/demo/bump-refusals/535" ] || fail "counted"
rm -f "$BIN/merge-moved"

case_ "a green PR GitHub keeps refusing is put to the stakeholder once, and merges once the cause is gone"
gh_items <<'ITEMS'
In_progress 7 A task being built
ITEMS
claimable
bump_updates 535
rm -f "$POSTED"
touch "$BIN/merge-fails"
: >"$WRITES"
run board demo bumps
failed "first"
run board demo bumps
failed "second"
[ ! -e "$POSTED" ] || fail "asked too soon: '$(cat "$POSTED")'"
same "still off the board" "" "$(cat "$WRITES")"
run board demo bumps
failed "third"
same "said" "PR #535: GitHub keeps refusing to merge it, asked the stakeholder" "$(cat "$OUT")"
grep -q "GitHub won't merge it: can't merge PR #535 (gh: Pull Request is not mergeable (HTTP 405))" "$POSTED" || fail "reason: '$(cat "$POSTED")'"
same "marker" "<!-- a-team:dev -->" "$(tail -n 1 "$POSTED")"
grep -q "ADD .*content=PR_535" "$WRITES" || fail "add: '$(cat "$WRITES")'"
grep -q 'item=PVTI_new .*option=OPT_ready' "$WRITES" || fail "Ready: '$(cat "$WRITES")'"
same "held by" '"dev"' "$(marks 535 | jq -c .held.by)"
same "history" '{"who":"dispatcher","what":"held"}' "$(history_json 535 | jq -c '.events[0] | {who, what}')"
rm -f "$POSTED"
KEEP_BOARD=1 run board demo bumps
failed "fourth"
[ ! -e "$POSTED" ] || fail "asked twice: '$(cat "$POSTED")'"
[ ! -e "$A_TEAM_STATE/demo/bump-refusals/535" ] || fail "counted while held"
rm -f "$BIN/merge-fails"
: >"$WRITES"
KEEP_BOARD=1 run board demo bumps
same "exit" 0 "$STATUS"
same "merged" "PR #535 (Bump MentalDesk.Tui from 0.3.8 to 0.3.9): merged" "$(cat "$OUT")"
grep -q 'item=PVTI_new .*option=OPT_done' "$WRITES" || fail "Done: '$(cat "$WRITES")'"

case_ "refusals are counted afresh once a merge succeeds"
rm -rf "$A_TEAM_STATE/demo/bump-refusals"
gh_items <<'ITEMS'
In_progress 7 A task being built
ITEMS
bump_updates 535
touch "$BIN/merge-fails"
run board demo bumps
rm -f "$BIN/merge-fails"
run board demo bumps
same "exit" 0 "$STATUS"
[ ! -e "$A_TEAM_STATE/demo/bump-refusals/535" ] || fail "kept: '$(cat "$A_TEAM_STATE/demo/bump-refusals/535")'"

case_ "a PR on the board merges from wherever it stands, to Done"
bump_board In_progress
bump_updates 535
: >"$WRITES"
run board demo bumps
same "exit" 0 "$STATUS"
grep -q 'item=PVTI_535 .*option=OPT_done' "$WRITES" || fail "Done: '$(cat "$WRITES")'"

case_ "a red Dependabot PR goes on the board with the Dev, In progress"
gh_items <<'ITEMS'
In_progress 7 A task being built
ITEMS
claimable
bump_updates 535
gh_runs <<'RUNS'
completed failure 2025-09-19T09:00:00Z build
RUNS
gh_main_runs <<'RUNS'
completed success 2025-09-19T08:00:00Z build
RUNS
: >"$WRITES"
run board demo bumps
same "exit" 0 "$STATUS"
same "said" "PR #535 (Bump MentalDesk.Tui from 0.3.8 to 0.3.9): CI failing, with the Dev" "$(cat "$OUT")"
grep -q "ADD .*content=PR_535" "$WRITES" || fail "add: '$(cat "$WRITES")'"
grep -q 'merge' "$WRITES" && fail "merged a red PR: '$(cat "$WRITES")'"
same "the Dev's" true "$(marks 535 | jq -c .dev)"
same "history" '{"who":"dispatcher","what":"added as In progress"}' "$(history_json 535 | jq -c '.events[0] | {who, what}')"

case_ "a red PR with no Lead comment still goes to the Dev"
gh_items <<'ITEMS'
In_progress 7 A task being built
ITEMS
claimable
bump_updates 535 '.comments = []'
gh_runs <<'RUNS'
completed failure 2025-09-19T09:00:00Z build
RUNS
: >"$WRITES"
run board demo bumps
same "exit" 0 "$STATUS"
grep -q "ADD .*content=PR_535" "$WRITES" || fail "add: '$(cat "$WRITES")'"

case_ "a red PR failing only what main fails waits for the fix"
gh_main_runs <<'RUNS'
completed failure 2025-09-19T08:00:00Z build
RUNS
bump_updates 535
: >"$WRITES"
run board demo bumps
same "exit" 0 "$STATUS"
same "said" "" "$(cat "$OUT")"
same "writes" "" "$(cat "$WRITES")"

case_ "a red PR the Dev already has, or has held, doesn't move"
for status in In_progress Ready; do
  bump_board "$status"
  gh_runs <<'RUNS'
completed failure 2025-09-19T09:00:00Z build
RUNS
  bump_updates 535
  run board demo list
  sqlite3 "$A_TEAM_STATE/history.db" "UPDATE board SET marks = '{\"dev\":true,\"held\":{\"at\":\"2025-09-19T09:00:00Z\",\"by\":\"dev\"}}' WHERE team = 'demo' AND item = 535;"
  [ "$status" = Ready ] || sqlite3 "$A_TEAM_STATE/history.db" "UPDATE board SET marks = '{\"dev\":true}' WHERE team = 'demo' AND item = 535;"
  : >"$WRITES"
  KEEP_BOARD=1 run board demo bumps
  same "$status: exit" 0 "$STATUS"
  same "$status: writes" "" "$(cat "$WRITES")"
done

case_ "a red PR a stakeholder released goes back to the Dev"
sqlite3 "$A_TEAM_STATE/history.db" "UPDATE board SET marks = '{\"dev\":true}' WHERE team = 'demo' AND item = 535;"
: >"$WRITES"
KEEP_BOARD=1 run board demo bumps
same "exit" 0 "$STATUS"
grep -q 'item=PVTI_535 .*option=OPT_progress' "$WRITES" || fail "back: '$(cat "$WRITES")'"
same "history" '{"who":"dispatcher","what":"Ready → In progress"}' "$(history_json 535 | jq -c '.events[0] | {who, what}')"

case_ "a red PR with the Dev starts a Dev run on it, ahead of an ordinary Ready task"
cp "$TEAM" "$TEAM.was"
jq '.wip.worktrees = 5' "$TEAM.was" >"$TEAM"
gh_items <<'ITEMS'
Ready 13 An ordinary task
In_progress 535 Bump MentalDesk.Tui from 0.3.8 to 0.3.9
ITEMS
dependabot_pr 535
edit_item 535 '.labels.nodes = [{name: "a-team:dev"}]'
gh_pr
jq -n '{number: 535, url: "https://github.com/mentaldesk/demo/pull/535", isDraft: false,
        headRefName: "dependabot/nuget/MentalDesk.Tui-0.3.9", mergeable: "MERGEABLE", mergeStateStatus: "CLEAN"}' >"$BUMP_VIEW"
gh_runs <<'RUNS'
completed failure 2025-09-19T09:00:00Z build
RUNS
gh_main_runs </dev/null
run board demo triggers dev
same "exit" 0 "$STATUS"
same "tasks" '[{"number":535,"title":"Bump MentalDesk.Tui from 0.3.8 to 0.3.9","reasons":["CI failed on Dependabot'"'"'s PR #535 at deadbee"]}]' "$(jq -c .tasks "$OUT")"
same "ready behind it" 13 "$(jq -c .ready "$OUT")"
run board demo pr 535
same "its own PR" '{"number":535,"review":"off"}' "$(jq -c '{number, review}' "$OUT")"

case_ "a Ready fix comes before a red PR"
edit_item 13 '.labels.nodes = [{name: "a-team:fix"}]'
run board demo triggers dev
same "exit" 0 "$STATUS"
same "tasks" '[]' "$(jq -c .tasks "$OUT")"
same "fix" 13 "$(jq -c .ready "$OUT")"
edit_item 13 '.labels.nodes = []'

case_ "a PR failing only what main fails starts no Dev run"
gh_main_runs <<'RUNS'
completed failure 2025-09-19T08:00:00Z build
RUNS
run board demo triggers dev
same "exit" 0 "$STATUS"
same "tasks" '[]' "$(jq -c .tasks "$OUT")"
gh_main_runs </dev/null

case_ "the Dev never moves a Dependabot PR to In review, and no Reviewer reads it"
reviewable
claimable
run board demo move dev 535 "In review"
failed "to In review"
grep -q "a Dependabot PR merges itself once it's green: leave #535 In progress" "$ERR" || fail "refusal: '$(cat "$ERR")'"
run board demo triggers reviewer
same "exit" 0 "$STATUS"
same "reviewer" '[]' "$(jq -c .tasks "$OUT")"

case_ "a PR with the Dev is the Dev's in waiting, however it's going"
bump_talk() {
  gh_talk <<TALK
535 body ${TODAY}T08:00:00Z dependabot[bot] Bumps MentalDesk.Tui.
$1
TALK
  jq '.data.repository.x535 += {url: "https://github.com/mentaldesk/demo/pull/535", isDraft: false, mergeable: "MERGEABLE",
        baseRefName: "main", headRefName: "dependabot/nuget/MentalDesk.Tui-0.3.9"}
      | del(.data.repository.x535.closedByPullRequestsReferences)' "$TALK" >"$TALK.new" && mv "$TALK.new" "$TALK"
}
bump_talk "535 comment ${TODAY}T08:05:00Z demo-app[bot] Nothing new to adopt.\n<!-- a-team:lead -->"
gh_runs <<'RUNS'
completed failure 2025-09-19T09:00:00Z build
RUNS
waiting
same "exit" 0 "$STATUS"
same "red" '{"status":"In progress","dependency":true,"pr":535,"turn":"dev","trouble":"CI failing","unready":"CI failing"}' \
  "$(jq -c '.[] | select(.number == 535) | {status, dependency, pr, turn, trouble, unready}' "$OUT")"
gh_runs <<'RUNS'
completed success 2025-09-19T09:00:00Z build
RUNS
waiting
same "green" '{"turn":"dev","unready":"green, about to merge"}' "$(jq -c '.[] | select(.number == 535) | {turn, unready}' "$OUT")"

case_ "the Dev holds a PR it can't make build, and hands it back"
gh_items <<'ITEMS'
In_progress 535 Bump MentalDesk.Tui from 0.3.8 to 0.3.9
ITEMS
dependabot_pr 535
edit_item 535 '.labels.nodes = [{name: "a-team:dev"}]'
reviewable
claimable
echo "Can't build: it needs an AgentSpec." >"$BIN/why"
run board demo hold dev 535 "$BIN/why"
same "hold" 0 "$STATUS"
KEEP_BOARD=1 run board demo move dev 535 Ready
same "move" 0 "$STATUS"
same "held by" '"dev"' "$(marks 535 | jq -c .held.by)"

case_ "a PR the Dev can't make build is the stakeholder's, as a question, with no more Dev runs until they reply"
gh_items <<'ITEMS'
Ready 535 Bump MentalDesk.Tui from 0.3.8 to 0.3.9
ITEMS
dependabot_pr 535
edit_item 535 '.labels.nodes = [{name: "a-team:dev"}, {name: "blocked"}]'
bump_talk "535 comment ${TODAY}T08:23:06Z demo-app[bot] Can't build: it needs an AgentSpec.\n<!-- a-team:dev -->
535 labeled ${TODAY}T08:23:14Z demo-app[bot] blocked"
gh_runs <<'RUNS'
completed failure 2025-09-19T09:00:00Z build
RUNS
waiting
same "exit" 0 "$STATUS"
same "held" "{\"status\":\"Ready\",\"dependency\":true,\"turn\":\"you\",\"heldBy\":\"dev\",\"question\":\"Can't build: it needs an AgentSpec.\",\"opens\":true}" \
  "$(jq -c '.[] | select(.number == 535) | {status, dependency, turn, heldBy, question, opens: (.prUrl == .url)}' "$OUT")"
run board demo triggers dev
same "exit" 0 "$STATUS"
same "no run" '[]' "$(jq -c .tasks "$OUT")"

case_ "their reply puts it back with the Dev"
bump_talk "535 comment ${TODAY}T08:23:06Z demo-app[bot] Can't build: it needs an AgentSpec.\n<!-- a-team:dev -->
535 labeled ${TODAY}T08:23:14Z demo-app[bot] blocked
535 comment ${TODAY}T10:50:00Z reviewer Wait for the library.\n"
run board demo triggers dev
same "exit" 0 "$STATUS"
same "a run" "[535]" "$(jq -c '[.tasks[].number]' "$OUT")"
same "reason" "[\"stakeholder answered the Dev's question on #535 (${TODAY}T10:50:00Z)\"]" "$(jq -c '.tasks[0].reasons' "$OUT")"
waiting
same "turn" '"dev"' "$(jq -c '.[] | select(.number == 535) | .turn' "$OUT")"

case_ "closing it takes it off the board"
edit_item 535 '.state = "CLOSED"'
KEEP_BOARD=1 run board demo list
KEEP_BOARD=1 run board demo waiting
same "exit" 0 "$STATUS"
same "gone" '[]' "$(jq -c 'map(select(.number == 535))' "$OUT")"
mv "$TEAM.was" "$TEAM"

# Another team's Dev opened this PR for a task of its own; `adopt` puts it on the board here.
upstream_pr() {
  jq -n --arg body "$1" '[{id: "PR_86", number: 86, isDraft: false,
    body: "Makes MenuLetters.Pick public.\n\nNeeded by \($body)\n\n<!-- a-team:dev -->"}]' >"$UPSTREAM"
}
upstream_edit() { jq "$1" "$UPSTREAM" >"$UPSTREAM.new" && mv "$UPSTREAM.new" "$UPSTREAM"; }

case_ "adopt puts another repo's Dev PR In review once it's ready"
reviewable
upstream_pr "mentaldesk/a-team#538"
run board demo adopt
same "exit" 0 "$STATUS"
same "said" "#86: added as In review" "$(cat "$OUT")"
grep -q 'ADD .*content=PR_86' "$WRITES" || fail "add: '$(cat "$WRITES")'"
grep -q 'item=PVTI_new .*option=OPT_review' "$WRITES" || fail "In review: '$(cat "$WRITES")'"
same "one list of the App's PRs" "pr list -R mentaldesk/demo --author app/demo-app --state open --json id,number,isDraft,body" \
  "$(cat "$UPSTREAM_CALLS")"

case_ "adopt puts another repo's Dev PR In progress while it's a draft"
reviewable
claimable
upstream_pr "mentaldesk/a-team#538"
upstream_edit '.[0].isDraft = true'
run board demo adopt
same "exit" 0 "$STATUS"
same "said" "#86: added as In progress" "$(cat "$OUT")"
grep -q 'ADD .*content=PR_86' "$WRITES" || fail "add: '$(cat "$WRITES")'"
grep -q 'item=PVTI_new .*option=OPT_progress' "$WRITES" || fail "In progress: '$(cat "$WRITES")'"

case_ "adopt leaves a stakeholder's draft, a PR already on the board, and one for a task in this repo"
for edit in '.[0].body |= sub("mentaldesk/a-team#538"; "mentaldesk/demo#12")' '.[0].isDraft = true | .[0].body |= sub("<!-- a-team:dev -->"; "")' \
    '.[0].body |= sub("<!-- a-team:dev -->"; "")' '.[0].isDraft = true | .[0].body |= sub("Needed by mentaldesk/a-team#538"; "")'; do
  upstream_pr "mentaldesk/a-team#538"
  upstream_edit "$edit"
  : >"$WRITES"
  run board demo adopt
  same "exit ($edit)" 0 "$STATUS"
  same "writes ($edit)" "" "$(cat "$WRITES")"
done
upstream_pr "mentaldesk/a-team#538"
jq '.data.organization.projectV2.items.nodes += [{id: "PVTI_86", fieldValueByName: {name: "In review"},
    content: {__typename: "PullRequest", id: "PR_86", number: 86, title: "Its PR", url: "https://github.com/mentaldesk/demo/pull/86",
              repository: {nameWithOwner: "mentaldesk/demo"}, labels: {nodes: []}}}]' "$ITEMS" >"$ITEMS.new" && mv "$ITEMS.new" "$ITEMS"
: >"$WRITES"
run board demo adopt
same "exit (on the board)" 0 "$STATUS"
same "writes (on the board)" "" "$(cat "$WRITES")"

case_ "adopt moves an adopted draft to In review once it's out of draft, and leaves it while it's still one"
jq '(.data.organization.projectV2.items.nodes[] | select(.id == "PVTI_86") | .fieldValueByName.name) = "In progress"' \
  "$ITEMS" >"$ITEMS.new" && mv "$ITEMS.new" "$ITEMS"
: >"$WRITES"
run board demo adopt
same "exit" 0 "$STATUS"
same "said" "#86: In progress → In review" "$(cat "$OUT")"
grep -q 'item=PVTI_86 .*option=OPT_review' "$WRITES" || fail "In review: '$(cat "$WRITES")'"
upstream_edit '.[0].isDraft = true'
: >"$WRITES"
run board demo adopt
same "exit" 0 "$STATUS"
same "draft writes" "" "$(cat "$WRITES")"

# The App's open PRs `update` reads, from lines of "<number> <draft> <mergeStateStatus> <head>".
app_prs() {
  jq -R -s 'split("\n") | map(select(length > 0) | split(" ")
    | {number: (.[0] | tonumber), isDraft: (.[1] == "true"), mergeStateStatus: .[2], headRefOid: .[3], baseRefName: "main"})' >"$UPSTREAM"
}

case_ "update merges main into each of the App's ready PRs that's behind it, and nothing else"
rm -f "$A_TEAM_STATE/demo/updated"
app_prs <<'PRS'
912 false BEHIND aaa1111
913 true BEHIND bbb2222
914 false CLEAN ccc3333
915 false BLOCKED ddd4444
916 false DIRTY eee5555
PRS
: >"$WRITES"
run board demo update
same "exit" 0 "$STATUS"
same "said" "PR #912: merged main into it" "$(cat "$OUT")"
same "writes" "PUT api -X PUT repos/mentaldesk/demo/pulls/912/update-branch -f expected_head_sha=aaa1111" "$(cat "$WRITES")"
same "only the App's PRs" "pr list -R mentaldesk/demo --author app/demo-app --state open --json number,isDraft,mergeStateStatus,headRefOid,baseRefName" \
  "$(tail -n 1 "$UPSTREAM_CALLS")"

case_ "update doesn't merge main in again while GitHub still reads the PR it updated as behind"
: >"$WRITES"
run board demo update
same "exit" 0 "$STATUS"
same "said" "" "$(cat "$OUT")"
same "writes" "" "$(cat "$WRITES")"

case_ "update merges main in again once the PR has a new head and has fallen behind again"
app_prs <<'PRS'
912 false BEHIND fff6666
PRS
run board demo update
same "said" "PR #912: merged main into it" "$(cat "$OUT")"
grep -q 'pulls/912/update-branch -f expected_head_sha=fff6666' "$WRITES" || fail "again: '$(cat "$WRITES")'"

case_ "update says which PR it couldn't bring up to date, and tries it again on the next pass"
rm -f "$A_TEAM_STATE/demo/updated"
app_prs <<'PRS'
912 false BEHIND aaa1111
PRS
touch "$BIN/update-fails"
run board demo update
same "exit" 0 "$STATUS"
same "said" "PR #912: couldn't merge main into it" "$(cat "$OUT")"
grep -q "merge conflict between base and head" "$ERR" || fail "why: '$(cat "$ERR")'"
rm "$BIN/update-fails"
: >"$WRITES"
run board demo update
same "said" "PR #912: merged main into it" "$(cat "$OUT")"

case_ "--dry-run update says what it would merge and merges nothing"
rm -f "$A_TEAM_STATE/demo/updated"
: >"$WRITES"
run board --dry-run demo update
same "exit" 0 "$STATUS"
same "said" "(dry run) PR #912: merged main into it" "$(cat "$OUT")"
grep -q "dry-run: would merge main into PR #912" "$ERR" || fail "dry run: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"
[ ! -e "$A_TEAM_STATE/demo/updated" ] || fail "dry run remembered an update"

case_ "waiting shows another repo's Dev PR In review as yours, with itself as the PR to merge"
gh_items <<'ITEMS'
In_review 86 Let an app without AppMenu pick menu letters
ITEMS
edit_item 86 '.__typename = "PullRequest" | .labels.nodes = [] | del(.issueDependenciesSummary, .issueFieldValues)'
gh_talk <<TALK
86 body ${TODAY}T08:00:00Z demo-app[bot] Needed by mentaldesk/a-team#538\n<!-- a-team:dev -->
TALK
jq '.data.repository.x86 += {url: "https://github.com/mentaldesk/demo/pull/86", isDraft: false, mergeable: "MERGEABLE",
      baseRefName: "main", headRefName: "feat/public-menu-letters"}
    | del(.data.repository.x86.closedByPullRequestsReferences)' "$TALK" >"$TALK.new" && mv "$TALK.new" "$TALK"
waiting
same "exit" 0 "$STATUS"
same "upstream PR" '{"number":86,"pr":86,"branch":"feat/public-menu-letters","turn":"you","unready":""}' \
  "$(jq -c '.[] | select(.number == 86) | {number, pr, branch, turn, unready}' "$OUT")"

case_ "accepting another repo's Dev PR squash-merges it and moves it to Done"
reviewable
: >"$WRITES"
echo '{"head": {"sha": "deadbeefcafe", "ref": "feat/public-menu-letters", "repo": {"full_name": "mentaldesk/demo"}}}' >"$PULL"
run board demo accept you 86
same "exit" 0 "$STATUS"
same "said" "#86: merged PR #86" "$(cat "$OUT")"
grep -q 'PUT api -X PUT repos/mentaldesk/demo/pulls/86/merge -f merge_method=squash' "$WRITES" || fail "merge: '$(cat "$WRITES")'"
grep -q 'item=PVTI_86 .*option=OPT_done' "$WRITES" || fail "Done: '$(cat "$WRITES")'"

case_ "a stakeholder's comment on the docs PR triggers the Customer lead, and only it"
gh_items <<'ITEMS'
In_review 9 Docs: what's changed since 28 Sep
ITEMS
docs_pr 9
gh_thread pull </dev/null
gh_recent <<RECENT
9 ${TODAY}T08:30:00Z reviewer 0 Move this under Teams.
RECENT
run board demo triggers customer
same "exit" 0 "$STATUS"
same "reasons" "[\"stakeholder feedback on docs PR #9 (${TODAY}T08:30:00Z)\"]" "$(jq -c .reasons "$OUT")"
same "items" "[9]" "$(jq -c .items "$OUT")"
same "card" 9 "$(jq -c .card "$OUT")"
for role in lead dev; do
  run board demo triggers "$role"
  same "$role exit" 0 "$STATUS"
  jq -e '.reasons | any(contains("#9"))' "$OUT" >/dev/null && fail "$role: '$(jq -c .reasons "$OUT")'"
done

case_ "an answered comment on the docs PR, or someone else's, starts no run"
gh_recent <<RECENT
9 ${TODAY}T08:30:00Z reviewer 1 Move this under Teams.
9 ${TODAY}T08:40:00Z passer-by 0 Please add a section on my plugin.
RECENT
run board demo triggers customer
same "exit" 0 "$STATUS"
same "reasons" '[]' "$(jq -c .reasons "$OUT")"
# The Customer lead's docs proposal: while the config's `docs` page isn't in the repo, a draft PR adding it.
proposal_pr() {
  jq -n --argjson n "$1" --arg state "${2:-open}" --argjson draft "${3:-true}" \
    '{node_id: "PR_\($n)", number: $n, state: $state, draft: $draft,
      body: "Where the docs live, and their outline.\n\n<!-- a-team:customer -->"}' >"$PULL"
  jq -n --argjson n "$1" '{node_id: "PR_\($n)", number: $n, state: "open", pull_request: {},
      body: "<!-- a-team:customer -->"}' >"$ISSUE"
}
touch "$WORK/no-docs"

case_ "with no docs page, the Customer lead's one reason is to propose them, and done pitches and the audit wait"
gh_items <<'ITEMS'
Done 16 A pitch just accepted
ITEMS
accepted 16
run board demo triggers customer
same "exit" 0 "$STATUS"
same "reasons" "[\"no user docs yet: propose where they'll live\"]" "$(jq -c .reasons "$OUT")"
[ -e "$AUDITED" ] && fail "the audit isn't due as soon as the docs page appears"
jq '.data.organization.projectV2.fields.nodes[0].options += [{id: "OPT_review", name: "In review"}]' "$META" >"$META.new" &&
  mv "$META.new" "$META"
jq -n '{node_id: "PR_15", number: 15, state: "open", pull_request: {}, body: "<!-- a-team:customer -->"}' >"$ISSUE"
run board demo add customer 15 "In review"
failed "a docs PR with no docs page"
grep -q "docs/index.md isn't in mentaldesk/demo yet: propose the docs before writing any" "$ERR" ||
  fail "docs PR with no docs page: '$(cat "$ERR")'"

case_ "the Customer lead puts its own draft docs proposal in Pitched, marked as its"
: >"$WRITES"
gh_items <<'ITEMS'
Pitched 12 Docs proposal: where the user docs live
ITEMS
docs_pr 12
jq '(.data.organization.projectV2.items.nodes[] | select(.content.number == 12)) |= (.fieldValueByName = null)
    | (.data.organization.projectV2.items.nodes[].content.labels.nodes) = []' "$ITEMS" >"$ITEMS.new" &&
  mv "$ITEMS.new" "$ITEMS"
proposal_pr 12
run board demo add customer 12 Pitched
same "exit" 0 "$STATUS"
same "said" "#12: added as Pitched" "$(cat "$OUT")"
same "the Customer lead's" true "$(marks 12 | jq -c .customer)"
grep -q 'labels\[\]=pitch' "$WRITES" && fail "propose: labelled a pitch, which is the Lead's"
grep -q 'item=PVTI_12 .*option=OPT_pitched' "$WRITES" || fail "propose: no move in '$(cat "$WRITES")'"

case_ "it proposes only with its own open draft PR, one at a time, and only while the docs page is missing"
: >"$WRITES"
proposal_pr 12 open false
run board demo add customer 12 Pitched
failed "a PR that isn't a draft"
grep -q "customer may only put its own open draft docs proposal in Pitched (#12 isn't one)" "$ERR" ||
  fail "not a draft: '$(cat "$ERR")'"
proposal_pr 12
jq '.body = "Closes #7\n\n<!-- a-team:dev -->"' "$PULL" >"$PULL.new" && mv "$PULL.new" "$PULL"
run board demo add customer 12 Pitched
failed "someone else's PR"
gh_items <<'ITEMS'
Pitched 12 Docs proposal: where the user docs live
ITEMS
docs_pr 12
proposal_pr 13
run board demo add customer 13 Pitched
failed "a second proposal"
grep -q "customer's #12 is still open: no docs proposal beside it" "$ERR" || fail "a second proposal: '$(cat "$ERR")'"
gh_items </dev/null
proposal_pr 13
rm "$WORK/no-docs"
run board demo add customer 13 Pitched
failed "a proposal with docs"
grep -q "docs/index.md is already in mentaldesk/demo: no docs proposal needed" "$ERR" ||
  fail "a proposal with docs: '$(cat "$ERR")'"
touch "$WORK/no-docs"
same "writes" "" "$(cat "$WRITES")"

case_ "while the proposal is open, nothing but feedback on it starts a run, and no docs PR goes up without the page"
gh_items <<'ITEMS'
Pitched 12 Docs proposal: where the user docs live
Done 16 A pitch just accepted
ITEMS
docs_pr 12
accepted 16
proposal_pr 12
run board demo triggers customer
same "exit" 0 "$STATUS"
same "reasons" '[]' "$(jq -c .reasons "$OUT")"
jq -n '{node_id: "PR_15", number: 15, state: "open", pull_request: {}, body: "<!-- a-team:customer -->"}' >"$ISSUE"
run board demo add customer 15 "In review"
failed "a docs PR before the proposal merges"
grep -q "docs/index.md isn't in mentaldesk/demo yet: propose the docs before writing any" "$ERR" ||
  fail "docs PR too soon: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"

case_ "a docs PR goes into In review beside a leftover proposal once the docs page is in the repo"
rm "$WORK/no-docs"
jq '.data.organization.projectV2.fields.nodes[0].options += [{id: "OPT_review", name: "In review"}]' "$META" >"$META.new" &&
  mv "$META.new" "$META"
jq -n '{node_id: "PR_15", number: 15, state: "open", draft: false, body: "<!-- a-team:customer -->"}' >"$PULL"
run board demo add customer 15 "In review"
same "exit" 0 "$STATUS"
grep -q 'option=OPT_review' "$WRITES" || fail "beside a proposal: no move in '$(cat "$WRITES")'"
grep -q 'PVTI_12' "$WRITES" && fail "beside a proposal: the proposal moved in '$(cat "$WRITES")'"
touch "$WORK/no-docs"
: >"$WRITES"

case_ "a stakeholder's comment on the proposal triggers the Customer lead, not the Lead"
gh_recent <<RECENT
12 ${TODAY}T09:00:00Z reviewer 0 Put them under docs/ instead.
RECENT
run board demo triggers customer
same "reasons" "[\"stakeholder feedback on docs proposal #12 (${TODAY}T09:00:00Z)\"]" "$(jq -c .reasons "$OUT")"
same "items" "[12]" "$(jq -c .items "$OUT")"
for role in lead dev; do
  run board demo triggers "$role"
  same "$role exit" 0 "$STATUS"
  jq -e '.reasons | any(contains("#12"))' "$OUT" >/dev/null && fail "$role: '$(jq -c .reasons "$OUT")'"
done
gh_recent </dev/null

case_ "beside a leftover proposal listed first, feedback on the docs PR still triggers the Customer lead"
rm "$WORK/no-docs"
gh_items <<'ITEMS'
Pitched 12 Docs proposal: where the user docs live
In_review 15 Docs: what's changed since 28 Sep
ITEMS
docs_pr 12
docs_pr 15
gh_thread pull </dev/null
gh_recent <<RECENT
15 ${TODAY}T09:10:00Z reviewer 0 Move this under Teams.
RECENT
run board demo triggers customer
same "exit" 0 "$STATUS"
jq -e --arg r "stakeholder feedback on docs PR #15 (${TODAY}T09:10:00Z)" '.reasons | index($r)' "$OUT" >/dev/null ||
  fail "feedback on the docs PR: '$(jq -c .reasons "$OUT")'"
same "card" 15 "$(jq -c .card "$OUT")"
gh_recent </dev/null
touch "$WORK/no-docs"

case_ "the Customer lead can't merge, approve or move its proposal"
for args in "accept customer 12" "approve customer 12" "move customer 12 Approved" "move customer 12 Done"; do
  # shellcheck disable=SC2086 # one argument per word
  run board demo $args
  failed "customer: $args"
done

case_ "accepting the docs proposal marks the draft ready, squash-merges it and moves it to Done"
: >"$WRITES"
echo '{"head": {"sha": "deadbeefcafe", "ref": "docs/customer-lead-proposal", "repo": {"full_name": "mentaldesk/demo"}}, "draft": true}' >"$PULL"
run board demo accept you 12
same "exit" 0 "$STATUS"
same "said" "#12: merged document pitch #12" "$(cat "$OUT")"
same "first write" "READY pr ready 12 -R mentaldesk/demo" "$(head -1 "$WRITES")"
grep -q 'PUT api -X PUT repos/mentaldesk/demo/pulls/12/merge -f merge_method=squash' "$WRITES" || fail "merge: '$(cat "$WRITES")'"
grep -q 'item=PVTI_12 .*option=OPT_done' "$WRITES" || fail "Done: '$(cat "$WRITES")'"

case_ "once the docs page is there, the audit writes the docs it outlines, and done pitches come back"
rm "$WORK/no-docs"
gh_items <<'ITEMS'
Done 12 Docs proposal: where the user docs live
Done 16 A pitch just accepted
ITEMS
docs_pr 12
accepted 16
run board demo triggers customer
same "reasons" '["pitch #16 is done: check the docs cover what it shipped","weekly docs audit: check the docs as a whole"]' \
  "$(jq -c .reasons "$OUT")"
jq -n '{node_id: "PR_15", number: 15, state: "open", pull_request: {}, body: "<!-- a-team:customer -->"}' >"$ISSUE"
jq '.data.organization.projectV2.fields.nodes[0].options += [{id: "OPT_review", name: "In review"}]' "$META" >"$META.new" &&
  mv "$META.new" "$META"
run board demo add customer 15 "In review"
same "docs PR" 0 "$STATUS"
run board demo covered customer 16
run board demo audited customer
run board demo triggers customer
same "written" '[]' "$(jq -c .reasons "$OUT")"

case_ "a Customer lead with no docs page configured starts no run"
jq 'del(.docs)' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
rm -f "$AUDITED"
run board demo triggers customer
same "exit" 0 "$STATUS"
same "reasons" '[]' "$(jq -c .reasons "$OUT")"
jq '.docs = "docs/index.md"' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"

case_ "while GitHub doesn't answer for the docs page, the Customer lead gets no run, its audit keeps its date, and the others run"
gh_items <<'ITEMS'
Approved 20 A pitch to break down
Ready 21 A task
ITEMS
jq '.wip.worktrees = 2' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
echo 12345 >"$AUDITED"
touch "$WORK/docs-down"
run board demo triggers customer
same "exit" 0 "$STATUS"
same "reasons" '[]' "$(jq -c .reasons "$OUT")"
same "the audit's date" 12345 "$(cat "$AUDITED")"
run board demo triggers lead
same "the Lead" true "$(jq '.reasons | index("#20 was approved: break it down") != null' "$OUT")"
run board demo triggers dev
same "the Dev" 21 "$(jq .ready "$OUT")"

case_ "while GitHub doesn't answer for the docs page, the Customer lead's add says so and to try again"
: >"$WRITES"
proposal_pr 15
run board demo add customer 15 Pitched
failed "a docs proposal"
grep -q "GitHub didn't answer for docs/index.md: try again" "$ERR" || fail "docs proposal: '$(cat "$ERR")'"
run board demo add customer 15 "In review"
failed "a docs PR"
grep -q "GitHub didn't answer for docs/index.md: try again" "$ERR" || fail "docs PR: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"
rm "$WORK/docs-down"

case_ "when GitHub does answer, a 404 clears the audit and starts the proposal, and a docs page keeps the audit"
run board demo triggers customer
same "the page there" '["weekly docs audit: check the docs as a whole"]' "$(jq -c .reasons "$OUT")"
same "the audit's date" 12345 "$(cat "$AUDITED")"
touch "$WORK/no-docs"
run board demo triggers customer
same "a 404" "[\"no user docs yet: propose where they'll live\"]" "$(jq -c .reasons "$OUT")"
[ -e "$AUDITED" ] && fail "a 404 keeps the audit's date"
rm "$WORK/no-docs"
echo "$(date +%s)" >"$AUDITED"

# `customer_issue <n> <body>`: #<n> is an open issue with <body>.
customer_issue() { gh_child "$1" - - "$2" && jq '.state = "open"' "$ISSUE" >"$ISSUE.new" && mv "$ISSUE.new" "$ISSUE"; }

case_ "the Customer lead files a feature nobody can find as an Idea, marked as a discovery"
jq '.wip = {pitched: 2, exploring: 4, ideas: 2}' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
gh_items <<'ITEMS'
Idea 41 I can't find how to pause one role
ITEMS
jq '.data.organization.projectV2.fields.nodes[0].options += [{id: "OPT_idea", name: "Idea"}]' "$META" >"$META.new" && mv "$META.new" "$META"
customer_issue 41 "**Evidence**: the guide's Dashboard page doesn't say.

<!-- a-team:customer -->"
run board demo add customer 41 Idea
same "exit" 0 "$STATUS"
same "a discovery" '{"idea":true}' "$(marks 41)"
same "added" "#41: added as Idea" "$(cat "$OUT")"

case_ "the Customer lead adds nothing past Idea but its docs proposal, and no issue it didn't open"
for to in Exploring Ready; do
  run board demo add customer 41 "$to"
  failed "add as $to"
  grep -qF "customer may not add items as '$to'" "$ERR" || fail "add as $to: '$(cat "$ERR")'"
done
run board demo add customer 41 Pitched
failed "an issue as Pitched"
grep -qF "customer may only put its own open draft docs proposal in Pitched (#41 isn't one)" "$ERR" ||
  fail "an issue as Pitched: '$(cat "$ERR")'"
customer_issue 41 "Seen in the wild.

<!-- a-team:lead -->"
run board demo add customer 41 Idea
failed "someone else's issue"
one_line "someone else's issue"
grep -qF "customer may only add an open issue it opened as an Idea (#41 isn't one)" "$ERR" ||
  fail "someone else's issue: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"

case_ "the Customer lead's Ideas count towards wip.ideas, and a full queue refuses one more"
gh_items <<'ITEMS'
Idea 41 I can't find how to pause one role
Idea 42 I can't tell which team a card is from
Idea 43 I can't find where the logs are
ITEMS
edit_item 41 '.labels.nodes = [{name: "a-team:idea"}]'
edit_item 42 '.labels.nodes = [{name: "a-team:idea"}]'
customer_issue 43 "<!-- a-team:customer -->"
run board demo add customer 43 Idea
failed "full queue"
one_line "full queue"
grep -qF "2 discovered Ideas are waiting for triage (wip.ideas is 2): leave #43 off the board" "$ERR" ||
  fail "full queue: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"
A_TEAM_STATE="$WORK/state" run board demo lead-next
same "exit" 0 "$STATUS"
same "lead-next" '{"turn":"none","room":null}' "$(jq -c '{turn, room}' "$OUT")"

case_ "only discoveries waiting for a Priority fill the Customer lead's wip.ideas: a prioritised or skipped one leaves room"
jq '.wip.ideas = 1' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
customer_ideas() {
  gh_items "$@" <<'ITEMS'
Idea 41 I can't find how to pause one role
Idea 42 I can't tell which team a card is from
Idea 43 I can't find where the logs are
ITEMS
  edit_item 41 '.labels.nodes = [{name: "a-team:idea"}]'
  edit_item 42 '.labels.nodes = [{name: "a-team:idea"}, {name: "a-team:skipped"}]'
  customer_issue 43 "<!-- a-team:customer -->"
}
customer_ideas 41
run board --dry-run demo add customer 43 Idea
same "room" 0 "$STATUS"
customer_ideas
run board demo add customer 43 Idea
failed "its Priority cleared"
grep -qF "1 discovered Ideas are waiting for triage (wip.ideas is 1): leave #43 off the board" "$ERR" ||
  fail "its Priority cleared: '$(cat "$ERR")'"
jq '.wip.ideas = 2' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"

case_ "the Lead pitches a Customer lead's Idea once it's ranked, like its own discoveries"
gh_items 41 <<'ITEMS'
Idea 41 I can't find how to pause one role
Idea 42 I can't tell which team a card is from
ITEMS
edit_item 41 '.labels.nodes = [{name: "a-team:idea"}]'
edit_item 42 '.labels.nodes = [{name: "a-team:idea"}]'
customer_issue 41 "<!-- a-team:customer -->"
A_TEAM_STATE="$WORK/state" run board demo lead-next
same "exit" 0 "$STATUS"
same "lead-next" '{"turn":"pitch","item":41}' "$(jq -c '{turn, item: .item.number}' "$OUT")"
[ -e "$WORK/state/demo/lead/card" ] && fail "outside a run: recorded a card"

case_ "a Lead run started across the board records the Idea it pitches, and keeps a card it was started for"
rm -f "$WORK/state/demo/lead/card"
A_TEAM_RUN_TEAM=demo A_TEAM_STATE="$WORK/state" run board --dry-run demo lead-next
[ -e "$WORK/state/demo/lead/card" ] && fail "dry run: recorded a card"
A_TEAM_RUN_TEAM=demo A_TEAM_STATE="$WORK/state" run board demo lead-next
same "card" '{"number":41}' "$(cat "$WORK/state/demo/lead/card")"
echo '{"number":7}' >"$WORK/state/demo/lead/card"
A_TEAM_RUN_TEAM=demo A_TEAM_STATE="$WORK/state" run board demo lead-next
same "kept" '{"number":7}' "$(cat "$WORK/state/demo/lead/card")"
rm -f "$WORK/state/demo/lead/card"
waiting
same "triage" '[42]' "$(jq -c 'map(select(.reason == "waiting to be ranked") | .number)' "$OUT")"

# --- the Lead's recommended rank -------------------------------------------------------------

finish_suite
