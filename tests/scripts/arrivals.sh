#!/usr/bin/env bash
# The repo's open issues arriving on the board by themselves, with a mirror or without.
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

mirrored() {
  fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 },
  "wip": { "pitched": 3, "exploring": 3, "ideas": 3 } }
JSON
}

case_ "an open issue on neither the board nor its project arrives in Triage once, and the mirror gets it"
mirrored
gh_items <<'ITEMS'
Pitched 30 A pitch
Ready 31 A task
ITEMS
every_status
issues_from_items
repo_issue 45 "Generate preview images"
run board demo list
same "exit" 0 "$STATUS"
same "board" '[[30,"Pitched"],[31,"Ready"],[45,"Idea"]]' "$(statuses)"
grep -q "ADD .*content=I_45" "$WRITES" || fail "not added to the mirror: $(cat "$WRITES")"
grep -c "ADD " "$WRITES" | grep -qx 1 || fail "added more than #45: $(cat "$WRITES")"
same "history" "board None → Idea · opened on GitHub" "$(history_lines 45)"
kept board demo list
same "once" 1 "$(count_of 45)"
same "board again" '[[30,"Pitched"],[31,"Ready"],[45,"Idea"]]' "$(statuses)"
same "mirror link kept" PVTI_30 "$(jq -r '.[] | select(.number == 30) | .id' "$OUT")"

case_ "closed issues, pull requests and other repos' issues don't arrive"
mirrored
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
mirrored
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

case_ "an issue the project added by itself, with no Status, arrives once in Triage"
mirrored
gh_items <<'ITEMS'
Ready 31 A task
ITEMS
every_status
add_card 45 null "Auto-added"
issues_from_items
run board demo list
same "board" '[[31,"Ready"],[45,"Idea"]]' "$(statuses)"
grep -q "ADD " "$WRITES" && fail "added again: $(cat "$WRITES")"
grep -q "item=PVTI_45 .*option=OPT_idea" "$WRITES" || fail "Status not set on the mirror: $(cat "$WRITES")"

case_ "the project adding an issue the read brought in first makes one card, with no Content already exists failure (#619)"
mirrored
gh_items <<'ITEMS'
Ready 31 A task
ITEMS
every_status
run board demo list
add_card 45 Idea "Auto-added"
issues_from_items
touch "$BIN/read-fails" "$BIN/add-exists"
kept board demo list
same "board" '[[31,"Ready"],[45,"Idea"]]' "$(statuses)"
same "mirror item" PVTI_45 "$(jq -r '.[] | select(.number == 45) | .id' "$OUT")"
same "no failure" "" "$(sqlite3 "$A_TEAM_STATE/history.db" "SELECT COALESCE(failed, '') FROM boards WHERE team = 'demo';")"
rm "$BIN/read-fails" "$BIN/add-exists"

case_ "with no mirror, an issue opened on GitHub reaches Triage, and one closed and reopened comes back where it was (#715)"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "mirror": false,
  "wip": { "pitched": 3, "exploring": 3, "ideas": 3 } }
JSON
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
same "project calls" 0 "$(grep -ci projectv2 "$BIN/args")"

case_ "someone else's issue waits in Triage for a Priority; a stakeholder's can be pitched unranked"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "stakeholders": ["reviewer"], "app": { "id": 7, "slug": "demo-app" }, "mirror": false,
  "wip": { "pitched": 3, "exploring": 3, "ideas": 3 } }
JSON
gh_items </dev/null
echo '{"number": 45, "body": "Please add previews"}' >"$ISSUE"
repo_issue 45 "Generate preview images" breq-toren
run board demo lead-next
same "not offered" null "$(jq -c '.item.number' "$OUT")"
KEEP_BOARD=1 waiting
same "waits for your rank" '"opened by breq-toren, waits for your rank"' "$(jq -c '.[] | select(.number == 45) | .reason' "$OUT")"
jq '(.[] | select(.number == 45)).issueFieldValues.nodes = [{name: "Low", field: {name: "Priority"}}]' "$REPO_ISSUES" >"$REPO_ISSUES.new" &&
  mv "$REPO_ISSUES.new" "$REPO_ISSUES"
run board demo lead-next
same "ranked, offered" 45 "$(jq -c '.item.number' "$OUT")"
echo '[]' >"$REPO_ISSUES"
repo_issue 45 "Generate preview images" reviewer
run board demo lead-next
same "a stakeholder's, offered" 45 "$(jq -c '.item.number' "$OUT")"

case_ "setup says how many of the repo's open issues will arrive in Triage"
mirrored
gh_items <<'ITEMS'
Ready 31 A task
ITEMS
every_status
issues_from_items
repo_issue 45 "One"
repo_issue 46 "Two"
run board demo list
repo_issue 47 "Three"
KEEP_BOARD=1 run board --dry-run demo setup
grep -qx "(dry run) 1 open issue on mentaldesk/demo will arrive in Triage" "$OUT" || fail "setup: $(cat "$OUT")"

finish_suite
