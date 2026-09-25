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
# The issue numbers passed as arguments are the ones with a Priority set. An item's labels follow
# its status, as a real board's do: a pitch carries `pitch` from Exploring on, and a task the Dev
# has claimed is the one In progress or In review.
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
                repository: {nameWithOwner: $repo},
                labels: {nodes: (($f[0] | gsub("_"; " ")) as $s
                  | if ["Exploring", "Pitched", "Approved", "Building"] | index($s)
                    then [{name: "pitch"}]
                    elif ["In progress", "In review"] | index($s) then [{name: "a-team:dev"}]
                    else [] end)},
                issueDependenciesSummary: {blockedBy: 0},
                issueFieldValues: {nodes: (if $ranked | index($f[1] | tonumber)
                                           then [{name: "High", field: {name: "Priority"}}] else [] end)}}})
    | {data: {organization: {projectV2: {items: {pageInfo: {hasNextPage: false, endCursor: null}, nodes: .}}}}}' \
    >"$ITEMS"
  TALK="$BIN/talk.json"
  echo '{"data": {"repository": {}}}' >"$TALK"
  ISSUE="$BIN/issue.json" THREAD="$BIN/thread.json"
  LINE="$BIN/line.json" REVIEWS="$BIN/reviews.json" RECENT="$BIN/recent.json"
  ACKED="$BIN/acked" POSTED="$BIN/posted" EMPTY="$BIN/empty.json"
  BLOCKED="$BIN/blocked.json" WRITES="$BIN/writes"
  : >"$ACKED"
  : >"$POSTED"
  : >"$WRITES"
  gh_blocked </dev/null
  FIELDS="$BIN/fields.json"
  jq -n '{data: {organization: {issueFields: {nodes: [{id: "IF_priority", name: "Priority", options: [
    {id: "OP_urgent", name: "Urgent"}, {id: "OP_high", name: "High"},
    {id: "OP_medium", name: "Medium"}, {id: "OP_low", name: "Low"}]}]}}}}' >"$FIELDS"
  echo '[]' >"$EMPTY"
  gh_thread </dev/null
  gh_recent </dev/null
  RUNS="$BIN/runs.json"
  gh_runs <<'RUNS'
completed success 2025-09-19T09:00:00Z build
RUNS
  cat >"$BIN/gh" <<SH
#!/usr/bin/env bash
echo call >>"$CALLS"
case " \$* " in
  *addReaction*) printf '%s\n' "\$@" | sed -n 's/^subject=//p' >>"$ACKED"; echo '{}'; exit 0 ;;
  *updateIssueFieldValue*) printf '%s ' "\$@" | tr -d '\n' >>"$WRITES"; echo >>"$WRITES"; echo '{}'; exit 0 ;;
  *issueFields*) page="$FIELDS" ;;
  *"issue comment"*) cat >"$POSTED"; exit 0 ;;
  *check-runs*) page="$RUNS" ;;
  *issueOrPullRequest*) page="$TALK" ;;
  *reviews*) page="$REVIEWS" ;;
  *"/issues/comments?since"*) page="$RECENT" ;;
  *"comments?since"*) page="$EMPTY" ;;
  *"-X POST"*dependencies/blocked_by*) echo "POST \$*" >>"$WRITES"; echo '{}'; exit 0 ;;
  *"-X DELETE"*dependencies/blocked_by*) echo "DELETE \$*" >>"$WRITES"; echo '{}'; exit 0 ;;
  *dependencies/blocked_by*) page="$BLOCKED" ;;
  *"/issues/"*"/comments"*) page="$THREAD" ;;
  *"/pulls/"*"/comments"*) page="$LINE" ;;
  *"/issues/"[0-9]*) page="$ISSUE" ;;
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

