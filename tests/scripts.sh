#!/usr/bin/env bash
#
# tests/scripts.sh — the shell side of a-team. Run it with `bash tests/scripts.sh`; CI does too.
#
set -uo pipefail
# As in CI: a run's own team would otherwise reach every test's gh and git.
unset A_TEAM_RUN_TEAM

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
  TALK="$BIN/talk.json" UNLINKED="$BIN/unlinked.json"
  echo '{"data": {"repository": {}}}' >"$TALK"
  echo '{"data": {"repository": {}}}' >"$UNLINKED"
  ISSUE="$BIN/issue.json" THREAD="$BIN/thread.json"
  LINE="$BIN/line.json" REVIEWS="$BIN/reviews.json" RECENT="$BIN/recent.json"
  ACKED="$BIN/acked" POSTED="$BIN/posted" EMPTY="$BIN/empty.json"
  BLOCKED="$BIN/blocked.json" WRITES="$BIN/writes"
  EDITED="$BIN/edited" SUBS="$BIN/subs.json"
  echo '[]' >"$SUBS"
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
  FILTERS="$BIN/filters"
  : >"$FILTERS"
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
  *"issue close"*) [ ! -e "$BIN/close-fails" ] || { echo "gh: Resource not accessible by integration (HTTP 403)" >&2; exit 1; }
                   echo "CLOSE \$*" >>"$WRITES"; exit 0 ;;
  *"issue edit"*"--body"*) echo "EDIT \$*" >>"$WRITES"; printf '%s' "\${@: -1}" >"$EDITED"; exit 0 ;;
  *"-X POST"*sub_issues*) echo "POST \$*" >>"$WRITES"; echo '{}'; exit 0 ;;
  *"-X DELETE"*sub_issue*) echo "DELETE \$*" >>"$WRITES"; echo '{}'; exit 0 ;;
  *sub_issues*) page="$SUBS" ;;
  *"--remove-label"* | *"--add-label"*) echo "\$*" >>"$WRITES"; exit 0 ;;
  *"/labels -f labels[]="*) echo "\$*" >>"$WRITES"; echo '[]'; exit 0 ;;
  *"label list"*) page="$EMPTY" ;;
  *"label create"*) echo "\$*" >>"$WRITES"; exit 0 ;;
  *"--input -"*) cat >"$BIN/mutation.json"; echo '{}'; exit 0 ;;
  *check-runs*) page="$RUNS" ;;
  *issueOrPullRequest*) page="$TALK" ;;
  *": pullRequest(number"*) page="$UNLINKED" ;;
  *closedByPullRequestsReferences*) page="$PRS" ;;
  *reviews*) page="$REVIEWS" ;;
  *"/issues/comments?since"*) page="$RECENT" ;;
  *"comments?since"*) page="$EMPTY" ;;
  *"-X POST"*dependencies/blocked_by*) echo "POST \$*" >>"$WRITES"; echo '{}'; exit 0 ;;
  *"-X DELETE"*dependencies/blocked_by*) echo "DELETE \$*" >>"$WRITES"; echo '{}'; exit 0 ;;
  *dependencies/blocked_by*) page="$BLOCKED" ;;
  *"/issues/"*"/comments"*) page="$THREAD" ;;
  *"/pulls/"*"/comments"*) page="$LINE" ;;
  *"-X PUT"*"/merge"*) [ ! -e "$BIN/merge-fails" ] || { echo "gh: Pull Request is not mergeable (HTTP 405)" >&2; exit 1; }
                      echo "PUT \$*" >>"$WRITES"; echo '{}'; exit 0 ;;
  *"-X DELETE"*"/git/refs/"*) echo "DELETE \$*" >>"$WRITES"; echo '{}'; exit 0 ;;
  *"/issues/404"*) echo "gh: Not Found (HTTP 404)" >&2; exit 1 ;;
  *"/pulls/"[0-9]*) page="$PULL" ;;
  *"/issues/"[0-9]*) page="$ISSUE" ;;
  *) page="$ITEMS" ;;
esac
for arg in "\$@"; do
  case \$arg in filter=*) printf '%s\n' "\${arg#filter=}" >>"$FILTERS" ;; esac
done
case " \$* " in
  *checkSuites*)
    jq --slurpfile runs "$RUNS" '(\$runs[0].check_runs | map({name, status: (.status | ascii_upcase),
        conclusion: (.conclusion | if . == null then null else ascii_upcase end),
        completedAt: .completed_at, url: .html_url})) as \$runs
      | walk(if type == "object" and has("isDraft")
             then . + {commits: {nodes: [{commit: {checkSuites: {nodes: [{checkRuns: {nodes: \$runs}}]}}}]}}
             else . end)' "\$page" >"\$page.checked"
    page="\$page.checked" ;;
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

# `gh_pr <number> <draft> [<issue>]`: the open PR that closes every issue `pr` asks about. No
# arguments, none. With <issue>, GitHub hasn't linked it: only its body says it closes #<issue>.
gh_pr() {
  jq -n --arg n "${1:-}" --arg draft "${2:-true}" --arg issue "${3:-}" '
    (if $n == "" then [] else [{number: ($n | tonumber), url: "https://github.com/mentaldesk/demo/pull/\($n)",
       isDraft: ($draft == "true"), headRefName: "task", mergeable: "MERGEABLE"}] end) as $pr
    | {data: {repository: {
        issue: {closedByPullRequestsReferences: {nodes: (if $issue == "" then $pr else [] end)}},
        pullRequests: {nodes: (if $issue == "" then []
          else $pr | map(. + {body: "Closes #\($issue)\n\n<!-- a-team:dev -->"}) end)}}}}' >"$PRS"
}

