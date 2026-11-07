#!/usr/bin/env bash
# Repos a team contributes to.
# shellcheck source=tests/scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
# shellcheck disable=SC2034,SC2154 # helpers read what an area sets, and an area reads what lib.sh sets

case_ "the Dev is woken for its own PRs on a repo the team contributes to, on the task each is needed by"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 },
  "wip": { "worktrees": 3 }, "contributesTo": ["mentaldesk/style", "mentaldesk/lib"] }
JSON
gh_items <<'ITEMS'
Ready 13 Needs a style guide change
Done 15 Already merged
ITEMS
edit_item 13 '.labels.nodes = [{name: "a-team:dev"}, {name: "blocked"}]'
cat >"$UPSTREAM" <<'JSON'
[{"number": 71, "isDraft": true, "headRefOid": "abcdef1234", "createdAt": "2026-10-01T09:00:00Z",
  "body": "Needed by mentaldesk/demo#13\n\n<!-- a-team:dev -->", "comments": [], "reviews": [],
  "statusCheckRollup": [{"__typename": "CheckRun", "status": "COMPLETED", "conclusion": "SUCCESS"}]},
 {"number": 72, "isDraft": true, "headRefOid": "0123456789", "createdAt": "2026-10-01T09:00:00Z",
  "body": "Needed by mentaldesk/demo#13\n\n<!-- a-team:dev -->", "comments": [], "reviews": [],
  "statusCheckRollup": [{"__typename": "CheckRun", "status": "COMPLETED", "conclusion": "FAILURE"},
                        {"__typename": "StatusContext", "state": "PENDING"}]},
 {"number": 73, "isDraft": false, "headRefOid": "aaaaaaa", "createdAt": "2026-10-01T09:00:00Z",
  "body": "Needed by mentaldesk/demo#13\n\n<!-- a-team:dev -->",
  "comments": [{"author": {"login": "demo-app"}, "createdAt": "2026-10-02T09:00:00Z", "body": "Done.\n\n<!-- a-team:dev -->"},
               {"author": {"login": "reviewer"}, "createdAt": "2026-10-01T10:00:00Z", "body": "Answered already"},
               {"author": {"login": "stranger"}, "createdAt": "2026-10-03T09:00:00Z", "body": "Not the stakeholder"}],
  "reviews": [{"author": {"login": "reviewer"}, "submittedAt": "2026-10-04T09:00:00Z", "state": "COMMENTED", "body": "Rename it"}],
  "statusCheckRollup": [{"__typename": "StatusContext", "state": "SUCCESS"}]},
 {"number": 74, "isDraft": true, "headRefOid": "bbbbbbb", "createdAt": "2026-10-01T09:00:00Z",
  "body": "Needed by mentaldesk/other#13\n\n<!-- a-team:dev -->", "comments": [], "reviews": [],
  "statusCheckRollup": [{"__typename": "CheckRun", "status": "COMPLETED", "conclusion": "FAILURE"}]},
 {"number": 75, "isDraft": true, "headRefOid": "ccccccc", "createdAt": "2026-10-01T09:00:00Z",
  "body": "Needed by mentaldesk/demo#13", "comments": [], "reviews": [],
  "statusCheckRollup": [{"__typename": "CheckRun", "status": "COMPLETED", "conclusion": "FAILURE"}]},
 {"number": 76, "isDraft": true, "headRefOid": "ddddddd", "createdAt": "2026-10-01T09:00:00Z",
  "body": "Needed by mentaldesk/demo#15\n\n<!-- a-team:dev -->", "comments": [], "reviews": [],
  "statusCheckRollup": [{"__typename": "CheckRun", "status": "COMPLETED", "conclusion": "FAILURE"}]}]
