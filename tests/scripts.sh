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
  META="$BIN/meta.json"
  jq -n '{data: {organization: {projectV2: {id: "PVT_1", field: {id: "PVTSSF_status", options: [
    {id: "OPT_exploring", name: "Exploring"}, {id: "OPT_pitched", name: "Pitched"},
    {id: "OPT_approved", name: "Approved"}]}}}}}' >"$META"
  gh_thread </dev/null
  gh_recent </dev/null
  RUNS="$BIN/runs.json"
  gh_runs <<'RUNS'
completed success 2025-09-19T09:00:00Z build
RUNS
  PRS="$BIN/prs.json" PULL="$BIN/pull.json"
  gh_pr
  echo '{"head": {"sha": "deadbeefcafe"}}' >"$PULL"
  cat >"$BIN/gh" <<SH
#!/usr/bin/env bash
echo call >>"$CALLS"
case " \$* " in
  *addReaction*) printf '%s\n' "\$@" | sed -n 's/^subject=//p' >>"$ACKED"; echo '{}'; exit 0 ;;
  *updateIssueFieldValue*) printf '%s ' "\$@" | tr -d '\n' >>"$WRITES"; echo >>"$WRITES"; echo '{}'; exit 0 ;;
  *updateProjectV2ItemFieldValue*) printf '%s ' "\$@" | tr -d '\n' >>"$WRITES"; echo >>"$WRITES"; echo '{}'; exit 0 ;;
  *ProjectV2SingleSelectField*) page="$META" ;;
  *issueFields*) page="$FIELDS" ;;
  *": issue(number"*) jq '{data: {repository: ([.data.organization.projectV2.items.nodes[].content
                        | {key: "i\(.number)", value: {issueFieldValues}}] | from_entries)}}' "$ITEMS"; exit 0 ;;
  *"issue comment"*) cat >"$POSTED"; exit 0 ;;
  *"issue edit"*) echo "\$*" >>"$WRITES"; exit 0 ;;
  *"label list"*) page="$EMPTY" ;;
  *check-runs*) page="$RUNS" ;;
  *issueOrPullRequest*) page="$TALK" ;;
  *closedByPullRequestsReferences*) page="$PRS" ;;
  *reviews*) page="$REVIEWS" ;;
  *"/issues/comments?since"*) page="$RECENT" ;;
  *"comments?since"*) page="$EMPTY" ;;
  *"-X POST"*dependencies/blocked_by*) echo "POST \$*" >>"$WRITES"; echo '{}'; exit 0 ;;
  *"-X DELETE"*dependencies/blocked_by*) echo "DELETE \$*" >>"$WRITES"; echo '{}'; exit 0 ;;
  *dependencies/blocked_by*) page="$BLOCKED" ;;
  *"/issues/"*"/comments"*) page="$THREAD" ;;
  *"/pulls/"*"/comments"*) page="$LINE" ;;
  *"/issues/404"*) echo "gh: Not Found (HTTP 404)" >&2; exit 1 ;;
  *"/pulls/"[0-9]*) page="$PULL" ;;
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

# Applies a jq update to the content of #<n> on the page gh_items wrote.
edit_item() {
  jq --argjson n "$1" "(.data.organization.projectV2.items.nodes[].content | select(.number == \$n)) |= ($2)" \
    "$ITEMS" >"$ITEMS.new" && mv "$ITEMS.new" "$ITEMS"
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

# `gh_pr <number> <draft>`: the open PR that closes every issue `pr` asks about. No arguments, none.
gh_pr() {
  jq -n --arg n "${1:-}" --arg draft "${2:-true}" '{data: {repository: {issue: {closedByPullRequestsReferences: {nodes:
    (if $n == "" then [] else [{number: ($n | tonumber), url: "https://github.com/mentaldesk/demo/pull/\($n)",
       isDraft: ($draft == "true"), headRefName: "task", mergeable: "MERGEABLE"}] end)}}}}}' >"$PRS"
}

# The one GraphQL page `waiting` reads for whose turn it is, from lines of
# "<n> <kind> <timestamp> <author> <text...>". The body line is the item's own; the pr-* kinds
# (pr-body, pr-comment, pr-review, pr-line) belong to the open PR that closes #<n>, which is
# numbered 900 + n and is ready and mergeable unless `gh_talk <draft> <mergeable>` says otherwise.
# A kind ending `+seen` carries the 👀 a run leaves on a comment it has read, and `\n` in the
# text is a line break.
gh_talk() {
  jq -R -s --arg draft "${1:-false}" --arg mergeable "${2:-MERGEABLE}" '
    def who($login): if $login | endswith("[bot]")
      then {__typename: "Bot", login: ($login | rtrimstr("[bot]"))} else {__typename: "User", login: $login} end;
    def seen: {reactions: {totalCount: (if .seen then 1 else 0 end)}};
    def node($rows; $number):
      ($rows | map(select(.kind == "body")) | first) as $body
      | {number: $number, createdAt: $body.at, body: ($body.body // ""),
         author: who($body.author // ""), reactions: {totalCount: 0},
         comments: {nodes: ($rows | map(select(.kind == "comment")
           | seen + {createdAt: .at, body: .body, author: who(.author)}))},
         reviews: {nodes: ($rows | map(select(.kind == "review" or .kind == "line")
           | if .kind == "review"
             then seen + {createdAt: .at, body: .body, state: "COMMENTED",
                   author: who(.author), comments: {nodes: []}}
             else {createdAt: .at, body: "", state: "COMMENTED", author: who(.author),
                   reactions: {totalCount: 0},
                   comments: {nodes: [seen + {createdAt: .at, body: .body,
                                              author: who(.author)}]}}
             end))}};
    def open_pr($rows; $number): node($rows; $number)
      + {url: "https://github.com/mentaldesk/demo/pull/\($number)", isDraft: ($draft == "true"),
         mergeable: $mergeable, baseRefName: "main", headRefOid: "deadbee"};
    split("\n") | map(select(length > 0)) | map(split(" ") as $f
      | {n: ($f[0] | tonumber), kind: ($f[1] | rtrimstr("+seen")), at: $f[2], author: $f[3],
         body: ($f[4:] | join(" ") | split("\\n") | join("\n")),
         seen: ($f[1] | endswith("+seen"))})
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
# comments are read too. Every comment's node id is "IC_<its line>", and `\n` in the text is a
# line break.
gh_thread() {
  local rows
  rows=$(jq -R -s 'split("\n") | map(select(length > 0)) | to_entries
    | map(.key as $i | .value | split(" ") as $f
      | {id: "IC_\($i)", kind: $f[0], at: $f[1], author: $f[2], eyes: ($f[3] | tonumber),
         body: ($f[4:] | join(" ") | split("\\n") | join("\n")),
         url: "https://github.com/mentaldesk/demo/issues/7#\($i)"})')
  local rest='{node_id: .id, user: {login: .author}, created_at: .at, body: .body,
               html_url: .url, reactions: {eyes: .eyes}}'
  jq --arg pull "${1:-}" "(map(select(.kind == \"body\")) | first // {})
    | $rest + {id: 4242, number: 7, title: \"The whole thread\"}
    + (if \$pull == \"pull\" then {pull_request: {}} else {} end)" <<<"$rows" >"$ISSUE"
  jq "[.[] | select(.kind == \"comment\") | $rest]" <<<"$rows" >"$THREAD"
  jq "[.[] | select(.kind == \"line\") | $rest + {path: \"board.sh\", line: 1}]" <<<"$rows" >"$LINE"
  jq '{data: {repository: {pullRequest: {reviews: {nodes:
        [.[] | select(.kind == "review")
         | {id, url, state: "COMMENTED", body, submittedAt: .at,
            author: (if .author | endswith("[bot]") then {__typename: "Bot", login: (.author | rtrimstr("[bot]"))}
                     else {__typename: "User", login: .author} end),
            reactions: {totalCount: .eyes}}]}}}}}' <<<"$rows" >"$REVIEWS"
}

# The repo-wide conversation comments of the last day that `triggers` reads, from lines of
# "<n> <timestamp> <author> <eyes> <text...>", where `\n` in the text is a line break.
gh_recent() {
  jq -R -s 'split("\n") | map(select(length > 0)) | map(split(" ") as $f
    | {issue_url: "https://api.github.com/repos/mentaldesk/demo/issues/\($f[0])",
       user: {login: $f[2]}, created_at: $f[1],
       body: ($f[4:] | join(" ") | split("\\n") | join("\n")),
       reactions: {eyes: ($f[3] | tonumber)}})' >"$RECENT"
}

# Times read in the reviewer's own zone, so these cases pin one they can predict.
export TZ=UTC
TODAY=$(jq -rn 'now | strftime("%Y-%m-%d")')

# ago <minutes>: a timestamp that long before now, for the cases that meet board.sh's own clock.
ago() { jq -rn --argjson m "$1" 'now - $m * 60 | strftime("%Y-%m-%dT%H:%M:%SZ")'; }

case_ "waiting returns what's at a gate, and nothing else, in two calls"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 } }
JSON
gh_items <<'ITEMS'
Pitched 106 Both gates are mine
In_review 115 I can change any of the keys
Ready 128 Everything waiting on me
Done 99 Already merged
ITEMS
gh_talk <<TALK
106 body 2025-09-19T08:14:00Z reviewer The pitch\n<!-- a-team:lead -->
115 body ${TODAY}T08:14:00Z demo-app[bot] The task\n<!-- a-team:lead -->
TALK
run board demo waiting
same "exit" 0 "$STATUS"
same "numbers" '[106,115]' "$(jq -c '[.[].number]' "$OUT")"
same "statuses" '["Pitched","In review"]' "$(jq -c '[.[].status]' "$OUT")"
same "fields" '["number","pitch","priority","reason","status","team","title","turn","url"]' "$(jq -c '.[0] | keys' "$OUT")"
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
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
106 comment ${TODAY}T09:30:00Z reviewer What about the second gate?
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 comment ${TODAY}T08:30:00Z demo-app[bot] Draft PR #9 is up.\n<!-- a-team:dev -->
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
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
106 comment+seen ${TODAY}T09:30:00Z reviewer What about the second gate?
106 comment ${TODAY}T11:00:00Z demo-app[bot] Redrafted.\n<!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 comment+seen ${TODAY}T10:15:00Z reviewer This one needs a test.
115 comment ${TODAY}T11:45:00Z demo-app[bot] Added one.\n<!-- a-team:dev -->
TALK
run board demo waiting
same "exit" 0 "$STATUS"
same "pitch turn" '"you"' "$(jq -c '.[0].turn' "$OUT")"
same "pitch reason" '"awaiting your approval since 11:00"' "$(jq -c '.[0].reason' "$OUT")"
same "task turn" '"you"' "$(jq -c '.[1].turn' "$OUT")"
same "task reason" '"awaiting your acceptance since 11:45"' "$(jq -c '.[1].reason' "$OUT")"

