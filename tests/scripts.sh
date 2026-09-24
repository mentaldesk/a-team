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
# The issue numbers passed as arguments are the ones with a Priority set.
gh_items() {
  BIN=$(mktemp -d "$WORK/bin.XXXXXX")
  ITEMS="$BIN/items.json"
  CALLS="$BIN/calls"
  jq -R -s --arg repo mentaldesk/demo \
    --argjson ranked "$(printf '%s\n' "$@" | jq -R . | jq -s 'map(select(length > 0) | tonumber)')" '
    split("\n") | map(select(length > 0)) | map(split(" ") as $f | {
      id: "PVTI_\($f[1])",
      fieldValueByName: {name: ($f[0] | gsub("_"; " "))},
      content: {__typename: "Issue", number: ($f[1] | tonumber), title: ($f[2:] | join(" ")),
                url: "https://github.com/\($repo)/issues/\($f[1])",
                repository: {nameWithOwner: $repo}, labels: {nodes: []},
                issueDependenciesSummary: {blockedBy: 0},
                issueFieldValues: {nodes: (if $ranked | index($f[1] | tonumber)
                                           then [{name: "High", field: {name: "Priority"}}] else [] end)}}})
    | {data: {organization: {projectV2: {items: {pageInfo: {hasNextPage: false, endCursor: null}, nodes: .}}}}}' \
    >"$ITEMS"
  TALK="$BIN/talk.json"
  echo '{"data": {"repository": {}}}' >"$TALK"
  RUNS="$BIN/runs.json"
  gh_runs <<'RUNS'
completed success 2025-09-19T09:00:00Z build
RUNS
  cat >"$BIN/gh" <<SH
#!/usr/bin/env bash
echo call >>"$CALLS"
case " \$* " in
  *check-runs*) page="$RUNS" ;;
  *issueOrPullRequest*) page="$TALK" ;;
  *) page="$ITEMS" ;;
esac
filter=
while [ \$# -gt 0 ]; do
  [ "\$1" = --jq ] && { filter=\$2; break; }
  shift
done
if [ -n "\$filter" ]; then jq -r "\$filter" "\$page"; else cat "\$page"; fi
SH
  chmod +x "$BIN/gh"
  PATH="$BIN:$PATH"
}

# The check runs on a PR's head commit, from lines of "<status> <conclusion> <finished> <name>",
# with "-" where a run of that status has no such field.
gh_runs() {
  jq -R -s 'split("\n") | map(select(length > 0)) | map(split(" ") as $f
    | {status: $f[0], name: $f[3], html_url: "https://github.com/mentaldesk/demo/runs/1",
       conclusion: (if $f[1] == "-" then null else $f[1] end),
       completed_at: (if $f[2] == "-" then null else $f[2] end)})
    | {check_runs: .}' >"$RUNS"
}