JSON
run board demo triggers dev
same "exit" 0 "$STATUS"
same "tasks" '[{"number":13,"title":"Needs a style guide change","reasons":["mentaldesk/style#71 is green but still a draft","CI failed on mentaldesk/style#72 at 0123456","stakeholder feedback on mentaldesk/style#73 (2026-10-04T09:00:00Z)","mentaldesk/lib#71 is green but still a draft","CI failed on mentaldesk/lib#72 at 0123456","stakeholder feedback on mentaldesk/lib#73 (2026-10-04T09:00:00Z)"]}]' \
  "$(jq -c .tasks "$OUT")"
same "one list per repo, of the App's PRs" "pr list -R mentaldesk/style --author app/demo-app|pr list -R mentaldesk/lib --author app/demo-app" \
  "$(sed 's/ --state.*//' "$UPSTREAM_CALLS" | paste -sd '|' -)"

case_ "a stakeholder's line comments on an upstream PR wake the Dev, though their review has no body"
cat >"$UPSTREAM" <<'JSON'
[{"number": 77, "isDraft": false, "headRefOid": "eeeeeee", "createdAt": "2026-10-01T09:00:00Z",
  "body": "Needed by mentaldesk/demo#13\n\n<!-- a-team:dev -->", "comments": [],
  "reviews": [{"author": {"login": "reviewer"}, "submittedAt": "2026-10-05T09:00:00Z", "state": "COMMENTED", "body": ""}],
  "statusCheckRollup": [{"__typename": "StatusContext", "state": "PENDING"}]}]
JSON
cat >"$UPSTREAM_LINES" <<'JSON'
[{"pull_request_url": "https://api.github.com/repos/mentaldesk/style/pulls/77", "user": {"login": "reviewer"}, "created_at": "2026-10-05T09:00:00Z"},
 {"pull_request_url": "https://api.github.com/repos/mentaldesk/style/pulls/80", "user": {"login": "reviewer"}, "created_at": "2026-10-06T09:00:00Z"}]
JSON
run board demo triggers dev
same "exit" 0 "$STATUS"
same "tasks" '[{"number":13,"title":"Needs a style guide change","reasons":["stakeholder feedback on mentaldesk/style#77 (2026-10-05T09:00:00Z)"]}]' \
  "$(jq -c .tasks "$OUT")"
echo '[]' >"$UPSTREAM_LINES"

case_ "without repos it contributes to, the Dev's triggers list no other repo's PRs"
jq 'del(.contributesTo)' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
: >"$UPSTREAM_CALLS"
run board demo triggers dev
same "exit" 0 "$STATUS"
same "tasks" '[]' "$(jq -c .tasks "$OUT")"
same "calls" "" "$(cat "$UPSTREAM_CALLS")"
echo '[]' >"$UPSTREAM"

case_ "the Dev's prompt authorises the repos the team contributes to, and only with some named"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "workdir": "~/code/demo", "contributesTo": ["mentaldesk/style", "mentaldesk/lib"] }
JSON
run task-prompt demo dev
same "exit" 0 "$STATUS"
grep -qF -- "- The same on the other repos this team contributes to, mentaldesk/style, mentaldesk/lib: clone them" "$OUT" ||
  fail "prompt names the repos: '$(cat "$OUT")'"
grep -qF "push branches other than the default branch, open draft PRs" "$OUT" || fail "prompt: no push or PRs upstream"
grep -qF "You must never merge a PR, push to the default branch, force-push" "$OUT" || fail "prompt: limits gone"
grep -qF "{{" "$OUT" && fail "prompt: a placeholder left in '$(cat "$OUT")'"
for empty in '[]' null; do
  jq --argjson to "$empty" '.contributesTo = $to' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
  run task-prompt demo dev
  same "exit with $empty" 0 "$STATUS"
  grep -q "contributes\|{{" "$OUT" && fail "prompt with $empty: '$(cat "$OUT")'"
  same "prompt with $empty is today's" "$(sed -e '/{{contributes}}/d' -e 's|{{team}}|demo|g; s|{{repo}}|mentaldesk/demo|g; s|{{workdir}}|~/code/demo|g' "$ROOT/tasks/dev.md")" "$(cat "$OUT")"
done

finish_suite
