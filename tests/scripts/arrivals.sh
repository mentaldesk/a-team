#!/usr/bin/env bash
# The repo's open issues arriving on the board by themselves.
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

case_ "closed issues, pull requests and other repos' issues don't arrive"
demo_team
gh_items <<'ITEMS'
Ready 31 A task
ITEMS
repo_issue 46 "Closed long ago" reviewer CLOSED
repo_issue 47 "A PR" reviewer OPEN PullRequest
repo_issue 48 "Elsewhere"
jq '(.[] | select(.number == 48)).repository.nameWithOwner = "mentaldesk/other"' "$REPO_ISSUES" >"$REPO_ISSUES.new" && mv "$REPO_ISSUES.new" "$REPO_ISSUES"
run board demo list
same "board" '[[31,"Ready"]]' "$(statuses)"

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

finish_suite
