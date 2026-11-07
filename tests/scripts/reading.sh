#!/usr/bin/env bash
# Reading a card: status fields, body, conversation, priority.
# shellcheck source=tests/scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
# shellcheck disable=SC2034,SC2154 # helpers read what an area sets, and an area reads what lib.sh sets

case_ "a board whose Done is called something else has that left off instead"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "project": { "owner": "mentaldesk", "number": 1, "statusMap": { "Done": "Shipped" } } }
JSON
gh_items <<'ITEMS'
Shipped 99 Already merged
ITEMS
waiting
same "exit" 0 "$STATUS"
same "nothing waiting" '[]' "$(cat "$OUT")"

case_ "a status field whose name isn't one word is read whole, since a filter GitHub can't match finds nothing"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "project": { "owner": "mentaldesk", "number": 1, "statusField": "Work state" } }
JSON
gh_items <<'ITEMS'
Done 99 Already merged
ITEMS
jq '.data.organization.projectV2.fields.nodes[0].name = "Work state"' "$META" >"$META.new" && mv "$META.new" "$META"
waiting
same "exit" 0 "$STATUS"

case_ "body returns an issue's number, title and body, in one call"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 } }
JSON
gh_items <<'ITEMS'
Idea 7 An Idea of my own
ITEMS
gh_thread <<TALK
body ${TODAY}T08:00:00Z reviewer 0 ## Opportunity
TALK
run board demo body 7
same "exit" 0 "$STATUS"
same "number" 7 "$(jq -c .number "$OUT")"
same "title" '"The whole thread"' "$(jq -c .title "$OUT")"
same "body" '"## Opportunity"' "$(jq -c .body "$OUT")"
same "api calls" 1 "$(grep -c '' <"$CALLS")"

case_ "reading a body writes nothing, and --dry-run has no change to report"
: >"$WRITES"
run board --dry-run demo body 7
same "exit" 0 "$STATUS"
same "writes" "" "$(cat "$WRITES")"
same "body" '"## Opportunity"' "$(jq -c .body "$OUT")"
grep -q "dry-run" "$ERR" && fail "body dry-run: it claimed a change in '$(cat "$ERR")'"

case_ "an issue that can't be read is refused in one line, naming it"
run board demo body 404
failed "unreadable issue"
one_line "unreadable issue"
grep -q "can't read #404" "$ERR" || fail "unreadable issue: '$(cat "$ERR")'"

case_ "body names no role, so either agent may read one"
run board demo body
failed "body with no issue"
one_line "body with no issue"
grep -q "usage: board.sh demo body <n>" "$ERR" || fail "body usage: '$(cat "$ERR")'"

case_ "conversation merges the issue's comments with its PR's description and comments, oldest first, in one call"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 } }
JSON
gh_items <<'ITEMS'
In_review 6 A task with a PR
ITEMS
gh_talk <<'TALK'
6 body 2026-10-01T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
6 comment 2026-10-01T08:10:00Z demo-app[bot] Draft PR #906 is up.\n\n<!-- a-team:dev -->
6 pr-body 2026-10-01T08:05:00Z demo-app[bot] Closes #6\n\n<!-- a-team:dev -->
6 pr-comment 2026-10-01T09:00:00Z reviewer Does it scroll?
6 comment 2026-10-01T09:30:00Z stranger +1
6 pr-comment 2026-10-01T09:40:00Z demo-app[bot] It does. <!-- a-team:dev -->
6 comment 2026-10-01T10:00:00Z demo-app[bot] Validated.\n<!-- a-team:lead -->
TALK
: >"$CALLS"
run board demo conversation 6
same "exit" 0 "$STATUS"
same "who" '["dev","dev","you","stranger","dev","lead"]' "$(jq -c '[.[].who]' "$OUT")"
same "bodies" '["Closes #6","Draft PR #906 is up.","Does it scroll?","+1","It does.","Validated."]' \
  "$(jq -c '[.[].body]' "$OUT")"
same "prs" '[906,null,906,null,906,null]' "$(jq -c '[.[].pr]' "$OUT")"
same "description" '[true,false,false,false,false,false]' "$(jq -c '[.[].description]' "$OUT")"
same "at" '"2026-10-01T08:05:00Z"' "$(jq -c '.[0].at' "$OUT")"
same "urls" '["https://github.com/mentaldesk/demo/pull/906","https://github.com/mentaldesk/demo/issues/6#issuecomment-1","https://github.com/mentaldesk/demo/pull/906#issuecomment-1","https://github.com/mentaldesk/demo/issues/6#issuecomment-2","https://github.com/mentaldesk/demo/pull/906#issuecomment-2","https://github.com/mentaldesk/demo/issues/6#issuecomment-3"]' \
  "$(jq -c '[.[].url]' "$OUT")"