# The one GraphQL page `waiting` reads for whose turn it is, from lines of
# "<n> <kind> <timestamp> <author> <text...>". The body line is the item's own; the pr-* kinds
# (pr-body, pr-comment, pr-review, pr-line) belong to the open PR that closes #<n>, which is
# numbered 900 + n and is ready and mergeable unless `gh_talk <draft> <mergeable>` says otherwise.
# `gh_talk <draft> <mergeable> unlinked` leaves that PR unlinked, found only by what its body closes.
# A kind ending `+seen` carries the 👀 a run leaves on a comment it has read, a `labeled` row's text
# is the label added, and `\n` in the text is a line break.
gh_talk() {
  jq -R -s --arg draft "${1:-false}" --arg mergeable "${2:-MERGEABLE}" \
    --argjson linked "$([ "${3:-}" = unlinked ] && echo false || echo true)" '
    def who($login): if $login | endswith("[bot]")
      then {__typename: "Bot", login: ($login | rtrimstr("[bot]"))} else {__typename: "User", login: $login} end;
    def seen: {reactions: {totalCount: (if .seen then 1 else 0 end)}};
    def node($rows; $number):
      ($rows | map(select(.kind == "body")) | first) as $body
      | {number: $number, createdAt: $body.at, body: ($body.body // ""),
         author: who($body.author // ""), reactions: {totalCount: 0},
         timelineItems: {nodes: ($rows | map(select(.kind == "labeled")
           | {createdAt: .at, label: {name: .body}}))},
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
        | {n: .[0].n, own: node($own; .[0].n),
           pr: (if ($pr | length) > 0 then open_pr($pr; 900 + .[0].n) else null end)})
    | {talk: {data: {repository: ((map({key: "x\(.n)", value: (.own + {closedByPullRequestsReferences:
          {nodes: (if .pr != null and $linked then [.pr] else [] end)}})}) | from_entries)
          + if $linked then {} else {openPrs: {nodes: map(.pr // empty | {number, body})}} end)}},
       unlinked: {data: {repository: (map(.pr // empty | {key: "p\(.number)", value: .}) | from_entries)}}}' \
    >"$BIN/talk.all.json"
  jq .talk "$BIN/talk.all.json" >"$TALK"
  jq .unlinked "$BIN/talk.all.json" >"$UNLINKED"
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
YESTERDAY=$(jq -rn 'now - 86400 | strftime("%Y-%m-%d")')

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

case_ "waiting asks GitHub for the board without what's Done"
same "filter" '-Status:"Done"' "$(cat "$FILTERS")"

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
same "api calls" 2 "$(grep -c '' <"$CALLS")"

case_ "so it is when GitHub hasn't linked that PR to the task, and only its body closes it"
gh_talk false MERGEABLE unlinked <<TALK
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 pr-body ${TODAY}T08:25:00Z demo-app[bot] Closes #115\n<!-- a-team:dev -->
115 pr-comment ${TODAY}T10:15:00Z reviewer This one needs a test.
TALK
: >"$CALLS"
run board demo waiting
same "exit" 0 "$STATUS"
same "task turn" '"dev"' "$(jq -c '.[1].turn' "$OUT")"
same "task reason" '"answering your feedback since 10:15"' "$(jq -c '.[1].reason' "$OUT")"
same "api calls" 3 "$(grep -c '' <"$CALLS")"

case_ "but a PR the Dev didn't open isn't taken for the task's on its word"
gh_talk false MERGEABLE unlinked <<TALK
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 pr-body ${TODAY}T08:25:00Z passer-by Closes #115
115 pr-comment ${TODAY}T10:15:00Z reviewer This one needs a test.
TALK
run board demo waiting
same "exit" 0 "$STATUS"
same "task turn" '"you"' "$(jq -c '.[1].turn' "$OUT")"

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
  '["base","checks","conflicting","draft","number","pitch","pr","prUrl","priority","reason","status","team","title","turn","unready","url"]' \
  "$(jq -c '.[1] | keys' "$OUT")"
same "pitch flags" '[true,false]' "$(jq -c '[.[].pitch]' "$OUT")"
same "pr" 1015 "$(jq -c '.[1].pr' "$OUT")"
same "prUrl" '"https://github.com/mentaldesk/demo/pull/1015"' "$(jq -c '.[1].prUrl' "$OUT")"

case_ "a green, mergeable, ready PR with nothing unanswered stays the reviewer's"
same "checks" '"pass"' "$(jq -c '.[1].checks' "$OUT")"
same "conflicting" false "$(jq -c '.[1].conflicting' "$OUT")"
same "draft" false "$(jq -c '.[1].draft' "$OUT")"
same "base" '"main"' "$(jq -c '.[1].base' "$OUT")"
same "unready" '""' "$(jq -c '.[1].unready' "$OUT")"
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
same "still unready to merge" '"CI failing"' "$(jq -c '.[1].unready' "$OUT")"
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

case_ "a pitch that needs the reviewer's answer comes with that section as its question"
gh_items <<'ITEMS'
Pitched 257 Devs or worktrees
Pitched 258 Only assumed
Pitched 259 Heading only
Pitched 260 Says none
Pitched 261 Old format
In_review 262 A finished pitch
ITEMS
edit_item 262 '.labels.nodes = [{name: "pitch"}]'
gh_talk <<TALK
257 body ${TODAY}T08:00:00Z demo-app[bot] ## Proposal\n\nIt.\n\n## Assumed\n\n1. **Squash.** Always.\n\n## Needs your answer\n\n1. **Devs or Worktrees?** I can't pick.\n\n---\n\n## Original idea\n\n> Hi\n\n<!-- a-team:lead -->
258 body ${TODAY}T08:00:00Z demo-app[bot] ## Assumed\n\n1. **Squash.** Always.\n\n<!-- a-team:lead -->
259 body ${TODAY}T08:00:00Z demo-app[bot] ## Needs your answer\n\n## Original idea\n\n> Hi\n<!-- a-team:lead -->
260 body ${TODAY}T08:00:00Z demo-app[bot] ## Needs your answer\n\nNone.\n\n<!-- a-team:lead -->
261 body ${TODAY}T08:00:00Z demo-app[bot] ## Open questions\n\n1. **Which?** I'd pick one.\n<!-- a-team:lead -->
262 body ${TODAY}T08:00:00Z demo-app[bot] ## Needs your answer\n\n1. **Which?** Still open.\n<!-- a-team:lead -->
TALK
run board demo waiting
same "exit" 0 "$STATUS"
same "question" '"## Needs your answer\n\n1. **Devs or Worktrees?** I can'"'"'t pick."' "$(jq -c '.[0].question' "$OUT")"
same "turn" '"you"' "$(jq -c '.[0].turn' "$OUT")"
same "reason" '"asked you since 08:00"' "$(jq -c '.[0].reason' "$OUT")"
same "the others" '[[258,null],[259,null],[260,null],[261,null],[262,null]]' "$(jq -c '[.[1:][] | [.number, .question]]' "$OUT")"
same "their reasons" '"awaiting your approval since 08:00"' "$(jq -c '.[1].reason' "$OUT")"

case_ "once the reviewer answers a pitch's question, it's the Lead's turn and still asks it"
gh_talk <<TALK
257 body ${TODAY}T08:00:00Z demo-app[bot] ## Needs your answer\n\n1. **Devs or Worktrees?** I can't pick.\n<!-- a-team:lead -->
257 comment ${TODAY}T10:50:00Z reviewer Devs.
TALK
run board demo waiting
same "exit" 0 "$STATUS"
same "turn" '"lead"' "$(jq -c '.[0].turn' "$OUT")"
same "reason" '"answering your feedback since 10:50"' "$(jq -c '.[0].reason' "$OUT")"
same "question" '"## Needs your answer\n\n1. **Devs or Worktrees?** I can'"'"'t pick."' "$(jq -c '.[0].question' "$OUT")"

case_ "a Needs your answer heading inside a code block isn't a question, and doesn't end one"
# #261 shows the new pitch format in a mockup; 264 adds a real section above it.
PITCH_261=$(awk 'NR > 1 { printf "\\n" } { printf "%s", $0 }' "$ROOT/tests/fixtures/pitch-261.md")
gh_items <<'ITEMS'
Pitched 263 Only a mockup
Pitched 264 A real one and a mockup
Pitched 265 A question with a code block
Pitched 266 A tilde fence
ITEMS
gh_talk <<TALK
263 body ${TODAY}T08:00:00Z demo-app[bot] $PITCH_261
264 body ${TODAY}T08:00:00Z demo-app[bot] ## Needs your answer\n\n1. **Which?** Real.\n\n$PITCH_261
265 body ${TODAY}T08:00:00Z demo-app[bot] ## Needs your answer\n\n1. **Which?** Like:\n\n   \`\`\`\n   # not a heading\n   \`\`\`\n\n2. **And?** More.\n\n## Original idea\n\n> Hi\n<!-- a-team:lead -->
266 body ${TODAY}T08:00:00Z demo-app[bot] ~~~~\n## Needs your answer\n~~~\n1. Still fenced.\n~~~~\n<!-- a-team:lead -->
TALK
run board demo waiting
same "exit" 0 "$STATUS"
same "questions" '[[263,null],[264,"## Needs your answer\n\n1. **Which?** Real."],[265,"## Needs your answer\n\n1. **Which?** Like:\n\n   ```\n   # not a heading\n   ```\n\n2. **And?** More."],[266,null]]' \
  "$(jq -c '[.[] | [.number, .question]]' "$OUT")"

case_ "a task the Dev handed back with a question waits on the reviewer, with the question"
gh_items <<'ITEMS'
In_review 115 I can change any of the keys
Ready 192 I can reply to a pitch
Ready 195 Held by me
Ready 196 Waits on another task
ITEMS
edit_item 192 '.labels.nodes = [{name: "a-team:dev"}, {name: "blocked"}]'
edit_item 195 '.labels.nodes = [{name: "blocked"}]'
edit_item 196 '.issueDependenciesSummary.blockedBy = 1'
gh_talk <<TALK
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
192 body ${TODAY}T07:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
192 comment ${TODAY}T08:23:06Z demo-app[bot] Which marker should it post?\n\n<!-- a-team:dev -->
192 labeled ${TODAY}T08:23:14Z demo-app[bot] blocked
195 body ${TODAY}T07:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
195 comment ${TODAY}T07:05:00Z demo-app[bot] Split off from #194.\n<!-- a-team:lead -->
195 labeled ${TODAY}T09:00:00Z reviewer blocked
TALK
: >"$CALLS"
run board demo waiting
same "exit" 0 "$STATUS"
same "numbers" '[115,192]' "$(jq -c '[.[].number]' "$OUT")"
same "status" '"Ready"' "$(jq -c '.[1].status' "$OUT")"
same "turn" '"you"' "$(jq -c '.[1].turn' "$OUT")"
same "reason" '"asked you since 08:23"' "$(jq -c '.[1].reason' "$OUT")"
same "question" '"Which marker should it post?"' "$(jq -c '.[1].question' "$OUT")"
same "api calls" 2 "$(grep -c '' <"$CALLS")"

case_ "once the reviewer replies to the question, it's the Dev's turn"
gh_talk <<TALK
192 body ${TODAY}T07:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
192 comment ${TODAY}T08:23:06Z demo-app[bot] Which marker should it post?\n<!-- a-team:dev -->
192 labeled ${TODAY}T08:23:14Z demo-app[bot] blocked
192 comment+seen ${TODAY}T10:50:00Z reviewer The Dev's own.
TALK
run board demo waiting
same "exit" 0 "$STATUS"
same "turn" '"dev"' "$(jq -c '.[] | select(.number == 192) | .turn' "$OUT")"
same "reason" '"reading your answer since 10:50"' "$(jq -c '.[] | select(.number == 192) | .reason' "$OUT")"

case_ "a Dev comment long before the task was labelled blocked is no question"
gh_talk <<TALK
192 body ${TODAY}T07:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
192 comment ${TODAY}T07:10:00Z demo-app[bot] Draft PR #9 is up.\n<!-- a-team:dev -->
192 labeled ${TODAY}T09:00:00Z reviewer blocked
TALK
run board demo waiting
same "exit" 0 "$STATUS"
same "numbers" '[115]' "$(jq -c '[.[].number]' "$OUT")"

case_ "a reply to the Dev's question starts a Dev run, and a held task's comment doesn't"
gh_talk <<TALK
192 body ${TODAY}T07:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
192 comment ${TODAY}T08:23:06Z demo-app[bot] Which marker should it post?\n<!-- a-team:dev -->
192 labeled ${TODAY}T08:23:14Z demo-app[bot] blocked
192 comment ${TODAY}T10:50:00Z reviewer The Dev's own.
195 body ${TODAY}T07:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
195 labeled ${TODAY}T09:00:00Z reviewer blocked
195 comment ${TODAY}T09:30:00Z reviewer Not yet, I'm holding this.
TALK
run board demo triggers dev
same "exit" 0 "$STATUS"
same "reasons" "[\"stakeholder answered the Dev's question on #192 (${TODAY}T10:50:00Z)\"]" "$(jq -c .reasons "$OUT")"
run board demo waiting
same "no unread in waiting" 'null' "$(jq -c '.[] | select(.number == 192) | .unread' "$OUT")"

case_ "once a run has read the reply, it starts no more runs"
gh_talk <<TALK
192 body ${TODAY}T07:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
192 comment ${TODAY}T08:23:06Z demo-app[bot] Which marker should it post?\n<!-- a-team:dev -->
192 labeled ${TODAY}T08:23:14Z demo-app[bot] blocked
192 comment+seen ${TODAY}T10:50:00Z reviewer The Dev's own.
TALK
run board demo triggers dev
same "exit" 0 "$STATUS"
same "reasons" '[]' "$(jq -c .reasons "$OUT")"

case_ "the Dev asking again makes the question the reviewer's once more"
gh_talk <<TALK
192 body ${TODAY}T07:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
192 comment ${TODAY}T08:23:06Z demo-app[bot] Which marker should it post?\n<!-- a-team:dev -->
192 labeled ${TODAY}T08:23:14Z demo-app[bot] blocked
192 comment+seen ${TODAY}T10:50:00Z reviewer I don't understand the question.
192 comment ${TODAY}T11:00:00Z demo-app[bot] Put another way: the Lead's marker or the Dev's?\n<!-- a-team:dev -->
TALK
run board demo waiting
same "turn" '"you"' "$(jq -c '.[] | select(.number == 192) | .turn' "$OUT")"
same "reason" '"asked you since 11:00"' "$(jq -c '.[] | select(.number == 192) | .reason' "$OUT")"
run board --dry-run demo unblock dev 192
failed "unblock before a reply"
grep -q "no stakeholder has replied" "$ERR" || fail "unblock before a reply: '$(cat "$ERR")'"

case_ "the Dev may clear blocked once the reviewer has replied, and reads the reply once"
gh_talk <<TALK
192 body ${TODAY}T07:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
192 comment ${TODAY}T08:23:06Z demo-app[bot] Which marker should it post?\n<!-- a-team:dev -->
192 labeled ${TODAY}T08:23:14Z demo-app[bot] blocked
192 comment ${TODAY}T10:50:00Z reviewer The Dev's own.
TALK
gh_thread <<THREAD
body ${TODAY}T07:00:00Z demo-app[bot] 0 The task\n<!-- a-team:lead -->
comment ${TODAY}T08:23:06Z demo-app[bot] 0 Which marker should it post?\n<!-- a-team:dev -->
comment ${TODAY}T10:50:00Z reviewer 0 The Dev's own.
THREAD
: >"$WRITES"
: >"$ACKED"
run board --dry-run demo unblock dev 192
same "dry-run exit" 0 "$STATUS"
same "dry-run said" "(dry run) #192: no longer blocked" "$(cat "$OUT")"
same "dry-run writes" "" "$(cat "$WRITES")"
same "dry-run acked" "" "$(cat "$ACKED")"
A_TEAM_RUN_STARTED=${TODAY}T23:59:59Z run board demo unblock dev 192
same "exit" 0 "$STATUS"
same "writes" "issue edit 192 -R mentaldesk/demo --remove-label blocked" "$(cat "$WRITES")"
same "acked" "IC_2" "$(cat "$ACKED")"

case_ "the Dev may not clear blocked on a task the reviewer holds, nor may the Lead"
gh_talk <<TALK
195 body ${TODAY}T07:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
195 labeled ${TODAY}T09:00:00Z reviewer blocked
195 comment ${TODAY}T09:30:00Z reviewer Not yet, I'm holding this.
TALK
: >"$WRITES"
run board demo unblock dev 195
failed "held task"
grep -q "a stakeholder is holding it" "$ERR" || fail "held task: '$(cat "$ERR")'"
run board demo unblock lead 192
failed "lead"
run board demo unblock dev 115
failed "a task in review"
same "writes" "" "$(cat "$WRITES")"

case_ "a board whose Done is called something else has that left off instead"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "project": { "owner": "mentaldesk", "number": 1, "statusMap": { "Done": "Shipped" } } }
JSON
gh_items <<'ITEMS'
Shipped 99 Already merged
ITEMS
run board demo waiting
same "exit" 0 "$STATUS"
same "filter" '-Status:"Shipped"' "$(cat "$FILTERS")"

case_ "a status field whose name isn't one word is read whole, since a filter GitHub can't match finds nothing"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "project": { "owner": "mentaldesk", "number": 1, "statusField": "Work state" } }
JSON
gh_items <<'ITEMS'
Done 99 Already merged
ITEMS
run board demo waiting
same "exit" 0 "$STATUS"
same "filter" '' "$(cat "$FILTERS")"

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
same "api calls" 1 "$(grep -c '' <"$CALLS")"

case_ "a card nobody has said anything on has an empty conversation"
gh_talk <<'TALK'
6 body 2026-10-01T08:00:00Z reviewer An Idea of my own
TALK
run board demo conversation 6
same "exit" 0 "$STATUS"
same "conversation" '[]' "$(jq -c . "$OUT")"

case_ "a conversation that can't be read is refused in one line, naming the issue"
DOWN=$(mktemp -d "$WORK/down.XXXXXX")
printf '#!/usr/bin/env bash\necho "gh: HTTP 502" >&2\nexit 1\n' >"$DOWN/gh"
chmod +x "$DOWN/gh"
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
  grep -q "$role may not approve a pitch; approving is the stakeholders' own gate" "$ERR" ||
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

# Accepting: the app's merge, the gate a task leaves by.
case_ "accept squash-merges the task's PR and deletes its branch"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 } }
JSON
gh_items <<'ITEMS'
In_review 7 A task in front of me
In_progress 8 A task still being built
ITEMS
gh_pr 907 false
echo '{"head": {"sha": "deadbeefcafe", "ref": "feat/task", "repo": {"full_name": "mentaldesk/demo"}}}' >"$PULL"
run board demo accept you 7
same "exit" 0 "$STATUS"
same "said" "#7: merged PR #907" "$(cat "$OUT")"
same "writes" "PUT api -X PUT repos/mentaldesk/demo/pulls/907/merge -f merge_method=squash
DELETE api -X DELETE repos/mentaldesk/demo/git/refs/heads/feat/task" "$(cat "$WRITES")"

case_ "a branch on a fork is left where it is"
: >"$WRITES"
echo '{"head": {"sha": "deadbeefcafe", "ref": "feat/task", "repo": {"full_name": "someone/demo"}}}' >"$PULL"
run board demo accept you 7
same "exit" 0 "$STATUS"
same "writes" "PUT api -X PUT repos/mentaldesk/demo/pulls/907/merge -f merge_method=squash" "$(cat "$WRITES")"

case_ "a merge GitHub refuses says why in one line, and deletes nothing"
: >"$WRITES"
touch "$BIN/merge-fails"
run board demo accept you 7
failed "refused merge"
one_line "refused merge"
grep -q "can't merge PR #907 (gh: Pull Request is not mergeable (HTTP 405))" "$ERR" || fail "refused merge: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"
rm "$BIN/merge-fails"

case_ "--dry-run says what it would merge and merges nothing"
run board --dry-run demo accept you 7
same "exit" 0 "$STATUS"
same "writes" "" "$(cat "$WRITES")"
grep -q "would squash-merge PR #907" "$ERR" || fail "accept dry run: '$(cat "$ERR")'"

case_ "neither agent may accept a task: that gate is the reviewer's own"
for role in lead dev; do
  run board demo accept "$role" 7
  failed "$role accepting"
  one_line "$role accepting"
  grep -q "$role may not accept a task; accepting is the stakeholders' own gate" "$ERR" ||
    fail "$role accepting: '$(cat "$ERR")'"
done
same "writes" "" "$(cat "$WRITES")"

case_ "accept is only for a task In review with an open PR"
run board demo accept you 8
failed "accept In progress"
grep -q "is in 'In progress'" "$ERR" || fail "accept In progress: '$(cat "$ERR")'"
run board demo accept you 404
grep -q "#404 is not on the board" "$ERR" || fail "accept off the board: '$(cat "$ERR")'"
gh_pr
run board demo accept you 7
failed "accept with no PR"
grep -q "#7 has no open PR to merge" "$ERR" || fail "accept with no PR: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"

case_ "waiting counts a pitch In review's tasks, and how many are still open"
gh_items <<'ITEMS'
In_review 7 A validated pitch
In_review 8 A task in front of me
ITEMS
edit_item 7 '.labels.nodes = [{name: "pitch"}]'
gh_talk <<TALK
7 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
8 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
TALK
jq '.data.repository |= with_entries(.value.subIssuesSummary = {total: 3, completed: 1})' "$TALK" >"$TALK.new" &&
  mv "$TALK.new" "$TALK"
run board demo waiting
same "exit" 0 "$STATUS"
same "pitch tasks" '[3,2]' "$(jq -c '.[] | select(.number == 7) | [.tasks, .openTasks]' "$OUT")"
same "task tasks" '[null,null]' "$(jq -c '.[] | select(.number == 8) | [.tasks, .openTasks]' "$OUT")"

case_ "the agents' settings deny accept, beside approve"
grep -qF '"Bash(a-team board * accept *)"' "$ROOT/settings/agents.json" || fail "no accept deny rule"

case_ "the agents' settings deny removing a label by hand, so blocked is cleared only through unblock"
grep -qF '"Bash(gh issue edit *--remove-label*)"' "$ROOT/settings/agents.json" || fail "no remove-label deny rule"

case_ "accept closes a validated pitch as done once every one of its tasks is closed"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 } }
JSON
gh_items <<'ITEMS'
In_review 7 A validated pitch
Building 8 A pitch still being built
ITEMS
edit_item 7 '.labels.nodes = [{name: "pitch"}]'
edit_item 8 '.labels.nodes = [{name: "pitch"}]'
echo '[{"number": 11, "state": "closed"}, {"number": 12, "state": "closed"}]' >"$SUBS"
run board demo accept you 7
same "exit" 0 "$STATUS"
same "said" "#7: closed as done" "$(cat "$OUT")"
same "writes" "CLOSE issue close 7 -R mentaldesk/demo --reason completed" "$(cat "$WRITES")"

case_ "--dry-run says it would close the pitch and closes nothing"
: >"$WRITES"
run board --dry-run demo accept you 7
same "exit" 0 "$STATUS"
same "writes" "" "$(cat "$WRITES")"
grep -q "would close #7 as completed" "$ERR" || fail "accept pitch dry run: '$(cat "$ERR")'"

case_ "a close GitHub refuses says why in one line"
touch "$BIN/close-fails"
run board demo accept you 7
failed "refused close"
one_line "refused close"
grep -q "can't close #7 (gh: Resource not accessible by integration (HTTP 403))" "$ERR" ||
  fail "refused close: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"
rm "$BIN/close-fails"

case_ "a pitch with a task still open is refused, saying how many, and stays open"
echo '[{"number": 11, "state": "closed"}, {"number": 12, "state": "open"}, {"number": 13, "state": "open"}]' >"$SUBS"
run board demo accept you 7
failed "open tasks"
one_line "open tasks"
grep -q "#7 has 2 open tasks" "$ERR" || fail "open tasks: '$(cat "$ERR")'"
echo '[{"number": 11, "state": "closed"}, {"number": 12, "state": "open"}]' >"$SUBS"
run board demo accept you 7
grep -q "#7 has 1 open task$" "$ERR" || fail "one open task: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"

case_ "a pitch is accepted only In review"
echo '[]' >"$SUBS"
run board demo accept you 8
failed "accept a pitch Building"
grep -q "only a pitch In review can be accepted (#8 is in 'Building')" "$ERR" ||
  fail "accept a pitch Building: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"

case_ "the agents' settings deny approve, beside priority"
grep -qF '"Bash(a-team board * approve *)"' "$ROOT/settings/agents.json" || fail "no approve deny rule"

case_ "comment you posts your words as they are: no marker, and no 👀 on anything"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 } }
JSON
gh_items <<'ITEMS'
Pitched 7 A pitch in front of me
ITEMS
gh_thread <<TALK
body ${TODAY}T02:10:00Z demo-app[bot] 0 The pitch\n<!-- a-team:lead -->
comment ${TODAY}T02:20:00Z reviewer 0 Needs a second option.
TALK
echo "Not yet: the second option is still missing." >"$WORK/mine"
run board demo comment you 7 "$WORK/mine"
same "exit" 0 "$STATUS"
same "posted" "Not yet: the second option is still missing." "$(cat "$POSTED")"
same "acked" "" "$(cat "$ACKED")"

case_ "feedback reports it as yours and unanswered, so it starts a run"
gh_thread <<TALK
body ${TODAY}T02:10:00Z demo-app[bot] 0 The pitch\n<!-- a-team:lead -->
comment ${TODAY}T02:30:00Z reviewer 0 $(cat "$POSTED")
TALK
run board demo feedback lead 7
same "exit" 0 "$STATUS"
same "unanswered" '["Not yet: the second option is still missing."]' "$(jq -c '[.[].body]' "$OUT")"

case_ "comment still refuses a role it doesn't know"
: >"$POSTED"
run board demo comment reviewer 7 "$WORK/mine"
failed "unknown role"
one_line "unknown role"
grep -q "unknown role 'reviewer' (lead | dev | you)" "$ERR" || fail "unknown role: '$(cat "$ERR")'"
same "posted" "" "$(cat "$POSTED")"

case_ "--dry-run shows the comment you'd post and posts nothing"
run board --dry-run demo comment you 7 "$WORK/mine"
same "exit" 0 "$STATUS"
same "posted" "" "$(cat "$POSTED")"
grep -qF "  | Not yet: the second option is still missing." "$ERR" || fail "comment you dry run: '$(cat "$ERR")'"
grep -q "would comment on #7" "$ERR" || fail "comment you dry run: '$(cat "$ERR")'"

case_ "the agents' settings deny comment you, beside accept"
grep -qF '"Bash(a-team board * comment you *)"' "$ROOT/settings/agents.json" || fail "no comment you deny rule"

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
same "reasons" "[\"stakeholder feedback on #7 (${TODAY}T02:28:46Z)\"]" "$(jq -c .reasons "$OUT")"

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
same "reasons" "[\"stakeholder feedback on #7 (${TODAY}T02:28:46Z)\"]" "$(jq -c .reasons "$OUT")"

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
gh_talk <<TALK
106 body ${YESTERDAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
106 comment ${YESTERDAY}T09:00:00Z demo-app[bot] Wrong thread.\n<!-- a-team:dev -->
106 comment ${YESTERDAY}T09:30:00Z demo-app[bot] A comment that lost its marker.
115 body ${YESTERDAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 comment ${YESTERDAY}T09:00:00Z reviewer > Drafted.\n<!-- a-team:dev -->
TALK
run board demo waiting
same "exit" 0 "$STATUS"
same "pitch turn" '"you"' "$(jq -c '.[0].turn' "$OUT")"
same "pitch reason" "\"awaiting your approval since $(jq -rn 'now - 86400 | strftime("%d %b")') 08:00\"" "$(jq -c '.[0].reason' "$OUT")"
same "task turn" '"dev"' "$(jq -c '.[1].turn' "$OUT")"
gh_items <<'ITEMS'
Pitched 7 A pitch in front of me
ITEMS

# Stakeholders: whoever the config lists, not one login, is who the team answers.
KEPT_CONFIG=$CONFIG KEPT_TEAM=$TEAM
case_ "a second stakeholder's comment is feedback, and a stranger's isn't"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "stakeholders": ["reviewer", "second"], "app": { "id": 7, "slug": "demo-app" },
  "project": { "owner": "mentaldesk", "number": 1 } }
JSON
gh_thread <<'TALK'
body 2026-10-01T02:10:00Z demo-app[bot] 0 The pitch\n<!-- a-team:lead -->
comment 2026-10-01T02:20:00Z second 0 Needs a second option.
comment 2026-10-01T02:28:46Z stranger 0 Ship it now.
TALK
run board demo feedback lead 7
same "exit" 0 "$STATUS"
same "unanswered" '["Needs a second option."]' "$(jq -c '[.[].body]' "$OUT")"

case_ "comment acks the second stakeholder's, and leaves the stranger's alone"
: >"$ACKED"
export A_TEAM_RUN_STARTED=2026-10-01T03:00:00Z
run board demo comment lead 7 "$WORK/reply"
same "exit" 0 "$STATUS"
same "acked" "IC_1" "$(cat "$ACKED")"
unset A_TEAM_RUN_STARTED

case_ "triggers starts a run for the second stakeholder, and not the stranger"
gh_recent <<RECENT
7 ${TODAY}T02:20:00Z second 0 Needs a second option.
7 ${TODAY}T02:28:46Z stranger 0 Ship it now.
RECENT
run board demo triggers lead
same "exit" 0 "$STATUS"
same "reasons" "[\"stakeholder feedback on #7 (${TODAY}T02:20:00Z)\"]" "$(jq -c .reasons "$OUT")"

case_ "waiting hands the gate to the Lead for the second stakeholder, and not for the stranger"
gh_items <<'ITEMS'
Pitched 106 Both gates are mine
Pitched 107 Someone else's opinion
ITEMS
gh_talk <<'TALK'
106 body 2026-10-01T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
106 comment 2026-10-01T09:30:00Z second What about the second gate?
107 body 2026-10-01T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
107 comment 2026-10-01T09:30:00Z stranger What about the second gate?
TALK
run board demo waiting
same "exit" 0 "$STATUS"
same "turns" '["lead","you"]' "$(jq -c '[.[].turn]' "$OUT")"

case_ "a config that still says reviewer answers that one person, as before"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" },
  "project": { "owner": "mentaldesk", "number": 1 } }
JSON
gh_thread <<'TALK'
body 2026-10-01T02:10:00Z demo-app[bot] 0 The pitch\n<!-- a-team:lead -->
comment 2026-10-01T02:20:00Z reviewer 0 Needs a second option.
comment 2026-10-01T02:28:46Z second 0 Ship it now.
TALK
run board demo feedback lead 7
same "exit" 0 "$STATUS"
same "unanswered" '["Needs a second option."]' "$(jq -c '[.[].body]' "$OUT")"
CONFIG=$KEPT_CONFIG TEAM=$KEPT_TEAM
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
same "label" 1 "$(grep -c 'issues/11/labels -f labels\[\]=a-team:displaced' "$WRITES")"
grep -q "option=OPT_exploring" "$WRITES" || fail "status not set: '$(cat "$WRITES")'"

case_ "a repeat demote moves without labelling again"
edit_item 11 '.labels.nodes += [{name: "a-team:displaced"}]'
: >"$WRITES"
run board demo move lead 11 Exploring
same "exit" 0 "$STATUS"
same "label" 0 "$(grep -c '/labels' "$WRITES")"
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

case_ "setup drops the Todo and In Progress GitHub made, unless an item has one"
cp "$META" "$BIN/meta.saved"
jq '.data.organization.projectV2.field.options += [{id: "OPT_todo", name: "Todo"}, {id: "OPT_ip", name: "In Progress"}]' \
  "$BIN/meta.saved" >"$META"
jq '.data.organization.projectV2.items.nodes[0].fieldValueByName.name = "In Progress"' "$ITEMS" >"$ITEMS.new" && mv "$ITEMS.new" "$ITEMS"
run board --dry-run demo setup
same "exit" 0 "$STATUS"
grep -q '^  drop  Todo$' "$OUT" || fail "setup: Todo not dropped in '$(cat "$OUT")'"
grep -q '^  keep  In Progress$' "$OUT" || fail "setup: In Progress in use but not kept in '$(cat "$OUT")'"
grep -q '^  keep  Exploring$' "$OUT" || fail "setup: '$(cat "$OUT")'"
mv "$BIN/meta.saved" "$META"

case_ "setup applies exactly what its dry run listed"
run board --dry-run demo setup
listed=$(cat "$OUT")
: >"$WRITES"
run board demo setup
same "exit" 0 "$STATUS"
[ -n "$listed" ] || fail "dry run: nothing listed"
same "options" "$(sed -En 's/^  (keep|add ) {2}//p' <<<"$listed")" \
  "$(jq -r '.variables.input.singleSelectOptions[].name' "$BIN/mutation.json")"
same "labels" "$(sed -n 's/^(dry run) created label //p' <<<"$listed")" \
  "$(sed -n 's/^label create \([^ ]*\) .*/\1/p' "$WRITES")"

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

# `gh_child <n> <parent> <labels> [body]`: the issue `link` and `unlink` read, a sub-issue of
# <parent> ("-" for none) carrying the comma-separated <labels> ("-" for none).
gh_child() {
  jq -n --argjson n "$1" --arg parent "$2" --arg labels "$3" --arg body "${4:-Spotted while reviewing.}" '
    {id: (9000 + $n), number: $n, title: "The follow-up", body: $body,
     labels: (if $labels == "-" then [] else $labels | split(",") | map({name: .}) end),
     parent_issue_url: (if $parent == "-" then null
                        else "https://api.github.com/repos/mentaldesk/demo/issues/\($parent)" end)}' >"$ISSUE"
  : >"$POSTED"
  : >"$WRITES"
  : >"$EDITED"
}

case_ "link refuses to file an idea under a pitch, in one line, and writes nothing"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 } }
JSON
gh_items <<'ITEMS'
Building 10 A pitch
Idea 30 An idea
Exploring 31 A draft
Pitched 32 A pitch in front of the stakeholder
Approved 33 An approved pitch
Building 34 A pitch being built
Ready 40 A task
ITEMS
for n in 30 31 32 33 34; do
  gh_child "$n" - -
  run board demo link 10 "$n"
  failed "link #$n"
  one_line "link #$n"
  grep -qF "#$n is an idea, not a task: say \"Follow-up from #10\" in its body instead of linking it" "$ERR" ||
    fail "link #$n: '$(cat "$ERR")'"
  same "link #$n writes" "" "$(cat "$WRITES")"
done
gh_child 50 - pitch
run board demo link 10 50
failed "link labelled pitch"
grep -qF "#50 is an idea, not a task" "$ERR" || fail "link labelled pitch: '$(cat "$ERR")'"
same "link labelled pitch writes" "" "$(cat "$WRITES")"

case_ "link still files a task that's Ready, or not on the board yet"
for n in 40 41; do
  gh_child "$n" - -
  run board demo link 10 "$n"
  same "link #$n exit" 0 "$STATUS"
  same "link #$n said" "#$n is now a sub-issue of #10" "$(cat "$OUT")"
  grep -q "^POST .*/issues/10/sub_issues .*sub_issue_id=90$n" "$WRITES" || fail "link #$n: '$(cat "$WRITES")'"
done

case_ "unlink takes an idea off a pitch in any status before Done, says so on the pitch, and names the pitch on the idea"
for parent in Pitched Approved Building In_review; do
  gh_items <<ITEMS
$parent 10 A pitch
Building 30 The follow-up
ITEMS
  edit_item 10 '.labels.nodes = [{name: "pitch"}]'
  gh_child 30 10 pitch
  run board demo unlink lead 10 30
  same "$parent exit" 0 "$STATUS"
  same "$parent said" "#30 is no longer a sub-issue of #10; said so on #10, and #30 names #10 as where it came from" "$(cat "$OUT")"
  grep -q "^DELETE .*/issues/10/sub_issue .*sub_issue_id=9030" "$WRITES" || fail "$parent: no unlink in '$(cat "$WRITES")'"
  grep -q "^#30 (The follow-up) grew out of this pitch and is its own item now, so it no longer holds this one up" "$POSTED" ||
    fail "$parent: comment '$(cat "$POSTED")'"
  grep -q '<!-- a-team:lead -->' "$POSTED" || fail "$parent: no lead marker in '$(cat "$POSTED")'"
  same "$parent body" "Follow-up from #10

Spotted while reviewing." "$(cat "$EDITED")"
done

case_ "an idea not labelled pitch yet is taken off too, by its board status"
gh_items <<'ITEMS'
Building 10 A pitch
Idea 30 The follow-up
ITEMS
gh_child 30 10 -
run board demo unlink lead 10 30
same "exit" 0 "$STATUS"
grep -q "^DELETE " "$WRITES" || fail "idea: no unlink in '$(cat "$WRITES")'"

case_ "unlink leaves the idea's body alone when it already names the pitch"
gh_child 30 10 - "Follow-up from #10, where the stakeholder asked for it."
run board demo unlink lead 10 30
same "exit" 0 "$STATUS"
same "edited" "" "$(cat "$EDITED")"
grep -q "^EDIT " "$WRITES" && fail "named: body edited anyway"
gh_child 30 10 - "Follow-up from #100."
run board demo unlink lead 10 30
grep -q "^Follow-up from #10$" "$EDITED" || fail "#100 isn't #10: '$(cat "$EDITED")'"

case_ "unlink refuses an idea under a Done pitch, or one that isn't under it, and writes nothing"
gh_items <<'ITEMS'
Done 10 A pitch
Building 20 Another pitch
Building 30 The follow-up
ITEMS
edit_item 10 '.labels.nodes = [{name: "pitch"}]'
gh_child 30 10 pitch
run board demo unlink lead 10 30
failed "done"
one_line "done"
grep -q "may not change a Done pitch (#10)" "$ERR" || fail "done: '$(cat "$ERR")'"
same "done writes" "" "$(cat "$WRITES")"
run board demo unlink lead 20 30
failed "not a sub-issue"
one_line "not a sub-issue"
grep -q "#30 is not a sub-issue of #20" "$ERR" || fail "not a sub-issue: '$(cat "$ERR")'"
same "not a sub-issue writes" "" "$(cat "$WRITES")"
same "posted" "" "$(cat "$POSTED")"

case_ "a task still unlinks only when Ready, from a Building pitch, with nothing said"
gh_items <<'ITEMS'
Building 10 A pitch
Ready 40 A task
In_progress 41 A started task
In_review 42 A task in review
Done 43 A done task
ITEMS
gh_child 40 10 -
run board demo unlink lead 10 40
same "ready exit" 0 "$STATUS"
same "ready said" "#40 is no longer a sub-issue of #10" "$(cat "$OUT")"
grep -q "^DELETE " "$WRITES" || fail "ready: no unlink in '$(cat "$WRITES")'"
same "ready posted" "" "$(cat "$POSTED")"
same "ready edited" "" "$(cat "$EDITED")"
for n in 41 42 43; do
  gh_child "$n" 10 -
  run board demo unlink lead 10 "$n"
  failed "task #$n"
  grep -q "may only take a Ready task off a pitch" "$ERR" || fail "task #$n: '$(cat "$ERR")'"
  same "task #$n writes" "" "$(cat "$WRITES")"
done
gh_child 40 10 -
run board demo unlink dev 10 40
failed "dev"
grep -q "dev may not take a task off a pitch" "$ERR" || fail "dev: '$(cat "$ERR")'"

case_ "children shows each child's board status, null when it isn't on the board"
gh_items <<'ITEMS'
Building 10 A pitch
Ready 40 A task
Building 30 The follow-up
ITEMS
jq -n '[{number: 40, title: "A task", state: "open", labels: []},
        {number: 30, title: "The follow-up", state: "open", labels: [{name: "pitch"}]},
        {number: 44, title: "Not on the board", state: "closed", labels: []}]' >"$SUBS"
run board demo children 10
same "exit" 0 "$STATUS"
same "children" '[{"number":40,"status":"Ready"},{"number":30,"status":"Building"},{"number":44,"status":null}]' \
  "$(jq -c 'map({number, status})' "$OUT")"
same "labels" '["pitch"]' "$(jq -c '.[1].labels' "$OUT")"

case_ "--dry-run says every write unlink would make, and makes none"
gh_items <<'ITEMS'
Building 10 A pitch
Building 30 The follow-up
ITEMS
gh_child 30 10 pitch
run board --dry-run demo unlink lead 10 30
same "exit" 0 "$STATUS"
grep -q "would take #30 off #10" "$ERR" || fail "dry run: no unlink in '$(cat "$ERR")'"
grep -q "would comment on #10" "$ERR" || fail "dry run: no comment in '$(cat "$ERR")'"
grep -q "would add 'Follow-up from #10' to #30" "$ERR" || fail "dry run: no body edit in '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"
same "posted" "" "$(cat "$POSTED")"
same "edited" "" "$(cat "$EDITED")"

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

case_ "a PR GitHub hasn't linked to its task is still found, by what its body closes"
gh_pr 912 true 12
run board demo triggers dev
same "exit" 0 "$STATUS"
same "reasons" '["PR #912 is green but still a draft"]' "$(jq -c .reasons "$OUT")"
run board demo pr 12
same "exit" 0 "$STATUS"
same "pr" 912 "$(jq -c .number "$OUT")"
same "fields" '["headRefName","isDraft","mergeable","number","url"]' "$(jq -c keys "$OUT")"

case_ "a task no PR closes, linked or not, has none"
gh_pr 912 true 13
run board demo triggers dev
same "exit" 0 "$STATUS"
same "reasons" '["#12 is In progress but has no PR: an earlier run didn'"'"'t finish"]' "$(jq -c .reasons "$OUT")"
run board demo pr 12
same "exit" 0 "$STATUS"
same "pr" null "$(cat "$OUT")"

case_ "triggers groups the Dev's reasons by task, and names the Ready task a run could claim"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 },
  "wip": { "worktrees": 3 } }
JSON
gh_items <<'ITEMS'
In_progress 12 A task with its draft PR up
Ready 13 Something to start
ITEMS
gh_pr 912 true
gh_runs <<'RUNS'
completed failure 2025-09-19T09:00:00Z build
RUNS
run board demo triggers dev
same "exit" 0 "$STATUS"
same "tasks" '[{"number":12,"title":"A task with its draft PR up","reasons":["CI failed on PR #912 at deadbee"]}]' \
  "$(jq -c .tasks "$OUT")"
same "ready" 13 "$(jq -c .ready "$OUT")"
same "chores" '[]' "$(jq -c .chores "$OUT")"
run board demo triggers lead
same "lead has no tasks" null "$(jq -c .tasks "$OUT")"

# A project whose Status field has the options a claim moves through.
claimable() {
  jq '.data.organization.projectV2.field.options += [{id: "OPT_ready", name: "Ready"}, {id: "OPT_progress", name: "In progress"}]' \
    "$META" >"$META.new" && mv "$META.new" "$META"
}

case_ "claim moves the next Ready task to In progress and labels it before a run starts"
gh_items 14 <<'ITEMS'
Ready 13 Something to start
Ready 14 Something more pressing
ITEMS
claimable
run board demo claim dev
same "exit" 0 "$STATUS"
same "claimed" '{"number":14,"title":"Something more pressing"}' "$(cat "$OUT")"
grep -q 'issue edit 14 -R mentaldesk/demo --add-label a-team:dev' "$WRITES" || fail "claim: no label in '$(cat "$WRITES")'"
grep -q 'item=PVTI_14 .*option=OPT_progress' "$WRITES" || fail "claim: no move in '$(cat "$WRITES")'"

case_ "claim refuses a task that's already claimed, and claims nothing"
gh_items <<'ITEMS'
In_progress 12 Taken by another run
Ready 13 Something to start
ITEMS
claimable
run board demo claim dev 12
failed "already claimed"
one_line "already claimed"
same "writes" "" "$(cat "$WRITES")"

case_ "claim refuses when every worktree is taken, and says null when nothing is Ready"
gh_items <<'ITEMS'
In_progress 11 One
In_progress 12 Two
In_progress 15 Three
Ready 13 Something to start
ITEMS
claimable
run board demo claim dev
failed "no worktree"
grep -q "no free worktree for #13" "$ERR" || fail "no worktree: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"
gh_items <<'ITEMS'
In_progress 12 Two
ITEMS
run board demo claim dev
same "exit" 0 "$STATUS"
same "nothing" null "$(cat "$OUT")"

case_ "claim --dry-run prints the claim instead of making it"
gh_items <<'ITEMS'
Ready 13 Something to start
ITEMS
claimable
run board --dry-run demo claim dev
same "exit" 0 "$STATUS"
same "would claim" '{"number":13,"title":"Something to start"}' "$(cat "$OUT")"
grep -q "would label #13 a-team:dev" "$ERR" || fail "dry run: no label in '$(cat "$ERR")'"
grep -q "would set item PVTI_13 to 'In progress'" "$ERR" || fail "dry run: no move in '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"

case_ "a run bound to a task can hand it back, and can't touch another"
gh_items <<'ITEMS'
In_progress 12 This run's task
In_progress 16 Another run's task
ITEMS
claimable
gh_pr
echo 'Which marker?' >"$WORK/question"
A_TEAM_RUN_TASK=12 run board demo move dev 12 Ready
same "hand back" 0 "$STATUS"
A_TEAM_RUN_TASK=12 run board demo depends dev 12 16 "both rewrite the same view"
same "defer" 0 "$STATUS"
A_TEAM_RUN_TASK=12 run board demo comment dev 12 "$WORK/question"
same "ask" 0 "$STATUS"
: >"$WRITES"
: >"$POSTED"
A_TEAM_RUN_TASK=12 run board demo move dev 16 Ready
failed "other task"
grep -q "this run is for #12: leave #16 to a run of its own" "$ERR" || fail "other task: '$(cat "$ERR")'"
A_TEAM_RUN_TASK=12 run board demo comment dev 16 "$WORK/question"
failed "other comment"
A_TEAM_RUN_TASK=12 run board demo depends dev 16 12 "the other way round"
failed "other depends"
same "writes" "" "$(cat "$WRITES")"
same "posted" "" "$(cat "$POSTED")"
gh_pr 912 true
A_TEAM_RUN_TASK=12 run board demo comment dev 912 "$WORK/question"
same "its own PR" 0 "$STATUS"

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

case_ "a team moved aside to <name>.json.removed is neither listed nor dispatched"
jq '.dispatch.hold = []' "$TEAM" >"$CONFIG/teams/gone.json.removed"
: >"$A_TEAM_STATE/dispatch.log"
A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/dispatch.sh" --dry-run
grep -q 'gone' "$A_TEAM_STATE/dispatch.log" && fail "removed: gone was dispatched"
run teams
grep -q 'gone' "$OUT" && fail "removed: teams lists '$(cat "$OUT")'"
grep -q '^demo ' "$OUT" || fail "removed: teams dropped demo: '$(cat "$OUT")'"
rm "$CONFIG/teams/gone.json.removed"

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

# A dispatcher whose a-team answers `triggers dev` with $DEV_TRIGGERS, records each claim, and
# claims $CLAIMED. `task-prompt` is one line, and the Lead never has anything to do.
dev_dispatcher() {
  DISPATCH=$(mktemp -d "$WORK/dispatch.XXXXXX")
  mkdir -p "$DISPATCH/bin" "$DISPATCH/scripts"
  cp "$ROOT"/scripts/*.sh "$DISPATCH/scripts/"
  cp -R "$ROOT/settings" "$DISPATCH/"
  CLAIMS="$DISPATCH/claims" DEV_TRIGGERS="$DISPATCH/triggers.json" CLAIMED="$DISPATCH/claimed.json"
  : >"$CLAIMS"
  echo null >"$CLAIMED"
  cat >"$DISPATCH/bin/a-team" <<SH
#!/usr/bin/env bash
case " \$* " in
  *" claim dev "*) echo "\$*" >>"$CLAIMS"; cat "$CLAIMED" ;;
  *" triggers dev"*) cat "$DEV_TRIGGERS" ;;
  *" triggers lead"*) echo '{"reasons": [], "creative": false}' ;;
  *" task-prompt "*) echo "Run one shift." ;;
esac
SH
  chmod +x "$DISPATCH/bin/a-team"
  A_TEAM_STATE=$(mktemp -d "$WORK/state.XXXXXX")
}
dispatch_dev() { A_TEAM_CONFIG="$CONFIG" bash "$DISPATCH/scripts/dispatch.sh" "$@"; }

case_ "a run for a task already in hand claims nothing new, and names its task"
A_TEAM_STATE_WAS=$A_TEAM_STATE
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "app": { "id": 7, "slug": "demo-app" }, "dispatch": { "enabled": true } }
JSON
dev_dispatcher
jq -n '{reasons: ["CI failed on PR #912 at deadbee", "Ready task available (e.g. #13) and a free worktree"], creative: false,
        tasks: [{number: 12, title: "Fix the pane", reasons: ["CI failed on PR #912 at deadbee"]}],
        ready: 13, chores: ["PR #900 has merged: clean up its worktree"]}' >"$DEV_TRIGGERS"
dispatch_dev --dry-run
grep -q 'demo dev: would start: #12: CI failed on PR #912 at deadbee;PR #900 has merged: clean up its worktree' \
  "$A_TEAM_STATE/dispatch.log" || fail "in hand: '$(cat "$A_TEAM_STATE/dispatch.log")'"
same "claims" "" "$(cat "$CLAIMS")"
same "task" '{"number":12,"title":"Fix the pane"}' "$(cat "$A_TEAM_STATE/demo/dev/dry-task")"

case_ "the same reasons again wait, and the next task with something new gets the run"
jq '.tasks += [{number: 14, title: "Answer the review", reasons: ["stakeholder feedback on #914 (2025-09-19T09:00:00Z)"]}]' \
  "$DEV_TRIGGERS" >"$DEV_TRIGGERS.new" && mv "$DEV_TRIGGERS.new" "$DEV_TRIGGERS"
: >"$A_TEAM_STATE/dispatch.log"
dispatch_dev --dry-run
grep -q 'demo dev: would start: #14: stakeholder feedback on #914' "$A_TEAM_STATE/dispatch.log" ||
  fail "next task: '$(cat "$A_TEAM_STATE/dispatch.log")'"
: >"$A_TEAM_STATE/dispatch.log"
dispatch_dev --dry-run
grep -q 'claim dev' "$CLAIMS" || fail "with both waiting, the Ready task wasn't claimed"

case_ "with nothing in hand, a Ready task is claimed before the run starts"
dev_dispatcher
jq -n '{reasons: ["Ready task available (e.g. #13) and a free worktree"], creative: false, tasks: [], ready: 13, chores: []}' \
  >"$DEV_TRIGGERS"
echo '{"number": 13, "title": "Something to start"}' >"$CLAIMED"
dispatch_dev --dry-run
same "claims" "board --dry-run demo claim dev" "$(cat "$CLAIMS")"
grep -q 'demo dev: would start: #13: Ready task #13 to build' "$A_TEAM_STATE/dispatch.log" ||
  fail "claimed: '$(cat "$A_TEAM_STATE/dispatch.log")'"
same "why" "Ready task #13 to build" "$(cat "$A_TEAM_STATE/demo/dev/dry-last-reasons")"

case_ "a claim that's refused starts nothing, and says why"
sed -i.bak 's|^  \*" claim dev "\*).*|  *" claim dev "*) echo "board.sh: no free worktree for #13" >\&2; exit 1 ;;|' "$DISPATCH/bin/a-team"
: >"$A_TEAM_STATE/dispatch.log"
dispatch_dev --dry-run
grep -q 'demo dev: claim failed: board.sh: no free worktree for #13' "$A_TEAM_STATE/dispatch.log" ||
  fail "refused: '$(cat "$A_TEAM_STATE/dispatch.log")'"
grep -q 'would start' "$A_TEAM_STATE/dispatch.log" && fail "refused: a run started"

case_ "merged worktrees alone start no Dev run"
dev_dispatcher
jq -n '{reasons: ["PR #900 has merged: clean up its worktree"], creative: false, tasks: [], ready: null,
        chores: ["PR #900 has merged: clean up its worktree"]}' >"$DEV_TRIGGERS"
dispatch_dev --dry-run
grep -q 'demo dev' "$A_TEAM_STATE/dispatch.log" 2>/dev/null && fail "chores: '$(cat "$A_TEAM_STATE/dispatch.log")'"

case_ "the run's task reaches its prompt, its environment and the state the dashboard reads"
WORKDIR=$(mktemp -d "$WORK/workdir.XXXXXX")
jq --arg w "$WORKDIR" '.workdir = $w' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
jq -n '{reasons: [], creative: false, tasks: [], ready: 13, chores: []}' >"$DEV_TRIGGERS"
echo '{"number": 13, "title": "Something to start"}' >"$CLAIMED"
LAUNCHED="$DISPATCH/launched"
cat >"$DISPATCH/bin/claude" <<SH
#!/usr/bin/env bash
{ echo "task: \$A_TEAM_RUN_TASK"; printf '%s\n' "\$2"; } >"$LAUNCHED"
SH
chmod +x "$DISPATCH/bin/claude"
dispatch_dev
for _ in $(seq 50); do [ -s "$LAUNCHED" ] && break; sleep 0.1; done
same "claims" "board demo claim dev" "$(cat "$CLAIMS")"
same "env" "task: 13" "$(sed -n 1p "$LAUNCHED")"
grep -q '^This run is for #13 Something to start, and only that task.$' "$LAUNCHED" ||
  fail "prompt: '$(cat "$LAUNCHED")'"
same "state" '{"number":13,"title":"Something to start"}' "$(cat "$A_TEAM_STATE/demo/dev/task")"

# Claims hand out the tasks queued in $QUEUE one at a time, then refuse for want of a worktree;
# claude stays up until killed, recording the task each run is for in $LAUNCHED.
queued_dispatcher() {
  dev_dispatcher
  QUEUE="$DISPATCH/queue" LAUNCHED="$DISPATCH/launched"
  : >"$QUEUE"
  : >"$LAUNCHED"
  cat >"$DISPATCH/bin/a-team" <<SH
#!/usr/bin/env bash
case " \$* " in
  *" claim dev "*)
    echo "\$*" >>"$CLAIMS"
    n=\$(head -n 1 "$QUEUE")
    [ -n "\$n" ] || { echo "board.sh: no free worktree for #99 (3 in use, wip.worktrees is 3)" >&2; exit 1; }
    tail -n +2 "$QUEUE" >"$QUEUE.rest" && mv "$QUEUE.rest" "$QUEUE"
    echo "{\"number\": \$n, \"title\": \"Task \$n\"}" ;;
  *" triggers dev"*) cat "$DEV_TRIGGERS" ;;
  *" triggers lead"*) echo '{"reasons": [], "creative": false}' ;;
  *" task-prompt "*) echo "Run one shift." ;;
esac
SH
  cat >"$DISPATCH/bin/claude" <<SH
#!/usr/bin/env bash
echo "\$A_TEAM_RUN_TASK" >>"$LAUNCHED"
exec sleep 60
SH
  chmod +x "$DISPATCH/bin/claude"
}
devs() { jq --argjson n "$1" '.wip.devs = $n' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"; }
alive() { kill -0 "$(cat "$A_TEAM_STATE/demo/dev/runs/$1/pid")" 2>/dev/null; }
launched() { for _ in $(seq 50); do [ "$(grep -c '' "$LAUNCHED")" -ge "$1" ] && break; sleep 0.1; done; }
stop_runs() { for pid in "$A_TEAM_STATE"/demo/dev/runs/*/pid; do kill "$(cat "$pid")" 2>/dev/null; done; }

case_ "with devs at 3 and three Ready tasks, one pass starts three runs, each on its own task with its own state"
queued_dispatcher
devs 3
printf '13\n14\n15\n16\n' >"$QUEUE"
jq -n '{reasons: [], creative: false, tasks: [], ready: 13, chores: ["PR #900 has merged: clean up its worktree"]}' >"$DEV_TRIGGERS"
dispatch_dev
launched 3
same "claims" 3 "$(grep -c '' "$CLAIMS")"
same "launched" "13 14 15" "$(sort "$LAUNCHED" | paste -sd ' ' -)"
for n in 13 14 15; do
  alive "$n" || fail "#$n: its run isn't going"
  same "#$n task" "{\"number\":$n,\"title\":\"Task $n\"}" "$(cat "$A_TEAM_STATE/demo/dev/runs/$n/task")"
  same "#$n log" "$n" "$(readlink "$A_TEAM_STATE/demo/dev/runs/$n/latest.jsonl" | sed 's/.*-\([0-9]*\)\.jsonl$/\1/')"
done
same "pids" 3 "$(cat "$A_TEAM_STATE"/demo/dev/runs/*/pid | sort -u | grep -c '')"
same "chores ride with the first run" "Ready task #13 to build
PR #900 has merged: clean up its worktree" "$(cat "$A_TEAM_STATE/demo/dev/runs/13/last-reasons")"
same "the others' reasons" "Ready task #14 to build" "$(cat "$A_TEAM_STATE/demo/dev/runs/14/last-reasons")"
same "the role follows the latest" '{"number":15,"title":"Task 15"}' "$(cat "$A_TEAM_STATE/demo/dev/task")"

case_ "status lists each live Dev run with its task and how long it has run"
A_TEAM_CONFIG="$CONFIG" bash "$DISPATCH/scripts/status.sh" >"$OUT"
for n in 13 14 15; do
  grep -q "^  #$n Task $n: running 0h00m (pid $(cat "$A_TEAM_STATE/demo/dev/runs/$n/pid"))$" "$OUT" ||
    fail "status: no line for #$n in '$(cat "$OUT")'"
done

case_ "with every Dev slot taken, a pass claims nothing"
dispatch_dev
same "claims" 3 "$(grep -c '' "$CLAIMS")"

case_ "a run for a task in hand starts while the others build, and never a second run for a task already going"
devs 4
jq -n '{reasons: [], creative: false, ready: null, chores: [],
        tasks: [{number: 14, title: "Task 14", reasons: ["stakeholder feedback on #914 (2025-09-19T09:00:00Z)"]},
                {number: 20, title: "Task 20", reasons: ["stakeholder feedback on #920 (2025-09-19T09:00:00Z)"]}]}' >"$DEV_TRIGGERS"