# What blocks a task, from lines of "<n>", each becoming a prerequisite whose id is "DEP_<n>".
gh_blocked() {
  jq -R -s 'split("\n") | map(select(length > 0))
    | map({number: (. | tonumber), id: "DEP_\(.)"})' >"$BLOCKED"
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
# A kind ending `+seen` carries the 👀 a run leaves on a comment it has read.
gh_talk() {
  jq -R -s --arg draft "${1:-false}" --arg mergeable "${2:-MERGEABLE}" '
    def seen: {reactions: {totalCount: (if .seen then 1 else 0 end)}};
    def node($rows; $number):
      ($rows | map(select(.kind == "body")) | first) as $body
      | {number: $number, createdAt: $body.at, body: ($body.body // ""),
         author: {login: ($body.author // "")}, reactions: {totalCount: 0},
         comments: {nodes: ($rows | map(select(.kind == "comment")
           | seen + {createdAt: .at, body: .body, author: {login: .author}}))},
         reviews: {nodes: ($rows | map(select(.kind == "review" or .kind == "line")
           | if .kind == "review"
             then seen + {createdAt: .at, body: .body, state: "COMMENTED",
                   author: {login: .author}, comments: {nodes: []}}
             else {createdAt: .at, body: "", state: "COMMENTED", author: {login: .author},
                   reactions: {totalCount: 0},
                   comments: {nodes: [seen + {createdAt: .at, body: .body,
                                              author: {login: .author}}]}}
             end))}};
    def open_pr($rows; $number): node($rows; $number)
      + {url: "https://github.com/mentaldesk/demo/pull/\($number)", isDraft: ($draft == "true"),
         mergeable: $mergeable, baseRefName: "main", headRefOid: "deadbee"};
    split("\n") | map(select(length > 0)) | map(split(" ") as $f
      | {n: ($f[0] | tonumber), kind: ($f[1] | rtrimstr("+seen")), at: $f[2], author: $f[3],
         body: ($f[4:] | join(" ")), seen: ($f[1] | endswith("+seen"))})
    | group_by(.n) | map(
        (map(select(.kind | startswith("pr-") | not))) as $own
        | (map(select(.kind | startswith("pr-")) | .kind |= ltrimstr("pr-"))) as $pr
        | {key: "x\(.[0].n)",
           value: (node($own; .[0].n) + {closedByPullRequestsReferences: {nodes:
             (if ($pr | length) > 0 then [open_pr($pr; 900 + .[0].n)] else [] end)}})})
    | from_entries | {data: {repository: .}}' >"$TALK"
}

# The whole thread on #7, which `feedback` and `comment` read, from lines of
# "<kind> <timestamp> <author> <eyes> <text...>" — kinds body, comment, review and line, and
# <eyes> the reaction count on it. With `gh_thread pull`, #7 is a PR, so its reviews and line
# comments are read too. Every comment's node id is "IC_<its line>".
gh_thread() {
  local rows
  rows=$(jq -R -s 'split("\n") | map(select(length > 0)) | to_entries
    | map(.key as $i | .value | split(" ") as $f
      | {id: "IC_\($i)", kind: $f[0], at: $f[1], author: $f[2], eyes: ($f[3] | tonumber),
         body: ($f[4:] | join(" ")), url: "https://github.com/mentaldesk/demo/issues/7#\($i)"})')
  local rest='{node_id: .id, user: {login: .author}, created_at: .at, body: .body,
               html_url: .url, reactions: {eyes: .eyes}}'
  jq --arg pull "${1:-}" "(map(select(.kind == \"body\")) | first // {}) | $rest + {id: 4242}
    + (if \$pull == \"pull\" then {pull_request: {}} else {} end)" <<<"$rows" >"$ISSUE"
  jq "[.[] | select(.kind == \"comment\") | $rest]" <<<"$rows" >"$THREAD"
  jq "[.[] | select(.kind == \"line\") | $rest + {path: \"board.sh\", line: 1}]" <<<"$rows" >"$LINE"
  jq '{data: {repository: {pullRequest: {reviews: {nodes:
        [.[] | select(.kind == "review")
         | {id, url, state: "COMMENTED", body, submittedAt: .at,
            author: {login: .author}, reactions: {totalCount: .eyes}}]}}}}}' <<<"$rows" >"$REVIEWS"
}