case_ "a comment from anyone but the reviewer is nobody's turn"
gh_talk <<TALK
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
106 comment ${TODAY}T09:30:00Z passer-by Have you considered doing it differently?
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 comment ${TODAY}T09:30:00Z passer-by This looks wrong to me.
TALK
run board demo waiting
same "exit" 0 "$STATUS"
same "turns" '["you","you"]' "$(jq -c '[.[].turn]' "$OUT")"

case_ "the reviewer answering on the task's PR rather than on the task is still the Dev's turn"
gh_talk <<TALK
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 comment ${TODAY}T08:30:00Z demo-app[bot] Draft PR #9 is up.\n<!-- a-team:dev -->
115 pr-body ${TODAY}T08:25:00Z demo-app[bot] Closes #115\n<!-- a-team:dev -->
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
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 pr-body ${TODAY}T08:25:00Z demo-app[bot] Closes #115\n<!-- a-team:dev -->
115 pr-review ${TODAY}T09:00:00Z reviewer Nearly there.
115 pr-line ${TODAY}T10:45:00Z reviewer This name reads oddly.
TALK
run board demo waiting
same "exit" 0 "$STATUS"
same "task turn" '"dev"' "$(jq -c '.[1].turn' "$OUT")"
same "task reason" '"answering your feedback since 10:45"' "$(jq -c '.[1].reason' "$OUT")"

case_ "the Dev answering on the PR hands the task back"
gh_talk <<TALK
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 pr-body ${TODAY}T08:25:00Z demo-app[bot] Closes #115\n<!-- a-team:dev -->
115 pr-comment+seen ${TODAY}T10:15:00Z reviewer This one needs a test.
115 pr-comment ${TODAY}T11:45:00Z demo-app[bot] Added one.\n<!-- a-team:dev -->
TALK
run board demo waiting
same "exit" 0 "$STATUS"
same "task turn" '"you"' "$(jq -c '.[1].turn' "$OUT")"
same "task reason" '"awaiting your acceptance since 11:45"' "$(jq -c '.[1].reason' "$OUT")"

case_ "an In review item carries its open PR, and a Pitched one carries none"
gh_talk <<TALK
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 pr-body ${TODAY}T08:25:00Z demo-app[bot] Closes #115\n<!-- a-team:dev -->
TALK
run board demo waiting
same "exit" 0 "$STATUS"
same "pitch fields" '["number","pitch","priority","reason","status","team","title","turn","url"]' "$(jq -c '.[0] | keys' "$OUT")"
same "task fields" \
  '["checks","conflicting","draft","number","pitch","pr","prUrl","priority","reason","status","team","title","turn","url"]' \
  "$(jq -c '.[1] | keys' "$OUT")"
same "pitch flags" '[true,false]' "$(jq -c '[.[].pitch]' "$OUT")"
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
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 pr-body ${TODAY}T08:25:00Z demo-app[bot] Closes #115\n<!-- a-team:dev -->
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
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 pr-body ${TODAY}T08:25:00Z demo-app[bot] Closes #115\n<!-- a-team:dev -->
TALK
run board demo waiting
same "exit" 0 "$STATUS"
same "conflicting" true "$(jq -c '.[1].conflicting' "$OUT")"
same "task turn" '"dev"' "$(jq -c '.[1].turn' "$OUT")"
same "task reason" '"conflicts with main"' "$(jq -c '.[1].reason' "$OUT")"

case_ "a PR GitHub hasn't worked the conflict out for yet is nobody's fault"
gh_talk false UNKNOWN <<TALK
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 pr-body ${TODAY}T08:25:00Z demo-app[bot] Closes #115\n<!-- a-team:dev -->
TALK
run board demo waiting
same "exit" 0 "$STATUS"
same "conflicting" false "$(jq -c '.[1].conflicting' "$OUT")"
same "task turn" '"you"' "$(jq -c '.[1].turn' "$OUT")"

case_ "a PR still in draft is the Dev's turn"
gh_talk true <<TALK
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 pr-body ${TODAY}T08:25:00Z demo-app[bot] Closes #115\n<!-- a-team:dev -->
TALK
run board demo waiting
same "exit" 0 "$STATUS"
same "draft" true "$(jq -c '.[1].draft' "$OUT")"
same "task turn" '"dev"' "$(jq -c '.[1].turn' "$OUT")"
same "task reason" '"still a draft"' "$(jq -c '.[1].reason' "$OUT")"

case_ "a ready PR whose CI is still running, after a push to answer feedback, isn't your turn yet"
gh_talk <<TALK
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 pr-body ${TODAY}T08:25:00Z demo-app[bot] Closes #115\n<!-- a-team:dev -->
115 pr-comment+seen ${TODAY}T10:15:00Z reviewer This one needs a test.
115 pr-comment ${TODAY}T10:40:00Z demo-app[bot] Added one.\n<!-- a-team:dev -->
TALK
gh_runs <<RUNS
completed success ${TODAY}T10:45:00Z build
in_progress - - windows
RUNS
run board demo waiting
same "exit" 0 "$STATUS"
same "checks" '"pending"' "$(jq -c '.[1].checks' "$OUT")"
same "task turn" '"dev"' "$(jq -c '.[1].turn' "$OUT")"
same "task trouble" '"CI running"' "$(jq -c '.[1].trouble' "$OUT")"
same "task reason" '"CI running"' "$(jq -c '.[1].reason' "$OUT")"
gh_runs <<RUNS
completed success ${TODAY}T10:45:00Z build
completed success ${TODAY}T10:55:00Z windows
RUNS
run board demo waiting
same "exit" 0 "$STATUS"
same "task turn" '"you"' "$(jq -c '.[1].turn' "$OUT")"
same "task reason" '"awaiting your acceptance since 10:40"' "$(jq -c '.[1].reason' "$OUT")"

case_ "a task with no PR yet waits on the reviewer since the task was opened"
gh_talk <<TALK
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
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
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
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
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
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

# Approving: the other write the app makes, and the other gate that is the reviewer's alone.
case_ "approve moves a Pitched pitch to Approved on the project"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 } }
JSON
gh_items <<'ITEMS'
Pitched 7 A pitch in front of me
Exploring 8 A pitch still being drafted
Idea 9 An Idea of my own
ITEMS
run board demo approve you 7
same "exit" 0 "$STATUS"
same "said" "#7: Pitched -> Approved" "$(cat "$OUT")"
same "writes" 1 "$(grep -c '' <"$WRITES")"
grep -q "item=PVTI_7 " "$WRITES" || fail "approve: not the pitch's item in '$(cat "$WRITES")'"
grep -q "option=OPT_approved " "$WRITES" || fail "approve: not the Approved option in '$(cat "$WRITES")'"

case_ "--dry-run says what it would approve and approves nothing"
: >"$WRITES"
run board --dry-run demo approve you 7
same "exit" 0 "$STATUS"
same "writes" "" "$(cat "$WRITES")"
grep -q "would set item PVTI_7 to 'Approved'" "$ERR" || fail "approve dry run: '$(cat "$ERR")'"

case_ "neither agent may approve a pitch: that gate is the reviewer's own"
for role in lead dev; do
  : >"$WRITES"
  run board demo approve "$role" 7
  failed "$role approving"
  one_line "$role approving"
  grep -q "$role may not approve a pitch; approving is the reviewer's own gate" "$ERR" ||
    fail "$role approving: '$(cat "$ERR")'"
  same "$role writes" "" "$(cat "$WRITES")"
done

case_ "any other role is unknown"
run board demo approve reviewer 7
failed "unknown role"
one_line "unknown role"
grep -q "unknown role 'reviewer' (you)" "$ERR" || fail "unknown role: '$(cat "$ERR")'"

case_ "approve is not a general move: only a pitch-labelled issue in Pitched"
for n in 8 9 404; do
  : >"$WRITES"
  run board demo approve you "$n"
  failed "approve #$n"
  one_line "approve #$n"
  same "approve #$n writes" "" "$(cat "$WRITES")"
done
grep -q "#404 is not on the board" "$ERR" || fail "approve off the board: '$(cat "$ERR")'"
run board demo approve you 8
grep -q "is in 'Exploring'" "$ERR" || fail "approve Exploring: '$(cat "$ERR")'"
run board demo approve you 9
grep -q "#9 is not a pitch" "$ERR" || fail "approve Idea: '$(cat "$ERR")'"
edit_item 7 '.labels.nodes = []'
run board demo approve you 7
failed "approve unlabelled"
grep -q "#7 is not a pitch" "$ERR" || fail "approve unlabelled: '$(cat "$ERR")'"
edit_item 7 '.labels.nodes = [{name: "pitch"}] | .__typename = "PullRequest"'
run board demo approve you 7
failed "approve a PR"
grep -q "#7 is not an issue" "$ERR" || fail "approve a PR: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"

case_ "the agents' settings deny approve, beside priority"
grep -qF '"Bash(a-team board * approve *)"' "$ROOT/settings/agents.json" || fail "no approve deny rule"

# The 👀: a reviewer comment is answered once a run has left one on it, and a run leaves one only
# on what it could have seen. The races replayed here are the ones in pitch #3. $TODAY is on or
# after board.sh's ACK_FROM, so these cases see the 👀 rule and the dated ones below the old.
case_ "comment acks what the run could have seen, and not a comment that arrived while it worked"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" },
  "project": { "owner": "mentaldesk", "number": 1 },
  "wip": { "pitched": 3, "exploring": 4, "ideas": 4 } }
JSON
gh_items <<'ITEMS'
Pitched 7 A pitch in front of me
ITEMS
gh_thread <<TALK
body ${TODAY}T02:10:00Z demo-app[bot] 0 The pitch\n<!-- a-team:lead -->
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
body ${TODAY}T02:10:00Z demo-app[bot] 0 The pitch\n<!-- a-team:lead -->
comment ${TODAY}T02:20:00Z reviewer 1 Needs a second option.
comment ${TODAY}T02:28:46Z reviewer 0 And do the same for subissue links.
comment ${TODAY}T02:33:03Z demo-app[bot] 0 Added one.\n<!-- a-team:lead -->
TALK
run board demo feedback lead 7
same "exit" 0 "$STATUS"
same "unanswered" '["And do the same for subissue links."]' "$(jq -c '[.[].body]' "$OUT")"

case_ "triggers names it too, so it gets a run of its own"
gh_recent <<RECENT
7 ${TODAY}T02:20:00Z reviewer 1 Needs a second option.
7 ${TODAY}T02:28:46Z reviewer 0 And do the same for subissue links.
7 ${TODAY}T02:33:03Z demo-app[bot] 0 Added one.\n<!-- a-team:lead -->
RECENT
run board demo triggers lead
same "exit" 0 "$STATUS"
same "reasons" "[\"reviewer feedback on #7 (${TODAY}T02:28:46Z)\"]" "$(jq -c .reasons "$OUT")"