dispatch_dev
launched 4
same "launched" "13 14 15 20" "$(sort -n "$LAUNCHED" | paste -sd ' ' -)"
alive 20 || fail "#20: its run isn't going"

case_ "an over-long run is killed, and the others are left alone"
echo $(($(date +%s) - 3 * 3600)) >"$A_TEAM_STATE/demo/dev/runs/13/last-start"
dispatch_dev
for _ in $(seq 50); do alive 13 || break; sleep 0.1; done
alive 13 && fail "the over-long run is still going"
for n in 14 15 20; do alive "$n" || fail "#$n was stopped with it"; done
grep -q "demo dev: killed run .* after 180 minutes" "$A_TEAM_STATE/dispatch.log" || fail "kill: '$(cat "$A_TEAM_STATE/dispatch.log")'"

case_ "a held Dev starts nothing new, and its runs go on"
jq '.dispatch.hold = ["dev"]' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
printf '30\n' >"$QUEUE"
jq -n '{reasons: [], creative: false, tasks: [], ready: 30, chores: []}' >"$DEV_TRIGGERS"
dispatch_dev
same "claims" 3 "$(grep -c '' "$CLAIMS")"
for n in 14 15 20; do alive "$n" || fail "#$n stopped when the Dev was held"; done
stop_runs