# The one GraphQL page `waiting` reads for whose turn it is, from lines of
# "<n> <kind> <timestamp> <author> <text...>". The body line is the item's own; the pr-* kinds
# (pr-body, pr-comment, pr-review, pr-line) belong to the open PR that closes #<n>, which is
# numbered 900 + n and is ready and mergeable unless `gh_talk <draft> <mergeable>` says otherwise.
gh_talk() {
  jq -R -s --arg draft "${1:-false}" --arg mergeable "${2:-MERGEABLE}" '
    def node($rows; $number):
      ($rows | map(select(.kind == "body")) | first) as $body
      | {number: $number, createdAt: $body.at, body: ($body.body // ""),
         author: {login: ($body.author // "")},
         comments: {nodes: ($rows | map(select(.kind == "comment")
           | {createdAt: .at, body: .body, author: {login: .author}}))},
         reviews: {nodes: ($rows | map(select(.kind == "review" or .kind == "line")
           | if .kind == "review"
             then {createdAt: .at, body: .body, state: "COMMENTED",
                   author: {login: .author}, comments: {nodes: []}}
             else {createdAt: .at, body: "", state: "COMMENTED", author: {login: .author},
                   comments: {nodes: [{createdAt: .at, body: .body, author: {login: .author}}]}}
             end))}};
    def open_pr($rows; $number): node($rows; $number)
      + {url: "https://github.com/mentaldesk/demo/pull/\($number)", isDraft: ($draft == "true"),
         mergeable: $mergeable, baseRefName: "main", headRefOid: "deadbee"};
    split("\n") | map(select(length > 0)) | map(split(" ") as $f
      | {n: ($f[0] | tonumber), kind: $f[1], at: $f[2], author: $f[3], body: ($f[4:] | join(" "))})
    | group_by(.n) | map(
        (map(select(.kind | startswith("pr-") | not))) as $own
        | (map(select(.kind | startswith("pr-")) | .kind |= ltrimstr("pr-"))) as $pr
        | {key: "x\(.[0].n)",
           value: (node($own; .[0].n) + {closedByPullRequestsReferences: {nodes:
             (if ($pr | length) > 0 then [open_pr($pr; 900 + .[0].n)] else [] end)}})})
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
same "fields" '["number","priority","reason","status","team","title","turn","url"]' "$(jq -c '.[0] | keys' "$OUT")"
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

case_ "the reviewer answering on the task's PR rather than on the task is still the Dev's turn"
gh_talk <<TALK
106 body ${TODAY}T08:00:00Z reviewer The pitch <!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z reviewer The task <!-- a-team:lead -->
115 comment ${TODAY}T08:30:00Z reviewer Draft PR #9 is up. <!-- a-team:dev -->
115 pr-body ${TODAY}T08:25:00Z reviewer Closes #115 <!-- a-team:dev -->
115 pr-comment ${TODAY}T10:15:00Z reviewer This one needs a test.
TALK
: >"$CALLS"
run board demo waiting
same "exit" 0 "$STATUS"
same "task turn" '"dev"' "$(jq -c '.[1].turn' "$OUT")"
same "task reason" '"answering your feedback since 10:15"' "$(jq -c '.[1].reason' "$OUT")"
same "api calls" 3 "$(grep -c '' <"$CALLS")"

case_ "a review and a line comment on that PR count as feedback too"
gh_talk <<TALK
106 body ${TODAY}T08:00:00Z reviewer The pitch <!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z reviewer The task <!-- a-team:lead -->
115 pr-body ${TODAY}T08:25:00Z reviewer Closes #115 <!-- a-team:dev -->
115 pr-review ${TODAY}T09:00:00Z reviewer Nearly there.
115 pr-line ${TODAY}T10:45:00Z reviewer This name reads oddly.
TALK
run board demo waiting
same "exit" 0 "$STATUS"
same "task turn" '"dev"' "$(jq -c '.[1].turn' "$OUT")"
same "task reason" '"answering your feedback since 10:45"' "$(jq -c '.[1].reason' "$OUT")"

case_ "the Dev answering on the PR hands the task back"
gh_talk <<TALK
106 body ${TODAY}T08:00:00Z reviewer The pitch <!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z reviewer The task <!-- a-team:lead -->
115 pr-body ${TODAY}T08:25:00Z reviewer Closes #115 <!-- a-team:dev -->
115 pr-comment ${TODAY}T10:15:00Z reviewer This one needs a test.
115 pr-comment ${TODAY}T11:45:00Z reviewer Added one. <!-- a-team:dev -->
TALK
run board demo waiting
same "exit" 0 "$STATUS"
same "task turn" '"you"' "$(jq -c '.[1].turn' "$OUT")"
same "task reason" '"awaiting your acceptance since 11:45"' "$(jq -c '.[1].reason' "$OUT")"

case_ "an In review item carries its open PR, and a Pitched one carries none"
gh_talk <<TALK
106 body ${TODAY}T08:00:00Z reviewer The pitch <!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z reviewer The task <!-- a-team:lead -->
115 pr-body ${TODAY}T08:25:00Z reviewer Closes #115 <!-- a-team:dev -->
TALK
run board demo waiting
same "exit" 0 "$STATUS"
same "pitch fields" '["number","priority","reason","status","team","title","turn","url"]' "$(jq -c '.[0] | keys' "$OUT")"
same "task fields" \
  '["checks","conflicting","draft","number","pr","prUrl","priority","reason","status","team","title","turn","url"]' \
  "$(jq -c '.[1] | keys' "$OUT")"
same "pr" 1015 "$(jq -c '.[1].pr' "$OUT")"
same "prUrl" '"https://github.com/mentaldesk/demo/pull/1015"' "$(jq -c '.[1].prUrl' "$OUT")"

case_ "a green, mergeable, ready PR with nothing unanswered stays the reviewer's"
same "checks" '"pass"' "$(jq -c '.[1].checks' "$OUT")"
same "conflicting" false "$(jq -c '.[1].conflicting' "$OUT")"
same "draft" false "$(jq -c '.[1].draft' "$OUT")"
same "task turn" '"you"' "$(jq -c '.[1].turn' "$OUT")"
same "task reason" '"awaiting your acceptance since 08:25"' "$(jq -c '.[1].reason' "$OUT")"

case_ "a PR whose CI is failing is the Dev's turn, since the run finished"
gh_runs <<RUNS
completed failure ${TODAY}T09:02:00Z build
RUNS
run board demo waiting
same "exit" 0 "$STATUS"
same "checks" '"fail"' "$(jq -c '.[1].checks' "$OUT")"
same "task turn" '"dev"' "$(jq -c '.[1].turn' "$OUT")"
same "task trouble" '"CI failing"' "$(jq -c '.[1].trouble' "$OUT")"
same "task reason" '"CI failing since 09:02"' "$(jq -c '.[1].reason' "$OUT")"

case_ "an unanswered comment outranks the failing build it hasn't been answered with"
gh_talk <<TALK
106 body ${TODAY}T08:00:00Z reviewer The pitch <!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z reviewer The task <!-- a-team:lead -->
115 pr-body ${TODAY}T08:25:00Z reviewer Closes #115 <!-- a-team:dev -->
115 pr-comment ${TODAY}T10:15:00Z reviewer This one needs a test.
TALK
run board demo waiting
same "exit" 0 "$STATUS"
same "checks" '"fail"' "$(jq -c '.[1].checks' "$OUT")"
same "task turn" '"dev"' "$(jq -c '.[1].turn' "$OUT")"
same "task trouble" null "$(jq -c '.[1].trouble' "$OUT")"
same "task reason" '"answering your feedback since 10:15"' "$(jq -c '.[1].reason' "$OUT")"

case_ "a PR that conflicts with its base is the Dev's turn"
gh_runs <<RUNS
completed success ${TODAY}T09:00:00Z build
RUNS
gh_talk false CONFLICTING <<TALK
106 body ${TODAY}T08:00:00Z reviewer The pitch <!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z reviewer The task <!-- a-team:lead -->
115 pr-body ${TODAY}T08:25:00Z reviewer Closes #115 <!-- a-team:dev -->
TALK
run board demo waiting
same "exit" 0 "$STATUS"
same "conflicting" true "$(jq -c '.[1].conflicting' "$OUT")"
same "task turn" '"dev"' "$(jq -c '.[1].turn' "$OUT")"
same "task reason" '"conflicts with main"' "$(jq -c '.[1].reason' "$OUT")"

case_ "a PR GitHub hasn't worked the conflict out for yet is nobody's fault"
gh_talk false UNKNOWN <<TALK
106 body ${TODAY}T08:00:00Z reviewer The pitch <!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z reviewer The task <!-- a-team:lead -->
115 pr-body ${TODAY}T08:25:00Z reviewer Closes #115 <!-- a-team:dev -->
TALK
run board demo waiting
same "exit" 0 "$STATUS"
same "conflicting" false "$(jq -c '.[1].conflicting' "$OUT")"
same "task turn" '"you"' "$(jq -c '.[1].turn' "$OUT")"

case_ "a PR still in draft is the Dev's turn"
gh_talk true <<TALK
106 body ${TODAY}T08:00:00Z reviewer The pitch <!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z reviewer The task <!-- a-team:lead -->
115 pr-body ${TODAY}T08:25:00Z reviewer Closes #115 <!-- a-team:dev -->
TALK
run board demo waiting
same "exit" 0 "$STATUS"
same "draft" true "$(jq -c '.[1].draft' "$OUT")"
same "task turn" '"dev"' "$(jq -c '.[1].turn' "$OUT")"
same "task reason" '"still a draft"' "$(jq -c '.[1].reason' "$OUT")"

case_ "a task with no PR yet waits on the reviewer since the task was opened"
gh_talk <<TALK
106 body ${TODAY}T08:00:00Z reviewer The pitch <!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z reviewer The task <!-- a-team:lead -->
TALK
run board demo waiting
same "exit" 0 "$STATUS"
same "task turn" '"you"' "$(jq -c '.[1].turn' "$OUT")"
same "task reason" '"awaiting your acceptance since 08:00"' "$(jq -c '.[1].reason' "$OUT")"

case_ "a team with nothing at a gate waits on nothing, and asks nobody whose turn it is"
gh_items <<'ITEMS'
Ready 128 Everything waiting on me
ITEMS
run board demo waiting
same "exit" 0 "$STATUS"
same "items" '[]' "$(jq -c . "$OUT")"
same "api calls" 1 "$(grep -c '' <"$CALLS")"

case_ "a gated item carries the Priority the cards colour its number by"
gh_items 106 <<'ITEMS'
Pitched 106 Both gates are mine
In_review 115 I can change any of the keys
ITEMS
gh_talk <<TALK
106 body ${TODAY}T08:00:00Z reviewer The pitch <!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z reviewer The task <!-- a-team:lead -->
TALK
run board demo waiting
same "exit" 0 "$STATUS"
same "priorities" '["High",null]' "$(jq -c '[.[].priority]' "$OUT")"

case_ "the Ideas with no Priority are waiting on the reviewer to rank them"
gh_items 26 <<'ITEMS'
Pitched 106 Both gates are mine
Idea 6 The agents can't say what they'd change
Idea 26 A pitch I've shelved
In_review 115 I can change any of the keys
Ready 128 Everything waiting on me
ITEMS
gh_talk <<TALK
106 body ${TODAY}T08:00:00Z reviewer The pitch <!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z reviewer The task <!-- a-team:lead -->
TALK
run board demo waiting
same "exit" 0 "$STATUS"
same "numbers" '[106,115,6]' "$(jq -c '[.[].number]' "$OUT")"
same "idea status" '"Idea"' "$(jq -c '.[2].status' "$OUT")"
same "idea turn" '"you"' "$(jq -c '.[2].turn' "$OUT")"
same "idea reason" '"waiting to be ranked"' "$(jq -c '.[2].reason' "$OUT")"
same "idea fields" '["number","reason","status","team","title","turn","url"]' "$(jq -c '.[2] | keys' "$OUT")"
same "idea url" '"https://github.com/mentaldesk/demo/issues/6"' "$(jq -c '.[2].url' "$OUT")"
same "api calls" 2 "$(grep -c '' <"$CALLS")"

case_ "a team whose every Idea is ranked has none of them waiting"
gh_items 6 26 <<'ITEMS'
Idea 6 The agents can't say what they'd change
Idea 26 A pitch I've shelved
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