case_ "waiting says the same: the gate is the Lead's until the 👀 is there"
gh_talk <<TALK
7 body ${TODAY}T02:10:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
7 comment+seen ${TODAY}T02:20:00Z reviewer Needs a second option.
7 comment ${TODAY}T02:28:46Z reviewer And do the same for subissue links.
7 comment ${TODAY}T02:33:03Z demo-app[bot] Added one.\n<!-- a-team:lead -->
TALK
run board demo waiting
same "exit" 0 "$STATUS"
same "turn" '"lead"' "$(jq -c '.[0].turn' "$OUT")"
same "reason" '"answering your feedback since 02:28"' "$(jq -c '.[0].reason' "$OUT")"

case_ "and hands it back once the 👀 is"
gh_talk <<TALK
7 body ${TODAY}T02:10:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
7 comment+seen ${TODAY}T02:20:00Z reviewer Needs a second option.
7 comment+seen ${TODAY}T02:28:46Z reviewer And do the same for subissue links.
7 comment ${TODAY}T02:33:03Z demo-app[bot] Added one.\n<!-- a-team:lead -->
TALK
run board demo waiting
same "exit" 0 "$STATUS"
same "turn" '"you"' "$(jq -c '.[0].turn' "$OUT")"
same "reason" '"awaiting your approval since 02:33"' "$(jq -c '.[0].reason' "$OUT")"

# Before ACK_FROM a marker counted wherever it appeared, so these threads carry it mid-line as the
# team used to leave it, and have to keep reading exactly as they did.
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

# Before APP_FROM the team spoke as the reviewer, and the marker counted only alone on the body's
# last line. GitHub's Quote reply copies a team comment's source, marker and all, into your own words.
case_ "a Quote reply carrying the team's marker is your feedback"
gh_thread <<TALK
body ${TODAY}T02:10:00Z demo-app[bot] 0 The pitch\n<!-- a-team:lead -->
comment ${TODAY}T02:20:00Z reviewer 1 Needs a second option.
comment ${TODAY}T02:28:46Z reviewer 0 > The pitch\n> <!-- a-team:lead -->\n\nAnd include the path.
TALK
run board demo feedback lead 7
same "exit" 0 "$STATUS"
same "unanswered" "[\"${TODAY}T02:28:46Z\"]" "$(jq -c '[.[].at]' "$OUT")"

case_ "triggers names the Quote reply too, so a run starts for it"
gh_recent <<TALK
7 ${TODAY}T02:20:00Z reviewer 1 Needs a second option.
7 ${TODAY}T02:28:46Z reviewer 0 > The pitch\n> <!-- a-team:lead -->\n\nAnd include the path.
TALK
run board demo triggers lead
same "exit" 0 "$STATUS"
same "reasons" "[\"reviewer feedback on #7 (${TODAY}T02:28:46Z)\"]" "$(jq -c .reasons "$OUT")"

case_ "waiting says the same: the Quote reply makes the gate the Lead's"
gh_talk <<TALK
7 body ${TODAY}T02:10:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
7 comment+seen ${TODAY}T02:20:00Z reviewer Needs a second option.
7 comment ${TODAY}T02:28:46Z reviewer > The pitch\n> <!-- a-team:lead -->\n\nAnd include the path.
TALK
run board demo waiting
same "exit" 0 "$STATUS"
same "turn" '"lead"' "$(jq -c '.[0].turn' "$OUT")"
same "reason" '"answering your feedback since 02:28"' "$(jq -c '.[0].reason' "$OUT")"

case_ "a Quote reply that is your only comment is returned, not dropped"
gh_thread <<'TALK'
body 2026-09-25T02:10:00Z reviewer 0 The pitch\n<!-- a-team:lead -->
comment 2026-09-25T02:28:46Z reviewer 0 > The pitch\n> <!-- a-team:lead -->\n\nAnd include the path.
TALK
run board demo feedback lead 7
same "exit" 0 "$STATUS"
same "unanswered" '["2026-09-25T02:28:46Z"]' "$(jq -c '[.[].at]' "$OUT")"

case_ "comment acks a Quote reply, so it doesn't come back once answered"
: >"$ACKED"
export A_TEAM_RUN_STARTED=2026-09-25T03:00:00Z
run board demo comment lead 7 "$WORK/reply"
same "exit" 0 "$STATUS"
same "acked" "IC_1" "$(cat "$ACKED")"
unset A_TEAM_RUN_STARTED

case_ "before APP_FROM, a marker mid-sentence or in a fenced code block is your feedback"
gh_thread <<'TALK'
body 2026-09-25T02:10:00Z reviewer 0 The pitch\n<!-- a-team:lead -->
comment 2026-09-25T02:20:00Z reviewer 0 Does <!-- a-team:lead --> have to be last?
comment 2026-09-25T02:28:46Z reviewer 0 Every body ends with\n```\n<!-- a-team:lead -->\n```
TALK
run board demo feedback lead 7
same "exit" 0 "$STATUS"
same "unanswered" '["2026-09-25T02:20:00Z","2026-09-25T02:28:46Z"]' "$(jq -c '[.[].at]' "$OUT")"

case_ "before APP_FROM, the team's own comment under your login is not feedback, so an upgrade reopens nothing"
gh_thread <<'TALK'
body 2026-09-25T02:10:00Z reviewer 0 The pitch\n<!-- a-team:lead -->
comment 2026-09-25T02:33:03Z reviewer 0 Added one.\n<!-- a-team:lead -->
TALK
run board demo feedback lead 7
same "exit" 0 "$STATUS"
same "unanswered" '[]' "$(jq -c . "$OUT")"

# Since APP_FROM the team speaks only as its App, so the author alone says whose words a comment is.
case_ "after APP_FROM, the reviewer's comment is feedback whatever its body, marker on its last line or not"
gh_thread <<'TALK'
body 2026-10-01T02:10:00Z demo-app[bot] 0 The pitch\n<!-- a-team:lead -->
comment 2026-10-01T02:20:00Z reviewer 0 Pasted from the other thread:\n\nDrafted.\n<!-- a-team:lead -->
comment 2026-10-01T02:28:46Z reviewer 0 Does <!-- a-team:dev --> have to be last?
TALK
run board demo feedback lead 7
same "exit" 0 "$STATUS"
same "unanswered" '["2026-10-01T02:20:00Z","2026-10-01T02:28:46Z"]' "$(jq -c '[.[].at]' "$OUT")"

case_ "comment acks it too"
: >"$ACKED"
export A_TEAM_RUN_STARTED=2026-10-01T03:00:00Z
run board demo comment lead 7 "$WORK/reply"
same "exit" 0 "$STATUS"
same "acked" "IC_1
IC_2" "$(cat "$ACKED")"
unset A_TEAM_RUN_STARTED

case_ "the App's comments and reviews are never feedback, however GitHub spells its login"
gh_thread pull <<'TALK'
body 2026-10-01T02:10:00Z demo-app[bot] 0 Closes #7\n<!-- a-team:dev -->
comment 2026-10-01T02:20:00Z demo-app[bot] 0 A comment that lost its marker.
review 2026-10-01T02:25:00Z demo-app[bot] 0 A review that lost its marker.
line 2026-10-01T02:28:46Z demo-app[bot] 0 A line comment that lost its marker.
TALK
run board demo feedback dev 7
same "exit" 0 "$STATUS"
same "unanswered" '[]' "$(jq -c . "$OUT")"

case_ "triggers doesn't start a run for the App's own words either"
gh_recent <<'RECENT'
7 2026-10-01T02:20:00Z demo-app[bot] 0 A comment that lost its marker.
RECENT
run board demo triggers lead
same "exit" 0 "$STATUS"
same "reasons" '[]' "$(jq -c .reasons "$OUT")"

case_ "waiting reads the App from GraphQL's spelling, and each role from its own marker"
gh_items <<'ITEMS'
Pitched 106 Both gates are mine
In_review 115 I can change any of the keys
ITEMS
gh_talk <<'TALK'
106 body 2026-10-01T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
106 comment 2026-10-01T09:00:00Z demo-app[bot] Wrong thread.\n<!-- a-team:dev -->
106 comment 2026-10-01T09:30:00Z demo-app[bot] A comment that lost its marker.
115 body 2026-10-01T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 comment 2026-10-01T09:00:00Z reviewer > Drafted.\n<!-- a-team:dev -->
TALK
run board demo waiting
same "exit" 0 "$STATUS"
same "pitch turn" '"you"' "$(jq -c '.[0].turn' "$OUT")"
same "pitch reason" '"awaiting your approval since 01 Oct 08:00"' "$(jq -c '.[0].reason' "$OUT")"
same "task turn" '"dev"' "$(jq -c '.[1].turn' "$OUT")"
gh_items <<'ITEMS'
Pitched 7 A pitch in front of me
ITEMS

# Unset, A_TEAM_RUN_STARTED means now, so these threads are dated back from it, not from an hour of
# the day the suite could be running before.
OPENED=$(ago 90) ASKED=$(ago 60) ASKED_AGAIN=$(ago 50)

case_ "a review and a line comment on a PR are acked like any other comment"
gh_thread pull <<TALK
body $OPENED demo-app[bot] 0 Closes #7\n<!-- a-team:dev -->
review $ASKED reviewer 0 Nearly there.
line $ASKED_AGAIN reviewer 0 This name reads oddly.
TALK
: >"$ACKED"
A_TEAM_RUN_STARTED=$(ago 30)
export A_TEAM_RUN_STARTED
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
body $OPENED demo-app[bot] 0 Closes #7\n<!-- a-team:dev -->
review $ASKED reviewer 1 Nearly there.
line $ASKED_AGAIN reviewer 0 This name reads oddly.
TALK
: >"$ACKED"
run board demo comment dev 7 "$WORK/reply"
same "exit" 0 "$STATUS"
same "acked" "IC_2" "$(cat "$ACKED")"

case_ "a comment from anyone but the reviewer is not acked"
gh_thread <<TALK
body $OPENED demo-app[bot] 0 The pitch\n<!-- a-team:lead -->
comment $ASKED passer-by 0 Have you considered doing it differently?
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
body $OPENED demo-app[bot] 0 The idea\n<!-- a-team:lead -->
comment $ASKED reviewer 0 Worth a look.
TALK
: >"$ACKED"
run board demo skip lead 7 "$WORK/reply"
same "exit" 0 "$STATUS"
same "acked" "IC_1" "$(cat "$ACKED")"

