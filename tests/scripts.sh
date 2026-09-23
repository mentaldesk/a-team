#!/usr/bin/env bash
#
# tests/scripts.sh — the shell side of a-team. Run it with `bash tests/scripts.sh`; CI does too.
#
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
A_TEAM="$ROOT/bin/a-team"
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT
OUT="$WORK/out" ERR="$WORK/err"
CONFIG=$WORK TEAM='' STATUS=0
failures=0

case_() { echo "$*"; }
fail() { echo "  FAIL $*"; failures=$((failures + 1)); }
same() { [ "$2" = "$3" ] || fail "$1: expected '$2', got '$3'"; }
failed() { [ "$STATUS" -ne 0 ] || fail "$1: expected a non-zero exit, got $STATUS"; }
one_line() { same "$1 stderr" 1 "$(grep -c '' <"$ERR")"; }

# A config directory of its own, holding one team called demo, written from stdin.
fixture() {
  CONFIG=$(mktemp -d "$WORK/config.XXXXXX")
  mkdir -p "$CONFIG/teams"
  TEAM="$CONFIG/teams/demo.json"
  cat >"$TEAM"
}

run() {
  A_TEAM_CONFIG="$CONFIG" "$A_TEAM" "$@" >"$OUT" 2>"$ERR"
  STATUS=$?
}

enabled() { jq -c .dispatch.enabled "$TEAM"; }

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

# The one GraphQL page board.sh's `items` reads, from lines of "<status with _ for space> <n> <title>".
gh_items() {
  BIN=$(mktemp -d "$WORK/bin.XXXXXX")
  ITEMS="$BIN/items.json"
  CALLS="$BIN/calls"
  jq -R -s --arg repo mentaldesk/demo '
    split("\n") | map(select(length > 0)) | map(split(" ") as $f | {
      id: "PVTI_\($f[1])",
      fieldValueByName: {name: ($f[0] | gsub("_"; " "))},
      content: {__typename: "Issue", number: ($f[1] | tonumber), title: ($f[2:] | join(" ")),
                url: "https://github.com/\($repo)/issues/\($f[1])",
                repository: {nameWithOwner: $repo}, labels: {nodes: []},
                issueDependenciesSummary: {blockedBy: 0}}})
    | {data: {organization: {projectV2: {items: {pageInfo: {hasNextPage: false, endCursor: null}, nodes: .}}}}}' \
    >"$ITEMS"
  TALK="$BIN/talk.json"
  echo '{"data": {"repository": {}}}' >"$TALK"
  cat >"$BIN/gh" <<SH
#!/usr/bin/env bash
echo call >>"$CALLS"
if printf '%s\\n' "\$@" | grep -q issueOrPullRequest; then cat "$TALK"; else cat "$ITEMS"; fi
SH
  chmod +x "$BIN/gh"
  PATH="$BIN:$PATH"
}