# The repo-wide conversation comments of the last day that `triggers` reads, from lines of
# "<n> <timestamp> <author> <eyes> <text...>".
gh_recent() {
  jq -R -s 'split("\n") | map(select(length > 0)) | map(split(" ") as $f
    | {issue_url: "https://api.github.com/repos/mentaldesk/demo/issues/\($f[0])",
       user: {login: $f[2]}, created_at: $f[1], body: ($f[4:] | join(" ")),
       reactions: {eyes: ($f[3] | tonumber)}})' >"$RECENT"
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

case_ "a comment a run has read and answered hands the gate back"
gh_talk <<TALK
106 body ${TODAY}T08:00:00Z reviewer The pitch <!-- a-team:lead -->
106 comment+seen ${TODAY}T09:30:00Z reviewer What about the second gate?
106 comment ${TODAY}T11:00:00Z reviewer Redrafted. <!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z reviewer The task <!-- a-team:lead -->
115 comment+seen ${TODAY}T10:15:00Z reviewer This one needs a test.
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
115 pr-comment+seen ${TODAY}T10:15:00Z reviewer This one needs a test.
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

# Ranking: the one field the app writes, and the gate it is the reviewer's alone to clear.
case_ "priority sets the field's own option on the issue, and nothing on the project"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "project": { "owner": "mentaldesk", "number": 1 } }
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
grep -q "issue=IC_0 " "$WRITES" || fail "priority: not the issue's node id in '$(cat "$WRITES")'"
grep -q "field=IF_priority " "$WRITES" || fail "priority: not the field's id in '$(cat "$WRITES")'"
grep -q "option=OP_high " "$WRITES" || fail "priority: not the option's id in '$(cat "$WRITES")'"
grep -q "updateIssueFieldValue" "$WRITES" || fail "priority: not the issue-field mutation"
grep -q "singleSelectOptionId" "$WRITES" || fail "priority: no option in the mutation"

case_ "none clears it rather than setting an option"
: >"$WRITES"
run board demo priority you 6 none
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

# The 👀: a reviewer comment is answered once a run has left one on it, and a run leaves one only
# on what it could have seen. The races replayed here are the ones in pitch #3. $TODAY is on or
# after board.sh's ACK_FROM, so these cases see the 👀 rule and the dated ones below the old.
case_ "comment acks what the run could have seen, and not a comment that arrived while it worked"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer",
  "project": { "owner": "mentaldesk", "number": 1 },
  "wip": { "pitched": 3, "exploring": 4, "ideas": 4 } }
JSON
gh_items <<'ITEMS'
Pitched 7 A pitch in front of me
ITEMS
gh_thread <<TALK
body ${TODAY}T02:10:00Z reviewer 0 The pitch <!-- a-team:lead -->
comment ${TODAY}T02:20:00Z reviewer 0 Needs a second option.
comment ${TODAY}T02:28:46Z reviewer 0 And do the same for subissue links.
TALK
echo "Added one." >"$WORK/reply"
export A_TEAM_RUN_STARTED=${TODAY}T02:21:49Z
run board demo comment lead 7 "$WORK/reply"
same "exit" 0 "$STATUS"
same "acked" "IC_1" "$(cat "$ACKED")"
grep -q '<!-- a-team:lead -->' "$POSTED" || fail "reply: no marker in '$(cat "$POSTED")'"
unset A_TEAM_RUN_STARTED

case_ "the comment that arrived mid-run is still unanswered, however recently the role replied"
gh_thread <<TALK
body ${TODAY}T02:10:00Z reviewer 0 The pitch <!-- a-team:lead -->
comment ${TODAY}T02:20:00Z reviewer 1 Needs a second option.
comment ${TODAY}T02:28:46Z reviewer 0 And do the same for subissue links.
comment ${TODAY}T02:33:03Z reviewer 0 Added one. <!-- a-team:lead -->
TALK
run board demo feedback lead 7
same "exit" 0 "$STATUS"
same "unanswered" '["And do the same for subissue links."]' "$(jq -c '[.[].body]' "$OUT")"

