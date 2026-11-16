#!/usr/bin/env bash
# The repo's open issues and PRs arriving on the board by themselves.
# shellcheck source=tests/scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
# shellcheck disable=SC2034,SC2154 # helpers read what an area sets, and an area reads what lib.sh sets

# repo_issue <n> <title> [author] [state] [type]: an issue the repo's read returns, open and by the reviewer unless said.
repo_issue() {
  jq --argjson n "$1" --arg title "$2" --arg author "${3:-reviewer}" --arg state "${4:-OPEN}" --arg type "${5:-Issue}" \
    '. + [{__typename: $type, id: "I_\($n)", number: $n, title: $title, url: "https://github.com/mentaldesk/demo/issues/\($n)",
           state: $state, repository: {nameWithOwner: "mentaldesk/demo"}, labels: {nodes: []},
           author: {__typename: "User", login: $author}, issueDependenciesSummary: {blockedBy: 0}, issueFieldValues: {nodes: []}}]' \
    "$REPO_ISSUES" >"$REPO_ISSUES.new" && mv "$REPO_ISSUES.new" "$REPO_ISSUES"
}

# The repo's read returns every issue on the page gh_items wrote, open, as well as any repo_issue added.
issues_from_items() {
  jq '[.data.organization.projectV2.items.nodes[].content | .state //= "OPEN" | .author //= {__typename: "User", login: "reviewer"}]' \
    "$ITEMS" >"$REPO_ISSUES"
}

count_of() { jq --argjson n "$1" 'map(select(.number == $n)) | length' "$OUT"; }

demo_team() {
  fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 },
  "wip": { "pitched": 3, "exploring": 3, "ideas": 3 } }
JSON
}

case_ "an open issue the board hasn't got arrives in Triage once, with no Priority, whatever GitHub's field says"
demo_team
gh_items <<'ITEMS'
Pitched 30 A pitch
Ready 31 A task
ITEMS
issues_from_items
repo_issue 45 "Generate preview images"
jq '(.[] | select(.number == 45)).issueFieldValues.nodes = [{name: "High", field: {name: "Priority"}}]' "$REPO_ISSUES" >"$REPO_ISSUES.new" &&
  mv "$REPO_ISSUES.new" "$REPO_ISSUES"
run board demo list
same "exit" 0 "$STATUS"
same "board" '[[30,"Pitched"],[31,"Ready"],[45,"Idea"]]' "$(statuses)"
same "no Priority" null "$(jq -c '.[] | select(.number == 45) | .priority' "$OUT")"
same "writes" "" "$(cat "$WRITES")"
same "history" "board None → Idea · opened on GitHub" "$(history_lines 45)"
kept board demo list
same "once" 1 "$(count_of 45)"
same "board again" '[[30,"Pitched"],[31,"Ready"],[45,"Idea"]]' "$(statuses)"

case_ "closed issues and other repos' issues don't arrive"
demo_team
gh_items <<'ITEMS'
Ready 31 A task
ITEMS
repo_issue 46 "Closed long ago" reviewer CLOSED
repo_issue 48 "Elsewhere"
jq '(.[] | select(.number == 48)).repository.nameWithOwner = "mentaldesk/other"' "$REPO_ISSUES" >"$REPO_ISSUES.new" && mv "$REPO_ISSUES.new" "$REPO_ISSUES"
run board demo list
same "board" '[[31,"Ready"]]' "$(statuses)"

# repo_pull <n> <title> [draft] [closes]: an open PR the repo's read returns, a draft if said, closing issue <closes> if given.
repo_pull() {
  jq --argjson n "$1" --arg title "$2" --argjson draft "${3:-false}" --arg closes "${4:-}" \
    '. + [{__typename: "PullRequest", id: "PR_\($n)", number: $n, title: $title, url: "https://github.com/mentaldesk/demo/pull/\($n)",
           state: "OPEN", isDraft: $draft, repository: {nameWithOwner: "mentaldesk/demo"}, labels: {nodes: []}, author: {login: "reviewer"},
           closingIssuesReferences: {nodes: (if $closes == "" then [] else [{number: ($closes | tonumber), repository: {nameWithOwner: "mentaldesk/demo"}}] end)}}]' \
    "$REPO_PULLS" >"$REPO_PULLS.new" && mv "$REPO_PULLS.new" "$REPO_PULLS"
}

case_ "an open PR that closes nothing on the board arrives in the lane its draft says, with no Priority; one that closes a card doesn't"
demo_team
gh_items <<'ITEMS'
In_progress 31 A task
ITEMS
issues_from_items
repo_pull 47 "Tidy the README"
repo_pull 48 "Try a new layout" true
repo_pull 49 "Build the task" false 31
run board demo list
same "exit" 0 "$STATUS"
same "board" '[[31,"In progress"],[47,"In review"],[48,"In progress"]]' "$(statuses)"
same "no Priority" '[null,null]' "$(jq -c '[.[] | select(.number >= 47) | .priority]' "$OUT")"
same "history" "board None → In review · opened on GitHub" "$(history_lines 47)"
same "writes" "" "$(cat "$WRITES")"
kept board demo overview
same "lanes" '[["In progress","In progress"],["In review","In review"],["In progress","In progress"]]' "$(jq -c '[.[] | [.status, .lane]]' "$OUT")"

case_ "a repo with more than 100 open issues and PRs has every one on the board"
demo_team
gh_items </dev/null
for n in $(seq 1 130); do repo_issue "$n" "Issue $n"; done
for n in $(seq 201 305); do repo_pull "$n" "PR $n"; done
run board demo list
same "exit" 0 "$STATUS"
same "issues" 130 "$(jq '[.[] | select(.status == "Idea")] | length' "$OUT")"
same "PRs" 105 "$(jq '[.[] | select(.status == "In review")] | length' "$OUT")"