# The one GraphQL page `waiting` reads for whose turn it is, from lines of
# "<n> body|comment <timestamp> <author> <text...>". The body line is the item's own.
gh_talk() {
  jq -R -s '
    split("\n") | map(select(length > 0)) | map(split(" ") as $f
      | {n: ($f[0] | tonumber), kind: $f[1], at: $f[2], author: $f[3], body: ($f[4:] | join(" "))})
    | group_by(.n) | map((map(select(.kind == "body")) | first) as $body | {
        key: "x\(.[0].n)",
        value: {number: .[0].n, createdAt: $body.at, body: ($body.body // ""),
                author: {login: ($body.author // "")},
                comments: {nodes: map(select(.kind == "comment")
                  | {createdAt: .at, body: .body, author: {login: .author}})}}})
    | from_entries | {data: {repository: .}}' >"$TALK"
}

# Times read in the reviewer's own zone, so these cases pin one they can predict.
export TZ=UTC
TODAY=$(jq -rn 'now | strftime("%Y-%m-%d")')

case_ "waiting returns what's at a gate, and nothing else, in two calls"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "project": { "owner": "mentaldesk", "number": 1 } }
JSON
gh_items <<'ITEMS'
Pitched 106 Both gates are mine
In_review 115 I can change any of the keys
Ready 128 Everything waiting on me
Done 99 Already merged
ITEMS
gh_talk <<TALK
106 body 2025-09-19T08:14:00Z reviewer The pitch <!-- a-team:lead -->
115 body ${TODAY}T08:14:00Z reviewer The task <!-- a-team:lead -->
TALK
run board demo waiting
same "exit" 0 "$STATUS"
same "numbers" '[106,115]' "$(jq -c '[.[].number]' "$OUT")"
same "statuses" '["Pitched","In review"]' "$(jq -c '[.[].status]' "$OUT")"
same "fields" '["number","reason","status","team","title","turn","url"]' "$(jq -c '.[0] | keys' "$OUT")"
same "title" '"Both gates are mine"' "$(jq -c '.[0].title' "$OUT")"
same "url" '"https://github.com/mentaldesk/demo/issues/106"' "$(jq -c '.[0].url' "$OUT")"
same "team" '"demo"' "$(jq -c '.[0].team' "$OUT")"
same "api calls" 2 "$(grep -c '' <"$CALLS")"

case_ "a gate nobody has answered is the reviewer's, since the item was opened"
same "pitch turn" '"you"' "$(jq -c '.[0].turn' "$OUT")"
same "pitch reason" '"awaiting your approval since 19 Sep 08:14"' "$(jq -c '.[0].reason' "$OUT")"
same "task turn" '"you"' "$(jq -c '.[1].turn' "$OUT")"
same "task reason" '"awaiting your acceptance since 08:14"' "$(jq -c '.[1].reason' "$OUT")"

case_ "a reviewer comment since the role last spoke makes it the role's turn"
gh_talk <<TALK
106 body ${TODAY}T08:00:00Z reviewer The pitch <!-- a-team:lead -->
106 comment ${TODAY}T09:30:00Z reviewer What about the second gate?
115 body ${TODAY}T08:00:00Z reviewer The task <!-- a-team:lead -->
115 comment ${TODAY}T08:30:00Z reviewer Draft PR #9 is up. <!-- a-team:dev -->
115 comment ${TODAY}T10:15:00Z reviewer This one needs a test.
TALK
run board demo waiting
same "exit" 0 "$STATUS"
same "pitch turn" '"lead"' "$(jq -c '.[0].turn' "$OUT")"
same "pitch reason" '"answering your feedback since 09:30"' "$(jq -c '.[0].reason' "$OUT")"
same "task turn" '"dev"' "$(jq -c '.[1].turn' "$OUT")"
same "task reason" '"answering your feedback since 10:15"' "$(jq -c '.[1].reason' "$OUT")"

case_ "a role that has answered since hands the gate back"
gh_talk <<TALK
106 body ${TODAY}T08:00:00Z reviewer The pitch <!-- a-team:lead -->
106 comment ${TODAY}T09:30:00Z reviewer What about the second gate?
106 comment ${TODAY}T11:00:00Z reviewer Redrafted. <!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z reviewer The task <!-- a-team:lead -->
115 comment ${TODAY}T10:15:00Z reviewer This one needs a test.
115 comment ${TODAY}T11:45:00Z reviewer Added one. <!-- a-team:dev -->
TALK
run board demo waiting
same "exit" 0 "$STATUS"
same "pitch turn" '"you"' "$(jq -c '.[0].turn' "$OUT")"
same "pitch reason" '"awaiting your approval since 11:00"' "$(jq -c '.[0].reason' "$OUT")"
same "task turn" '"you"' "$(jq -c '.[1].turn' "$OUT")"
same "task reason" '"awaiting your acceptance since 11:45"' "$(jq -c '.[1].reason' "$OUT")"

case_ "a comment from anyone but the reviewer is nobody's turn"
gh_talk <<TALK
106 body ${TODAY}T08:00:00Z reviewer The pitch <!-- a-team:lead -->
106 comment ${TODAY}T09:30:00Z passer-by Have you considered doing it differently?
115 body ${TODAY}T08:00:00Z reviewer The task <!-- a-team:lead -->
115 comment ${TODAY}T09:30:00Z passer-by This looks wrong to me.
TALK
run board demo waiting
same "exit" 0 "$STATUS"
same "turns" '["you","you"]' "$(jq -c '[.[].turn]' "$OUT")"

case_ "a team with nothing at a gate waits on nothing, and asks nobody whose turn it is"
gh_items <<'ITEMS'
Ready 128 Everything waiting on me
ITEMS
run board demo waiting
same "exit" 0 "$STATUS"
same "items" '[]' "$(jq -c . "$OUT")"
same "api calls" 1 "$(grep -c '' <"$CALLS")"

case_ "a-team with no command opens the app, and dashboard opens it on the Dashboard"
APP=$(mktemp -d "$WORK/app.XXXXXX")
mkdir -p "$APP/bin" "$APP/scripts" "$APP/libexec"
cp "$ROOT/bin/a-team" "$APP/bin/"
cp "$ROOT"/scripts/*.sh "$APP/scripts/"
cat >"$APP/libexec/a-team-dashboard" <<SH
#!/usr/bin/env bash
printf '%s\n' "\$@" >"$WORK/app-args"
SH
chmod +x "$APP/libexec/a-team-dashboard"

"$APP/bin/a-team" >"$OUT" 2>"$ERR"
STATUS=$?
same "exit" 0 "$STATUS"
same "arguments" "" "$(cat "$WORK/app-args")"

"$APP/bin/a-team" dashboard tuicode >"$OUT" 2>"$ERR"
STATUS=$?
same "exit" 0 "$STATUS"
same "arguments" "--area dashboard tuicode" "$(tr '\n' ' ' <"$WORK/app-args" | sed 's/ $//')"

[ "$failures" -eq 0 ] || { echo "$failures failed"; exit 1; }
echo "all passed"