case_ "status with no Dev run going, and with one"
A_TEAM_CONFIG="$CONFIG" bash "$DISPATCH/scripts/status.sh" >"$OUT"
grep -q "^demo dev: idle, last started" "$OUT" || fail "none: '$(cat "$OUT")'"
jq '.dispatch.hold = []' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
devs 1
dispatch_dev
launched 5
A_TEAM_CONFIG="$CONFIG" bash "$DISPATCH/scripts/status.sh" >"$OUT"
grep -q "^demo dev: running$" "$OUT" || fail "one: '$(cat "$OUT")'"
same "one run" 1 "$(grep -c '^  #30 Task 30: running 0h00m' "$OUT")"
stop_runs

case_ "devs stops at the worktrees limit quietly, and a missing devs reads as 1"
queued_dispatcher
devs 5
printf '13\n14\n' >"$QUEUE"
jq -n '{reasons: [], creative: false, tasks: [], ready: 13, chores: []}' >"$DEV_TRIGGERS"
dispatch_dev --dry-run
same "would start" 2 "$(grep -c 'demo dev: would start' "$A_TEAM_STATE/dispatch.log")"
grep -q 'claim failed' "$A_TEAM_STATE/dispatch.log" && fail "limit: '$(cat "$A_TEAM_STATE/dispatch.log")'"
jq 'del(.wip)' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
printf '13\n14\n' >"$QUEUE"
: >"$A_TEAM_STATE/dispatch.log"
dispatch_dev --dry-run
same "without devs" 1 "$(grep -c 'demo dev: would start' "$A_TEAM_STATE/dispatch.log")"