case_ "--dry-run says which reactions it would add and adds none"
gh_thread <<TALK
body $OPENED demo-app[bot] 0 The pitch\n<!-- a-team:lead -->
comment $ASKED reviewer 0 Needs a second option.
TALK
: >"$ACKED"
: >"$POSTED"
run board --dry-run demo comment lead 7 "$WORK/reply"
same "exit" 0 "$STATUS"
same "acked" "" "$(cat "$ACKED")"
same "posted" "" "$(cat "$POSTED")"
grep -q "would add 👀 to your comment of $ASKED on #7" "$ERR" || fail "dry run: nothing about the 👀 in '$(cat "$ERR")'"

case_ "lead-next announces a swap only for pitches that have never been displaced"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "project": { "owner": "mentaldesk", "number": 1 },
  "wip": { "pitched": 2, "exploring": 4, "ideas": 4 } }
JSON
gh_items 21 22 <<'ITEMS'
Pitched 11 Never displaced
Pitched 12 Displaced before
Exploring 21 A higher draft, never in Pitched
Exploring 22 A higher draft, back again
ITEMS
edit_item 12 '.labels.nodes += [{name: "a-team:displaced"}]'
edit_item 22 '.labels.nodes += [{name: "a-team:displaced"}]'
A_TEAM_STATE="$WORK/state" run board demo lead-next
same "exit" 0 "$STATUS"
same "demote" '[[11,true],[12,false]]' "$(jq -c '[.demote[] | [.number, .announce]] | sort' "$OUT")"
same "promote" '[[21,true],[22,false]]' "$(jq -c '[.promote[] | [.number, .announce]] | sort' "$OUT")"

case_ "a first demote labels the pitch a-team:displaced"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "project": { "owner": "mentaldesk", "number": 1 },
  "wip": { "pitched": 1 } }
JSON
gh_items 21 <<'ITEMS'
Pitched 11 Out-ranked
Exploring 21 A higher draft
ITEMS
run board demo move lead 11 Exploring
same "exit" 0 "$STATUS"
same "label" 1 "$(grep -c 'issue edit 11 .*--add-label a-team:displaced' "$WRITES")"
grep -q "option=OPT_exploring" "$WRITES" || fail "status not set: '$(cat "$WRITES")'"

case_ "a repeat demote moves without labelling again"
edit_item 11 '.labels.nodes += [{name: "a-team:displaced"}]'
: >"$WRITES"
run board demo move lead 11 Exploring
same "exit" 0 "$STATUS"
same "label" 0 "$(grep -c 'issue edit' "$WRITES")"
grep -q "option=OPT_exploring" "$WRITES" || fail "status not set: '$(cat "$WRITES")'"

case_ "--dry-run says it would label a first demote, and labels nothing"
edit_item 11 '.labels.nodes -= [{name: "a-team:displaced"}]'
: >"$WRITES"
run board --dry-run demo move lead 11 Exploring
same "exit" 0 "$STATUS"
same "writes" "" "$(cat "$WRITES")"
grep -q "would label #11 a-team:displaced" "$ERR" || fail "dry run: nothing about the label in '$(cat "$ERR")'"

case_ "setup creates the a-team:displaced label"
run board --dry-run demo setup
same "exit" 0 "$STATUS"
grep -q "created label a-team:displaced" "$OUT" || fail "setup: '$(cat "$OUT")'"

case_ "either role may block either task, across pitches and at any status"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 } }
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

case_ "with every worktree taken, the Dev isn't woken for a Ready task"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 },
  "wip": { "worktrees": 2 } }
JSON
gh_items <<'ITEMS'
In_review 12 Waiting on a pane nobody has built
In_review 14 Waiting on the reviewer
Ready 13 Something to start
Done 15 Merged with its blocked label left on
ITEMS
edit_item 15 '.labels.nodes = [{name: "a-team:dev"}, {name: "blocked"}]'
run board demo triggers dev
same "exit" 0 "$STATUS"
same "reasons" '[]' "$(jq -c .reasons "$OUT")"

case_ "a task blocked by another issue frees its worktree, and wip counts it apart"
edit_item 12 '.issueDependenciesSummary.blockedBy = 1'
run board demo triggers dev
same "exit" 0 "$STATUS"
same "reasons" '["Ready task available (e.g. #13) and a free worktree"]' "$(jq -c .reasons "$OUT")"
run board demo wip
same "exit" 0 "$STATUS"
same "dev" '{"Done":1,"In review":1,"blocked":1}' "$(jq -c .dev "$OUT")"

case_ "so does one the reviewer holds with the blocked label"
edit_item 12 '.issueDependenciesSummary.blockedBy = 0 | .labels.nodes += [{name: "blocked"}]'
run board demo triggers dev
same "exit" 0 "$STATUS"
same "reasons" '["Ready task available (e.g. #13) and a free worktree"]' "$(jq -c .reasons "$OUT")"

case_ "a draft PR whose CI is still running doesn't wake the Dev"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 },
  "wip": { "worktrees": 1 } }
JSON
gh_items <<'ITEMS'
In_progress 12 A task with its draft PR up
ITEMS
gh_pr 912 true
gh_runs <<'RUNS'
completed success 2025-09-19T09:00:00Z build
in_progress - - windows
RUNS
run board demo triggers dev
same "exit" 0 "$STATUS"
same "reasons" '[]' "$(jq -c .reasons "$OUT")"

case_ "a failed check wakes the Dev while the rest still run, and again once they've finished"
gh_runs <<'RUNS'
completed failure 2025-09-19T09:00:00Z build
in_progress - - windows
RUNS
run board demo triggers dev
same "exit" 0 "$STATUS"
same "reasons" '["CI failed on PR #912 at deadbee, other checks still running"]' "$(jq -c .reasons "$OUT")"
gh_runs <<'RUNS'
completed failure 2025-09-19T09:00:00Z build
completed success 2025-09-19T09:20:00Z windows
RUNS
run board demo triggers dev
same "exit" 0 "$STATUS"
same "reasons" '["CI failed on PR #912 at deadbee"]' "$(jq -c .reasons "$OUT")"

case_ "a draft PR that goes green wakes the Dev to mark it ready"
gh_runs <<'RUNS'
completed success 2025-09-19T09:00:00Z build
completed success 2025-09-19T09:20:00Z windows
RUNS
run board demo triggers dev
same "exit" 0 "$STATUS"
same "reasons" '["PR #912 is green but still a draft"]' "$(jq -c .reasons "$OUT")"

# --- a-team try -----------------------------------------------------------------------------
# A throwaway origin holding main and one PR head, a checkout cloned from it that has only main,
# and a config pointing workdir and checkout at them. `try_fixture [<try command>]`.
try_fixture() {
  TRY_WORK=$(mktemp -d "$WORK/try.XXXXXX")
  CHECKOUT="$TRY_WORK/main"
  TRY_ORIGIN="$TRY_WORK/origin"
  local origin=$TRY_ORIGIN seed="$TRY_WORK/seed"
  git -c init.defaultBranch=main init -q --bare "$origin"
  git -c init.defaultBranch=main clone -q "$origin" "$seed" 2>/dev/null
  git -C "$seed" config user.email test@example.com
  git -C "$seed" config user.name Test
  echo one >"$seed/file"
  git -C "$seed" add -A
  git -C "$seed" commit -qm first
  git -C "$seed" push -q origin HEAD:refs/heads/main
  echo two >"$seed/file"
  git -C "$seed" commit -qam "the change under review"
  TRY_SHA=$(git -C "$seed" rev-parse HEAD)
  git -C "$seed" push -q origin HEAD:refs/pull/7/head
  rm -rf "$seed"
  # file:// rather than a path: a local clone hardlinks the whole object store, PR head included.
  git clone -q "file://$origin" "$CHECKOUT"
  fixture <<JSON
{ "repo": "mentaldesk/demo", "workdir": "$TRY_WORK", "checkout": "$CHECKOUT"$(
    [ -n "${1:-}" ] && printf ', "try": "%s"' "$1"
  ) }
JSON
}

# The PR `try` looks up: `try_gh [<state> [<head repo>]]`. Anything else is a 404, as a PR that
# isn't there would be. `try_closes <number> <body>` makes it close an issue; `try_closes fail`
# makes reading that fail.
try_gh() {
  TRY_BIN=$(mktemp -d "$WORK/trybin.XXXXXX")
  jq -n --arg state "${1:-open}" --arg repo "${2:-mentaldesk/demo}" --arg sha "$TRY_SHA" \
    '{number: 7, title: "A change worth a look", state: $state,
      head: {sha: $sha, repo: {full_name: $repo}}}' >"$TRY_BIN/pull.json"
  cat >"$TRY_BIN/gh" <<SH
#!/usr/bin/env bash
case " \$* " in
  *"/pulls/7"*) cat "$TRY_BIN/pull.json" ;;
  *" graphql "*)
    [ ! -e "$TRY_BIN/closes.fail" ] || { echo "gh: HTTP 502" >&2; exit 1; }
    cat "$TRY_BIN/closes.json" 2>/dev/null || true ;;
  *) echo "gh: Not Found (HTTP 404)" >&2; exit 1 ;;
esac
SH
  chmod +x "$TRY_BIN/gh"
  cat >"$TRY_BIN/record" <<SH
#!/usr/bin/env bash
{ pwd; git rev-parse HEAD; echo "\${A_TEAM_STATE:-}"; echo "\${A_TEAM_DRY_RUN:-}"; } >"$TRY_WORK/ran"
SH
  chmod +x "$TRY_BIN/record"
  PATH="$TRY_BIN:$PATH"
}

try_closes() {
  if [ "$1" = fail ]; then
    touch "$TRY_BIN/closes.fail"
  else
    jq -cn --argjson number "$1" --arg body "$2" '{number: $number, body: $body}' >"$TRY_BIN/closes.json"
  fi
}

worktrees() { git -C "$CHECKOUT" worktree list | sed 1d; }

# Lands a commit on origin's <branch> after the checkout was cloned, so the checkout's own view of
# it is stale, as it normally is. `land <branch>` sets TRY_LANDED to the new commit.
land() {
  local seed="$TRY_WORK/land"
  git clone -q "file://$TRY_ORIGIN" "$seed" 2>/dev/null
  git -C "$seed" -c user.email=test@example.com -c user.name=Test commit -q --allow-empty -m landed
  TRY_LANDED=$(git -C "$seed" rev-parse HEAD)
  git -C "$seed" push -q origin "HEAD:refs/heads/$1"
  rm -rf "$seed"
}