case_ "triggers names it too, so it gets a run of its own"
gh_recent <<RECENT
7 ${TODAY}T02:20:00Z reviewer 1 Needs a second option.
7 ${TODAY}T02:28:46Z reviewer 0 And do the same for subissue links.
7 ${TODAY}T02:33:03Z reviewer 0 Added one. <!-- a-team:lead -->
RECENT
run board demo triggers lead
same "exit" 0 "$STATUS"
same "reasons" "[\"reviewer feedback on #7 (${TODAY}T02:28:46Z)\"]" "$(jq -c .reasons "$OUT")"

case_ "waiting says the same: the gate is the Lead's until the 👀 is there"
gh_talk <<TALK
7 body ${TODAY}T02:10:00Z reviewer The pitch <!-- a-team:lead -->
7 comment+seen ${TODAY}T02:20:00Z reviewer Needs a second option.
7 comment ${TODAY}T02:28:46Z reviewer And do the same for subissue links.
7 comment ${TODAY}T02:33:03Z reviewer Added one. <!-- a-team:lead -->
TALK
run board demo waiting
same "exit" 0 "$STATUS"
same "turn" '"lead"' "$(jq -c '.[0].turn' "$OUT")"
same "reason" '"answering your feedback since 02:28"' "$(jq -c '.[0].reason' "$OUT")"

case_ "and hands it back once the 👀 is"
gh_talk <<TALK
7 body ${TODAY}T02:10:00Z reviewer The pitch <!-- a-team:lead -->
7 comment+seen ${TODAY}T02:20:00Z reviewer Needs a second option.
7 comment+seen ${TODAY}T02:28:46Z reviewer And do the same for subissue links.
7 comment ${TODAY}T02:33:03Z reviewer Added one. <!-- a-team:lead -->
TALK
run board demo waiting
same "exit" 0 "$STATUS"
same "turn" '"you"' "$(jq -c '.[0].turn' "$OUT")"
same "reason" '"awaiting your approval since 02:33"' "$(jq -c '.[0].reason' "$OUT")"

case_ "a comment older than ACK_FROM with no 👀 keeps the watermark rule, so an upgrade reopens nothing"
gh_thread <<'TALK'
body 2026-09-20T02:10:00Z reviewer 0 The pitch <!-- a-team:lead -->
comment 2026-09-20T02:28:46Z reviewer 0 And do the same for subissue links.
comment 2026-09-20T02:33:03Z reviewer 0 Added one. <!-- a-team:lead -->
TALK
run board demo feedback lead 7
same "exit" 0 "$STATUS"
same "unanswered" '[]' "$(jq -c . "$OUT")"

case_ "an older comment the role never answered is still returned"
gh_thread <<'TALK'
body 2026-09-20T02:10:00Z reviewer 0 The pitch <!-- a-team:lead -->
comment 2026-09-20T02:33:03Z reviewer 0 Drafted. <!-- a-team:lead -->
comment 2026-09-20T02:40:00Z reviewer 0 Still needs a second option.
TALK
run board demo feedback lead 7
same "exit" 0 "$STATUS"
same "unanswered" '["Still needs a second option."]' "$(jq -c '[.[].body]' "$OUT")"

case_ "a review and a line comment on a PR are acked like any other comment"
gh_thread pull <<TALK
body ${TODAY}T08:00:00Z reviewer 0 Closes #7 <!-- a-team:dev -->
review ${TODAY}T09:00:00Z reviewer 0 Nearly there.
line ${TODAY}T09:10:00Z reviewer 0 This name reads oddly.
TALK
: >"$ACKED"
export A_TEAM_RUN_STARTED=${TODAY}T09:30:00Z
run board demo comment dev 7 "$WORK/reply"
same "exit" 0 "$STATUS"
same "acked" "IC_1 IC_2" "$(tr '\n' ' ' <"$ACKED" | sed 's/ $//')"
unset A_TEAM_RUN_STARTED