case_ "stop ends every Dev run going"
queued_dispatcher
devs 2
printf '13\n14\n' >"$QUEUE"
jq -n '{reasons: [], creative: false, tasks: [], ready: 13, chores: []}' >"$DEV_TRIGGERS"
dispatch_dev
launched 2
PIDS=$(cat "$A_TEAM_STATE"/demo/dev/runs/*/pid | sort -n | paste -sd ' ' -)
A_TEAM_CONFIG="$CONFIG" bash "$DISPATCH/scripts/pause.sh" stop demo dev >"$OUT" 2>&1
for _ in $(seq 50); do alive 13 || alive 14 || break; sleep 0.1; done
alive 13 && fail "stop: #13 is still going"
alive 14 && fail "stop: #14 is still going"
grep -q "stopped demo dev's run (${PIDS// /, })" "$OUT" || fail "stop: '$(cat "$OUT")'"
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
mkdir -p "$APP_BIN/work/main"
app_fixture() {
  fixture <<JSON
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "project": { "owner": "mentaldesk", "number": 1 },
  "vision": "docs/vision.md", "workdir": "$APP_BIN/work", "app": { "id": 7, "slug": "demo-app" } }
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

# A second copy of a-team, like an installed one alongside a worktree.
COPY=$(mktemp -d "$WORK/copy.XXXXXX")
cp -R "$ROOT/bin" "$ROOT/scripts" "$COPY/"
# Runs "$@" into $OUT and $ERR, killing it after 10 seconds so a loop fails instead of hanging.
within() {
  "$@" >"$OUT" 2>"$ERR" &
  local pid=$! watchdog
  (sleep 10; kill "$pid") >/dev/null 2>&1 &
  watchdog=$!
  wait "$pid"
  STATUS=$?
  kill "$watchdog" 2>/dev/null
}

for order in "$ROOT/bin:$COPY/bin" "$COPY/bin:$ROOT/bin"; do
  case_ "with two copies of the wrappers on PATH ($order), gh and git return, as the App in a run"
  app_fixture
  cached ghs_cached 3600
  wrappers="$order:$APP_BIN/real:$PATH"
  A_TEAM_RUN_TEAM='' A_TEAM_CONFIG="$CONFIG" PATH="$wrappers" within gh --version
  same "gh outside a run" 'GH_TOKEN=
[--version]' "$(cat "$OUT")"
  A_TEAM_RUN_TEAM=demo A_TEAM_CONFIG="$CONFIG" PATH="$wrappers" within gh --version
  same "gh in a run" 'GH_TOKEN=ghs_cached
[--version]' "$(cat "$OUT")"
  A_TEAM_RUN_TEAM='' A_TEAM_CONFIG="$CONFIG" PATH="$wrappers" within git --version
  same "git outside a run exit" 0 "$STATUS"
  grep -q '^git version' "$OUT" || fail "git outside a run: '$(cat "$OUT" "$ERR")'"
  A_TEAM_RUN_TEAM=demo A_TEAM_CONFIG="$CONFIG" PATH="$wrappers" within git config user.name
  same "git in a run" 'demo-app[bot]' "$(cat "$OUT")"
done

# board.sh's check against a board that's fine, with the App's own view of it broken by
# PROJECT_UNREADABLE or PRIORITY_UNREADABLE when it asks with the cached token, and the board itself
# by REPO_GONE, NO_VISION, NO_PROJECT, NO_FIELD, and the options in meta.json and labels in labels.json.
mkdir -p "$APP_BIN/board"
board_options() {
  jq -n --args '{data: {organization: {projectV2: {id: "PVT_1", field: {id: "PVTSSF_status", options:
    ($ARGS.positional | map({id: ., name: .}))}}}}}' "$@" >"$APP_BIN/board/meta.json"
}
board_options Idea Exploring Pitched Approved Building Ready "In progress" "In review" Done
board_labels() { jq -n --args '$ARGS.positional | map({name: .})' "$@" >"$APP_BIN/board/labels.json"; }
board_labels pitch a-team:dev a-team:idea a-team:skipped a-team:displaced blocked
echo '{"data": {"organization": {"projectV2": {"items": {"totalCount": 0,
  "pageInfo": {"hasNextPage": false, "endCursor": null}, "nodes": []}}}}}' >"$APP_BIN/board/items.json"
echo '{"data": {"organization": {"issueFields": {"nodes": [{"id": "IF_priority", "name": "Priority",
  "options": []}]}}}}' >"$APP_BIN/board/fields.json"
cat >"$APP_BIN/board/gh" <<SH
#!/usr/bin/env bash
refused() { echo "gh: Resource not accessible by integration" >&2; exit 1; }
case " \$* " in
  *" repos/mentaldesk/demo/contents/"*) [ -z "\${NO_VISION:-}" ] || { echo "gh: Not Found (HTTP 404)" >&2; exit 1; }; exit 0 ;;
  *" repos/mentaldesk/demo "*) [ -z "\${REPO_GONE:-}" ] || { echo "gh: Not Found (HTTP 404)" >&2; exit 1; }; exit 0 ;;
  *"label list"*) page="$APP_BIN/board/labels.json" ;;
  *ProjectV2SingleSelectField*)
    [ -z "\${NO_PROJECT:-}" ] || { echo '{"data": {"organization": {"projectV2": null}}}'
      echo "gh: Could not resolve to a ProjectV2 with the number 1." >&2; exit 1; }
    [ -z "\${NO_FIELD:-}" ] || { echo '{"data": {"organization": {"projectV2": {"id": "PVT_1", "field": null}}}}'
      echo "gh: Could not resolve to a Unions::ProjectV2FieldConfiguration with the name Status" >&2; exit 1; }
    page="$APP_BIN/board/meta.json" ;;
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
same "no app exit" 1 "$STATUS"
grep -q '^app       demo has no GitHub App: run a-team app create demo, then install it$' "$OUT" ||
  fail "no app: '$(cat "$OUT")'"

case_ "check reports the App's identity when every part of it works"
app_fixture
cached ghs_cached 3600
checked
same "exit" 0 "$STATUS"
same "identity" 'identity: demo-app[bot] · token ok · project 1 read+write ok · Priority readable · push access to mentaldesk/demo ok' \
  "$(grep '^identity' "$OUT")"

case_ "check says which part of the identity is wrong, and what to do about it"
# says <exit> <line>: check exited so, and printed exactly that one problem.
says() {
  same "exit" "$1" "$STATUS"
  same "problems" "$2" "$(grep -E '^[a-z]+ {2,}' "$OUT")"
}
NO_KEY=1 checked
says 1 "app       demo-app[bot] has no key in the login Keychain (service 'a-team-app', account 'mentaldesk'): run a-team app create demo"
rm -f "$CACHE"
MINT_FAILS=1 checked
same "no token exit" 1 "$STATUS"
grep -q "^app       demo-app\[bot\] can't get a token: " "$OUT" || fail "no token: '$(cat "$OUT")'"
cached ghs_cached 3600
PROJECT_UNREADABLE=1 checked
says 1 "app       demo-app[bot] can't read project 1: grant the organisation's \"Projects: read and write\", and install the App on mentaldesk/demo"
grants '.permissions.organization_projects = "read"'
checked
says 1 "app       demo-app[bot] can only read project 1: grant the organisation's \"Projects: read and write\""
cached ghs_cached 3600
PRIORITY_UNREADABLE=1 checked
same "priority exit" 1 "$STATUS"
grep -q "^app       demo-app\[bot\] can't read the Priority field: grant the organisation's \"Issue Fields: read\"" "$OUT" ||
  fail "priority: '$(cat "$OUT")'"
grants '.permissions.contents = "read"'
checked
says 1 "app       demo-app[bot] can't push to mentaldesk/demo: grant the repository's \"Contents: read and write\""

case_ "check names each problem with the board in a line of its own"
cached ghs_cached 3600
checked
says 0 ""
REPO_GONE=1 checked
says 1 "repo      can't reach mentaldesk/demo: Not Found (HTTP 404)"
NO_PROJECT=1 checked
says 1 "project   can't read mentaldesk project 1: Could not resolve to a ProjectV2 with the number 1."
grep -q '^identity: demo-app\[bot\] · token ok · Priority readable' "$OUT" || fail "no project: the App's view in '$(cat "$OUT")'"
NO_FIELD=1 checked
says 1 "project   no single-select field 'Status' on mentaldesk project 1"
board_options Idea Approved Building Ready "In progress" "In review" Done
checked
says 1 "status    2 of 9 options missing from 'Status': Exploring, Pitched"
board_options Idea Exploring Pitched Approved Building Ready "In progress" "In review" Done

case_ "check names what's missing from the machine"
rm -rf "$APP_BIN/work/main"
checked
says 1 "checkout  $APP_BIN/work/main isn't there: gh repo clone mentaldesk/demo $APP_BIN/work/main"
rm -rf "$APP_BIN/work"
checked
says 1 "workdir   $APP_BIN/work isn't there, so the agents would have nothing to work in"
mkdir -p "$APP_BIN/work/main"

case_ "a missing vision or labels are problems the team can still run with, so check exits 2"
NO_VISION=1 checked
says 2 "vision    docs/vision.md isn't in mentaldesk/demo yet: the Lead will draft one and open it as a draft PR"
board_labels pitch a-team:dev blocked
checked
says 2 "labels    no 'a-team:idea' label, so the Lead can't flag the ideas it finds for you
labels    no 'a-team:skipped' label, so the Lead can't pass over an idea, and keeps coming back to it
labels    no 'a-team:displaced' label, so the Lead tells you every time it bumps a pitch out of Pitched, not just the first"
board_labels pitch a-team:dev a-team:idea a-team:skipped a-team:displaced blocked

case_ "check reports every problem it finds, not just the first"
board_labels pitch a-team:dev a-team:idea a-team:skipped blocked
NO_VISION=1 NO_FIELD=1 checked
says 1 "vision    docs/vision.md isn't in mentaldesk/demo yet: the Lead will draft one and open it as a draft PR
project   no single-select field 'Status' on mentaldesk project 1
labels    no 'a-team:displaced' label, so the Lead tells you every time it bumps a pitch out of Pitched, not just the first"
board_labels pitch a-team:dev a-team:idea a-team:skipped a-team:displaced blocked

case_ "check reports a config it can't read, or one with no repo or project, as problems"
before=$(cat "$TEAM")
printf '{\n "repo": "mentaldesk/demo",\n}\n' >"$TEAM"
checked
same "unparsable exit" 1 "$STATUS"
grep -q "^config    can't read demo.json: .*line 3" "$OUT" || fail "unparsable: '$(cat "$OUT")'"
echo '["not a team"]' >"$TEAM"
checked
says 1 "config    demo.json isn't a JSON object"
echo "$before" | jq 'del(.repo, .project.number)' >"$TEAM"
checked
same "no repo exit" 1 "$STATUS"
grep -q '^repo      demo.json names no repo$' "$OUT" || fail "no repo: '$(cat "$OUT")'"
grep -q '^project   demo.json names no project number$' "$OUT" || fail "no project number: '$(cat "$OUT")'"
echo "$before" >"$TEAM"

case_ "any other command refuses a config it can't read, in one line"
echo '["not a team"]' >"$TEAM"
PATH="$APP_BIN/board:$PATH" run board demo wip
failed "unreadable wip"
same "unreadable wip" "board.sh: demo.json isn't a JSON object" "$(cat "$ERR")"
echo "$before" >"$TEAM"

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

case_ "app create --no-open fails rather than opening the install page while the App isn't installed"
: >"$OPENED"
NOT_INSTALLED=1 run app create demo --no-open
failed "not installed"
grep -q "shared-app isn't installed on mentaldesk/demo yet" "$ERR" || fail "not installed: '$(cat "$ERR")'"
same "opened" '' "$(cat "$OPENED")"

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