case_ "try runs the team's command in a worktree at the PR's head, sandboxed, and clears up after"
try_fixture record
try_gh
run try demo 7
same "exit" 0 "$STATUS"
same "working directory" "$TRY_WORK/.try/7" "$(sed -n 1p "$TRY_WORK/ran")"
same "commit" "$TRY_SHA" "$(sed -n 2p "$TRY_WORK/ran")"
same "A_TEAM_STATE" "$TRY_WORK/.try/state/7" "$(sed -n 3p "$TRY_WORK/ran")"
same "A_TEAM_DRY_RUN" 1 "$(sed -n 4p "$TRY_WORK/ran")"
grep -q 'A_TEAM_DRY_RUN=1' "$OUT" || fail "sandbox: nothing about it in '$(cat "$OUT")'"
grep -q 'Running: record' "$OUT" || fail "run: nothing about what it ran in '$(cat "$OUT")'"
grep -q 'Merge #7 if it did what you wanted' "$OUT" || fail "merge: no invitation in '$(cat "$OUT")'"
same "worktrees left" "" "$(worktrees)"
[ ! -e "$TRY_WORK/.try/7" ] || fail "clean exit: the worktree is still there"
[ ! -e "$TRY_WORK/.try/state/7" ] || fail "clean exit: the sandbox state is still there"

case_ "no try command in the config drops you into a shell in the worktree instead"
try_fixture
try_gh
run try demo 7 <<IN
pwd >"$TRY_WORK/shell"
IN
same "exit" 0 "$STATUS"
same "working directory" "$TRY_WORK/.try/7" "$(cat "$TRY_WORK/shell")"
[ ! -e "$TRY_WORK/ran" ] || fail "shell: the team's command ran as well"
grep -q 'Ctrl+D' "$OUT" || fail "shell: nothing saying how to come back in '$(cat "$OUT")'"
grep -q 'try:demo#7' "$ERR" || fail "shell: the prompt doesn't name the PR"

case_ "a worktree you changed something in is kept, with the line that removes it"
try_fixture 'touch note.md'
try_gh
run try demo 7
same "exit" 0 "$STATUS"
[ -d "$TRY_WORK/.try/7" ] || fail "kept: the worktree was removed anyway"
grep -q 'you changed 1 file there' "$OUT" || fail "kept: nothing about what you changed in '$(cat "$OUT")'"
grep -q 'a-team try demo 7 --clean' "$OUT" || fail "kept: no --clean line in '$(cat "$OUT")'"

case_ "--clean removes it without running anything, and is happy when there's nothing to remove"
run try demo 7 --clean
same "exit" 0 "$STATUS"
same "worktrees left" "" "$(worktrees)"
[ ! -e "$TRY_WORK/.try/7" ] || fail "--clean: the worktree is still there"
[ ! -e "$TRY_WORK/.try/state/7" ] || fail "--clean: the sandbox state is still there"
run try demo 7 --clean
same "exit" 0 "$STATUS"
grep -q 'Nothing to remove' "$OUT" || fail "--clean twice: '$(cat "$OUT")'"

case_ "a PR that isn't there is refused in one line, leaving nothing behind"
try_fixture record
try_gh
run try demo 404
failed "no such PR"
one_line "no such PR"
[ ! -e "$TRY_WORK/.try" ] || fail "no such PR: something was left in .try"

case_ "so is one that's already closed"
try_gh closed
run try demo 7
failed "closed PR"
one_line "closed PR"
[ ! -e "$TRY_WORK/.try" ] || fail "closed PR: something was left in .try"

case_ "a PR from somewhere else asks first, and n stops it before anything is fetched"
try_gh open someone-else/demo
run try demo 7 <<<n
failed "fork"
grep -q 'someone-else/demo' "$OUT" || fail "fork: it doesn't say whose branch it is"
[ ! -e "$TRY_WORK/.try" ] || fail "fork: something was left in .try"
git -C "$CHECKOUT" cat-file -e "$TRY_SHA" 2>/dev/null && fail "fork: it fetched the PR anyway"

case_ "a PR that closes an issue shows its acceptance criteria, unticked and in order, before running"
try_fixture record
try_gh
try_closes 92 "$(printf '%s\r\n' '## Context' '' 'Why.' '' '## Acceptance criteria' '' \
  '- [ ] A grid cell never goes below' '      5 rows' '- [x] Moving the selection scrolls' \
  '  - [ ] and the strip never does' '' '## Tests' '' '- [ ] not this one')"
run try demo 7
same "exit" 0 "$STATUS"
[ -e "$TRY_WORK/ran" ] || fail "criteria: the team's command didn't run"
sed -n 2p "$OUT" | grep -q "at ${TRY_SHA:0:7} (closes #92)$" || fail "criteria: second line '$(sed -n 2p "$OUT")'"
same "checklist" "  What this should let you do
    [ ] A grid cell never goes below 5 rows
    [ ] Moving the selection scrolls
      [ ] and the strip never does" "$(sed -n '4,7p' "$OUT")"
same "then the sandbox" "" "$(sed -n 8p "$OUT")"
grep -q 'not this one' "$OUT" && fail "criteria: it ran on past the section in '$(cat "$OUT")'"

case_ "an issue with no acceptance criteria, or a PR closing nothing, shows no checklist and runs"
try_fixture record
try_gh
try_closes 92 "Just some words."
run try demo 7
same "exit" 0 "$STATUS"
[ -e "$TRY_WORK/ran" ] || fail "no criteria: the team's command didn't run"
grep -q 'What this should let you do' "$OUT" && fail "no criteria: a checklist in '$(cat "$OUT")'"
same "stderr" "" "$(cat "$ERR")"
try_gh
run try demo 7
same "exit" 0 "$STATUS"
grep -q 'closes\|What this should' "$OUT" && fail "closes nothing: '$(cat "$OUT")'"
same "stderr" "" "$(cat "$ERR")"

case_ "an issue that won't read is one line, and try runs anyway"
try_fixture record
try_gh
try_closes fail
run try demo 7
same "exit" 0 "$STATUS"
[ -e "$TRY_WORK/ran" ] || fail "unreadable: the team's command didn't run"
same "lines about it" 1 "$(grep -c 'Could not read' "$OUT")"
grep -q 'What this should' "$OUT" && fail "unreadable: a checklist in '$(cat "$OUT")'"

case_ "with no PR, there's no checklist"
try_fixture record
try_gh
try_closes 92 "$(printf '%s\n' '## Acceptance criteria' '- [ ] something')"
run try demo
same "exit" 0 "$STATUS"
grep -q 'What this should' "$OUT" && fail "no PR: a checklist in '$(cat "$OUT")'"

case_ "with no PR, try runs origin's default branch as it is now, not the checkout's stale copy"
try_fixture record
try_gh
land main
run try demo
same "exit" 0 "$STATUS"
same "working directory" "$TRY_WORK/.try/main" "$(sed -n 1p "$TRY_WORK/ran")"
same "commit" "$TRY_LANDED" "$(sed -n 2p "$TRY_WORK/ran")"
same "A_TEAM_STATE" "$TRY_WORK/.try/state/main" "$(sed -n 3p "$TRY_WORK/ran")"
same "A_TEAM_DRY_RUN" 1 "$(sed -n 4p "$TRY_WORK/ran")"
same "first line" "Fetching mentaldesk/demo main…" "$(sed -n 1p "$OUT")"
sed -n 2p "$OUT" | grep -q "at main ${TRY_LANDED:0:7}$" || fail "no PR: second line '$(sed -n 2p "$OUT")'"
grep -q '#' "$OUT" && fail "no PR: it talks about a PR in '$(cat "$OUT")'"
same "worktrees left" "" "$(worktrees)"
[ ! -e "$TRY_WORK/.try/main" ] || fail "no PR: the worktree is still there"
[ ! -e "$TRY_WORK/.try/state/main" ] || fail "no PR: the sandbox state is still there"

case_ "running it again after a merge gives you the newer commit"
land main
run try demo
same "exit" 0 "$STATUS"
same "commit" "$TRY_LANDED" "$(sed -n 2p "$TRY_WORK/ran")"

case_ "the default branch is whatever origin's HEAD names, not main"
land trunk
git -C "$TRY_ORIGIN" symbolic-ref HEAD refs/heads/trunk
run try demo
same "exit" 0 "$STATUS"
same "commit" "$TRY_LANDED" "$(sed -n 2p "$TRY_WORK/ran")"
same "first line" "Fetching mentaldesk/demo trunk…" "$(sed -n 1p "$OUT")"

case_ "with no PR, a shell names the branch, a change keeps the worktree, and --clean removes it"
try_fixture
run try demo <<IN
pwd >"$TRY_WORK/shell"
touch note.md
IN
same "exit" 0 "$STATUS"
same "working directory" "$TRY_WORK/.try/main" "$(cat "$TRY_WORK/shell")"
grep -q 'try:demo@main' "$ERR" || fail "shell: the prompt doesn't name the branch"
[ -d "$TRY_WORK/.try/main" ] || fail "kept: the worktree was removed anyway"
grep -q 'a-team try demo --clean' "$OUT" || fail "kept: no --clean line in '$(cat "$OUT")'"
run try demo --clean
same "exit" 0 "$STATUS"
same "worktrees left" "" "$(worktrees)"
[ ! -e "$TRY_WORK/.try/main" ] || fail "--clean: the worktree is still there"
[ ! -e "$TRY_WORK/.try/state/main" ] || fail "--clean: the sandbox state is still there"

case_ "an unknown team, or no team at all, is refused with the usage"
run try nobody 7
failed "unknown team"
grep -q "^usage: a-team try" "$ERR" || fail "unknown team: no usage in '$(cat "$ERR")'"
run try
failed "no team"
grep -q "^usage: a-team try" "$ERR" || fail "no team: no usage in '$(cat "$ERR")'"

case_ "try is in the usage text, and the example config carries the optional try key"
run help
grep -q '^  try <team> \[<pr>\]' "$OUT" || fail "usage: no try line with an optional PR"
same "example try" '"./bin/a-team dashboard"' "$(jq -c .try "$ROOT/examples/team.json")"

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

# A run that counts the TERMs it gets in $SIGNALS, as the dev's run in $A_TEAM_STATE.
fake_run() {
  SIGNALS="$A_TEAM_STATE/signals"
  : >"$SIGNALS"
  mkdir -p "$A_TEAM_STATE/demo/dev"
  bash -c "trap 'echo TERM >>\"$SIGNALS\"; exit' TERM; while :; do sleep 0.1; done" &
  RUN_PID=$!
  echo "$RUN_PID" >"$A_TEAM_STATE/demo/dev/pid"
}
held() { jq -c '.dispatch.hold' "$TEAM"; }

export A_TEAM_STATE
A_TEAM_STATE=$(mktemp -d "$WORK/state.XXXXXX")

case_ "stop ends the run once, holds the role, and lists what it left claimed"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 },
  "dispatch": { "enabled": true } }
JSON
gh_items <<'ITEMS'
In_progress 12 A task left half done
Ready 13 A task nobody has claimed
ITEMS
fake_run
run stop demo dev
wait "$RUN_PID"
same "exit" 0 "$STATUS"
same "signals" 1 "$(grep -c TERM "$SIGNALS")"
same "hold" '["dev"]' "$(held)"
same "enabled" true "$(enabled)"
grep -q "stopped demo dev's run ($RUN_PID)" "$OUT" || fail "stop: no word of the run in '$(cat "$OUT")'"
grep -q '#12 A task left half done' "$OUT" || fail "stop: #12 isn't listed as left claimed"
grep -q '#13' "$OUT" && fail "stop: #13 was never claimed"