case_ "A_TEAM_RUN_STARTED unset acks the whole thread: whoever ran it by hand has just read it"
: >"$ACKED"
run board demo comment dev 7 "$WORK/reply"
same "exit" 0 "$STATUS"
same "acked" "IC_1 IC_2" "$(tr '\n' ' ' <"$ACKED" | sed 's/ $//')"

case_ "a comment already carrying a 👀 is not acked again"
gh_thread pull <<TALK
body ${TODAY}T08:00:00Z reviewer 0 Closes #7 <!-- a-team:dev -->
review ${TODAY}T09:00:00Z reviewer 1 Nearly there.
line ${TODAY}T09:10:00Z reviewer 0 This name reads oddly.
TALK
: >"$ACKED"
run board demo comment dev 7 "$WORK/reply"
same "exit" 0 "$STATUS"
same "acked" "IC_2" "$(cat "$ACKED")"

case_ "a comment from anyone but the reviewer is not acked"
gh_thread <<TALK
body ${TODAY}T08:00:00Z reviewer 0 The pitch <!-- a-team:lead -->
comment ${TODAY}T09:00:00Z passer-by 0 Have you considered doing it differently?
TALK
: >"$ACKED"
run board demo comment lead 7 "$WORK/reply"
same "exit" 0 "$STATUS"
same "acked" "" "$(cat "$ACKED")"

case_ "skip acks too, or the Idea it just passed over would be back in the running at once"
gh_items <<'ITEMS'
Idea 7 An Idea with nothing to pitch in it
ITEMS
gh_thread <<TALK
body ${TODAY}T08:00:00Z reviewer 0 The idea <!-- a-team:lead -->
comment ${TODAY}T09:00:00Z reviewer 0 Worth a look.
TALK
: >"$ACKED"
run board demo skip lead 7 "$WORK/reply"
same "exit" 0 "$STATUS"
same "acked" "IC_1" "$(cat "$ACKED")"

case_ "--dry-run says which reactions it would add and adds none"
gh_thread <<TALK
body ${TODAY}T08:00:00Z reviewer 0 The pitch <!-- a-team:lead -->
comment ${TODAY}T09:00:00Z reviewer 0 Needs a second option.
TALK
: >"$ACKED"
: >"$POSTED"
run board --dry-run demo comment lead 7 "$WORK/reply"
same "exit" 0 "$STATUS"
same "acked" "" "$(cat "$ACKED")"
same "posted" "" "$(cat "$POSTED")"
grep -q "would add 👀 to your comment of ${TODAY}T09:00:00Z on #7" "$ERR" || fail "dry run: nothing about the 👀 in '$(cat "$ERR")'"

case_ "either role may block either task, across pitches and at any status"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "project": { "owner": "mentaldesk", "number": 1 } }
JSON
gh_items <<'ITEMS'
Building 10 A pitch
Building 20 Another pitch
Ready 11 A task of the first pitch
In_progress 21 A task of the second pitch
ITEMS
run board demo depends lead 11 21 "both rewrite the same view"
same "exit" 0 "$STATUS"
same "said" "#11 is now blocked by #21, and said why on #11" "$(cat "$OUT")"
grep -q "^POST .*/issues/11/dependencies/blocked_by" "$WRITES" || fail "depends: no POST in '$(cat "$WRITES")'"
grep -q "^Blocked by #21: both rewrite the same view$" "$POSTED" || fail "depends: no reason in '$(cat "$POSTED")'"
grep -q '<!-- a-team:lead -->' "$POSTED" || fail "depends: no lead marker in '$(cat "$POSTED")'"