case_ "a card Done on the board but open on GitHub comes back at its last move, though the board never saw it close"
demo_team
gh_items <<'ITEMS'
Done 45 Generate preview images
ITEMS
edit_item 45 '.state = "OPEN"'
issues_from_items
run board demo list
same "back" '[[45,"Idea"]]' "$(statuses)"
history_lines 45 | grep -qx "board Done → Idea · reopened" || fail "history: $(history_lines 45)"

case_ "a failed or short read of the repo's issues changes nothing, and is tried again"
demo_team
gh_items <<'ITEMS'
Ready 31 A task
ITEMS
repo_issue 45 "Generate preview images"
touch "$BIN/issues-read-fails"
run board demo list
same "failed exit" 0 "$STATUS"
same "failed" '[[31,"Ready"]]' "$(statuses)"
rm "$BIN/issues-read-fails"
touch "$BIN/issues-short"
: >"$BIN/issue-reads"
run board demo list
same "short" '[[31,"Ready"]]' "$(statuses)"
same "read twice" 2 "$(grep -c '' "$BIN/issue-reads")"
rm "$BIN/issues-short"
kept board demo list
same "next read" '[[31,"Ready"],[45,"Idea"]]' "$(statuses)"

case_ "an issue opened on GitHub reaches Triage, and one closed and reopened comes back where it was (#715)"
demo_team
gh_items <<'ITEMS'
Idea 45 Generate preview images
ITEMS
issues_from_items
: >"$BIN/args"
run board demo list
same "arrived" '[[45,"Idea"]]' "$(statuses)"
edit_item 45 '.state = "CLOSED" | .stateReason = "NOT_PLANNED"'
echo '[]' >"$REPO_ISSUES"
kept board demo list
same "closed" '[[45,"Done"]]' "$(statuses)"
edit_item 45 '.state = "OPEN" | .stateReason = "REOPENED"'
issues_from_items
kept board demo list
same "reopened" '[[45,"Idea"]]' "$(statuses)"
history_lines 45 | grep -qx "board Done → Idea · reopened" || fail "history: $(history_lines 45)"
same "project calls" 0 "$(grep -ciE 'projectv2|projectItems' "$BIN/args")"

case_ "someone else's issue waits in Triage for a Priority; a stakeholder's can be pitched unranked"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "stakeholders": ["reviewer"], "app": { "id": 7, "slug": "demo-app" },
  "wip": { "pitched": 3, "exploring": 3, "ideas": 3 } }
JSON
gh_items </dev/null
echo '{"number": 45, "body": "Please add previews"}' >"$ISSUE"
repo_issue 45 "Generate preview images" breq-toren
run board demo lead-next
same "not offered" null "$(jq -c '.item.number' "$OUT")"
KEEP_BOARD=1 waiting
same "waits for your rank" '"opened by breq-toren, waits for your rank"' "$(jq -c '.[] | select(.number == 45) | .reason' "$OUT")"
gh_talk <<TALK
45 body ${TODAY}T07:00:00Z breq-toren Please add previews
45 comment ${TODAY}T07:20:00Z demo-app[bot] Recommended: **High** · theme 2, small · clears the queue\n\n<!-- a-team:lead -->
TALK
run board demo list
KEEP_BOARD=1 waiting
same "keeps the Lead's case" '"theme 2, small · clears the queue · opened by breq-toren"' \
  "$(jq -c '.[] | select(.number == 45) | .reason' "$OUT")"
jq '(.[] | select(.number == 45)).issueFieldValues.nodes = [{name: "Low", field: {name: "Priority"}}]' "$REPO_ISSUES" >"$REPO_ISSUES.new" &&
  mv "$REPO_ISSUES.new" "$REPO_ISSUES"
run board demo lead-next
same "ranked on GitHub, not offered" null "$(jq -c '.item.number' "$OUT")"
kept board demo priority you 45 Low
kept board demo lead-next
same "ranked, offered" 45 "$(jq -c '.item.number' "$OUT")"
echo '[]' >"$REPO_ISSUES"
repo_issue 45 "Generate preview images" reviewer
run board demo lead-next
same "a stakeholder's, offered" 45 "$(jq -c '.item.number' "$OUT")"

case_ "setup says how many of the repo's open issues will arrive in Triage"
demo_team
gh_items <<'ITEMS'
Ready 31 A task
ITEMS
issues_from_items
repo_issue 45 "One"
repo_issue 46 "Two"
run board demo list
repo_issue 47 "Three"
KEEP_BOARD=1 run board --dry-run demo setup
grep -qx "(dry run) 1 open issue on mentaldesk/demo will arrive in Triage" "$OUT" || fail "setup: $(cat "$OUT")"

case_ "check notes the repo's open issues and PRs no card shows, and says nothing while they agree"
demo_team
gh_items <<'ITEMS'
In_progress 31 A task
ITEMS
issues_from_items
repo_pull 49 "Build the task" false 31
run board demo list
KEEP_BOARD=1 run board demo check
grep -q "^note     issues " "$OUT" && fail "agreeing: $(cat "$OUT")"
sqlite3 "$A_TEAM_STATE/history.db" "UPDATE boards SET issues_read = '2999-01-01T00:00:00Z' WHERE team = 'demo';"
repo_issue 46 "Opened since the last read"
repo_pull 47 "A PR since the last read"
KEEP_BOARD=1 run board demo check
grep -qx "note     issues    2 open on GitHub not on the board: #46, #47" "$OUT" || fail "differing: $(cat "$OUT")"

finish_suite