case_ "a dispatcher pass skips the held role, and starts its sibling"
APP=$(mktemp -d "$WORK/dispatch.XXXXXX")
mkdir -p "$APP/bin" "$APP/scripts"
cp "$ROOT"/scripts/*.sh "$APP/scripts/"
cat >"$APP/bin/a-team" <<'SH'
#!/usr/bin/env bash
if [ "$1" = token ] && [ -n "${NOT_INSTALLED:-}" ]; then
  echo "a-team token: app 7 isn't installed on mentaldesk/$2: install it at https://github.com/apps/demo-app/installations/new" >&2
  exit 1
fi
echo '{"reasons": ["work to do"], "creative": false}'
SH
chmod +x "$APP/bin/a-team"
A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/dispatch.sh" --dry-run
grep -q 'demo lead: would start' "$A_TEAM_STATE/dispatch.log" || fail "dispatch: the lead wasn't started"
grep -q 'demo dev' "$A_TEAM_STATE/dispatch.log" && fail "dispatch: the held dev was started"

case_ "a dispatcher pass skips a team with no app key, says why in status, and runs the others"
jq 'del(.app) | .dispatch.hold = []' "$TEAM" >"$CONFIG/teams/bare.json"
: >"$A_TEAM_STATE/dispatch.log"
rm -f "$A_TEAM_STATE"/demo/*/dry-*
A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/dispatch.sh" --dry-run
grep -q 'bare lead\|bare dev' "$A_TEAM_STATE/dispatch.log" && fail "no app: bare was started"
grep -q 'demo lead: would start' "$A_TEAM_STATE/dispatch.log" || fail "no app: demo wasn't started"
A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/status.sh" >"$OUT"
grep -q '^bare: stopped: no GitHub App: run a-team app create bare, then install it$' "$OUT" ||
  fail "no app: status says '$(cat "$OUT")'"
grep -q '^demo: stopped' "$OUT" && fail "no app: demo shows as stopped"

case_ "and the same for a team whose App isn't installed, until it is"
: >"$A_TEAM_STATE/dispatch.log"
NOT_INSTALLED=1 A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/dispatch.sh" --dry-run
grep -q "demo: stopped: app 7 isn't installed on mentaldesk/demo" "$A_TEAM_STATE/dispatch.log" ||
  fail "not installed: '$(cat "$A_TEAM_STATE/dispatch.log")'"
grep -q 'demo lead' "$A_TEAM_STATE/dispatch.log" && fail "not installed: demo was started"
A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/status.sh" >"$OUT"
grep -q "^demo: stopped: app 7 isn't installed" "$OUT" || fail "not installed: status says '$(cat "$OUT")'"
A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/dispatch.sh" --dry-run
A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/status.sh" >"$OUT"
grep -q '^demo: stopped' "$OUT" && fail "installed: demo still shows as stopped"
rm "$CONFIG/teams/bare.json"

case_ "resume <team> <role> lets it go and leaves dispatch.enabled alone"
jq '.dispatch.enabled = false' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
run resume demo dev
same "exit" 0 "$STATUS"
same "hold" '[]' "$(held)"
same "enabled" false "$(enabled)"

case_ "stop with no run going still holds, and says there was nothing to stop"
true &
dead=$!
wait "$dead"
echo "$dead" >"$A_TEAM_STATE/demo/dev/pid"
gh_items </dev/null
run stop demo dev
same "exit" 0 "$STATUS"
same "hold" '["dev"]' "$(held)"
grep -q 'no run to stop' "$OUT" || fail "dead pid: '$(cat "$OUT")'"
grep -q 'left nothing claimed' "$OUT" || fail "nothing claimed: '$(cat "$OUT")'"

case_ "stopping it again doesn't hold it twice"
run stop demo dev
same "hold" '["dev"]' "$(held)"

case_ "stop --dry-run names the run it would stop, and neither stops nor holds it"
run resume demo dev
fake_run
run stop --dry-run demo dev
same "exit" 0 "$STATUS"
same "hold" '[]' "$(held)"
grep -q "would stop run $RUN_PID" "$OUT" || fail "dry run: '$(cat "$OUT")'"
kill -0 "$RUN_PID" 2>/dev/null || fail "dry run: the run was stopped"

if [ "$(id -u)" != 0 ]; then
  case_ "a config stop can't write is refused, and the run goes on"
  chmod 444 "$TEAM"
  run stop demo dev
  failed "unwritable"
  grep -q "$TEAM" "$ERR" || fail "unwritable: the file isn't named in '$(cat "$ERR")'"
  kill -0 "$RUN_PID" 2>/dev/null || fail "unwritable: the run was stopped anyway"
  same "signals" 0 "$(grep -c TERM "$SIGNALS")"
  chmod 644 "$TEAM"
fi
kill "$RUN_PID" 2>/dev/null
wait "$RUN_PID" 2>/dev/null

case_ "stop needs a role it knows"
run stop demo
failed "no role"
run stop demo tester
failed "unknown role"

case_ "pause <team> <role> holds the role and lets its run finish"
jq '.dispatch.enabled = true' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
fake_run
run pause demo dev
same "exit" 0 "$STATUS"
same "hold" '["dev"]' "$(held)"
same "enabled" true "$(enabled)"
grep -q 'a-team resume demo dev' "$OUT" || fail "pause role: '$(cat "$OUT")'"
kill -0 "$RUN_PID" 2>/dev/null || fail "pause role: the run was stopped"
same "signals" 0 "$(grep -c TERM "$SIGNALS")"
kill "$RUN_PID" 2>/dev/null
wait "$RUN_PID" 2>/dev/null

case_ "a dispatcher pass skips the role pause held"
A_TEAM_STATE_WAS=$A_TEAM_STATE
A_TEAM_STATE=$(mktemp -d "$WORK/state.XXXXXX")
A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/dispatch.sh" --dry-run
grep -q 'demo lead: would start' "$A_TEAM_STATE/dispatch.log" || fail "dispatch: the lead wasn't started"
grep -q 'demo dev' "$A_TEAM_STATE/dispatch.log" && fail "dispatch: the paused dev was started"
A_TEAM_STATE=$A_TEAM_STATE_WAS

case_ "pause --dry-run <team> <role> says it would hold the role, and doesn't"
run resume demo dev
run pause --dry-run demo dev
same "exit" 0 "$STATUS"
same "hold" '[]' "$(held)"
grep -q "would add dev to dispatch.hold" "$OUT" || fail "dry run: '$(cat "$OUT")'"

case_ "pause needs a role it knows"
run pause demo tester
failed "unknown role"
same "hold" '[]' "$(held)"

case_ "stop and the per-role resume are in the usage text"
run help
grep -q '^  stop ' "$OUT" || fail "usage: no stop line"
grep -q '^  resume \[--dry-run\] <team> \[<role>\]' "$OUT" || fail "usage: resume takes no role"
grep -q '^  pause \[--dry-run\] <team> \[<role>\]' "$OUT" || fail "usage: pause takes no role"
# A claude that records what it was asked to resume, where, and what the run and the hold were by then.
fake_claude() {
  RESUMED="$BIN/resumed"
  : >"$RESUMED"
  cat >"$BIN/claude" <<SH
#!/usr/bin/env bash
{ echo "args: \$*"; echo "cwd: \$(pwd -P)"; echo "hold: \$(jq -c .dispatch.hold "$TEAM")"
  echo "signals: \$(grep -c TERM "$SIGNALS")"; } >>"$RESUMED"
exit ${1:-0}
SH
  chmod +x "$BIN/claude"
}
session_log() {
  printf '%s\n' 'not json' \
    '{"type":"system","subtype":"init","cwd":"/elsewhere","session_id":"sess-123"}' \
    '{"type":"assistant","session_id":"sess-123"}' >"$A_TEAM_STATE/demo/dev/latest.jsonl"
}

case_ "attach on a running role stops and holds it while you resume its session, then lets it start again"
WORKDIR=$(mktemp -d "$WORK/workdir.XXXXXX")
fixture <<JSON
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 },
  "workdir": "$WORKDIR", "dispatch": { "enabled": true } }
JSON
gh_items </dev/null
fake_run
session_log
fake_claude
run attach demo dev
wait "$RUN_PID"
same "exit" 0 "$STATUS"
same "resumed" "args: --resume sess-123" "$(sed -n 1p "$RESUMED")"
same "cwd" "cwd: $(cd "$WORKDIR" && pwd -P)" "$(sed -n 2p "$RESUMED")"
same "held first" 'hold: ["dev"]' "$(sed -n 3p "$RESUMED")"
same "stopped first" "signals: 1" "$(sed -n 4p "$RESUMED")"
same "released" '[]' "$(held)"

case_ "attach on a finished role doesn't signal anything, and resumes"
true &
dead=$!
wait "$dead"
echo "$dead" >"$A_TEAM_STATE/demo/dev/pid"
: >"$SIGNALS"
fake_claude
run attach demo dev
same "exit" 0 "$STATUS"
same "resumed" "args: --resume sess-123" "$(sed -n 1p "$RESUMED")"
same "signals" "signals: 0" "$(sed -n 4p "$RESUMED")"
same "released" '[]' "$(held)"

case_ "attach --dry-run names the session and workdir, and neither stops, holds nor resumes"
fake_run
fake_claude
run attach --dry-run demo dev
same "exit" 0 "$STATUS"
grep -q "would stop run $RUN_PID" "$OUT" || fail "dry run: '$(cat "$OUT")'"
grep -q "would run: claude --resume sess-123, in $WORKDIR" "$OUT" || fail "dry run: '$(cat "$OUT")'"
same "hold" '[]' "$(held)"
same "resumed" "" "$(cat "$RESUMED")"
kill -0 "$RUN_PID" 2>/dev/null || fail "dry run: the run was stopped"
kill "$RUN_PID" 2>/dev/null
wait "$RUN_PID" 2>/dev/null

case_ "a session that won't resume says so, and leaves the role stopped and held"
fake_claude 1
run attach demo dev
failed "won't resume"
grep -q "couldn't resume session sess-123" "$ERR" || fail "won't resume: '$(cat "$ERR")'"
same "held" '["dev"]' "$(held)"

case_ "attach with no session to resume is one clear line, and neither holds nor resumes"
run resume demo dev
echo '{"type":"assistant"}' >"$A_TEAM_STATE/demo/dev/latest.jsonl"
fake_claude
run attach demo dev
failed "no session"
one_line "no session"
grep -q "demo dev has no run with a session to resume" "$ERR" || fail "no session: '$(cat "$ERR")'"
same "hold" '[]' "$(held)"
same "resumed" "" "$(cat "$RESUMED")"

