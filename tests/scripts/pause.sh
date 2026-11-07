#!/usr/bin/env bash
# Pause and resume, and the board basics they lean on.
# shellcheck source=tests/scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
# shellcheck disable=SC2034,SC2154 # helpers read what an area sets, and an area reads what lib.sh sets

case_ "pause turns dispatch off and leaves every other key as it was"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "dispatch": { "enabled": true, "retryAfter": 60 }, "somethingNew": [1, 2] }
JSON
run pause demo
same "exit" 0 "$STATUS"
same "enabled" false "$(enabled)"
same "repo" '"mentaldesk/demo"' "$(jq -c .repo "$TEAM")"
same "retryAfter" 60 "$(jq -c .dispatch.retryAfter "$TEAM")"
same "somethingNew" '[1,2]' "$(jq -c .somethingNew "$TEAM")"

case_ "resume turns it back on"
run resume demo
same "exit" 0 "$STATUS"
same "enabled" true "$(enabled)"
same "somethingNew" '[1,2]' "$(jq -c .somethingNew "$TEAM")"

case_ "pause adds dispatch.enabled to a config that has no dispatch block"
fixture <<'JSON'
{ "repo": "mentaldesk/demo" }
JSON
run pause demo
same "exit" 0 "$STATUS"
same "enabled" false "$(enabled)"

case_ "--dry-run says what it would change and changes nothing"
fixture <<'JSON'
{ "dispatch": { "enabled": true } }
JSON
run pause --dry-run demo
same "exit" 0 "$STATUS"
same "enabled" true "$(enabled)"
grep -q "would set dispatch.enabled to false" "$OUT" || fail "dry run: nothing about the change in '$(cat "$OUT")'"

case_ "an unknown team is refused in one line"
run pause nobody
failed "unknown team"
one_line "unknown team"

case_ "a config that isn't JSON is refused in one line, unchanged"
fixture <<'JSON'
{ "dispatch": { "enabled": true
JSON
before=$(cat "$TEAM")
run pause demo
failed "bad JSON"
one_line "bad JSON"
same "file" "$before" "$(cat "$TEAM")"

case_ "a config that isn't an object is refused in one line, unchanged"
fixture <<'JSON'
["not a team"]
JSON
before=$(cat "$TEAM")
run pause demo
failed "not an object"
one_line "not an object"
same "file" "$before" "$(cat "$TEAM")"

if [ "$(id -u)" = 0 ]; then
  case_ "skipping the unwritable-config case: root can write anything"
else
  case_ "a config it can't write is refused in one line, unchanged"
  fixture <<'JSON'
{ "dispatch": { "enabled": true } }
JSON
  before=$(cat "$TEAM")
  chmod 444 "$TEAM"
  run pause demo
  failed "unwritable"
  one_line "unwritable"
  same "file" "$before" "$(cat "$TEAM")"
  chmod 644 "$TEAM"
fi

case_ "both commands are in the usage text"
run help
grep -q '^  pause ' "$OUT" || fail "usage: no pause line"
grep -q '^  resume ' "$OUT" || fail "usage: no resume line"















# Times read in the reviewer's own zone, so these cases pin one they can predict.
export TZ=UTC

finish_suite