case_ "the Dev may block its own In progress task, and the comment carries the Dev's marker"
: >"$WRITES"
run board demo depends dev 21 11 "taking #11 first; both are in WaitingView"
same "exit" 0 "$STATUS"
same "said" "#21 is now blocked by #11, and said why on #21" "$(cat "$OUT")"
grep -q "^POST .*/issues/21/dependencies/blocked_by" "$WRITES" || fail "depends: no POST in '$(cat "$WRITES")'"
grep -q '<!-- a-team:dev -->' "$POSTED" || fail "depends: no dev marker in '$(cat "$POSTED")'"

case_ "either role may undo it again, wherever it was drawn"
gh_blocked <<'DEPS'
21
DEPS
: >"$WRITES"
run board demo undepend dev 11 21 "looked again: #11 is in DashboardSettings"
same "exit" 0 "$STATUS"
same "said" "#11 is no longer blocked by #21, and said why on #11" "$(cat "$OUT")"
same "writes" "DELETE" "$(cut -d' ' -f1 <"$WRITES")"
grep -q "/issues/11/dependencies/blocked_by/DEP_21" "$WRITES" || fail "undepend: wrong url in '$(cat "$WRITES")'"
grep -q "^No longer blocked by #21: looked again: #11 is in DashboardSettings$" "$POSTED" ||
  fail "undepend: no reason in '$(cat "$POSTED")'"
grep -q '<!-- a-team:dev -->' "$POSTED" || fail "undepend: no dev marker in '$(cat "$POSTED")'"
run board demo undepend lead 11 21 "the Lead can undo the Dev's block too"
same "exit" 0 "$STATUS"
grep -q '<!-- a-team:lead -->' "$POSTED" || fail "undepend: no lead marker in '$(cat "$POSTED")'"

case_ "an unknown role is refused, in one line, by both verbs"
run board demo depends nobody 11 21 "why"
failed "depends role"
one_line "depends role"
grep -q "unknown role 'nobody' (lead | dev)" "$ERR" || fail "depends role: '$(cat "$ERR")'"
run board demo undepend nobody 11 21 "why"
failed "undepend role"
one_line "undepend role"
grep -q "unknown role 'nobody' (lead | dev)" "$ERR" || fail "undepend role: '$(cat "$ERR")'"

case_ "undepend on a pair that isn't linked is refused, in one line"
gh_blocked </dev/null
: >"$WRITES"
run board demo undepend lead 11 21 "not needed"
failed "undepend unlinked"
one_line "undepend unlinked"
grep -q "#11 is not blocked by #21" "$ERR" || fail "undepend unlinked: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"

case_ "the wrong number of arguments shows the new signature, in one line"
run board demo depends 11 21
failed "depends usage"
one_line "depends usage"
grep -qF 'usage: board.sh demo depends <role> <task> <prerequisite> "<why>"' "$ERR" ||
  fail "depends usage: '$(cat "$ERR")'"
run board demo undepend lead 11 21
failed "undepend usage"
one_line "undepend usage"
grep -qF 'usage: board.sh demo undepend <role> <task> <prerequisite> "<why>"' "$ERR" ||
  fail "undepend usage: '$(cat "$ERR")'"

case_ "--dry-run gives the same verdicts, and neither the dependency nor the comment is created"
: >"$POSTED"
: >"$WRITES"
run board --dry-run demo depends lead 11 21 "both rewrite the same view"
same "exit" 0 "$STATUS"
same "said" "(dry run) #11 is now blocked by #21, and said why on #11" "$(cat "$OUT")"
grep -q "Blocked by #21: both rewrite the same view" "$ERR" || fail "dry run: no comment in '$(cat "$ERR")'"
gh_blocked <<'DEPS'
21
DEPS
run board --dry-run demo undepend lead 11 21 "looked again"
same "exit" 0 "$STATUS"
same "said" "(dry run) #11 is no longer blocked by #21, and said why on #11" "$(cat "$OUT")"
grep -q "No longer blocked by #21: looked again" "$ERR" || fail "dry run: no comment in '$(cat "$ERR")'"
same "posted" "" "$(cat "$POSTED")"
same "writes" "" "$(cat "$WRITES")"

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