case_ "attach is in the usage text"
run help
grep -q '^  attach \[--dry-run\] <team> <role>' "$OUT" || fail "usage: no attach line"
unset A_TEAM_STATE

# --- the team's GitHub App -------------------------------------------------------------------
# A Keychain holding a real test key, and GitHub's App endpoints, stubbed on PATH. `security` has
# no key when NO_KEY is set; `curl` records each mint in $MINTS, keeps the JWT it was sent in
# $APP_BIN/jwt, grants what $APP_BIN/perms.json holds, turns down every JWT when MINT_FAILS is set,
# and finds no installation when NOT_INSTALLED is.
APP_BIN=$(mktemp -d "$WORK/app.XXXXXX")
MINTS="$APP_BIN/mints" OPENED="$APP_BIN/opened"
openssl genrsa 2048 2>/dev/null >"$APP_BIN/key.pem"
openssl rsa -in "$APP_BIN/key.pem" -pubout 2>/dev/null >"$APP_BIN/pub.pem"
openssl base64 -A <"$APP_BIN/key.pem" >"$APP_BIN/key.b64"
rm "$APP_BIN/key.pem"
echo '{"contents": "write", "issues": "write", "organization_projects": "write"}' >"$APP_BIN/perms.json"
cat >"$APP_BIN/security" <<SH
#!/usr/bin/env bash
[ -z "\${NO_KEY:-}" ] || exit 44
case " \$* " in *" -w "*) cat "$APP_BIN/key.b64" ;; esac
SH
cat >"$APP_BIN/curl" <<SH
#!/usr/bin/env bash
answer() { printf '%s\n%s' "\$2" "\$1"; }
[ -z "\${MINT_FAILS:-}" ] || { answer 401 '{"message": "Bad credentials"}'; exit; }
for arg; do
  case "\$arg" in @*) sed -n 's/^Authorization: Bearer //p' "\${arg#@}" >"$APP_BIN/jwt" ;; esac
done
case "\${!#}" in
  */repos/*/installation)
    if [ -n "\${NOT_INSTALLED:-}" ]; then answer 404 '{"message": "Not Found"}'; else answer 200 '{"id": 42}'; fi ;;
  */app) answer 200 '{"id": 7, "slug": "demo-app"}' ;;
  */users/demo-app%5Bbot%5D) echo lookup >>"$APP_BIN/lookups"; answer 200 '{"id": 99, "login": "demo-app[bot]"}' ;;
  */installation/repositories*) answer 200 '{"total_count": 1, "repositories": [{"full_name": "mentaldesk/demo"}]}' ;;
  */app/installations/42/access_tokens)
    sleep 0.2
    echo mint >>"$MINTS"
    answer 201 "\$(jq -n --arg t "ghs_\$\$" --slurpfile p "$APP_BIN/perms.json" \
      '{token: \$t, expires_at: (now + 3600 | todate), permissions: \$p[0]}')" ;;
  *) answer 404 '{"message": "Not Found"}' ;;
esac
SH
printf '#!/usr/bin/env bash\necho "$*" >>"%s"\n' "$OPENED" >"$APP_BIN/open"
chmod +x "$APP_BIN/security" "$APP_BIN/curl" "$APP_BIN/open"
PATH="$APP_BIN:$PATH"
export A_TEAM_STATE
A_TEAM_STATE=$(mktemp -d "$WORK/state.XXXXXX")
CACHE="$A_TEAM_STATE/demo/token.json"
mints() { grep -c '' "$MINTS" 2>/dev/null || echo 0; }
# cached <token> <seconds left>
cached() {
  mkdir -p "$(dirname "$CACHE")"
  jq -n --arg t "$1" --argjson left "$2" --slurpfile p "$APP_BIN/perms.json" \
    '{token: $t, expires_at: (now + $left | floor | todate), permissions: $p[0]}' >"$CACHE"
}
b64url_decode() {
  local s
  s=$(tr '_-' '/+')
  while [ $((${#s} % 4)) -ne 0 ]; do s="$s="; done
  printf '%s' "$s" | openssl base64 -d -A
}
app_fixture() {
  fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "project": { "owner": "mentaldesk", "number": 1 },
  "app": { "id": 7, "slug": "demo-app" } }
JSON
  rm -f "$CACHE" "$MINTS"
}

case_ "token mints one when nothing is cached, with a JWT the App's key signed"
app_fixture
run token demo
same "exit" 0 "$STATUS"
same "mints" 1 "$(mints)"
same "cached" "$(cat "$OUT")" "$(jq -r .token "$CACHE")"
IFS=. read -r jwt_header jwt_payload jwt_signature <"$APP_BIN/jwt"
same "issuer" '"7"' "$(b64url_decode <<<"$jwt_payload" | jq -c .iss)"
same "backdated" 600 "$(b64url_decode <<<"$jwt_payload" | jq '.exp - .iat')"
b64url_decode <<<"$jwt_signature" >"$APP_BIN/signature"
printf '%s.%s' "$jwt_header" "$jwt_payload" |
  openssl dgst -sha256 -verify "$APP_BIN/pub.pem" -signature "$APP_BIN/signature" >/dev/null ||
  fail "jwt: the signature doesn't verify against the App's key"

case_ "run again straight away, it prints the same token and mints nothing"
first=$(cat "$OUT")
run token demo
same "token" "$first" "$(cat "$OUT")"
same "mints" 1 "$(mints)"

case_ "with under five minutes left, it mints a new one"
cached ghs_expiring 280
run token demo
same "exit" 0 "$STATUS"
same "mints" 2 "$(mints)"
[ "$(cat "$OUT")" != ghs_expiring ] || fail "expiring: the old token was passed on"

case_ "with five minutes left, it's still reused"
cached ghs_fresh 330
run token demo
same "token" ghs_fresh "$(cat "$OUT")"

case_ "a malformed or empty cache is re-minted rather than passed on"
for broken in 'not json' '' '{"token": "", "expires_at": "2099-01-01T00:00:00Z"}' '{"token": "ghs_x"}'; do
  rm -f "$MINTS"
  printf '%s' "$broken" >"$CACHE"
  run token demo
  same "exit ($broken)" 0 "$STATUS"
  same "mints ($broken)" 1 "$(mints)"
  same "token ($broken)" "$(jq -r .token "$CACHE")" "$(cat "$OUT")"
done

case_ "two concurrent mints leave a readable cache holding one of their tokens"
rm -f "$CACHE"
A_TEAM_CONFIG="$CONFIG" "$A_TEAM" token demo >"$WORK/mint1" 2>&1 &
A_TEAM_CONFIG="$CONFIG" "$A_TEAM" token demo >"$WORK/mint2" 2>&1 &
wait
winner=$(jq -r .token "$CACHE")
{ [ "$winner" = "$(cat "$WORK/mint1")" ] || [ "$winner" = "$(cat "$WORK/mint2")" ]; } ||
  fail "concurrent: cache holds '$winner', not either run's token"
same "leftovers" 1 "$(find "$(dirname "$CACHE")" -name 'token.json*' | grep -c '')"

case_ "a key GitHub turns down is blamed on the key, in a-team's words, not on the install"
rm -f "$CACHE"
MINT_FAILS=1 run token demo
failed "wrong key"
grep -q "the one in the Keychain under account mentaldesk isn't that App's (Bad credentials (HTTP 401))" "$ERR" ||
  fail "wrong key: '$(cat "$ERR")'"
grep -q 'install' "$ERR" && fail "wrong key: told to install it: '$(cat "$ERR")'"
grep -q 'curl\|{' "$ERR" && fail "wrong key: curl's own output got through: '$(cat "$ERR")'"

case_ "an App that isn't installed on the repo is told where to install it"
NOT_INSTALLED=1 run token demo
failed "not installed"
grep -q "app 7 isn't installed on mentaldesk/demo: install it at https://github.com/apps/demo-app/installations/new" "$ERR" ||
  fail "not installed: '$(cat "$ERR")'"

case_ "a team with no app key has no token, and is told how to get one"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "project": { "owner": "mentaldesk", "number": 1 } }
JSON
run token demo
failed "no app"
grep -q 'a-team app create demo' "$ERR" || fail "no app: '$(cat "$ERR")'"

# The real gh the wrapper stands in front of: prints GH_TOKEN, then each argument on its own line.
mkdir -p "$APP_BIN/real"
printf '#!/usr/bin/env bash\necho "GH_TOKEN=${GH_TOKEN:-}"\nprintf "[%%s]\\n" "$@"\n' >"$APP_BIN/real/gh"
chmod +x "$APP_BIN/real/gh"
wrapped() {
  A_TEAM_CONFIG="$CONFIG" PATH="$ROOT/bin:$APP_BIN/real:$PATH" gh "$@" >"$OUT" 2>"$ERR"
  STATUS=$?
}

case_ "the gh wrapper passes arguments through unchanged, as the run's team's App"
app_fixture
cached ghs_cached 3600
A_TEAM_RUN_TEAM=demo wrapped api 'a b' '' '$x' "it's"
same "exit" 0 "$STATUS"
same "output" 'GH_TOKEN=ghs_cached
[api]
[a b]
[]
[$x]
[it'"'"'s]' "$(cat "$OUT")"
same "caller's GH_TOKEN" '' "${GH_TOKEN:-}"

case_ "outside a run, it's the real gh as it was"
A_TEAM_RUN_TEAM='' wrapped pr list
same "no run" 'GH_TOKEN=
[pr]
[list]' "$(cat "$OUT")"

case_ "in a run of a team with no app key, it fails and names the command, rather than running gh as you"
fixture <<'JSON'
{ "repo": "mentaldesk/demo" }
JSON
A_TEAM_RUN_TEAM=demo wrapped pr list
failed "no app"
grep -q 'GH_TOKEN' "$OUT" && fail "no app: the real gh ran"
grep -q 'a-team app create demo, then install it' "$ERR" || fail "no app: '$(cat "$ERR")'"

case_ "a team whose token won't mint fails, rather than running gh as you"
app_fixture
A_TEAM_RUN_TEAM=demo NO_KEY=1 wrapped pr list
failed "no key"
grep -q 'GH_TOKEN' "$OUT" && fail "no key: the real gh ran"

# board.sh's check against a board that's fine, with the App's own view of it broken by
# PROJECT_UNREADABLE or PRIORITY_UNREADABLE when it asks with the cached token.
mkdir -p "$APP_BIN/board"
jq -n '{data: {organization: {projectV2: {id: "PVT_1", field: {id: "PVTSSF_status", options:
  (["Idea", "Exploring", "Pitched", "Approved", "Building", "Ready", "In progress", "In review", "Done"]
   | map({id: ., name: .}))}}}}}' >"$APP_BIN/board/meta.json"