same "api calls" 1 "$(grep -c '' <"$CALLS")"

case_ "a Not now reads without its date marker"
gh_talk <<'TALK'
6 body 2026-10-01T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
6 comment 2026-10-01T09:00:00Z reviewer Later.\n\nNot now: back on 15 Oct.\n\n<!-- a-team:shelved until=2026-10-15 priority=High -->
TALK
run board demo conversation 6
same "bodies" '["Later.\n\nNot now: back on 15 Oct."]' "$(jq -c '[.[].body]' "$OUT")"

case_ "a card nobody has said anything on has an empty conversation"
gh_talk <<'TALK'
6 body 2026-10-01T08:00:00Z reviewer An Idea of my own
TALK
run board demo conversation 6
same "exit" 0 "$STATUS"
same "conversation" '[]' "$(jq -c . "$OUT")"

case_ "a conversation that can't be read is refused in one line, naming the issue"
DOWN=$(mktemp -d "$WORK/down.XXXXXX")
printf '#!/usr/bin/env bash\necho "gh: HTTP 502" >&2\nexit 1\n' | fake "$DOWN/gh"
PATH="$DOWN:$PATH" run board demo conversation 6
failed "unreadable conversation"
one_line "unreadable conversation"
grep -q "can't read the conversation on #6 (gh: HTTP 502)" "$ERR" || fail "unreadable conversation: '$(cat "$ERR")'"

# Ranking: the one field the app writes, and the gate it is the reviewer's alone to clear.
case_ "priority sets the field's own option on the issue, and nothing on the project"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 } }
JSON
gh_items <<'ITEMS'
Idea 6 The agents can't say what they'd change
ITEMS
gh_thread <<TALK
body ${TODAY}T08:00:00Z reviewer 0 An Idea of my own
TALK
run board demo priority you 6 High
same "exit" 0 "$STATUS"
same "said" "#6: Priority set to 'High'" "$(cat "$OUT")"
same "writes" 1 "$(grep -c '' <"$WRITES")"
grep -q "issue=I_6 " "$WRITES" || fail "priority: not the issue's node id in '$(cat "$WRITES")'"
grep -q "field=IF_priority " "$WRITES" || fail "priority: not the field's id in '$(cat "$WRITES")'"
grep -q "option=OP_high " "$WRITES" || fail "priority: not the option's id in '$(cat "$WRITES")'"
grep -q "updateIssueFieldValue" "$WRITES" || fail "priority: not the issue-field mutation"
grep -q "singleSelectOptionId" "$WRITES" || fail "priority: no option in the mutation"

case_ "none clears it rather than setting an option"
: >"$WRITES"
KEEP_BOARD=1 run board demo priority you 6 none
same "exit" 0 "$STATUS"
same "said" "#6: Priority cleared" "$(cat "$OUT")"
same "writes" 1 "$(grep -c '' <"$WRITES")"
grep -q "delete: true" "$WRITES" || fail "priority none: nothing deleted in '$(cat "$WRITES")'"
grep -q "singleSelectOptionId" "$WRITES" && fail "priority none: an option was set as well"

case_ "a value the field hasn't got is refused by name, listing the ones it has"
: >"$WRITES"
run board demo priority you 6 Urgentish
failed "unknown value"
one_line "unknown value"
grep -q "Urgent | High | Medium | Low | none" "$ERR" || fail "unknown value: no list in '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"

case_ "neither agent may rank an item: that gate is the reviewer's own"
for role in lead dev; do
  : >"$WRITES"
  run board demo priority "$role" 6 High
  failed "$role ranking"
  one_line "$role ranking"
  grep -q "$role may not set a Priority" "$ERR" || fail "$role ranking: '$(cat "$ERR")'"
  same "$role writes" "" "$(cat "$WRITES")"
done

case_ "--dry-run says what it would set and sets nothing"
: >"$WRITES"
run board --dry-run demo priority you 6 Low
same "exit" 0 "$STATUS"
same "writes" "" "$(cat "$WRITES")"
grep -q "would set Priority on #6 to 'Low'" "$ERR" || fail "dry run: nothing about the rank in '$(cat "$ERR")'"

# Approving: the other write the app makes, and the other gate that is the reviewer's alone.

finish_suite