echo '{"data": {"organization": {"projectV2": {"items": {"totalCount": 0,
  "pageInfo": {"hasNextPage": false, "endCursor": null}, "nodes": []}}}}}' >"$APP_BIN/board/items.json"
echo '{"data": {"organization": {"issueFields": {"nodes": [{"id": "IF_priority", "name": "Priority",
  "options": []}]}}}}' >"$APP_BIN/board/fields.json"
cat >"$APP_BIN/board/gh" <<SH
#!/usr/bin/env bash
refused() { echo "gh: Resource not accessible by integration" >&2; exit 1; }
case " \$* " in
  *ProjectV2SingleSelectField*) page="$APP_BIN/board/meta.json" ;;
  *totalCount*) [ "\${GH_TOKEN:-}" = ghs_cached ] && [ -z "\${PROJECT_UNREADABLE:-}" ] || refused
                page="$APP_BIN/board/items.json" ;;
  *issueFields*) [ "\${GH_TOKEN:-}" = ghs_cached ] && [ -z "\${PRIORITY_UNREADABLE:-}" ] || refused
                 page="$APP_BIN/board/fields.json" ;;
  *) page="$APP_BIN/board/items.json" ;;
esac
filter=
while [ \$# -gt 0 ]; do
  [ "\$1" = --jq ] && { filter=\$2; break; }
  shift
done
if [ -n "\$filter" ]; then jq -r "\$filter" "\$page"; else cat "\$page"; fi
SH
chmod +x "$APP_BIN/board/gh"
checked() { PATH="$APP_BIN/board:$PATH" run board demo check; }
grants() { jq "$1" "$CACHE" >"$CACHE.new" && mv "$CACHE.new" "$CACHE"; }

case_ "check fails a team with no app key, and names the command"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "project": { "owner": "mentaldesk", "number": 1 } }
JSON
checked
failed "no app"
grep -q '^identity · NO APP' "$ERR" || fail "no app: '$(cat "$ERR")'"
grep -q 'run a-team app create demo, then install it' "$ERR" || fail "no app: no command in '$(cat "$ERR")'"

case_ "check reports the App's identity when every part of it works"
app_fixture
cached ghs_cached 3600
checked
same "exit" 0 "$STATUS"
same "identity" 'identity: demo-app[bot] · token ok · project 1 read+write ok · Priority readable · push access to mentaldesk/demo ok' \
  "$(grep '^identity' "$OUT")"

case_ "check says which part of the identity is wrong, and what to do about it"
NO_KEY=1 checked
failed "no key"
grep -q "NO KEY" "$ERR" && grep -q 'a-team app create demo' "$ERR" || fail "no key: '$(cat "$ERR")'"
rm -f "$CACHE"
MINT_FAILS=1 checked
failed "no token"
grep -q 'NO TOKEN' "$ERR" || fail "no token: '$(cat "$ERR")'"
cached ghs_cached 3600
PROJECT_UNREADABLE=1 checked
failed "project unreadable"
grep -q 'PROJECT 1 UNREADABLE' "$ERR" || fail "project unreadable: '$(cat "$ERR")'"
grants '.permissions.organization_projects = "read"'
checked
failed "project read-only"
grep -q 'PROJECT 1 READ-ONLY' "$ERR" || fail "project read-only: '$(cat "$ERR")'"
cached ghs_cached 3600
PRIORITY_UNREADABLE=1 checked
failed "priority"
grep -q 'PRIORITY UNREADABLE' "$ERR" && grep -q 'Issue Fields: read' "$ERR" || fail "priority: '$(cat "$ERR")'"
grants '.permissions.contents = "read"'
checked
failed "no push"
grep -q 'NO PUSH' "$ERR" || fail "no push: '$(cat "$ERR")'"

case_ "examples/team.json carries the app key and stays valid"
jq -e 'has("app")' "$ROOT/examples/team.json" >/dev/null || fail "example: no app key"

case_ "app create reuses the App another team under the same owner already has"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer" }
JSON
echo '{ "repo": "mentaldesk/other", "app": { "id": 9, "slug": "shared-app" } }' >"$CONFIG/teams/other.json"
: >"$OPENED"
NOT_INSTALLED=1 run app create demo
same "exit" 0 "$STATUS"
same "app" '{"id":9,"slug":"shared-app"}' "$(jq -c .app "$TEAM")"
same "reviewer" '"reviewer"' "$(jq -c .reviewer "$TEAM")"
same "opened" 'https://github.com/apps/shared-app/installations/new' "$(cat "$OPENED")"

case_ "app create again on a team whose App is installed doesn't reopen the install page"
: >"$OPENED"
run app create demo
same "exit" 0 "$STATUS"
grep -q 'installed    on mentaldesk/demo already' "$OUT" || fail "installed: '$(cat "$OUT")'"
same "opened" '' "$(cat "$OPENED")"

case_ "app create again with a key that isn't the App's says so, rather than opening the install page"
MINT_FAILS=1 run app create demo
failed "wrong key"
grep -q "isn't that App's" "$ERR" || fail "wrong key: '$(cat "$ERR")'"
same "opened" '' "$(cat "$OPENED")"

case_ "app create with the owner's key in the Keychain but no team naming it needs the App's id"
fixture <<'JSON'
{ "repo": "mentaldesk/demo" }
JSON
run app create demo
failed "no id"
grep -q -- '--id' "$ERR" || fail "no id: '$(cat "$ERR")'"
run app create demo --id 7
same "exit" 0 "$STATUS"
same "app" '{"id":7,"slug":"demo-app"}' "$(jq -c .app "$TEAM")"

# credential <action> <host> [<path>]: asks the helper as git would.
credential() {
  printf 'protocol=https\nhost=%s\n%s\n' "$2" "${3:+path=$3}" |
    A_TEAM_CONFIG="$CONFIG" bash "$ROOT/scripts/credential.sh" demo "$1" >"$OUT" 2>"$ERR"
  STATUS=$?
}

case_ "the credential helper answers github.com with the App's token, in git's format"
app_fixture
cached ghs_cached 3600
credential get github.com mentaldesk/demo.git
same "exit" 0 "$STATUS"
same "answer" 'username=x-access-token
password=ghs_cached' "$(cat "$OUT")"

case_ "it says nothing to any other host, or to store and erase"
credential get gitlab.com mentaldesk/demo.git
same "other host" '' "$(cat "$OUT")"
credential store github.com mentaldesk/demo.git
same "store" '' "$(cat "$OUT")"

case_ "a repo the App can't reach makes git give up at once, saying where to install it"
credential get github.com mentaldesk/elsewhere.git
same "answer" 'quit=1' "$(cat "$OUT")"
grep -q "demo-app\[bot\] has no access to mentaldesk/elsewhere: install the App there at https://github.com/apps/demo-app/installations/new" "$ERR" ||
  fail "no access: '$(cat "$ERR")'"

case_ "a token that won't mint makes git give up at once, saying why"
rm -f "$CACHE"
NO_KEY=1 credential get github.com mentaldesk/demo.git
same "answer" 'quit=1' "$(cat "$OUT")"
grep -q 'no private key in the login Keychain' "$ERR" || fail "no key: '$(cat "$ERR")'"

REPO_DIR=$(mktemp -d "$WORK/repo.XXXXXX")
export GIT_CONFIG_GLOBAL="$APP_BIN/gitconfig" GIT_CONFIG_NOSYSTEM=1
printf '[user]\n\tname = Reviewer\n\temail = reviewer@example.com\n' >"$GIT_CONFIG_GLOBAL"
git -C "$REPO_DIR" init -q
# git as a run would find it: bin/ first on PATH.
run_git() {
  A_TEAM_CONFIG="$CONFIG" PATH="$ROOT/bin:$PATH" git -C "$REPO_DIR" "$@" >"$OUT" 2>"$ERR"
  STATUS=$?
}
last_commit() { git -C "$REPO_DIR" log -1 --format='%an <%ae> / %cn <%ce>'; }

case_ "in a run, a commit is the App's bot's, and outside one it's yours"
app_fixture
cached ghs_cached 3600
rm -f "$APP_BIN/lookups" "$A_TEAM_STATE/demo/bot.json"
global=$(cat "$GIT_CONFIG_GLOBAL")
A_TEAM_RUN_TEAM=demo run_git commit -q --allow-empty -m bot
same "exit" 0 "$STATUS"
same "in a run" 'demo-app[bot] <99+demo-app[bot]@users.noreply.github.com> / demo-app[bot] <99+demo-app[bot]@users.noreply.github.com>' \
  "$(last_commit)"
run_git commit -q --allow-empty -m me
same "outside" 'Reviewer <reviewer@example.com> / Reviewer <reviewer@example.com>' "$(last_commit)"
A_TEAM_RUN_TEAM=demo run_git commit -q --allow-empty -m again
same "bot looked up once" 1 "$(grep -c '' "$APP_BIN/lookups")"
same "global config" "$global" "$(cat "$GIT_CONFIG_GLOBAL")"
same "repo config" '' "$(git -C "$REPO_DIR" config --local --get-regexp '^(user|credential)\.')"
same "caller's GIT_CONFIG_COUNT" '' "${GIT_CONFIG_COUNT:-}"

case_ "in a run, git asks only the App's helper, and gets the token"
A_TEAM_RUN_TEAM=demo run_git config --get-all credential.helper
same "helpers" "
!bash '$ROOT/bin/../scripts/credential.sh' 'demo'" "$(cat "$OUT")"
printf 'protocol=https\nhost=github.com\npath=mentaldesk/demo.git\n\n' |
  A_TEAM_RUN_TEAM=demo run_git credential fill
grep -qx 'password=ghs_cached' "$OUT" || fail "fill: '$(cat "$OUT")'"

case_ "config the caller already passes in GIT_CONFIG_COUNT still counts"
GIT_CONFIG_COUNT=1 GIT_CONFIG_KEY_0=a-team.test GIT_CONFIG_VALUE_0=kept A_TEAM_RUN_TEAM=demo run_git config a-team.test
same "kept" kept "$(cat "$OUT")"

case_ "a team with no app key gets no helper and no identity"
fixture <<'JSON'
{ "repo": "mentaldesk/demo" }
JSON
A_TEAM_RUN_TEAM=demo run_git config --get-all credential.helper
same "helpers" '' "$(cat "$OUT")"
A_TEAM_RUN_TEAM=demo run_git commit -q --allow-empty -m no-app
same "no app" 'Reviewer <reviewer@example.com> / Reviewer <reviewer@example.com>' "$(last_commit)"
unset GIT_CONFIG_GLOBAL GIT_CONFIG_NOSYSTEM
unset A_TEAM_STATE

[ "$failures" -eq 0 ] || { echo "$failures failed"; exit 1; }
echo "all passed"
