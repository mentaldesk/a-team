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
# Board writes record history in the state folder, which mustn't be the real one.
export A_TEAM_STATE="$WORK/state"
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
      content: {__typename: "Issue", id: "I_\($f[1])", number: ($f[1] | tonumber), title: ($f[2:] | join(" ")),
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
    {id: "OPT_approved", name: "Approved"}, {id: "OPT_done", name: "Done"}]}}}}}' >"$META"
  gh_thread </dev/null
  gh_recent </dev/null
  RUNS="$BIN/runs.json"
  gh_runs <<'RUNS'
completed success 2025-09-19T09:00:00Z build
RUNS
  PRS="$BIN/prs.json" PULL="$BIN/pull.json" REVIEWING="$BIN/reviewing.json"
  gh_pr
  echo '{"head": {"sha": "deadbeefcafe"}}' >"$PULL"
  FILTERS="$BIN/filters"
  : >"$FILTERS"
  CAUGHT="$BIN/caught.json"
  gh_caught </dev/null
  UPDATES="$BIN/updates.json"
  echo '[]' >"$UPDATES"
  cat >"$BIN/gh" <<SH
#!/usr/bin/env bash
echo call >>"$CALLS"
case " \$* " in
  *addReaction*) printf '%s\n' "\$@" | sed -n 's/^subject=//p' >>"$ACKED"; echo '{}'; exit 0 ;;
  *updateIssueFieldValue*) printf '%s ' "\$@" | tr -d '\n' >>"$WRITES"; echo >>"$WRITES"; echo '{}'; exit 0 ;;
  *updateProjectV2ItemFieldValue*) printf '%s ' "\$@" | tr -d '\n' >>"$WRITES"; echo >>"$WRITES"; echo '{}'; exit 0 ;;
  *"pr list"*"app/dependabot"*) page="$UPDATES" ;;
  *ProjectV2SingleSelectField*) page="$META" ;;
  *issueFields*) page="$FIELDS" ;;
  *": issue(number"*) jq '{data: {repository: ([.data.organization.projectV2.items.nodes[].content
                        | {key: "i\(.number)", value: {issueFieldValues}}] | from_entries)}}' "$ITEMS"; exit 0 ;;
  *"issue comment"*) cat >"$POSTED"; exit 0 ;;
  *"pr comment"*) echo "PR COMMENT \$*" >>"$WRITES"; cat >"$POSTED"; exit 0 ;;
  *"issue create"*) [ ! -e "$BIN/create-fails" ] || { echo "gh: Could not resolve to a Repository (HTTP 404)" >&2; exit 1; }
                    echo "CREATE \$*" >>"$WRITES"; cat >"$POSTED"; echo "https://github.com/mentaldesk/demo/issues/77"; exit 0 ;;
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
  *IssueEvents*) printf '%s\n' "\$@" >"$BIN/caught-args"; echo >>"$BIN/caught-calls"; page="$CAUGHT" ;;
  *issueOrPullRequest*) page="$TALK" ;;
  *"isDraft body labels"*) page="$REVIEWING" ;;
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
  *"/contents/"*) [ ! -e "$WORK/no-docs" ] || { echo "gh: Not Found (HTTP 404)" >&2; exit 1; }; exit 0 ;;
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

# What the catch-up finds on GitHub, from lines of "<n> <kind> <timestamp> <login> [<detail>]": kinds
# comment, merged, closed (<detail> the PR that closed it, or the reason) and moved (<detail> "<from>><to>",
# with _ for a space, on project <owner>/<number> if a third field follows). A login ending [bot] is a Bot.
gh_caught() {
  jq -R -s 'def who($login): if $login | endswith("[bot]")
      then {__typename: "Bot", login: ($login | rtrimstr("[bot]"))} else {__typename: "User", login: $login} end;
    split("\n") | map(select(length > 0)) | to_entries | map(.key as $i | .value | split(" ") as $f
      | {n: ($f[0] | tonumber), id: "EV_\($i)", createdAt: $f[2], kind: $f[1], who: who($f[3]), detail: ($f[4] // "")}
      | if .kind == "comment" then {n, node: {__typename: "IssueComment", id, createdAt, author: .who}}
        elif .kind == "merged" then {n, pull: true, node: {__typename: "MergedEvent", id, createdAt, actor: .who}}
        elif .kind == "closed" then {n, node: ({__typename: "ClosedEvent", id, createdAt, actor: .who}
          + if .detail | test("^[0-9]+$") then {stateReason: "COMPLETED",
              closer: {__typename: "PullRequest", number: (.detail | tonumber)}}
            else {stateReason: (if .detail == "" then null else .detail end), closer: null} end)}
        else (.detail | split(">") | map(gsub("_"; " "))) as $move | ($f[5] // "mentaldesk/1" | split("/")) as $p
          | {n, node: {__typename: "ProjectV2ItemStatusChangedEvent", id, createdAt, actor: .who,
              previousStatus: $move[0], status: $move[1],
              project: {number: ($p[1] | tonumber), owner: {login: $p[0]}}}} end)
    | group_by(.n) | map({number: .[0].n} + (if any(.[]; .pull) then {merged: true} else {} end)
        + {timelineItems: {nodes: map(.node)}})
    | {data: {search: {nodes: .}, nodes: []}}' >"$CAUGHT"
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
  gh_reviewing
}

# `gh_reviewed [<login>]`: the Reviewer's review on the PR gh_pr wrote, posted as <login>, by default the team's App.
gh_reviewed() {
  jq --arg login "${1:-demo-app[bot]}" '(.. | objects | select(has("isDraft"))) += {comments: {nodes: [
      {body: "Nothing needs changing.\n\n<!-- a-team:reviewer -->",
       author: (if $login | endswith("[bot]") then {__typename: "Bot", login: ($login | rtrimstr("[bot]"))}
                else {__typename: "User", login: $login} end)}]}}' "$PRS" >"$PRS.new" && mv "$PRS.new" "$PRS"
  gh_reviewing
}

# `gh_reviewing [<update>]`: the PR `review` reads, from the one gh_pr wrote: an open Dev PR, changed by a jq <update>.
gh_reviewing() {
  jq "{data: {repository: {pullRequest: ((.data.repository | .issue.closedByPullRequestsReferences.nodes
      + .pullRequests.nodes)[0] // null | if . == null then null else
      {state: \"OPEN\", body: \"Closes #12\n\n<!-- a-team:dev -->\", labels: {nodes: []}, comments: {nodes: []}} + .
      | ${1:-.} end)}}}" "$PRS" >"$REVIEWING"
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

# The read above began the record, so every read from here also catches up from GitHub: one call more.
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
same "api calls" 3 "$(grep -c '' <"$CALLS")"

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
same "api calls" 4 "$(grep -c '' <"$CALLS")"

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

case_ "a PR GitHub hasn't worked the conflict out for yet isn't ready to accept"
gh_talk false UNKNOWN <<TALK
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 pr-body ${TODAY}T08:25:00Z demo-app[bot] Closes #115\n<!-- a-team:dev -->
TALK
run board demo waiting
same "exit" 0 "$STATUS"
same "conflicting" false "$(jq -c '.[1].conflicting' "$OUT")"
same "unready" '"resolving mergeable status"' "$(jq -c '.[1].unready' "$OUT")"
same "task turn" '"dev"' "$(jq -c '.[1].turn' "$OUT")"
same "task reason" '"resolving mergeable status"' "$(jq -c '.[1].reason' "$OUT")"

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

case_ "with a Reviewer, a green draft is the Reviewer's turn until its review is posted"
jq '.roles.reviewer = true' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
run board demo waiting
same "exit" 0 "$STATUS"
same "task turn" '"reviewer"' "$(jq -c '.[1].turn' "$OUT")"
same "task trouble" '"awaiting review"' "$(jq -c '.[1].trouble' "$OUT")"
same "task reason" '"awaiting review"' "$(jq -c '.[1].reason' "$OUT")"
gh_runs <<RUNS
in_progress - - build
RUNS
run board demo waiting
same "running turn" '"dev"' "$(jq -c '.[1].turn' "$OUT")"
same "running trouble" '"CI running"' "$(jq -c '.[1].trouble' "$OUT")"
gh_runs <<RUNS
completed success ${TODAY}T09:00:00Z build
RUNS
gh_talk true <<TALK
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 pr-body ${TODAY}T08:25:00Z demo-app[bot] Closes #115\n<!-- a-team:dev -->
115 pr-comment+seen ${TODAY}T09:10:00Z demo-app[bot] Nothing needs changing.\n<!-- a-team:reviewer -->
TALK
run board demo waiting
same "reviewed turn" '"dev"' "$(jq -c '.[1].turn' "$OUT")"
same "reviewed trouble" '"still a draft"' "$(jq -c '.[1].trouble' "$OUT")"
jq 'del(.roles)' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"

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
same "api calls" 2 "$(grep -c '' <"$CALLS")"

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
same "api calls" 3 "$(grep -c '' <"$CALLS")"

case_ "a team whose every Idea is ranked has none of them waiting"
gh_items 6 26 <<'ITEMS'
Idea 6 The agents can't say what they'd change
Idea 26 A pitch I've shelved
ITEMS
run board demo waiting
same "exit" 0 "$STATUS"
same "items" '[]' "$(jq -c . "$OUT")"
same "api calls" 2 "$(grep -c '' <"$CALLS")"

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
same "api calls" 3 "$(grep -c '' <"$CALLS")"

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
run board demo approve nobody 7
failed "unknown role"
one_line "unknown role"
grep -q "unknown role 'nobody' (you)" "$ERR" || fail "unknown role: '$(cat "$ERR")'"

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

# Declining: the stakeholder's no to an Idea or a pitch, with the reason on the issue.
case_ "decline posts the reason as yours, closes the issue as not planned and moves it to Done"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 } }
JSON
gh_items <<'ITEMS'
Idea 9 An Idea I don't want
Pitched 7 A pitch I don't want
Exploring 8 A pitch still being drafted
Ready 10 A task
ITEMS
rm -f "$A_TEAM_STATE/history.db"
printf 'Already covered by #46.\n' >"$WORK/reason"
for n in 9 7; do
  : >"$WRITES"
  : >"$POSTED"
  run board demo decline you "$n" "$WORK/reason"
  same "#$n exit" 0 "$STATUS"
  same "#$n said" "#$n: declined" "$(cat "$OUT")"
  same "#$n posted" "Already covered by #46." "$(cat "$POSTED")"
  grep -q "^CLOSE issue close $n -R mentaldesk/demo --reason not planned$" "$WRITES" ||
    fail "#$n: not closed as not planned in '$(cat "$WRITES")'"
  grep -q "item=PVTI_$n .*option=OPT_done " "$WRITES" || fail "#$n: not moved to Done in '$(cat "$WRITES")'"
  same "#$n history" "you declined
you commented" "$(A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board demo history "$n" | jq -r '.events[] | "\(.who) \(.what)"')"
done

case_ "a decline isn't counted as accepted"
run board demo trend
same "accepted" 0 "$(jq .accepted "$OUT")"

case_ "--dry-run says what it would decline and changes nothing"
: >"$WRITES"
: >"$POSTED"
rm -f "$A_TEAM_STATE/history.db"
run board --dry-run demo decline you 9 "$WORK/reason"
same "exit" 0 "$STATUS"
same "writes" "" "$(cat "$WRITES")"
same "posted" "" "$(cat "$POSTED")"
grep -q "would close #9 as not planned" "$ERR" || fail "decline dry run: '$(cat "$ERR")'"
[ ! -e "$A_TEAM_STATE/history.db" ] || fail "dry run: recorded history"

case_ "no agent may decline: that gate is the stakeholders' own"
for role in lead dev customer reviewer; do
  run board demo decline "$role" 9 "$WORK/reason"
  failed "$role declining"
  one_line "$role declining"
  grep -q "$role may not decline an item; declining is the stakeholders' own gate" "$ERR" ||
    fail "$role declining: '$(cat "$ERR")'"
done
same "writes" "" "$(cat "$WRITES")"

case_ "a decline needs a reason"
printf ' \n\t\n' >"$WORK/blank"
run board demo decline you 9 "$WORK/blank"
failed "no reason"
one_line "no reason"
grep -q "a decline needs a reason" "$ERR" || fail "no reason: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"
same "posted" "" "$(cat "$POSTED")"

case_ "decline is only for an Idea or a Pitched pitch, never a task, a PR or the docs PR"
for n in 8 10 404; do
  run board demo decline you "$n" "$WORK/reason"
  failed "decline #$n"
  one_line "decline #$n"
done
grep -q "#404 is not on the board" "$ERR" || fail "decline off the board: '$(cat "$ERR")'"
run board demo decline you 10 "$WORK/reason"
grep -q "only an Idea or a Pitched pitch can be declined (#10 is in 'Ready')" "$ERR" || fail "decline a task: '$(cat "$ERR")'"
edit_item 7 '.labels.nodes = []'
run board demo decline you 7 "$WORK/reason"
failed "decline unlabelled"
grep -q "#7 is not a pitch" "$ERR" || fail "decline unlabelled: '$(cat "$ERR")'"
edit_item 7 '.labels.nodes = [{name: "pitch"}] | .__typename = "PullRequest"'
run board demo decline you 7 "$WORK/reason"
failed "decline a PR"
grep -q "#7 is not an issue" "$ERR" || fail "decline a PR: '$(cat "$ERR")'"
edit_item 7 '.labels.nodes = [{name: "a-team:customer"}] | .__typename = "PullRequest"'
run board demo decline you 7 "$WORK/reason"
failed "decline the docs PR"
same "writes" "" "$(cat "$WRITES")"
same "posted" "" "$(cat "$POSTED")"

case_ "a close GitHub refuses says why in one line"
touch "$BIN/close-fails"
run board demo decline you 9 "$WORK/reason"
failed "refused close"
grep -q "can't close #9 (gh: Resource not accessible by integration (HTTP 403))" "$ERR" ||
  fail "refused close: '$(cat "$ERR")'"
rm "$BIN/close-fails"

case_ "a Status option whose id is all digits is sent as a string"
jq '.data.organization.projectV2.field.options |= map(if .name == "Done" then .id = "98236657" else . end)' \
  "$META" >"$META.new" && mv "$META.new" "$META"
: >"$WRITES"
run board demo decline you 9 "$WORK/reason"
same "exit" 0 "$STATUS"
grep -q -- "-f option=98236657 " "$WRITES" || fail "numeric option: not a raw string in '$(cat "$WRITES")'"

case_ "the agents' settings deny decline"
grep -qF '"Bash(a-team board * decline *)"' "$ROOT/settings/agents.json" || fail "no decline deny rule"

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

case_ "finish closes a pitch in Building once every one of its tasks is closed, saying what the Lead tried"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 } }
JSON
gh_items <<'ITEMS'
Building 7 A pitch whose tasks have all merged
In_review 8 A pitch waiting on acceptance
Ready 9 A task
ITEMS
edit_item 8 '.labels.nodes = [{name: "pitch"}]'
echo '[{"number": 11, "state": "closed"}, {"number": 12, "state": "closed"}]' >"$SUBS"
echo "Tried both cases on main: they work." >"$WORK/checked"
run board demo finish lead 7 "$WORK/checked"
same "exit" 0 "$STATUS"
same "said" "#7: closed as done" "$(cat "$OUT")"
same "writes" "CLOSE issue close 7 -R mentaldesk/demo --reason completed" "$(cat "$WRITES")"
same "posted" "Tried both cases on main: they work." "$(head -1 "$POSTED")"
same "marker" "<!-- a-team:lead -->" "$(tail -1 "$POSTED")"

case_ "finish --dry-run says it would close the pitch and comment, and does neither"
: >"$WRITES"
: >"$POSTED"
run board --dry-run demo finish lead 7 "$WORK/checked"
same "exit" 0 "$STATUS"
same "writes" "" "$(cat "$WRITES")"
same "posted" "" "$(cat "$POSTED")"
grep -q "would close #7 as completed" "$ERR" || fail "finish dry run: no close in '$(cat "$ERR")'"
grep -q "would comment on #7" "$ERR" || fail "finish dry run: no comment in '$(cat "$ERR")'"

case_ "finish is the Lead's alone, for a pitch in Building with tasks, all of them closed"
run board demo finish dev 7 "$WORK/checked"
failed "finish as dev"
grep -q "only lead may close a pitch as done" "$ERR" || fail "finish as dev: '$(cat "$ERR")'"
run board demo finish lead 8 "$WORK/checked"
failed "finish In review"
grep -q "only a pitch in Building can be closed as done (#8 is in 'In review')" "$ERR" ||
  fail "finish In review: '$(cat "$ERR")'"
run board demo finish lead 9 "$WORK/checked"
failed "finish a task"
grep -q "#9 is not a pitch" "$ERR" || fail "finish a task: '$(cat "$ERR")'"
echo '[{"number": 11, "state": "closed"}, {"number": 12, "state": "open"}]' >"$SUBS"
run board demo finish lead 7 "$WORK/checked"
failed "finish with a task open"
one_line "finish with a task open"
grep -q "#7 has 1 open task$" "$ERR" || fail "finish with a task open: '$(cat "$ERR")'"
echo '[]' >"$SUBS"
run board demo finish lead 7 "$WORK/checked"
failed "finish with no tasks"
grep -q "#7 has no tasks, so nothing was built" "$ERR" || fail "finish with no tasks: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"
same "posted" "" "$(cat "$POSTED")"

case_ "the Lead closes a finished pitch itself rather than moving it to In review"
run board demo move lead 7 "In review"
failed "Building to In review"
grep -q "lead may not move #7 from 'Building' to 'In review'" "$ERR" ||
  fail "Building to In review: '$(cat "$ERR")'"
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
run board demo comment nobody 7 "$WORK/mine"
failed "unknown role"
one_line "unknown role"
grep -q "unknown role 'nobody' (lead | dev | customer | you)" "$ERR" || fail "unknown role: '$(cat "$ERR")'"
same "posted" "" "$(cat "$POSTED")"

case_ "--dry-run shows the comment you'd post and posts nothing"
run board --dry-run demo comment you 7 "$WORK/mine"
same "exit" 0 "$STATUS"
same "posted" "" "$(cat "$POSTED")"
grep -qF "  | Not yet: the second option is still missing." "$ERR" || fail "comment you dry run: '$(cat "$ERR")'"
grep -q "would comment on #7" "$ERR" || fail "comment you dry run: '$(cat "$ERR")'"

case_ "the agents' settings deny comment you, beside accept"
grep -qF '"Bash(a-team board * comment you *)"' "$ROOT/settings/agents.json" || fail "no comment you deny rule"

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

case_ "new you opens an issue as you, titled by the file's first line, and prints its number"
printf 'Remember the lane I was on\n\nIt always starts on the first team.\n' >"$WORK/idea"
run board demo new you "$WORK/idea"
same "exit" 0 "$STATUS"
same "number" 77 "$(cat "$OUT")"
grep -qF -- "--title Remember the lane I was on" "$WRITES" || fail "new: no title in '$(cat "$WRITES")'"
grep -qF -- "-R mentaldesk/demo" "$WRITES" || fail "new: not the team's repo in '$(cat "$WRITES")'"
same "body" "$(printf '\nIt always starts on the first team.')" "$(cat "$POSTED")"
grep -q "a-team:" "$POSTED" && fail "new: a marker in your issue"

case_ "new refuses an empty title, an agent, and opens nothing"
: >"$WRITES"
printf '   \nA body with no title\n' >"$WORK/untitled"
run board demo new you "$WORK/untitled"
failed "untitled"
one_line "untitled"
grep -q "an issue needs a title" "$ERR" || fail "untitled: '$(cat "$ERR")'"
for role in lead dev customer; do
  run board demo new "$role" "$WORK/idea"
  failed "$role new"
  grep -q "$role may not open an issue as you" "$ERR" || fail "$role new: '$(cat "$ERR")'"
done
same "writes" "" "$(cat "$WRITES")"

case_ "new that GitHub refuses says why"
touch "$BIN/create-fails"
run board demo new you "$WORK/idea"
failed "refused"
grep -q "Could not resolve to a Repository" "$ERR" || fail "refused: '$(cat "$ERR")'"
rm "$BIN/create-fails"

case_ "--dry-run shows the issue you'd open and opens nothing"
run board --dry-run demo new you "$WORK/idea"
same "exit" 0 "$STATUS"
same "writes" "" "$(cat "$WRITES")"
grep -q "would open an issue titled 'Remember the lane I was on'" "$ERR" || fail "new dry run: '$(cat "$ERR")'"

case_ "add you puts your own issue on the board as an Idea, with no label"
jq '.data.organization.projectV2.field.options += [{id: "OPT_idea", name: "Idea"}]' "$META" >"$META.new" && mv "$META.new" "$META"
gh_child 41 - - "It always starts on the first team."
run board demo add you 41 Idea
same "exit" 0 "$STATUS"
grep -q "OPT_idea" "$WRITES" || fail "add you: no move to Idea in '$(cat "$WRITES")'"
grep -q "labels" "$WRITES" && fail "add you: labelled '$(cat "$WRITES")'"

case_ "add you adds only an Idea"
for to in Ready Pitched; do
  run board demo add you 41 "$to"
  failed "add you as $to"
  grep -qF "you may not add items as '$to'" "$ERR" || fail "add you as $to: '$(cat "$ERR")'"
done

case_ "the agents' settings deny new you and add you"
grep -qF '"Bash(a-team board * new you *)"' "$ROOT/settings/agents.json" || fail "no new you deny rule"
grep -qF '"Bash(a-team board * add you *)"' "$ROOT/settings/agents.json" || fail "no add you deny rule"

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

case_ "triggers names it too, so it gets a run of its own, recorded on #7"
gh_recent <<RECENT
7 ${TODAY}T02:20:00Z reviewer 1 Needs a second option.
7 ${TODAY}T02:28:46Z reviewer 0 And do the same for subissue links.
7 ${TODAY}T02:33:03Z demo-app[bot] 0 Added one.\n<!-- a-team:lead -->
RECENT
run board demo triggers lead
same "exit" 0 "$STATUS"
same "reasons" "[\"stakeholder feedback on #7 (${TODAY}T02:28:46Z)\"]" "$(jq -c .reasons "$OUT")"
same "items" "[7]" "$(jq -c .items "$OUT")"
same "card" 7 "$(jq -c .card "$OUT")"

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
grep -q "unknown role 'nobody' (lead | dev | customer | reviewer)" "$ERR" || fail "depends role: '$(cat "$ERR")'"
run board demo undepend nobody 11 21 "why"
failed "undepend role"
one_line "undepend role"
grep -q "unknown role 'nobody' (lead | dev | customer | reviewer)" "$ERR" || fail "undepend role: '$(cat "$ERR")'"

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

case_ "the Dev puts a follow-up it opened on the board as an Idea, without the Lead's label"
gh_items <<'ITEMS'
In_progress 12 A task
ITEMS
jq '.data.organization.projectV2.field.options += [{id: "OPT_idea", name: "Idea"}]' "$META" >"$META.new" && mv "$META.new" "$META"
gh_child 41 - - "Follow-up from #12.

<!-- a-team:dev -->"
A_TEAM_RUN_TASK=12 run board demo add dev 41 Idea
same "exit" 0 "$STATUS"
grep -q "OPT_idea" "$WRITES" || fail "no move to Idea in '$(cat "$WRITES")'"
grep -q "a-team:idea" "$WRITES" && fail "labelled as the Lead's discovery: '$(cat "$WRITES")'"

case_ "the Dev adds no issue it didn't open, and nothing but an Idea"
gh_child 41 - - "Follow-up from #12.

<!-- a-team:lead -->"
run board demo add dev 41 Idea
failed "someone else's issue"
one_line "someone else's issue"
grep -qF "#41 has no dev marker: dev adds only the follow-ups it opened" "$ERR" ||
  fail "someone else's issue: '$(cat "$ERR")'"
gh_child 41 - - "Follow-up from #12.

<!-- a-team:dev -->"
for to in Ready Exploring; do
  run board demo add dev 41 "$to"
  failed "add as $to"
  grep -qF "dev may not add items as '$to'" "$ERR" || fail "add as $to: '$(cat "$ERR")'"
done
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

case_ "a PR GitHub hasn't linked to its task is still found, by what its body closes"
gh_pr 912 true 12
run board demo triggers dev
same "exit" 0 "$STATUS"
same "reasons" '["PR #912 is green but still a draft"]' "$(jq -c .reasons "$OUT")"
run board demo pr 12
same "exit" 0 "$STATUS"
same "pr" 912 "$(jq -c .number "$OUT")"
same "fields" '["headRefName","isDraft","mergeable","number","review","url"]' "$(jq -c keys "$OUT")"

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

case_ "triggers starts the Lead for each Dependabot PR it hasn't commented on yet"
cat >"$UPDATES" <<'JSON'
[{"number": 440, "title": "Bump MentalDesk.Tui", "comments": []},
 {"number": 441, "title": "Bump Terminal.Gui", "comments": [
   {"author": {"login": "demo-app"}, "body": "Nothing to adopt.\n\n<!-- a-team:lead -->"}]},
 {"number": 442, "title": "Bump Spectre.Console", "comments": [
   {"author": {"login": "stranger"}, "body": "<!-- a-team:lead -->"}]}]
JSON
run board demo triggers lead
same "exit" 0 "$STATUS"
same "reasons" '["Dependabot opened PR #440 (Bump MentalDesk.Tui): see what the update brings","Dependabot opened PR #442 (Bump Spectre.Console): see what the update brings"]' \
  "$(jq -c .reasons "$OUT")"
same "items" '[440,442]' "$(jq -c .items "$OUT")"
echo '[]' >"$UPDATES"

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

# The Customer lead: a third role a team can turn on, which touches only its own docs PR.
# `docs_pr <n>`: #<n> on the page gh_items wrote is the Customer lead's PR. `accepted <n> [<reason>]`:
# pitch #<n> is Done, closed as <reason> (COMPLETED unless given).
docs_pr() {
  edit_item "$1" '.__typename = "PullRequest" | .labels.nodes = [{name: "a-team:customer"}]
    | del(.issueDependenciesSummary, .issueFieldValues)'
}
accepted() {
  edit_item "$1" ".labels.nodes = [{name: \"pitch\"}] | .stateReason = \"${2:-COMPLETED}\""
}
customer_on() { jq '.roles.customer = true | .docs = "docs/index.md"' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"; }

case_ "the Customer lead never triggers for a team that hasn't turned it on"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "stakeholders": ["reviewer"], "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 } }
JSON
gh_items <<'ITEMS'
Done 5 A pitch accepted long ago
ITEMS
accepted 5
run board demo triggers customer
same "exit" 0 "$STATUS"
same "reasons" '[]' "$(jq -c .reasons "$OUT")"
[ -e "$A_TEAM_STATE/demo/customer/covered" ] && fail "off: it started counting what's covered"

case_ "turned on, it audits the docs straight away"
customer_on
run board demo triggers customer
same "first pass" '["weekly docs audit: check the docs as a whole"]' "$(jq -c .reasons "$OUT")"
run board demo audited customer
same "exit" 0 "$STATUS"
same "said" "docs audited: the next audit is in a week" "$(cat "$OUT")"

case_ "turned on, it starts from the pitches done after that, one reason each, and never for a shelved one"
run board demo triggers customer
same "first pass" '[]' "$(jq -c .reasons "$OUT")"
same "creative" false "$(jq -c .creative "$OUT")"
gh_items <<'ITEMS'
Done 5 A pitch accepted long ago
Done 6 A pitch just accepted
Done 8 A pitch closed as not planned
In_review 7 A pitch being validated
ITEMS
accepted 5
accepted 6
accepted 8 NOT_PLANNED
edit_item 7 '.labels.nodes = [{name: "pitch"}]'
run board demo triggers customer
same "exit" 0 "$STATUS"
same "reasons" '["pitch #6 is done: check the docs cover what it shipped"]' "$(jq -c .reasons "$OUT")"
same "api calls" 2 "$(grep -c '' <"$CALLS")"

case_ "covered stops a pitch triggering again, and goes in its history"
run board demo covered customer 6
same "exit" 0 "$STATUS"
same "said" "#6: docs checked" "$(cat "$OUT")"
run board demo triggers customer
same "reasons" '[]' "$(jq -c .reasons "$OUT")"
run board demo history 6
same "history" '"customer docs checked"' "$(jq -c '.events[0] | "\(.who) \(.what)"' "$OUT")"

case_ "covered is the Customer lead's alone, and only for a done pitch"
run board demo covered dev 6
failed "dev covering"
grep -q "only customer checks the docs" "$ERR" || fail "dev covering: '$(cat "$ERR")'"
run board demo covered customer 7
failed "covering a pitch In review"
grep -q "#7 isn't a done pitch" "$ERR" || fail "covering In review: '$(cat "$ERR")'"

case_ "the audit comes round again a week after the last one finished, and not before"
AUDITED="$A_TEAM_STATE/demo/customer/audited"
echo $(($(date +%s) - 7 * 24 * 60 * 60 + 60)) >"$AUDITED"
run board demo triggers customer
same "six days on" '[]' "$(jq -c .reasons "$OUT")"
echo $(($(date +%s) - 7 * 24 * 60 * 60)) >"$AUDITED"
run board demo triggers customer
same "a week on" '["weekly docs audit: check the docs as a whole"]' "$(jq -c .reasons "$OUT")"
run board demo triggers customer
same "until it's audited" '["weekly docs audit: check the docs as a whole"]' "$(jq -c .reasons "$OUT")"
run board demo audited customer
run board demo triggers customer
same "audited" '[]' "$(jq -c .reasons "$OUT")"

case_ "audited is the Customer lead's alone, and --dry-run records nothing"
echo 0 >"$AUDITED"
run board --dry-run demo audited customer
same "said" "(dry run) docs audited: the next audit is in a week" "$(cat "$OUT")"
same "unchanged" 0 "$(cat "$AUDITED")"
run board demo audited dev
failed "dev auditing"
grep -q "only customer audits the docs" "$ERR" || fail "dev auditing: '$(cat "$ERR")'"
jq 'del(.roles)' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
run board demo triggers customer
same "off" '[]' "$(jq -c .reasons "$OUT")"
run board demo audited customer
failed "auditing while off"
customer_on
echo $(($(date +%s))) >"$AUDITED"

case_ "the Customer lead adds its own open docs PR to In review, labelled as its"
gh_items <<'ITEMS'
In_progress 7 A task being built
In_review 9 Docs: what's changed since 28 Sep
ITEMS
docs_pr 9
jq '(.data.organization.projectV2.items.nodes[] | select(.content.number == 9)) |= (.fieldValueByName = null)' \
  "$ITEMS" >"$ITEMS.new" && mv "$ITEMS.new" "$ITEMS"
jq '.data.organization.projectV2.field.options += [{id: "OPT_review", name: "In review"}, {id: "OPT_done", name: "Done"}]' \
  "$META" >"$META.new" && mv "$META.new" "$META"
jq -n '{node_id: "PR_9", number: 9, state: "open", pull_request: {},
        body: "- Help → Guide covers the Customer lead (#48)\n\n<!-- a-team:customer -->"}' >"$ISSUE"
run board demo add customer 9 "In review"
same "exit" 0 "$STATUS"
grep -q 'issues/9/labels -f labels\[\]=a-team:customer' "$WRITES" || fail "add: no label in '$(cat "$WRITES")'"
grep -q 'item=PVTI_9 .*option=OPT_review' "$WRITES" || fail "add: no move in '$(cat "$WRITES")'"

case_ "it may add nothing but its own open docs PR, and only one at a time"
: >"$WRITES"
jq '.body = "Closes #7\n\n<!-- a-team:dev -->"' "$ISSUE" >"$ISSUE.new" && mv "$ISSUE.new" "$ISSUE"
run board demo add customer 9 "In review"
failed "someone else's PR"
grep -q "customer may only add its own open docs PR (#9 isn't one)" "$ERR" || fail "someone else's PR: '$(cat "$ERR")'"
gh_items <<'ITEMS'
In_review 9 Docs: what's changed since 28 Sep
In_review 10 Docs: what's changed since 1 Oct
ITEMS
docs_pr 9
jq -n '{node_id: "PR_10", number: 10, state: "open", pull_request: {}, body: "<!-- a-team:customer -->"}' >"$ISSUE"
run board demo add customer 10 "In review"
failed "a second docs PR"
grep -q "customer's docs PR #9 is still open: add to it rather than opening another" "$ERR" ||
  fail "a second docs PR: '$(cat "$ERR")'"
run board demo add customer 10 Ready
failed "adding to Ready"
grep -q "customer may not add items as 'Ready'" "$ERR" || fail "adding to Ready: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"

case_ "the Customer lead can't move pitches or tasks, change what a task waits on, or clear a gate"
gh_items <<'ITEMS'
In_progress 7 A task being built
Approved 11 A pitch to break down
In_review 9 Docs: what's changed since 28 Sep
ITEMS
docs_pr 9
for args in "move customer 7 Ready" "move customer 11 Building" "approve customer 11" "accept customer 9" \
  "priority customer 7 High" "depends customer 7 11 why" "claim customer" "skip customer 7 $WORK/none"; do
  # shellcheck disable=SC2086 # one argument per word
  run board demo $args
  failed "customer: $args"
done
same "writes" "" "$(cat "$WRITES")"

case_ "the Customer lead comments only on its docs PR, with its own marker"
printf "Added the Customer lead to Help → Guide." >"$WORK/said"
gh_thread pull <<'THREAD'
body 2025-09-19T08:00:00Z demo-app[bot] 0 Docs\n<!-- a-team:customer -->
THREAD
run board demo comment customer 9 "$WORK/said"
same "exit" 0 "$STATUS"
same "posted" "Added the Customer lead to Help → Guide.

<!-- a-team:customer -->" "$(cat "$POSTED")"
: >"$POSTED"
run board demo comment customer 7 "$WORK/said"
failed "commenting on a task"
grep -q "customer only touches its own docs PR (#7 isn't it)" "$ERR" || fail "commenting on a task: '$(cat "$ERR")'"
same "posted" "" "$(cat "$POSTED")"

case_ "accepting the docs PR squash-merges it, deletes its branch and moves it to Done"
: >"$WRITES"
jq '.data.organization.projectV2.field.options += [{id: "OPT_done", name: "Done"}]' "$META" >"$META.new" && mv "$META.new" "$META"
echo '{"head": {"sha": "deadbeefcafe", "ref": "docs/customer-lead", "repo": {"full_name": "mentaldesk/demo"}}}' >"$PULL"
run board demo accept you 9
same "exit" 0 "$STATUS"
same "said" "#9: merged docs PR #9" "$(cat "$OUT")"
grep -q 'PUT api -X PUT repos/mentaldesk/demo/pulls/9/merge -f merge_method=squash' "$WRITES" || fail "merge: '$(cat "$WRITES")'"
grep -q 'DELETE api -X DELETE repos/mentaldesk/demo/git/refs/heads/docs/customer-lead' "$WRITES" || fail "branch: '$(cat "$WRITES")'"
grep -q 'item=PVTI_9 .*option=OPT_done' "$WRITES" || fail "Done: '$(cat "$WRITES")'"

case_ "waiting shows the docs PR as the Customer lead's, with itself as the PR to merge"
gh_talk <<TALK
9 body ${TODAY}T08:14:00Z demo-app[bot] Docs\n<!-- a-team:customer -->
TALK
jq '.data.repository.x9 += {url: "https://github.com/mentaldesk/demo/pull/9", isDraft: false, mergeable: "MERGEABLE",
      baseRefName: "main"} | del(.data.repository.x9.closedByPullRequestsReferences)' "$TALK" >"$TALK.new" &&
  mv "$TALK.new" "$TALK"
run board demo waiting
same "exit" 0 "$STATUS"
same "docs PR" '{"number":9,"role":"customer","pr":9,"turn":"you","unready":""}' \
  "$(jq -c '.[] | select(.number == 9) | {number, role, pr, turn, unready}' "$OUT")"

case_ "conversation tells the Customer lead's words apart from the Lead's and the Dev's"
gh_talk <<TALK
9 body ${TODAY}T08:14:00Z demo-app[bot] Docs\n<!-- a-team:customer -->
9 comment ${TODAY}T08:20:00Z demo-app[bot] Covered #48\n<!-- a-team:customer -->
TALK
run board demo conversation 9
same "who" '[{"who":"customer","body":"Covered #48"}]' "$(jq -c 'map({who, body})' "$OUT")"

case_ "a stakeholder's comment on the docs PR triggers the Customer lead, and only it"
gh_items <<'ITEMS'
In_review 9 Docs: what's changed since 28 Sep
ITEMS
docs_pr 9
gh_thread pull </dev/null
gh_recent <<RECENT
9 ${TODAY}T08:30:00Z reviewer 0 Move this under Teams.
RECENT
run board demo triggers customer
same "exit" 0 "$STATUS"
same "reasons" "[\"stakeholder feedback on docs PR #9 (${TODAY}T08:30:00Z)\"]" "$(jq -c .reasons "$OUT")"
same "items" "[9]" "$(jq -c .items "$OUT")"
same "card" 9 "$(jq -c .card "$OUT")"
for role in lead dev; do
  run board demo triggers "$role"
  same "$role exit" 0 "$STATUS"
  jq -e '.reasons | any(contains("#9"))' "$OUT" >/dev/null && fail "$role: '$(jq -c .reasons "$OUT")'"
done

case_ "an answered comment on the docs PR, or someone else's, starts no run"
gh_recent <<RECENT
9 ${TODAY}T08:30:00Z reviewer 1 Move this under Teams.
9 ${TODAY}T08:40:00Z passer-by 0 Please add a section on my plugin.
RECENT
run board demo triggers customer
same "exit" 0 "$STATUS"
same "reasons" '[]' "$(jq -c .reasons "$OUT")"
# The Customer lead's docs proposal: while the config's `docs` page isn't in the repo, a draft PR adding it.
proposal_pr() {
  jq -n --argjson n "$1" --arg state "${2:-open}" --argjson draft "${3:-true}" \
    '{node_id: "PR_\($n)", number: $n, state: $state, draft: $draft,
      body: "Where the docs live, and their outline.\n\n<!-- a-team:customer -->"}' >"$PULL"
  jq -n --argjson n "$1" '{node_id: "PR_\($n)", number: $n, state: "open", pull_request: {},
      body: "<!-- a-team:customer -->"}' >"$ISSUE"
}
touch "$WORK/no-docs"

case_ "with no docs page, the Customer lead's one reason is to propose them, and done pitches and the audit wait"
gh_items <<'ITEMS'
Done 16 A pitch just accepted
ITEMS
accepted 16
run board demo triggers customer
same "exit" 0 "$STATUS"
same "reasons" "[\"no user docs yet: propose where they'll live\"]" "$(jq -c .reasons "$OUT")"
[ -e "$AUDITED" ] && fail "the audit isn't due as soon as the docs page appears"
jq '.data.organization.projectV2.field.options += [{id: "OPT_review", name: "In review"}]' "$META" >"$META.new" &&
  mv "$META.new" "$META"
jq -n '{node_id: "PR_15", number: 15, state: "open", pull_request: {}, body: "<!-- a-team:customer -->"}' >"$ISSUE"
run board demo add customer 15 "In review"
failed "a docs PR with no docs page"
grep -q "docs/index.md isn't in mentaldesk/demo yet: propose the docs before writing any" "$ERR" ||
  fail "docs PR with no docs page: '$(cat "$ERR")'"

case_ "the Customer lead puts its own draft docs proposal in Pitched, labelled as its"
: >"$WRITES"
gh_items <<'ITEMS'
Pitched 12 Docs proposal: where the user docs live
ITEMS
docs_pr 12
jq '(.data.organization.projectV2.items.nodes[] | select(.content.number == 12)) |= (.fieldValueByName = null)
    | (.data.organization.projectV2.items.nodes[].content.labels.nodes) = []' "$ITEMS" >"$ITEMS.new" &&
  mv "$ITEMS.new" "$ITEMS"
proposal_pr 12
run board demo add customer 12 Pitched
same "exit" 0 "$STATUS"
same "said" "#12: added as Pitched" "$(cat "$OUT")"
grep -q 'issues/12/labels -f labels\[\]=a-team:customer' "$WRITES" || fail "propose: no label in '$(cat "$WRITES")'"
grep -q 'labels\[\]=pitch' "$WRITES" && fail "propose: labelled a pitch, which is the Lead's"
grep -q 'item=PVTI_12 .*option=OPT_pitched' "$WRITES" || fail "propose: no move in '$(cat "$WRITES")'"

case_ "it proposes only with its own open draft PR, one at a time, and only while the docs page is missing"
: >"$WRITES"
proposal_pr 12 open false
run board demo add customer 12 Pitched
failed "a PR that isn't a draft"
grep -q "customer may only put its own open draft docs proposal in Pitched (#12 isn't one)" "$ERR" ||
  fail "not a draft: '$(cat "$ERR")'"
proposal_pr 12
jq '.body = "Closes #7\n\n<!-- a-team:dev -->"' "$PULL" >"$PULL.new" && mv "$PULL.new" "$PULL"
run board demo add customer 12 Pitched
failed "someone else's PR"
gh_items <<'ITEMS'
Pitched 12 Docs proposal: where the user docs live
ITEMS
docs_pr 12
proposal_pr 13
run board demo add customer 13 Pitched
failed "a second proposal"
grep -q "customer's #12 is still open: no docs proposal beside it" "$ERR" || fail "a second proposal: '$(cat "$ERR")'"
gh_items </dev/null
proposal_pr 13
rm "$WORK/no-docs"
run board demo add customer 13 Pitched
failed "a proposal with docs"
grep -q "docs/index.md is already in mentaldesk/demo: no docs proposal needed" "$ERR" ||
  fail "a proposal with docs: '$(cat "$ERR")'"
touch "$WORK/no-docs"
same "writes" "" "$(cat "$WRITES")"

case_ "while the proposal is open, nothing but feedback on it starts a run, and no docs PR goes up"
gh_items <<'ITEMS'
Pitched 12 Docs proposal: where the user docs live
Done 16 A pitch just accepted
ITEMS
docs_pr 12
accepted 16
proposal_pr 12
run board demo triggers customer
same "exit" 0 "$STATUS"
same "reasons" '[]' "$(jq -c .reasons "$OUT")"
jq -n '{node_id: "PR_15", number: 15, state: "open", pull_request: {}, body: "<!-- a-team:customer -->"}' >"$ISSUE"
run board demo add customer 15 "In review"
failed "a docs PR before the proposal merges"
grep -q "customer's docs proposal #12 isn't merged yet: no docs PR until it is" "$ERR" ||
  fail "docs PR too soon: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"

case_ "a stakeholder's comment on the proposal triggers the Customer lead, not the Lead"
gh_recent <<RECENT
12 ${TODAY}T09:00:00Z reviewer 0 Put them under docs/ instead.
RECENT
run board demo triggers customer
same "reasons" "[\"stakeholder feedback on docs proposal #12 (${TODAY}T09:00:00Z)\"]" "$(jq -c .reasons "$OUT")"
same "items" "[12]" "$(jq -c .items "$OUT")"
for role in lead dev; do
  run board demo triggers "$role"
  same "$role exit" 0 "$STATUS"
  jq -e '.reasons | any(contains("#12"))' "$OUT" >/dev/null && fail "$role: '$(jq -c .reasons "$OUT")'"
done
gh_recent </dev/null

case_ "the Customer lead can't merge, approve or move its proposal"
for args in "accept customer 12" "approve customer 12" "move customer 12 Approved" "move customer 12 Done"; do
  # shellcheck disable=SC2086 # one argument per word
  run board demo $args
  failed "customer: $args"
done
run board demo accept you 12
failed "accepting a proposal"
grep -q "only a docs PR In review can be accepted" "$ERR" || fail "accepting a proposal: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"

case_ "once the docs page is there, the audit writes the docs it outlines, and done pitches come back"
rm "$WORK/no-docs"
gh_items <<'ITEMS'
Done 12 Docs proposal: where the user docs live
Done 16 A pitch just accepted
ITEMS
docs_pr 12
accepted 16
run board demo triggers customer
same "reasons" '["pitch #16 is done: check the docs cover what it shipped","weekly docs audit: check the docs as a whole"]' \
  "$(jq -c .reasons "$OUT")"
jq -n '{node_id: "PR_15", number: 15, state: "open", pull_request: {}, body: "<!-- a-team:customer -->"}' >"$ISSUE"
jq '.data.organization.projectV2.field.options += [{id: "OPT_review", name: "In review"}]' "$META" >"$META.new" &&
  mv "$META.new" "$META"
run board demo add customer 15 "In review"
same "docs PR" 0 "$STATUS"
run board demo covered customer 16
run board demo audited customer
run board demo triggers customer
same "written" '[]' "$(jq -c .reasons "$OUT")"

case_ "a Customer lead with no docs page configured starts no run"
jq 'del(.docs)' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
rm -f "$AUDITED"
run board demo triggers customer
same "exit" 0 "$STATUS"
same "reasons" '[]' "$(jq -c .reasons "$OUT")"
jq '.docs = "docs/index.md"' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
# `customer_issue <n> <body>`: #<n> is an open issue with <body>.
customer_issue() { gh_child "$1" - - "$2" && jq '.state = "open"' "$ISSUE" >"$ISSUE.new" && mv "$ISSUE.new" "$ISSUE"; }

case_ "the Customer lead files a feature nobody can find as an Idea, labelled as a discovery"
jq '.wip = {pitched: 2, exploring: 4, ideas: 2}' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
gh_items <<'ITEMS'
Idea 41 I can't find how to pause one role
ITEMS
jq '.data.organization.projectV2.field.options += [{id: "OPT_idea", name: "Idea"}]' "$META" >"$META.new" && mv "$META.new" "$META"
customer_issue 41 "**Evidence**: the guide's Dashboard page doesn't say.

<!-- a-team:customer -->"
run board demo add customer 41 Idea
same "exit" 0 "$STATUS"
grep -q 'issues/41/labels -f labels\[\]=a-team:idea' "$WRITES" || fail "no discovery label in '$(cat "$WRITES")'"
grep -q 'a-team:customer' "$WRITES" && fail "labelled as the docs PR: '$(cat "$WRITES")'"
grep -q 'item=PVTI_41 .*option=OPT_idea' "$WRITES" || fail "no move to Idea in '$(cat "$WRITES")'"

case_ "the Customer lead adds nothing past Idea but its docs proposal, and no issue it didn't open"
for to in Exploring Ready; do
  run board demo add customer 41 "$to"
  failed "add as $to"
  grep -qF "customer may not add items as '$to'" "$ERR" || fail "add as $to: '$(cat "$ERR")'"
done
run board demo add customer 41 Pitched
failed "an issue as Pitched"
grep -qF "customer may only put its own open draft docs proposal in Pitched (#41 isn't one)" "$ERR" ||
  fail "an issue as Pitched: '$(cat "$ERR")'"
customer_issue 41 "Seen in the wild.

<!-- a-team:lead -->"
run board demo add customer 41 Idea
failed "someone else's issue"
one_line "someone else's issue"
grep -qF "customer may only add an open issue it opened as an Idea (#41 isn't one)" "$ERR" ||
  fail "someone else's issue: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"

case_ "the Customer lead's Ideas count towards wip.ideas, and a full queue refuses one more"
gh_items <<'ITEMS'
Idea 41 I can't find how to pause one role
Idea 42 I can't tell which team a card is from
Idea 43 I can't find where the logs are
ITEMS
edit_item 41 '.labels.nodes = [{name: "a-team:idea"}]'
edit_item 42 '.labels.nodes = [{name: "a-team:idea"}]'
customer_issue 43 "<!-- a-team:customer -->"
run board demo add customer 43 Idea
failed "full queue"
one_line "full queue"
grep -qF "2 discovered Ideas are waiting for triage (wip.ideas is 2): leave #43 off the board" "$ERR" ||
  fail "full queue: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"
A_TEAM_STATE="$WORK/state" run board demo lead-next
same "exit" 0 "$STATUS"
same "lead-next" '{"turn":"none","room":null}' "$(jq -c '{turn, room}' "$OUT")"

case_ "the Lead pitches a Customer lead's Idea once it's ranked, like its own discoveries"
gh_items 41 <<'ITEMS'
Idea 41 I can't find how to pause one role
Idea 42 I can't tell which team a card is from
ITEMS
edit_item 41 '.labels.nodes = [{name: "a-team:idea"}]'
edit_item 42 '.labels.nodes = [{name: "a-team:idea"}]'
customer_issue 41 "<!-- a-team:customer -->"
A_TEAM_STATE="$WORK/state" run board demo lead-next
same "exit" 0 "$STATUS"
same "lead-next" '{"turn":"pitch","item":41}' "$(jq -c '{turn, item: .item.number}' "$OUT")"
[ -e "$WORK/state/demo/lead/card" ] && fail "outside a run: recorded a card"

case_ "a Lead run started across the board records the Idea it pitches, and keeps a card it was started for"
rm -f "$WORK/state/demo/lead/card"
A_TEAM_RUN_TEAM=demo A_TEAM_STATE="$WORK/state" run board --dry-run demo lead-next
[ -e "$WORK/state/demo/lead/card" ] && fail "dry run: recorded a card"
A_TEAM_RUN_TEAM=demo A_TEAM_STATE="$WORK/state" run board demo lead-next
same "card" '{"number":41}' "$(cat "$WORK/state/demo/lead/card")"
echo '{"number":7}' >"$WORK/state/demo/lead/card"
A_TEAM_RUN_TEAM=demo A_TEAM_STATE="$WORK/state" run board demo lead-next
same "kept" '{"number":7}' "$(cat "$WORK/state/demo/lead/card")"
rm -f "$WORK/state/demo/lead/card"
run board demo waiting
same "triage" '[42]' "$(jq -c 'map(select(.reason == "waiting to be ranked") | .number)' "$OUT")"

# --- the Lead's recommended rank -------------------------------------------------------------
case_ "recommend labels an Idea with its rank, drops the others, and posts the case as the Lead"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" },
  "project": { "owner": "mentaldesk", "number": 1 }, "wip": { "pitched": 2, "exploring": 4, "ideas": 4 } }
JSON
gh_items <<'ITEMS'
Idea 41 Anything that goes wrong before the dashboard opens dumps a stack trace
Ready 42 A task
ITEMS
edit_item 41 '.labels.nodes = [{name: "rank:low"}]'
echo 'theme 3, small · a stack trace is the only error path there is' >"$WORK/case"
run board demo recommend lead 41 High "$WORK/case"
same "exit" 0 "$STATUS"
grep -q 'label create rank:high -R mentaldesk/demo --color d4323c' "$WRITES" || fail "label not created: '$(cat "$WRITES")'"
grep -q 'issue edit 41 -R mentaldesk/demo --add-label rank:high --remove-label rank:low' "$WRITES" ||
  fail "labels: '$(cat "$WRITES")'"
same "posted" 'Recommended: **High** · theme 3, small · a stack trace is the only error path there is

<!-- a-team:lead -->' "$(cat "$POSTED")"

case_ "recommend leaves a rank label that's already there alone"
echo '[{"name": "rank:high"}]' >"$EMPTY"
: >"$WRITES"
run board demo recommend lead 41 high "$WORK/case"
echo '[]' >"$EMPTY"
same "exit" 0 "$STATUS"
same "created" 0 "$(grep -c 'label create' "$WRITES")"

case_ "only the Lead recommends, only on an Idea, and only with a one-line case"
: >"$WRITES"
: >"$POSTED"
for role in you dev customer; do
  run board demo recommend "$role" 41 High "$WORK/case"
  failed "$role"
  grep -qF "$role may not recommend a rank" "$ERR" || fail "$role: '$(cat "$ERR")'"
done
run board demo recommend lead 42 High "$WORK/case"
failed "a task"
grep -qF "recommend is only for Ideas (#42 is in 'Ready')" "$ERR" || fail "a task: '$(cat "$ERR")'"
run board demo recommend lead 41 Soon "$WORK/case"
failed "unknown rank"
printf 'theme 3\nand a second line\n' >"$WORK/long-case"
run board demo recommend lead 41 High "$WORK/long-case"
failed "two lines"
grep -qF "the case for #41 is one line" "$ERR" || fail "two lines: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"
same "posted" "" "$(cat "$POSTED")"

case_ "the Lead's recommendation breaks a tie inside a Priority band, and says it did"
gh_items 41 42 43 <<'ITEMS'
Idea 41 Ranked High, recommended Low
Idea 42 Ranked High, recommended High
Idea 43 Ranked High, not recommended
ITEMS
edit_item 41 '.labels.nodes = [{name: "rank:low"}]'
edit_item 42 '.labels.nodes = [{name: "rank:high"}]'
A_TEAM_STATE="$WORK/state" run board demo lead-next
same "exit" 0 "$STATUS"
same "lead-next" '{"turn":"pitch","item":42,"actedOn":"High"}' "$(jq -c '{turn, item: .item.number, actedOn: .item.actedOn}' "$OUT")"

case_ "a recommendation the next Idea shares decided nothing"
edit_item 41 '.labels.nodes = [{name: "rank:high"}]'
A_TEAM_STATE="$WORK/state" run board demo lead-next
same "exit" 0 "$STATUS"
same "acted on" 'null' "$(jq -c '.item.actedOn' "$OUT")"

case_ "a stakeholder's Priority wins across bands, whatever the Lead recommends"
gh_items 43 <<'ITEMS'
Idea 41 Unranked, recommended Urgent
Idea 43 Ranked High, not recommended
ITEMS
edit_item 41 '.labels.nodes = [{name: "rank:urgent"}]'
echo '{"body": "Mine.", "state": "open"}' >"$ISSUE"
A_TEAM_STATE="$WORK/state" run board demo lead-next
same "exit" 0 "$STATUS"
same "item" 43 "$(jq -c '.item.number' "$OUT")"

case_ "the stakeholder's own unranked Ideas are pitched in the order the Lead recommends"
gh_items <<'ITEMS'
Idea 51 Not recommended
Idea 52 Recommended Low
Idea 53 Recommended Medium
ITEMS
edit_item 52 '.labels.nodes = [{name: "rank:low"}]'
edit_item 53 '.labels.nodes = [{name: "rank:medium"}]'
echo '{"body": "Mine.", "state": "open"}' >"$ISSUE"
A_TEAM_STATE="$WORK/state" run board demo lead-next
same "exit" 0 "$STATUS"
same "lead-next" '{"item":53,"actedOn":"Medium"}' "$(jq -c '{item: .item.number, actedOn: .item.actedOn}' "$OUT")"

case_ "an unranked Idea the team found is never pitched, however high the Lead recommends it"
gh_items <<'ITEMS'
Idea 61 Found by the Lead
Idea 62 The stakeholder's own
ITEMS
edit_item 61 '.labels.nodes = [{name: "a-team:idea"}, {name: "rank:urgent"}]'
echo '{"body": "Mine.", "state": "open"}' >"$ISSUE"
A_TEAM_STATE="$WORK/state" run board demo lead-next
same "exit" 0 "$STATUS"
same "item" 62 "$(jq -c '.item.number' "$OUT")"

case_ "waiting gives a recommended Idea its rank and the Lead's case, in the call it already makes"
gh_items <<'ITEMS'
Pitched 106 Both gates are mine
Idea 6 Recommended
Idea 7 Not recommended
ITEMS
edit_item 6 '.labels.nodes = [{name: "rank:high"}]'
gh_talk <<TALK
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
6 body ${TODAY}T07:00:00Z reviewer An idea
6 comment ${TODAY}T07:10:00Z demo-app[bot] Recommended: **Low** · theme 5, large · an older case\n\n<!-- a-team:lead -->
6 comment ${TODAY}T07:20:00Z demo-app[bot] Recommended: **High** · theme 2, small · clears the queue\n\n<!-- a-team:lead -->
6 comment ${TODAY}T07:30:00Z reviewer Recommended: **Low** · not the Lead's
TALK
run board demo waiting
same "exit" 0 "$STATUS"
same "ideas" '[[6,"High","theme 2, small · clears the queue"],[7,null,"waiting to be ranked"]]' \
  "$(jq -c 'map(select(.status == "Idea") | [.number, .recommendation, .reason])' "$OUT")"
same "api calls" 3 "$(grep -c '' <"$CALLS")"

case_ "setup creates the rank labels in the Priority colours"
run board --dry-run demo setup
same "exit" 0 "$STATUS"
for label in rank:urgent rank:high rank:medium rank:low; do
  grep -q "created label $label" "$OUT" || fail "setup: no $label in '$(cat "$OUT")'"
done

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
history_db() { (source "$ROOT/scripts/common.sh" && history_sql "$@"); }
# runs_recorded: "<role> <item or -> <ended?> <cost or -> <outcome or ->" for each run and item it was for.
runs_recorded() {
  history_db -separator ' ' "SELECT role, COALESCE(item, '-'), ended IS NOT NULL, COALESCE(cost, '-'),
    COALESCE(outcome, '-') FROM runs LEFT JOIN run_items ON run_items.run = runs.id ORDER BY runs.id, item;"
}

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
history_db "INSERT INTO runs (team, role, pid, log, started) VALUES ('demo', 'dev', $RUN_PID, 'a.jsonl', '2026-10-07T09:00:00Z');"
run stop demo dev
wait "$RUN_PID"
same "exit" 0 "$STATUS"
same "signals" 1 "$(grep -c TERM "$SIGNALS")"
same "recorded" "dev - 0 - stopped" "$(runs_recorded)"
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
if [ "$1" = token ] && [ "${NOT_INSTALLED:-}" = 2 ]; then
  echo "a-team token: other" >&2
  exit 1
fi
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

case_ "a dispatcher pass skips a team with no app key, says it stopped only on the second pass, and runs the others"
jq 'del(.app) | .dispatch.hold = []' "$TEAM" >"$CONFIG/teams/bare.json"
: >"$A_TEAM_STATE/dispatch.log"
rm -f "$A_TEAM_STATE"/demo/*/dry-*
A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/dispatch.sh" --dry-run
grep -q 'bare lead\|bare dev' "$A_TEAM_STATE/dispatch.log" && fail "no app: bare was started"
grep -q 'demo lead: would start' "$A_TEAM_STATE/dispatch.log" || fail "no app: demo wasn't started"
grep -q 'bare: stopped' "$A_TEAM_STATE/dispatch.log" && fail "no app: stopped after one pass"
A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/status.sh" >"$OUT"
grep -q '^bare: stopped' "$OUT" && fail "no app: status says stopped after one pass"
A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/dispatch.sh" --dry-run
grep -q 'bare lead\|bare dev' "$A_TEAM_STATE/dispatch.log" && fail "no app: bare was started on the second pass"
same "stopped once" 1 "$(grep -c 'bare: stopped: no GitHub App' "$A_TEAM_STATE/dispatch.log")"
A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/status.sh" >"$OUT"
grep -q '^bare: stopped: no GitHub App: run a-team app create bare, then install it$' "$OUT" ||
  fail "no app: status says '$(cat "$OUT")'"
grep -q '^demo: stopped' "$OUT" && fail "no app: demo shows as stopped"
A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/dispatch.sh" --dry-run
same "same reason, still once" 1 "$(grep -c 'bare: stopped' "$A_TEAM_STATE/dispatch.log")"
rm "$CONFIG/teams/bare.json"

case_ "a team whose check fails once and then passes says nothing"
: >"$A_TEAM_STATE/dispatch.log"
NOT_INSTALLED=1 A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/dispatch.sh" --dry-run
grep -q 'demo lead' "$A_TEAM_STATE/dispatch.log" && fail "blip: demo was started"
A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/dispatch.sh" --dry-run
grep -q 'demo: ' "$A_TEAM_STATE/dispatch.log" && fail "blip: '$(cat "$A_TEAM_STATE/dispatch.log")'"
A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/dispatch.sh" --dry-run
NOT_INSTALLED=1 A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/dispatch.sh" --dry-run
grep -q 'demo: stopped' "$A_TEAM_STATE/dispatch.log" && fail "blip: two failures that weren't in a row"
A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/dispatch.sh" --dry-run

case_ "a team whose App isn't installed says it stopped, again when the reason changes, and running again once it is"
: >"$A_TEAM_STATE/dispatch.log"
NOT_INSTALLED=1 A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/dispatch.sh" --dry-run
NOT_INSTALLED=1 A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/dispatch.sh" --dry-run
grep -q "demo: stopped: app 7 isn't installed on mentaldesk/demo" "$A_TEAM_STATE/dispatch.log" ||
  fail "not installed: '$(cat "$A_TEAM_STATE/dispatch.log")'"
grep -q 'demo lead' "$A_TEAM_STATE/dispatch.log" && fail "not installed: demo was started"
A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/status.sh" >"$OUT"
grep -q "^demo: stopped: app 7 isn't installed" "$OUT" || fail "not installed: status says '$(cat "$OUT")'"
NOT_INSTALLED=2 A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/dispatch.sh" --dry-run
same "new reason" 1 "$(grep -c 'demo: stopped: other' "$A_TEAM_STATE/dispatch.log")"
same "stopped twice in all" 2 "$(grep -c 'demo: stopped' "$A_TEAM_STATE/dispatch.log")"
A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/dispatch.sh" --dry-run
same "running again" 1 "$(grep -c 'demo: running again$' "$A_TEAM_STATE/dispatch.log")"
A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/status.sh" >"$OUT"
grep -q '^demo: stopped' "$OUT" && fail "installed: demo still shows as stopped"
A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/dispatch.sh" --dry-run
same "running again once" 1 "$(grep -c 'demo: running again' "$A_TEAM_STATE/dispatch.log")"

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

case_ "pause, stop, resume and attach take the Customer lead only on a team that has it on"
for cmd in "pause demo customer" "stop demo customer" "resume demo customer" "attach demo customer"; do
  # shellcheck disable=SC2086 # one argument per word
  run $cmd
  failed "$cmd while off"
  grep -q "demo has no Customer lead" "$ERR" || fail "$cmd while off: '$(cat "$ERR")'"
done
jq '.roles.customer = true' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
run pause demo customer
same "exit" 0 "$STATUS"
same "hold" '["customer"]' "$(held)"
A_TEAM_CONFIG="$CONFIG" bash "$ROOT/scripts/status.sh" >"$OUT"
grep -q '^demo customer: never run$' "$OUT" || fail "status: '$(cat "$OUT")'"
run resume demo customer
same "hold" '[]' "$(held)"
jq '.roles.reviewer = true' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
for role in customer reviewer; do
  run pause demo "$role"
  same "exit with both on" 0 "$STATUS"
  same "hold with both on" "[\"$role\"]" "$(held)"
  run resume demo "$role"
  same "hold with both on" '[]' "$(held)"
  run attach demo "$role"
  grep -q "demo $role has no run with a session" "$ERR" || fail "attach $role with both on: '$(cat "$ERR")'"
done
jq 'del(.roles)' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
A_TEAM_CONFIG="$CONFIG" bash "$ROOT/scripts/status.sh" >"$OUT"
grep -q 'customer' "$OUT" && fail "status while off: '$(cat "$OUT")'"

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

# A dispatcher whose a-team answers `triggers dev` with $DEV_TRIGGERS and `triggers reviewer` with
# $REVIEWER_TRIGGERS, records each claim, and claims $CLAIMED. `task-prompt` is one line, and the Lead never has anything to do.
dev_dispatcher() {
  DISPATCH=$(mktemp -d "$WORK/dispatch.XXXXXX")
  mkdir -p "$DISPATCH/bin" "$DISPATCH/scripts"
  cp "$ROOT"/scripts/*.sh "$DISPATCH/scripts/"
  cp -R "$ROOT/settings" "$DISPATCH/"
  echo 0.1.7 >"$DISPATCH/VERSION"
  CLAIMS="$DISPATCH/claims" DEV_TRIGGERS="$DISPATCH/triggers.json" CLAIMED="$DISPATCH/claimed.json"
  CUSTOMER_TRIGGERS="$DISPATCH/customer.json" REVIEWER_TRIGGERS="$DISPATCH/reviewer.json"
  : >"$CLAIMS"
  echo null >"$CLAIMED"
  echo '{"reasons": [], "creative": false}' >"$CUSTOMER_TRIGGERS"
  echo '{"reasons": [], "creative": false, "tasks": [], "ready": null, "chores": []}' >"$REVIEWER_TRIGGERS"
  cat >"$DISPATCH/bin/a-team" <<SH
#!/usr/bin/env bash
case " \$* " in
  *" claim dev "*) echo "\$*" >>"$CLAIMS"; cat "$CLAIMED" ;;
  *" triggers dev"*) cat "$DEV_TRIGGERS" ;;
  *" triggers lead"*) echo '{"reasons": [], "creative": false}' ;;
  *" triggers customer"*) echo "\$*" >>"$CLAIMS"; cat "$CUSTOMER_TRIGGERS" ;;
  *" triggers reviewer"*) cat "$REVIEWER_TRIGGERS" ;;
  *" task-prompt "*) echo "Run one shift." ;;
  *" version "*) cat "\$(dirname "\$0")/../VERSION" ;;
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

case_ "a Reviewer run is for one waiting PR at a time, named in the log, and claims nothing"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "app": { "id": 7, "slug": "demo-app" }, "dispatch": { "enabled": true },
  "roles": { "reviewer": true } }
JSON
dev_dispatcher
jq -n '{reasons: [], creative: false, tasks: [], ready: null, chores: []}' >"$DEV_TRIGGERS"
jq -n '{reasons: ["PR #912 is green and waiting for its review", "PR #914 is green and waiting for its review"],
        creative: false, ready: 13, chores: [],
        tasks: [{number: 12, title: "Fix the pane", reasons: ["PR #912 is green and waiting for its review"]},
                {number: 14, title: "Name the column", reasons: ["PR #914 is green and waiting for its review"]}]}' \
  >"$REVIEWER_TRIGGERS"
dispatch_dev --dry-run
grep -q 'demo reviewer: would start: #12: PR #912 is green and waiting for its review$' "$A_TEAM_STATE/dispatch.log" ||
  fail "reviewer: '$(cat "$A_TEAM_STATE/dispatch.log")'"
same "one run" 1 "$(grep -c 'demo reviewer: would start' "$A_TEAM_STATE/dispatch.log")"
same "task" '{"number":12,"title":"Fix the pane"}' "$(cat "$A_TEAM_STATE/demo/reviewer/dry-task")"
same "claims" "" "$(cat "$CLAIMS")"
: >"$A_TEAM_STATE/dispatch.log"
dispatch_dev --dry-run
grep -q 'demo reviewer: would start: #14: PR #914 is green and waiting for its review$' "$A_TEAM_STATE/dispatch.log" ||
  fail "next pass: '$(cat "$A_TEAM_STATE/dispatch.log")'"
same "next task" '{"number":14,"title":"Name the column"}' "$(cat "$A_TEAM_STATE/demo/reviewer/dry-task")"
: >"$A_TEAM_STATE/dispatch.log"
dispatch_dev --dry-run
same "both tried" "" "$(cat "$A_TEAM_STATE/dispatch.log")"
same "claims" "" "$(cat "$CLAIMS")"

case_ "a task stopped for the Reviewer is passed over, and the next one gets its run"
dev_dispatcher
jq -n '{reasons: [], creative: false, tasks: [], ready: null, chores: []}' >"$DEV_TRIGGERS"
jq -n '{reasons: ["PR #912 is green and waiting for its review", "PR #914 is green and waiting for its review"],
        creative: false, ready: null, chores: [],
        tasks: [{number: 12, title: "Fix the pane", reasons: ["PR #912 is green and waiting for its review"]},
                {number: 14, title: "Name the column", reasons: ["PR #914 is green and waiting for its review"]}]}' \
  >"$REVIEWER_TRIGGERS"
A_TEAM_CONFIG="$CONFIG" bash "$DISPATCH/scripts/pause.sh" stop demo reviewer 12 >"$OUT"
[ -e "$A_TEAM_STATE/demo/reviewer/runs/12/held" ] || fail "stop: nothing held under reviewer/runs/12"
dispatch_dev --dry-run
grep -q 'demo reviewer: would start: #14: ' "$A_TEAM_STATE/dispatch.log" ||
  fail "held: '$(cat "$A_TEAM_STATE/dispatch.log")'"
grep -q '#12' "$A_TEAM_STATE/dispatch.log" && fail "held: #12 was started"
same "claims" "" "$(cat "$CLAIMS")"

case_ "a trigger check that fails once and then passes says nothing, and the role sits that pass out"
dev_dispatcher
jq -n '{reasons: [], creative: false, tasks: [], ready: null, chores: []}' >"$DEV_TRIGGERS"
cp "$CONFIG/teams/demo.json" "$CONFIG/teams/other.json"
FAILS="$DISPATCH/fails"
FAILING="  *\" demo triggers dev\"*) [ -s $FAILS ] && { cat $FAILS >&2; exit 1; }; cat $DEV_TRIGGERS ;;"
FAILING=$FAILING perl -i -pe 'print "$ENV{FAILING}\n" if /^  \*" triggers dev"\*\)/' "$DISPATCH/bin/a-team"
echo "gh: connection reset" >"$FAILS"
echo '{"number": 13, "title": "Something to start"}' >"$CLAIMED"
jq '.ready = 13 | .reasons = ["Ready task available (e.g. #13) and a free worktree"]' "$DEV_TRIGGERS" >"$DISPATCH/ready.json"
cp "$DISPATCH/ready.json" "$DEV_TRIGGERS"
dispatch_dev --dry-run
grep -q 'demo dev' "$A_TEAM_STATE/dispatch.log" && fail "blip: '$(cat "$A_TEAM_STATE/dispatch.log")'"
grep -q 'other dev: would start' "$A_TEAM_STATE/dispatch.log" || fail "blip: other wasn't started"
same "claims" "board --dry-run other claim dev" "$(cat "$CLAIMS")"
: >"$FAILS"
: >"$A_TEAM_STATE/dispatch.log"
jq '.ready = null | .reasons = []' "$DEV_TRIGGERS" >"$DEV_TRIGGERS.new" && mv "$DEV_TRIGGERS.new" "$DEV_TRIGGERS"
dispatch_dev --dry-run
same "after a blip" "" "$(cat "$A_TEAM_STATE/dispatch.log")"
echo "gh: connection reset" >"$FAILS"
dispatch_dev --dry-run
: >"$FAILS"
dispatch_dev --dry-run
same "two failures not in a row" "" "$(cat "$A_TEAM_STATE/dispatch.log")"

case_ "two failed trigger checks in a row say so once, again when the reason changes, and once working again"
echo "gh: connection reset" >"$FAILS"
dispatch_dev --dry-run
dispatch_dev --dry-run
dispatch_dev --dry-run
same "failed once" "demo dev: triggers failed: gh: connection reset" "$(cut -d' ' -f2- "$A_TEAM_STATE/dispatch.log")"
echo "gh: API rate limit exceeded" >"$FAILS"
dispatch_dev --dry-run
dispatch_dev --dry-run
same "new reason" 1 "$(grep -c 'demo dev: triggers failed: gh: API rate limit exceeded$' "$A_TEAM_STATE/dispatch.log")"
grep -q 'other \|demo lead\|demo customer' "$A_TEAM_STATE/dispatch.log" && fail "another role: '$(cat "$A_TEAM_STATE/dispatch.log")'"
: >"$FAILS"
dispatch_dev --dry-run
dispatch_dev --dry-run
same "working again" 1 "$(grep -c 'demo dev: triggers working again$' "$A_TEAM_STATE/dispatch.log")"
same "lines" 3 "$(grep -c . "$A_TEAM_STATE/dispatch.log")"
rm "$CONFIG/teams/other.json"

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

case_ "a workdir that isn't there starts nothing, and says so rather than recording a start"
STATE_DIR="$A_TEAM_STATE/demo/dev"
rm -rf "$STATE_DIR"
: >"$A_TEAM_STATE/dispatch.log"
: >"$LAUNCHED"
jq --arg w "$WORK/nope" '.workdir = $w' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
echo '{"number": 14, "title": "Something else"}' >"$CLAIMED"
dispatch_dev
grep -qF "demo dev: cannot start: workdir $WORK/nope: no such directory" "$A_TEAM_STATE/dispatch.log" ||
  fail "cannot start: '$(cat "$A_TEAM_STATE/dispatch.log")'"
grep -q 'demo dev: started' "$A_TEAM_STATE/dispatch.log" && fail "logged a start: '$(cat "$A_TEAM_STATE/dispatch.log")'"
for file in last-start last-reasons pid latest.jsonl; do
  [ -e "$STATE_DIR/$file" ] || [ -L "$STATE_DIR/$file" ] && fail "wrote $file"
done
[ -s "$LAUNCHED" ] && fail "claude was started"
jq --arg w "$WORKDIR" '.workdir = $w' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"

case_ "only a team with the Customer lead on asks it for work, and its run gets its own rules too"
dev_dispatcher
jq -n '{reasons: [], creative: false, tasks: [], ready: null, chores: []}' >"$DEV_TRIGGERS"
jq -n '{reasons: ["pitch #5 is done: check the docs cover what it shipped"], creative: false}' >"$CUSTOMER_TRIGGERS"
printf '#!/usr/bin/env bash\n' >"$DISPATCH/bin/claude"
chmod +x "$DISPATCH/bin/claude"
dispatch_dev --dry-run
same "asked while off" "" "$(cat "$CLAIMS")"
jq '.roles.customer = true' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
dispatch_dev
same "asked" "board demo triggers customer --sweep" "$(cat "$CLAIMS")"
grep -qE 'demo customer: started [0-9]+ on 0\.1\.7: pitch #5 is done' "$A_TEAM_STATE/dispatch.log" ||
  fail "customer: '$(cat "$A_TEAM_STATE/dispatch.log")'"
[ -e "$A_TEAM_STATE/demo/customer/card" ] && fail "no card: recorded '$(cat "$A_TEAM_STATE/demo/customer/card")'"
SETTINGS="$A_TEAM_STATE/demo/customer/release/settings.json"
jq -e '.permissions.deny | index("Edit(**/*.cs)") and index("Bash(gh pr merge *)")' "$SETTINGS" >/dev/null ||
  fail "customer settings: '$(jq -c .permissions.deny "$SETTINGS")'"
jq -e '.permissions.allow | index("Bash(gh pr edit *)")' "$SETTINGS" >/dev/null ||
  fail "customer allow: '$(jq -c .permissions.allow "$SETTINGS")'"
jq -e '.permissions | (.allow | index("Bash(gh issue create *)")) and (.deny | index("Bash(gh issue create *)") | not)' \
  "$SETTINGS" >/dev/null ||
  fail "customer can't file an Idea: '$(jq -c .permissions "$SETTINGS")'"

case_ "an audit run that ends without recording the audit is retried later, not every pass"
for _ in $(seq 50); do kill -0 "$(cat "$A_TEAM_STATE/demo/customer/pid")" 2>/dev/null || break; sleep 0.1; done
jq -n '{reasons: ["weekly docs audit: check the docs as a whole"], creative: false}' >"$CUSTOMER_TRIGGERS"
: >"$A_TEAM_STATE/dispatch.log"
dispatch_dev --dry-run
dispatch_dev --dry-run
same "runs" 1 "$(grep -c 'demo customer: would start: weekly docs audit' "$A_TEAM_STATE/dispatch.log")"

case_ "a run for one card records it for Overseer, and the next run without one clears it"
jq -n '{reasons: ["stakeholder feedback on docs PR #9 (2025-09-19T09:00:00Z)"], creative: false, items: [9], card: 9}' \
  >"$CUSTOMER_TRIGGERS"
dispatch_dev --dry-run
same "card" '{"number":9}' "$(cat "$A_TEAM_STATE/demo/customer/dry-card")"
jq -n '{reasons: ["pitch #6 is done: check the docs cover what it shipped"], creative: false, items: [], card: null}' \
  >"$CUSTOMER_TRIGGERS"
dispatch_dev --dry-run
[ -e "$A_TEAM_STATE/demo/customer/dry-card" ] && fail "kept '$(cat "$A_TEAM_STATE/demo/customer/dry-card")'"
jq 'del(.roles)' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"

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
  *" version "*) cat "\$(dirname "\$0")/../VERSION" ;;
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
same "killed, until a pass sees it end" "dev 13 0 - killed" "$(runs_recorded | grep ' 13 ')"

case_ "a held Dev starts nothing new, and its runs go on"
jq '.dispatch.hold = ["dev"]' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
printf '30\n' >"$QUEUE"
jq -n '{reasons: [], creative: false, tasks: [], ready: 30, chores: []}' >"$DEV_TRIGGERS"
dispatch_dev
same "claims" 3 "$(grep -c '' "$CLAIMS")"
for n in 14 15 20; do alive "$n" || fail "#$n stopped when the Dev was held"; done
same "killed, once it ended, with no cost" "dev 13 1 - killed" "$(runs_recorded | grep ' 13 ')"
same "the others still running" "dev 14 0 - -
dev 15 0 - -
dev 20 0 - -" "$(runs_recorded | grep -v ' 13 ')"
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

case_ "a dry-run dispatch records no runs"
dev_dispatcher
jq '.project = {owner: "mentaldesk", number: 1}' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
jq -n '{reasons: [], creative: false, tasks: [], ready: 13, chores: []}' >"$DEV_TRIGGERS"
echo '{"number": 13, "title": "Something to start"}' >"$CLAIMED"
dispatch_dev --dry-run
grep -q 'would start' "$A_TEAM_STATE/dispatch.log" || fail "dry run: nothing would start"
[ -e "$A_TEAM_STATE/history.db" ] && fail "dry run: recorded '$(runs_recorded)'"

case_ "a run that ends with a result records how long it took, its cost and whether it erred"
# claude waits for $DISPATCH/finish, then prints $DISPATCH/result, if there is one, as its log's last line.
cat >"$DISPATCH/bin/claude" <<SH
#!/usr/bin/env bash
while [ ! -e "$DISPATCH/finish" ]; do sleep 0.1; done
cat "$DISPATCH/result" 2>/dev/null
SH
chmod +x "$DISPATCH/bin/claude"
run_gone() { for _ in $(seq 50); do kill -0 "$(cat "$A_TEAM_STATE/demo/dev/runs/$1/pid")" 2>/dev/null || return; sleep 0.1; done; }
dispatch_dev
same "running" "dev 13 0 - -" "$(runs_recorded)"
A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board demo history 13 >"$OUT"
same "history while running" '{"who":"dev","what":"run","run":{"ended":null,"cost":null,"outcome":null}}' \
  "$(jq -c '.events[0] | del(.at)' "$OUT")"
echo '{"type":"result","subtype":"success","is_error":false,"total_cost_usd":4.12}' >"$DISPATCH/result"
touch "$DISPATCH/finish"
run_gone 13
jq -n '{reasons: [], creative: false, tasks: [], ready: null, chores: []}' >"$DEV_TRIGGERS"
dispatch_dev
same "finished" "dev 13 1 4.12 -" "$(runs_recorded)"

case_ "a retried run is a second line, and one that dies without a result has no cost"
rm "$DISPATCH/result"
jq -n '{reasons: [], creative: false, ready: null, chores: [], tasks: [{number: 13, title: "Something to start", reasons: ["CI failed on PR #913 at deadbee"]}]}' >"$DEV_TRIGGERS"
dispatch_dev
run_gone 13
jq -n '{reasons: [], creative: false, tasks: [], ready: null, chores: []}' >"$DEV_TRIGGERS"
dispatch_dev
same "two runs" "dev 13 1 4.12 -
dev 13 1 - died" "$(runs_recorded)"
A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board demo history 13 >"$OUT"
same "history" "died -" "$(jq -r '[.events[] | .run.outcome // "-"] | join(" ")' "$OUT")"

case_ "an errored run says so"
echo '{"type":"result","subtype":"error_during_execution","is_error":true,"total_cost_usd":0.5}' >"$DISPATCH/result"
jq -n '{reasons: [], creative: false, ready: null, chores: [], tasks: [{number: 14, title: "Another", reasons: ["CI failed on PR #914 at deadbee"]}]}' >"$DEV_TRIGGERS"
dispatch_dev
run_gone 14
jq -n '{reasons: [], creative: false, tasks: [], ready: null, chores: []}' >"$DEV_TRIGGERS"
dispatch_dev
same "errored" "dev 14 1 0.5 error" "$(runs_recorded | grep ' 14 ')"

case_ "a Lead run is recorded on each item it was started for, and a discovery run on none"
sed -i.bak "s|^  \*\" triggers lead\"\*).*|  *\" triggers lead\"*) cat \"$DISPATCH/lead.json\" ;;|" "$DISPATCH/bin/a-team"
jq -n '{reasons: ["stakeholder feedback on #7 (2026-10-07T09:00:00Z)", "#9 was approved: break it down"], creative: false, items: [7, 9]}' \
  >"$DISPATCH/lead.json"
history_db "DELETE FROM runs; DELETE FROM run_items;"
rm "$DISPATCH/result"
dispatch_dev
for _ in $(seq 50); do kill -0 "$(cat "$A_TEAM_STATE/demo/lead/pid")" 2>/dev/null || break; sleep 0.1; done
jq -n '{reasons: [], creative: true, items: []}' >"$DISPATCH/lead.json"
jq '.dispatch.creativeEvery = 0' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
dispatch_dev
same "lead runs" "lead 7 1 - died
lead 9 1 - died
lead - 0 - -" "$(runs_recorded | sed -n '1,3p')"
for _ in $(seq 50); do kill -0 "$(cat "$A_TEAM_STATE/demo/lead/pid")" 2>/dev/null || break; sleep 0.1; done
jq 'del(.dispatch.creativeEvery)' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"

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

case_ "stop <team> dev <task> ends only that task's run, and holds that task but not the role"
jq '.dispatch.hold = []' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
queued_dispatcher
devs 2
printf '13\n14\n' >"$QUEUE"
jq -n '{reasons: [], creative: false, tasks: [], ready: 13, chores: []}' >"$DEV_TRIGGERS"
dispatch_dev
launched 2
A_TEAM_CONFIG="$CONFIG" bash "$DISPATCH/scripts/pause.sh" stop demo dev 13 >"$OUT" 2>&1
for _ in $(seq 50); do alive 13 || break; sleep 0.1; done
alive 13 && fail "stop by task: #13 is still going"
alive 14 || fail "stop by task: #14 was stopped too"
[ -f "$A_TEAM_STATE/demo/dev/runs/13/held" ] || fail "stop by task: #13 isn't held"
same "hold" '[]' "$(jq -c .dispatch.hold "$TEAM")"
grep -q "stopped demo dev's run on #13" "$OUT" || fail "stop by task: '$(cat "$OUT")'"

case_ "a pass starts no run on a task stopped by task, and still starts a new one"
printf '15\n' >"$QUEUE"
: >"$LAUNCHED"
jq -n '{reasons: [], creative: false, tasks: [{number: 13, title: "Task 13", reasons: ["CI failed on PR #913"]}], ready: 15,
        chores: []}' >"$DEV_TRIGGERS"
dispatch_dev
launched 1
same "launched" "15" "$(cat "$LAUNCHED")"

case_ "resume <team> dev <task> lets that task start again"
A_TEAM_CONFIG="$CONFIG" bash "$DISPATCH/scripts/pause.sh" resume demo dev 13 >"$OUT" 2>&1
[ -f "$A_TEAM_STATE/demo/dev/runs/13/held" ] && fail "resume by task: #13 is still held"
devs 3
: >"$LAUNCHED"
dispatch_dev
launched 1
same "launched" "13" "$(cat "$LAUNCHED")"
stop_runs

case_ "stop by task needs a Dev task number"
A_TEAM_CONFIG="$CONFIG" bash "$DISPATCH/scripts/pause.sh" stop demo lead 13 >"$OUT" 2>&1 && fail "lead task: accepted"
A_TEAM_CONFIG="$CONFIG" bash "$DISPATCH/scripts/pause.sh" stop demo dev x >"$OUT" 2>&1 && fail "bad task: accepted"

# claude records where its a-team is, its settings, and its version before and after $GO appears.
pinned_claude() {
  SEEN=$(mktemp -d "$WORK/seen.XXXXXX") GO="$WORK/go.$RANDOM"
  cat >"$DISPATCH/bin/claude" <<SH
#!/usr/bin/env bash
seen="$SEEN/\${A_TEAM_RUN_TASK:-lead}"
while [ \$# -gt 0 ]; do [ "\$1" = --settings ] && echo "\$2" >"\$seen.settings"; shift; done
command -v a-team >"\$seen.bin"
a-team version >"\$seen.before"
while [ ! -e "$GO" ]; do sleep 0.1; done
a-team version >"\$seen.after"
echo "\${A_TEAM_RUN_TASK:-lead}" >>"$LAUNCHED"
exec sleep 60
SH
  chmod +x "$DISPATCH/bin/claude"
}
seen() { for _ in $(seq 50); do [ -s "$SEEN/$1" ] && break; sleep 0.1; done; cat "$SEEN/$1" 2>/dev/null; }

case_ "each Dev run keeps the release it started on, even once an upgrade removes it"
jq '.dispatch.hold = []' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
queued_dispatcher
pinned_claude
devs 2
jq -n '{reasons: [], creative: false, tasks: [], ready: 13, chores: []}' >"$DEV_TRIGGERS"
printf '13\n' >"$QUEUE"
dispatch_dev
seen 13.before >/dev/null
echo 0.1.8 >"$DISPATCH/VERSION"
printf '14\n' >"$QUEUE"
dispatch_dev
RUNS="$A_TEAM_STATE/demo/dev/runs"
same "#13 a-team" "$RUNS/13/release/bin/a-team" "$(seen 13.bin)"
same "#14 a-team" "$RUNS/14/release/bin/a-team" "$(seen 14.bin)"
same "#13 copy" 0.1.7 "$(cat "$RUNS/13/release/VERSION")"
same "#14 copy" 0.1.8 "$(cat "$RUNS/14/release/VERSION")"
same "#13 settings" "$RUNS/13/release/settings.json" "$(seen 13.settings)"
grep -qF "\"Edit(/$RUNS/13/release/**)\"" "$RUNS/13/release/settings.json" ||
  fail "settings: '$(grep Edit "$RUNS/13/release/settings.json")'"
grep -qF "$DISPATCH/" "$RUNS/13/release/settings.json" && fail "settings name the original release"
grep -qE "demo dev: started [0-9]+ on 0\.1\.7: #13: " "$A_TEAM_STATE/dispatch.log" || fail "log: '$(cat "$A_TEAM_STATE/dispatch.log")'"
grep -qE "demo dev: started [0-9]+ on 0\.1\.8: #14: " "$A_TEAM_STATE/dispatch.log" || fail "log: '$(cat "$A_TEAM_STATE/dispatch.log")'"
mv "$DISPATCH" "$DISPATCH.gone"
touch "$GO"
same "#13 after the upgrade" 0.1.7 "$(seen 13.after)"
same "#14 after the upgrade" 0.1.8 "$(seen 14.after)"
mv "$DISPATCH.gone" "$DISPATCH"

case_ "a finished run's copy is removed, and a live run's is left alone"
kill "$(cat "$RUNS/13/pid")"
for _ in $(seq 50); do alive 13 || break; sleep 0.1; done
dispatch_dev
[ -e "$RUNS/13/release" ] && fail "#13's copy is still there"
[ -x "$RUNS/14/release/bin/a-team" ] || fail "#14's copy was removed while it ran"
stop_runs

case_ "the Lead's run keeps its copy in the role's folder"
sed -i.bak 's|^  \*" triggers lead"\*).*|  *" triggers lead"*) echo '"'"'{"reasons": ["#232 was approved"], "creative": false}'"'"' ;;|' "$DISPATCH/bin/a-team"
jq -n '{reasons: [], creative: false, tasks: [], ready: null, chores: []}' >"$DEV_TRIGGERS"
dispatch_dev
same "lead a-team" "$A_TEAM_STATE/demo/lead/release/bin/a-team" "$(seen lead.bin)"
same "lead settings" "$A_TEAM_STATE/demo/lead/release/settings.json" "$(seen lead.settings)"
kill "$(cat "$A_TEAM_STATE/demo/lead/pid")"

case_ "a dry run copies no release"
dev_dispatcher
jq -n '{reasons: [], creative: false, tasks: [], ready: 13, chores: []}' >"$DEV_TRIGGERS"
echo '{"number": 13, "title": "Something to start"}' >"$CLAIMED"
dispatch_dev --dry-run
grep -q 'demo dev: would start' "$A_TEAM_STATE/dispatch.log" || fail "dry run: '$(cat "$A_TEAM_STATE/dispatch.log")'"
same "copies" "" "$(find "$A_TEAM_STATE" -name release)"
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

case_ "attach <team> dev <task> stops and holds only that run while you resume its session, then lets it go"
run resume demo dev
for n in 21 22; do
  mkdir -p "$A_TEAM_STATE/demo/dev/runs/$n"
  bash -c "trap 'echo $n >>\"$SIGNALS\"; exit' TERM; while :; do sleep 0.1; done" &
  echo $! >"$A_TEAM_STATE/demo/dev/runs/$n/pid"
done
: >"$SIGNALS"
echo '{"type":"system","subtype":"init","session_id":"sess-21"}' >"$A_TEAM_STATE/demo/dev/runs/21/latest.jsonl"
fake_claude
run attach demo dev 21
same "exit" 0 "$STATUS"
same "resumed" "args: --resume sess-21" "$(sed -n 1p "$RESUMED")"
same "role not held" 'hold: []' "$(sed -n 3p "$RESUMED")"
same "stopped" "21" "$(cat "$SIGNALS")"
kill -0 "$(cat "$A_TEAM_STATE/demo/dev/runs/22/pid")" 2>/dev/null || fail "attach by task: #22 was stopped"
[ -f "$A_TEAM_STATE/demo/dev/runs/21/held" ] && fail "attach by task: #21 is still held"
kill "$(cat "$A_TEAM_STATE/demo/dev/runs/22/pid")" 2>/dev/null

case_ "attach is in the usage text"
run help
grep -q '^  attach \[--dry-run\] <team> <role>' "$OUT" || fail "usage: no attach line"
unset A_TEAM_STATE

# --- a-team vision ---------------------------------------------------------------------------
# A claude that records its arguments and where it ran, one per line, for `a-team vision`.
VISION_BIN=$(mktemp -d "$WORK/vision.XXXXXX")
cat >"$VISION_BIN/claude" <<SH
#!/usr/bin/env bash
pwd -P >"$VISION_BIN/asked"
printf '%s\n' "\$@" >>"$VISION_BIN/asked"
SH
chmod +x "$VISION_BIN/claude"
VISION_WORK=$(mktemp -d "$WORK/visionwork.XXXXXX")
mkdir -p "$VISION_WORK/main"
# vision_with <a-team>: runs that a-team's `vision demo` with the recording claude, from no terminal.
vision_with() {
  A_TEAM_CONFIG="$CONFIG" PATH="$VISION_BIN:$PATH" "$1" vision demo >"$OUT" 2>"$ERR" </dev/null
  STATUS=$?
}

case_ "vision hands the team's checkout to claude, briefed with the team's repo, vision and workdir"
fixture <<JSON
{ "repo": "mentaldesk/demo", "vision": "docs/why.md", "workdir": "$VISION_WORK" }
JSON
vision_with "$A_TEAM"
same "exit" 0 "$STATUS"
same "cwd" "$(cd "$VISION_WORK/main" && pwd -P)" "$(sed -n 1p "$VISION_BIN/asked")"
same "workdir" "--add-dir $VISION_WORK" "$(sed -n 2,3p "$VISION_BIN/asked" | paste -sd ' ' -)"
same "first message" "Write demo's vision with me." "$(tail -n 1 "$VISION_BIN/asked")"
grep -q '^--append-system-prompt$' "$VISION_BIN/asked" || fail "no brief: '$(cat "$VISION_BIN/asked")'"
grep -q 'Interview me, the stakeholder of demo (mentaldesk/demo)' "$VISION_BIN/asked" || fail "brief: no team or repo"
grep -q 'Write the vision to `docs/why.md`' "$VISION_BIN/asked" || fail "brief: no vision path"
grep -q "Run \`$A_TEAM board demo add lead <pr> Pitched\`" "$VISION_BIN/asked" || fail "brief: no board command"
grep -q '{{' "$VISION_BIN/asked" && fail "brief: a placeholder left in '$(grep '{{' "$VISION_BIN/asked")'"

case_ "vision reads its brief from an installed release, laid out as release.yml stages it"
STAGE=$(mktemp -d "$WORK/stage.XXXXXX")
staging=$(grep -E '^ +cp -R .* "\$stage/"$' "$ROOT/.github/workflows/release.yml" | sed -e 's/^ *cp -R //' -e 's| "\$stage/"$||')
[ -n "$staging" ] || fail "release.yml: no cp -R line staging the release"
(cd "$ROOT" && read -ra staged <<<"$staging" && cp -R "${staged[@]}" "$STAGE/")
rm -f "$VISION_BIN/asked"
vision_with "$STAGE/bin/a-team"
same "installed exit" 0 "$STATUS"
grep -q 'Interview me, the stakeholder of demo' "$VISION_BIN/asked" 2>/dev/null || fail "installed: no brief: '$(cat "$ERR")'"

case_ "vision --dry-run says what it would run, and runs nothing"
rm -f "$VISION_BIN/asked"
A_TEAM_CONFIG="$CONFIG" PATH="$VISION_BIN:$PATH" "$A_TEAM" vision --dry-run demo >"$OUT" 2>"$ERR"
same "dry run" "(dry run) would run claude in $VISION_WORK/main, briefed by $ROOT/tasks/vision.md" "$(cat "$OUT")"
[ -f "$VISION_BIN/asked" ] && fail "dry run: claude ran"

case_ "vision without a checkout names the clone command, and runs nothing"
fixture <<JSON
{ "repo": "mentaldesk/demo", "workdir": "$VISION_WORK/elsewhere" }
JSON
vision_with "$A_TEAM"
failed "no checkout"
same "no checkout" "a-team vision: $VISION_WORK/elsewhere/main isn't there: gh repo clone mentaldesk/demo $VISION_WORK/elsewhere/main" "$(cat "$ERR")"
[ -f "$VISION_BIN/asked" ] && fail "no checkout: claude ran"

case_ "vision is in the usage text"
run help
grep -q '^  vision \[--dry-run\] <team>' "$OUT" || fail "usage: no vision line"

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
# by REPO_GONE, NO_VISION, LEAD_VISION, NO_PROJECT, NO_FIELD, and the options in meta.json and labels in labels.json.
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
  *" repos/mentaldesk/demo/contents/"*) [ -z "\${NO_VISION:-}" ] || { echo "gh: Not Found (HTTP 404)" >&2; exit 1; }
    [ -z "\${LEAD_VISION:-}" ] || printf '# Vision\n\nWho it is for.\n\n<!-- a-team:lead -->\n'; exit 0 ;;
  *" repos/mentaldesk/demo "*) [ -z "\${REPO_GONE:-}" ] || { echo "gh: Not Found (HTTP 404)" >&2; exit 1; }; exit 0 ;;
  *"/actions/workflows/release.yml "*) [ -z "\${NO_RELEASE_WORKFLOW:-}" ] || { echo "gh: Not Found (HTTP 404)" >&2; exit 1; }; exit 0 ;;
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

case_ "a missing or Lead-drafted vision, or missing labels, are problems the team can still run with, so check exits 2"
NO_VISION=1 checked
says 2 "vision    docs/vision.md isn't in mentaldesk/demo yet: the Lead will draft one and open it as a draft PR"
LEAD_VISION=1 checked
says 2 "vision    drafted by the Lead: Write the vision with me replaces it"
board_labels pitch a-team:dev blocked
checked
says 2 "labels    no 'a-team:idea' label, so the Lead can't flag the ideas it finds for you
labels    no 'a-team:skipped' label, so the Lead can't pass over an idea, and keeps coming back to it
labels    no 'a-team:displaced' label, so the Lead tells you every time it bumps a pitch out of Pitched, not just the first"
board_labels pitch a-team:dev a-team:idea a-team:skipped a-team:displaced blocked

case_ "with a Customer lead, a docs page that's missing or not named is a problem the team can still run with"
jq '.roles.customer = true' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
board_labels pitch a-team:dev a-team:idea a-team:skipped a-team:displaced blocked a-team:customer
checked
says 2 "docs      demo.json names no docs page, so the Customer lead has nothing to keep right"
jq '.docs = "docs/index.md"' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
checked
says 0 ""
NO_VISION=1 checked
says 2 "vision    docs/vision.md isn't in mentaldesk/demo yet: the Lead will draft one and open it as a draft PR
docs      docs/index.md isn't in mentaldesk/demo yet: the Customer lead will propose the docs as a draft PR"
jq 'del(.roles, .docs)' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
board_labels pitch a-team:dev a-team:idea a-team:skipped a-team:displaced blocked
NO_VISION=1 checked
says 2 "vision    docs/vision.md isn't in mentaldesk/demo yet: the Lead will draft one and open it as a draft PR"

case_ "a release setting check can't act on is a problem the team can still run with"
jq '.release = "continuous"' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
checked
says 0 ""
NO_RELEASE_WORKFLOW=1 checked
says 2 "release   release is continuous, but mentaldesk/demo has no release.yml workflow for it to run"
jq '.release = "weekly"' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
checked
says 2 "release   release is 'weekly': expected never, daily or continuous"
jq 'del(.release)' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"

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

case_ "run brings the product checkout up to date before it prints the brief"
try_fixture
app_fixture
jq --arg c "$CHECKOUT" '.checkout = $c' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
cached ghs_cached 3600
land main
PATH="$APP_BIN/board:$PATH" run run demo lead
same "exit" 0 "$STATUS"
same "checkout" "$TRY_LANDED" "$(git -C "$CHECKOUT" rev-parse HEAD)"
grep -q '^## Product checkout' "$OUT" && fail "up to date: told to read around the checkout"

case_ "run says to read the default branch when the checkout can't be fast-forwarded to it"
git -C "$CHECKOUT" -c user.email=test@example.com -c user.name=Test commit -q --allow-empty -m local
land main
PATH="$APP_BIN/board:$PATH" run run demo lead
same "exit" 0 "$STATUS"
grep -q "^$CHECKOUT could not be brought up to date with origin/main, so read the product repo from origin/main" "$OUT" ||
  fail "diverged: '$(cat "$OUT")'"

case_ "run starts the Lead and the Dev on a team with the Customer lead turned on"
jq '.roles.customer = true' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
for role in lead dev; do
  PATH="$APP_BIN/board:$PATH" run run demo "$role"
  same "$role exit" 0 "$STATUS"
  grep -q "turned on" "$ERR" && fail "$role: '$(cat "$ERR")'"
done

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

# History: what `a-team board` did to each item, in $A_TEAM_STATE/history.db.
history_of() {
  A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board demo history "$1" | jq -r '.events[] | "\(.who) \(.what)"'
}
recorded() {
  same "$1 exit" 0 "$STATUS"
  same "$1 history" "$3" "$(history_of "$2")"
  rm -f "$A_TEAM_STATE/history.db"
}
unrecorded() {
  [ ! -e "$A_TEAM_STATE/history.db" ] || fail "$1: recorded '$(sqlite3 "$A_TEAM_STATE/history.db" 'SELECT * FROM events')'"
}

export A_TEAM_STATE
A_TEAM_STATE=$(mktemp -d "$WORK/state.XXXXXX")
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 },
  "wip": { "worktrees": 2 } }
JSON

case_ "a card with nothing recorded reads as empty, with no record file to read"
run board demo history 7
same "exit" 0 "$STATUS"
same "empty" '{"since":null,"events":[]}' "$(jq -c . "$OUT")"
unrecorded "history"

case_ "each move the board makes is one event, saying who and from where to where"
gh_items <<'ITEMS'
Ready 12 A task
Pitched 7 A pitch
In_review 8 A task in front of me
Idea 6 An idea
ITEMS
claimable
jq '.data.organization.projectV2.field.options += [{id: "OPT_review", name: "In review"}]' "$META" >"$META.new" && mv "$META.new" "$META"
run board demo move dev 12 "In progress"
recorded "move" 12 "dev Ready → In progress"
run board demo claim dev 12
recorded "claim" 12 "dev Ready → In progress"
run board demo approve you 7
recorded "approve" 7 "you Pitched → Approved"
run board demo priority you 6 High
recorded "priority" 6 "you Priority set to High"
run board demo priority you 6 none
recorded "priority cleared" 6 "you Priority cleared"
gh_pr 908 false
echo '{"head": {"sha": "deadbeefcafe", "ref": "feat/task", "repo": {"full_name": "mentaldesk/demo"}}}' >"$PULL"
run board demo accept you 8
recorded "accept" 8 "you accepted · PR #908 merged"

case_ "putting an item on the board reads as added as its status"
gh_child 41 - -
run board demo add lead 41 Ready
recorded "add" 41 "lead added as Ready"

case_ "comments, skips, links and dependencies are each one event"
echo "A reply." >"$WORK/reply"
run board demo comment lead 7 "$WORK/reply"
recorded "comment" 7 "lead commented"
run board demo comment you 7 "$WORK/reply"
recorded "comment you" 7 "you commented"
run board demo skip lead 6 "$WORK/reply"
recorded "skip" 6 "lead skipped"
gh_items <<'ITEMS'
Building 7 A pitch whose tasks have all merged
ITEMS
echo '[{"number": 11, "state": "closed"}]' >"$SUBS"
run board demo finish lead 7 "$WORK/reply"
recorded "finish" 7 "lead closed as done"
gh_items <<'ITEMS'
Building 10 A pitch
Ready 40 A task
ITEMS
gh_child 40 - -
run board demo link 10 40
recorded "link" 40 "lead made a sub-issue of #10"
gh_child 40 10 -
run board demo unlink lead 10 40
recorded "unlink" 40 "lead taken off #10"
run board demo depends lead 40 10 "same view"
recorded "depends" 40 "lead blocked by #10"
gh_blocked <<'DEPS'
10
DEPS
run board demo undepend lead 40 10 "not after all"
recorded "undepend" 40 "lead no longer blocked by #10"

case_ "reads, refusals, failures and --dry-run record nothing"
gh_items <<'ITEMS'
Pitched 7 A pitch
Exploring 8 A draft
ITEMS
for read in "list" "wip" "next" "body 7" "children 7" "pr 7" "feedback lead 7"; do
  # shellcheck disable=SC2086 # each read is its own words
  run board demo $read
  unrecorded "$read"
done
run board demo approve you 8
failed "approve a draft"
unrecorded "refused"
run board demo approve lead 7
unrecorded "lead approving"
gh_items <<'ITEMS'
In_review 8 A task in front of me
ITEMS
gh_pr 908 false
touch "$BIN/merge-fails"
run board demo accept you 8
failed "merge fails"
unrecorded "failed merge"
rm "$BIN/merge-fails"
run board --dry-run demo accept you 8
same "dry-run exit" 0 "$STATUS"
unrecorded "dry run"

case_ "history is newest first, and kept per team"
gh_items <<'ITEMS'
Pitched 7 A pitch
ITEMS
run board demo comment lead 7 "$WORK/reply"
run board demo approve you 7
cp "$TEAM" "$CONFIG/teams/other.json"
A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board other comment dev 7 "$WORK/reply" >/dev/null 2>&1
same "order" "you Pitched → Approved
lead commented" "$(history_of 7)"
same "other team" "dev commented" \
  "$(A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board other history 7 | jq -r '.events[] | "\(.who) \(.what)"')"
run board demo history 7
same "since" "$(jq -r '.events[-1].at' "$OUT")" "$(jq -r .since "$OUT")"

# A record begun at $1 by the Lead commenting on #7.
record_from() {
  rm -f "$A_TEAM_STATE/history.db"
  run board demo comment lead 7 "$WORK/reply"
  sqlite3 "$A_TEAM_STATE/history.db" "UPDATE events SET at = '$1'"
}
caught_calls() { if [ -f "$BIN/caught-calls" ]; then grep -c '' "$BIN/caught-calls"; else echo 0; fi; }
caught_arg() { sed -n "s/^$1=//p" "$BIN/caught-args"; }

case_ "the read catches up on what was done on GitHub directly, each as its own kind of event, in one query"
gh_items <<'ITEMS'
Pitched 7 A pitch
In_review 8 A task
ITEMS
record_from "$(ago 120)"
said=$(ago 40)
gh_caught <<CAUGHT
8 closed $(ago 50) reviewer 908
9 closed $(ago 49) reviewer COMPLETED
10 closed $(ago 48) reviewer NOT_PLANNED
7 comment $said reviewer
7 moved $(ago 30) reviewer Pitched>Approved
7 moved $(ago 29) reviewer >Idea
7 comment $(ago 20) octocat
911 merged $(ago 10) reviewer
CAUGHT
run board demo waiting
same "exit" 0 "$STATUS"
same "one query" 1 "$(caught_calls)"
same "comments and moves" "octocat commented
you added as Idea
you Pitched → Approved
you commented
lead commented" "$(history_of 7)"
same "merged" "you accepted · PR #908 merged" "$(history_of 8)"
same "closed" "you accepted · closed" "$(history_of 9)"
same "not planned" "you closed as not planned" "$(history_of 10)"
same "merged PR" "you accepted · PR #911 merged" "$(history_of 911)"
same "when" "$said" "$(A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board demo history 7 | jq -r '.events[-2].at')"

case_ "catching up again records nothing new"
run board demo waiting
same "exit" 0 "$STATUS"
same "twice" 5 "$(history_of 7 | grep -c '')"

case_ "the catch-up leaves out bots, other projects, and what happened before the record began"
record_from "$(ago 120)"
gh_caught <<CAUGHT
7 comment $(ago 40) demo-app[bot]
7 moved $(ago 39) github-project-automation[bot] In_review>Done
7 moved $(ago 38) reviewer Idea>Ready elsewhere/1
7 moved $(ago 37) reviewer Idea>Ready mentaldesk/2
7 comment $(ago 200) reviewer
CAUGHT
run board demo waiting
same "exit" 0 "$STATUS"
same "left out" "lead commented" "$(history_of 7)"

case_ "what you did from the dashboard isn't recorded twice when the catch-up sees it on GitHub"
record_from "$(ago 120)"
run board demo approve you 7
run board demo comment you 7 "$WORK/reply"
gh_pr 908 false
echo '{"head": {"sha": "deadbeefcafe", "ref": "feat/task", "repo": {"full_name": "mentaldesk/demo"}}}' >"$PULL"
run board demo accept you 8
gh_caught <<CAUGHT
7 moved $(ago 0) reviewer Pitched>Approved
7 comment $(ago 0) reviewer
7 comment $(ago 1) reviewer
8 closed $(ago 0) reviewer 908
CAUGHT
run board demo waiting
same "exit" 0 "$STATUS"
same "once each" "you commented
you Pitched → Approved
you commented
lead commented" "$(history_of 7)"
same "accepted once" "you accepted · PR #908 merged" "$(history_of 8)"

case_ "a decline from the dashboard stays a decline when the catch-up sees the close on GitHub"
gh_items <<'ITEMS'
Idea 9 An Idea
ITEMS
record_from "$(ago 120)"
run board demo decline you 9 "$WORK/reply"
gh_caught <<CAUGHT
9 comment $(ago 0) reviewer
9 closed $(ago 0) reviewer NOT_PLANNED
CAUGHT
run board demo waiting
same "exit" 0 "$STATUS"
same "declined once" "you declined
you commented" "$(history_of 9)"

case_ "the catch-up reaches back to the start of the record, then asks after the items whose Status moved"
gh_items <<'ITEMS'
Pitched 7 A pitch
In_review 8 A task
Ready 12 A task
ITEMS
start=$(ago 120)
record_from "$start"
run board demo waiting
same "from the start" "$start" "$(caught_arg since)"
same "search" "repo:mentaldesk/demo updated:>=$start" "$(caught_arg q)"
gh_items <<'ITEMS'
Approved 7 A pitch
Ready 12 A task
ITEMS
run board demo waiting
grep -q 'nodes(ids: \["I_7","I_8"\])' "$BIN/caught-args" || fail "moved: $(grep 'nodes(ids' "$BIN/caught-args")"
[[ $(caught_arg since) > $start ]] || fail "since: $(caught_arg since) is still the start of the record"

case_ "a fresh install begins its record on its first read, without asking GitHub"
rm -f "$A_TEAM_STATE/history.db"
gh_items <<'ITEMS'
Pitched 7 A pitch
ITEMS
run board demo waiting
same "exit" 0 "$STATUS"
same "no query" 0 "$(caught_calls)"
run board demo waiting
same "then one" 1 "$(caught_calls)"

case_ "--dry-run doesn't catch up"
gh_items <<'ITEMS'
Pitched 7 A pitch
ITEMS
run board --dry-run demo waiting
same "exit" 0 "$STATUS"
same "no query" 0 "$(caught_calls)"

trend() { A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board demo trend | jq -c .; }
queue() { sqlite3 "$A_TEAM_STATE/history.db" "SELECT waiting FROM queue WHERE team = 'demo' ORDER BY at"; }

case_ "with nothing recorded, trend has no start, no week ago and nothing accepted, and makes no record file"
rm -f "$A_TEAM_STATE/history.db"
same "empty" '{"since":null,"weekAgo":null,"accepted":0}' "$(trend)"
unrecorded "trend"

case_ "waiting records how many items it returned, at most once an hour"
gh_items <<'ITEMS'
Pitched 7 A pitch
In_review 8 A task
Ready 12 Not waiting
ITEMS
run board demo waiting
same "first" 2 "$(queue)"
gh_items <<'ITEMS'
Pitched 7 A pitch
ITEMS
run board demo waiting
same "within the hour" 2 "$(queue)"
sqlite3 "$A_TEAM_STATE/history.db" "UPDATE queue SET at = '$(ago 61)'"
run board demo waiting
same "an hour on" "2
1" "$(queue)"
run board --dry-run demo waiting
sqlite3 "$A_TEAM_STATE/history.db" "UPDATE queue SET at = '$(ago 61)'"
run board --dry-run demo waiting
same "dry run" "2
1" "$(queue)"

case_ "overview returns every card but Done, with when its Status was set, in one call"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "project": { "owner": "mentaldesk", "number": 1 } }
JSON
gh_items 404 <<'ITEMS'
Building 404 Overseer
Ready 414 Every board on one screen
In_review 415 Docs
Idea 395 A seed of mine
Done 99 Already merged
ITEMS
jq '.data.organization.projectV2.items.nodes[].fieldValueByName.updatedAt = "2026-10-01T08:00:00Z"' "$ITEMS" >"$ITEMS.new" &&
  mv "$ITEMS.new" "$ITEMS"
edit_item 414 '.parent = {number: 404}'
edit_item 415 '.labels.nodes = [{name: "a-team:customer"}]'
run board demo overview
same "exit" 0 "$STATUS"
same "numbers" '[404,414,415,395]' "$(jq -c '[.[].number]' "$OUT")"
same "kinds" '["pitch","task","docs","yours"]' "$(jq -c '[.[].kind]' "$OUT")"
same "fields" '["kind","number","parent","priority","since","status","team","title","url"]' "$(jq -c '.[0] | keys' "$OUT")"
same "since" '"2026-10-01T08:00:00Z"' "$(jq -c '.[0].since' "$OUT")"
same "parent" '404' "$(jq -c '.[1].parent' "$OUT")"
same "priority" '"High"' "$(jq -c '.[0].priority' "$OUT")"
same "status" '"In review"' "$(jq -c '.[2].status' "$OUT")"
same "filter" '-Status:"Done"' "$(cat "$FILTERS")"
same "api calls" 1 "$(grep -c '' <"$CALLS")"

case_ "trend: waiting a week ago, and each item accepted in the last 7 days counted once"
rm -f "$A_TEAM_STATE/history.db"
since=$(ago $((60 * 24 * 9)))
sqlite3 "$A_TEAM_STATE/history.db" "CREATE TABLE events (id INTEGER PRIMARY KEY, team TEXT NOT NULL, item INTEGER NOT NULL,
    at TEXT NOT NULL, who TEXT NOT NULL, what TEXT NOT NULL);
  CREATE TABLE queue (team TEXT NOT NULL, at TEXT NOT NULL, waiting INTEGER NOT NULL);
  INSERT INTO queue VALUES ('demo', '$since', 20), ('demo', '$(ago $((60 * 24 * 7 + 120)))', 12),
    ('demo', '$(ago $((60 * 24 * 6)))', 9), ('other', '$(ago $((60 * 24 * 7 + 60)))', 30);
  INSERT INTO events (team, item, at, who, what) VALUES
    ('demo', 8, '$(ago 50)', 'you', 'accepted · PR #908 merged'), ('demo', 908, '$(ago 50)', 'you', 'accepted · PR #908 merged'),
    ('demo', 7, '$(ago 40)', 'you', 'accepted · closed'), ('demo', 7, '$(ago 30)', 'you', 'accepted · closed'),
    ('demo', 911, '$(ago 20)', 'you', 'accepted · PR #911 merged'),
    ('demo', 9, '$(ago $((60 * 24 * 8)))', 'you', 'accepted · PR #909 merged'),
    ('demo', 10, '$(ago 10)', 'octocat', 'accepted · PR #910 merged'),
    ('demo', 11, '$(ago 10)', 'you', 'Pitched → Approved'),
    ('other', 12, '$(ago 10)', 'you', 'accepted · closed');"
same "trend" "{\"since\":\"$since\",\"weekAgo\":12,\"accepted\":3}" "$(trend)"

case_ "trend leaves a week ago out until the record reaches back a week, and a stale one is no week ago"
sqlite3 "$A_TEAM_STATE/history.db" "DELETE FROM queue WHERE at <= '$(ago $((60 * 24 * 7)))'"
same "partial" null "$(trend | jq .weekAgo)"
sqlite3 "$A_TEAM_STATE/history.db" "INSERT INTO queue VALUES ('demo', '$(ago $((60 * 24 * 8 + 60)))', 20)"
same "stale" null "$(trend | jq .weekAgo)"

trends() { A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board demo trends | jq -c .; }

case_ "with nothing recorded, trends has no start, no points and no cost, and makes no record file"
rm -f "$A_TEAM_STATE/history.db"
same "empty" '{"since":null,"queue":[],"accepted":[],"cycles":[],"cost":0}' "$(trends)"
unrecorded "trends"

case_ "trends: the last 15 days of the queue, each item accepted once at its latest, and runs' cost in 7 days"
rm -f "$A_TEAM_STATE/history.db"
since=$(ago $((60 * 24 * 20))) days3=$(ago $((60 * 24 * 3))) m90=$(ago 90) m50=$(ago 50) m30=$(ago 30)
sqlite3 "$A_TEAM_STATE/history.db" "CREATE TABLE events (id INTEGER PRIMARY KEY, team TEXT NOT NULL, item INTEGER NOT NULL,
    at TEXT NOT NULL, who TEXT NOT NULL, what TEXT NOT NULL);
  CREATE TABLE queue (team TEXT NOT NULL, at TEXT NOT NULL, waiting INTEGER NOT NULL);
  CREATE TABLE runs (id INTEGER PRIMARY KEY, team TEXT NOT NULL, role TEXT NOT NULL,
    pid INTEGER NOT NULL, log TEXT NOT NULL, started TEXT NOT NULL, ended TEXT, cost REAL, outcome TEXT);
  INSERT INTO queue VALUES ('demo', '$(ago $((60 * 24 * 16)))', 20), ('demo', '$days3', 12),
    ('demo', '$m90', 9), ('other', '$(ago 60)', 30);
  INSERT INTO events (team, item, at, who, what) VALUES
    ('demo', 8, '$m50', 'you', 'accepted · PR #908 merged'), ('demo', 908, '$m50', 'you', 'accepted · PR #908 merged'),
    ('demo', 7, '$(ago 40)', 'you', 'accepted · closed'), ('demo', 7, '$m30', 'you', 'accepted · closed'),
    ('demo', 9, '$since', 'you', 'accepted · PR #909 merged'),
    ('demo', 10, '$(ago 10)', 'octocat', 'accepted · PR #910 merged'),
    ('other', 12, '$(ago 10)', 'you', 'accepted · closed');
  INSERT INTO runs (team, role, pid, log, started, cost) VALUES ('demo', 'dev', 1, 'a', '$(ago 100)', 1.25),
    ('demo', 'lead', 2, 'b', '$(ago 200)', NULL), ('demo', 'dev', 3, 'c', '$(ago $((60 * 24 * 8)))', 9),
    ('other', 'dev', 4, 'd', '$(ago 100)', 4);"
same "trends" "{\"since\":\"$since\",\"queue\":[{\"at\":\"$days3\",\"waiting\":12},{\"at\":\"$m90\",\"waiting\":9}],\"accepted\":[\"$m50\",\"$m30\"],\"cycles\":[],\"cost\":1.25}" "$(trends)"

case_ "trends: a task's cycle runs from first entering Ready to its acceptance, with every spell In review counted"
base=$(date +%s); before() { jq -rn --argjson t "$base" --argjson m "$1" '$t - $m * 60 | strftime("%Y-%m-%dT%H:%M:%SZ")'; }
m600=$(before 600) m480=$(before 480) m420=$(before 420) m300=$(before 300) m120=$(before 120)
sqlite3 "$A_TEAM_STATE/history.db" "INSERT INTO events (team, item, at, who, what) VALUES
    ('demo', 20, '2026-10-01T00:00:00Z', 'lead', 'Idea → Exploring'),
    ('demo', 20, '$m600', 'lead', 'added as Ready'), ('demo', 20, '$(ago 540)', 'dev', 'Ready → In progress'),
    ('demo', 20, '$m480', 'dev', 'In progress → In review'), ('demo', 20, '$m420', 'you', 'In review → Ready'),
    ('demo', 20, '$(ago 360)', 'dev', 'Ready → In progress'), ('demo', 20, '$m300', 'dev', 'In progress → In review'),
    ('demo', 20, '$m120', 'you', 'accepted · PR #920 merged'), ('demo', 920, '$m120', 'you', 'accepted · PR #920 merged'),
    ('demo', 20, '$(ago 100)', 'you', 'commented'),
    ('demo', 21, '$(ago 300)', 'dev', 'Ready → In progress'), ('demo', 21, '$(ago 240)', 'lead', 'In progress → Ready'),
    ('demo', 21, '$(ago 200)', 'dev', 'Ready → In progress'), ('demo', 21, '$(ago 100)', 'dev', 'In progress → In review'),
    ('demo', 21, '$(ago 60)', 'you', 'accepted · PR #921 merged'),
    ('demo', 22, '$(ago 300)', 'lead', 'Exploring → Pitched'), ('demo', 22, '$(ago 200)', 'you', 'accepted · closed');"
same "cycles" "[{\"ready\":\"$m600\",\"accepted\":\"$m120\",\"review\":$((60 * 60 + 180 * 60))}]" "$(trends | jq -c .cycles)"
unset A_TEAM_STATE

# install.sh against a HOME and state of its own, with launchctl and the tools it checks for stubbed.
install_dispatcher() {
  HOME="$INSTALL_HOME" A_TEAM_STATE="$INSTALL_STATE" A_TEAM_BIN="$INSTALL_STUBS/a-team" PATH="$INSTALL_STUBS:$PATH" \
    bash "$ROOT/scripts/install.sh" "$@" >"$OUT" 2>"$ERR"
  STATUS=$?
}
INSTALL_HOME=$(mktemp -d "$WORK/home.XXXXXX") INSTALL_STATE=$(mktemp -d "$WORK/state.XXXXXX")
INSTALL_STUBS=$(mktemp -d "$WORK/stubs.XXXXXX")
for tool in claude gh; do printf '#!/usr/bin/env bash\n' >"$INSTALL_STUBS/$tool"; done
# A job bootout stops stays loaded for $LAUNCHCTL_LINGER more prints, and can't be bootstrapped until it's gone.
cat >"$INSTALL_STUBS/launchctl" <<SH
#!/usr/bin/env bash
left=\$(cat "$INSTALL_STUBS/lingering" 2>/dev/null || echo 0)
case \$1 in
  bootout) echo "\${LAUNCHCTL_LINGER:-0}" >"$INSTALL_STUBS/lingering" ;;
  print) [ "\$left" -gt 0 ] && echo \$((left - 1)) >"$INSTALL_STUBS/lingering" ;;
  bootstrap) [ "\$left" -eq 0 ] || { echo "Bootstrap failed: 5: Input/output error" >&2; exit 5; } ;;
esac
SH
printf '#!/usr/bin/env bash\n[ "$1" = version ] && echo 0.1.13-alpha.0.7\n' >"$INSTALL_STUBS/a-team"
chmod +x "$INSTALL_STUBS"/*

case_ "install records the binary, its version, the interval and the log beside the state"
install_dispatcher
same "exit" 0 "$STATUS"
same "record" "{\"bin\":\"$INSTALL_STUBS/a-team\",\"version\":\"0.1.13-alpha.0.7\",\"dryRun\":false,\"interval\":120,\"log\":\"$INSTALL_STATE/launchd.log\"}" \
  "$(jq -c 'del(.installedAt)' "$INSTALL_STATE/dispatcher.json")"
[ $(($(date +%s) - $(jq .installedAt "$INSTALL_STATE/dispatcher.json"))) -le 5 ] || fail "installedAt: $(jq .installedAt "$INSTALL_STATE/dispatcher.json")"

case_ "install waits for the dispatcher it replaces to stop before loading it again"
LAUNCHCTL_LINGER=3 install_dispatcher
same "exit" 0 "$STATUS"
same "stderr" "" "$(cat "$ERR")"

case_ "install --dry-run says so in the record"
install_dispatcher --dry-run
same "dry run" true "$(jq .dryRun "$INSTALL_STATE/dispatcher.json")"

case_ "install --uninstall removes the record"
install_dispatcher --uninstall
same "exit" 0 "$STATUS"
[ -e "$INSTALL_STATE/dispatcher.json" ] && fail "uninstall: the record is still there"

case_ "a dry-run pass says when the next one is due, without claiming a live dispatcher's next-pass"
A_TEAM_CONFIG=$(mktemp -d "$WORK/config.XXXXXX") A_TEAM_STATE="$INSTALL_STATE" bash "$ROOT/scripts/dispatch.sh" --dry-run
[ -f "$INSTALL_STATE/dry-next-pass" ] || fail "dry pass: no dry-next-pass"
[ -e "$INSTALL_STATE/next-pass" ] && fail "dry pass: wrote next-pass"

case_ "a pass run off schedule leaves the scheduled pass's countdown alone"
echo 1800000000 >"$INSTALL_STATE/next-pass"
A_TEAM_UNSCHEDULED=1 A_TEAM_CONFIG=$(mktemp -d "$WORK/config.XXXXXX") A_TEAM_STATE="$INSTALL_STATE" bash "$ROOT/scripts/dispatch.sh"
same "next-pass" 1800000000 "$(cat "$INSTALL_STATE/next-pass")"
rm "$INSTALL_STATE/next-pass"

# A pass as launchd runs the dispatcher install_dispatcher installed, from $PASS_BIN if that's set.
installed_pass() {
  HOME="$INSTALL_HOME" A_TEAM_STATE="$INSTALL_STATE" A_TEAM_BIN="${PASS_BIN:-$INSTALL_STUBS/a-team}" \
    A_TEAM_CONFIG=$(mktemp -d "$WORK/config.XXXXXX") bash "$ROOT/scripts/dispatch.sh" "$@"
}
in_record() { jq -r "$1" "$INSTALL_STATE/dispatcher.json"; }
record_version() { jq ".version = \"$1\" | .installedAt = 1700000000" "$INSTALL_STATE/dispatcher.json" >"$WORK/record" &&
  mv "$WORK/record" "$INSTALL_STATE/dispatcher.json"; }

case_ "launchd's pass writes the record for a dispatcher installed before there was one"
install_dispatcher
rm "$INSTALL_STATE/dispatcher.json"
installed_pass
same "record" "{\"bin\":\"$INSTALL_STUBS/a-team\",\"version\":\"0.1.13-alpha.0.7\",\"dryRun\":false,\"interval\":120,\"log\":\"$INSTALL_STATE/launchd.log\"}" \
  "$(jq -c 'del(.installedAt)' "$INSTALL_STATE/dispatcher.json")"

case_ "launchd's pass brings the record's version up to date, and keeps when it was installed"
record_version 0.1.12
installed_pass
same "version" 0.1.13-alpha.0.7 "$(in_record .version)"
same "installedAt" 1700000000 "$(in_record .installedAt)"

case_ "a pass launchd doesn't run leaves the record alone"
record_version 0.1.12
PASS_BIN=/elsewhere/bin/a-team installed_pass
same "another binary" 0.1.12 "$(in_record .version)"
installed_pass --dry-run
same "another mode" 0.1.12 "$(in_record .version)"
A_TEAM_UNSCHEDULED=1 installed_pass
same "off schedule" 0.1.12 "$(in_record .version)"
install_dispatcher --uninstall
installed_pass
[ -e "$INSTALL_STATE/dispatcher.json" ] && fail "uninstalled: a pass wrote the record"
rm -f "$INSTALL_STATE"/*next-pass

# status.sh's first line, for a state directory holding $1 as dispatcher.json (or none), next-pass $2 seconds from now
# (with prefix $3) and a pass in progress as pid $4.
dispatcher_line() {
  local state
  state=$(mktemp -d "$WORK/state.XXXXXX")
  [ -n "$1" ] && echo "$1" >"$state/dispatcher.json"
  [ -n "${2:-}" ] && echo $(($(date +%s) + $2)) >"$state/${3:-}next-pass"
  [ -n "${4:-}" ] && echo "$4" >"$state/${3:-}pass"
  A_TEAM_CONFIG=$(mktemp -d "$WORK/config.XXXXXX") A_TEAM_STATE="$state" HOME=/Users/me bash "$ROOT/scripts/status.sh" | head -n 1
}
installed() { jq -nc --argjson dry "$1" --argjson at $(($(date +%s) - $2)) \
  '{bin: "/Users/me/code/a-team/palette/bin/a-team", version: "0.1.13", dryRun: $dry, interval: 120, installedAt: $at}'; }

case_ "status opens with what's driving the teams"
same "live" "dispatcher · ~/code/a-team/palette/bin/a-team 0.1.13 · next pass 1:12" "$(dispatcher_line "$(installed false 600)" 72)"
same "dry run" "dispatcher · dry run: nothing will actually start · next pass 0:48" \
  "$(dispatcher_line "$(installed true 600)" 48 dry-)"
same "stopped" "dispatcher · stopped 14m ago" "$(dispatcher_line "$(installed false 3600)" $((120 - 14 * 60)))"
same "never installed" "dispatcher · nothing installed · run: a-team install" "$(dispatcher_line '')"
same "unrecorded" "dispatcher" "$(dispatcher_line '' 30)"
same "unrecorded, stale" "dispatcher · nothing installed · run: a-team install" "$(dispatcher_line '' -600)"

case_ "status counts an overdue pass as live while its process is, and stopped once it's gone"
same "busy pass" "dispatcher · ~/code/a-team/palette/bin/a-team 0.1.13 · next pass 0:00" \
  "$(dispatcher_line "$(installed false 3600)" -180 '' $$)"
same "gone" "dispatcher · stopped 5m ago" "$(dispatcher_line "$(installed false 3600)" -180 '' 999999)"
same "unrecorded, busy" "dispatcher" "$(dispatcher_line '' -180 '' $$)"

case_ "a scheduled pass marks itself in progress with its pid, and clears that when it ends"
PASS_STATE=$(mktemp -d "$WORK/state.XXXXXX") PASS_CONFIG=$(mktemp -d "$WORK/config.XXXXXX")
PASS_STUBS=$(mktemp -d "$WORK/stubs.XXXXXX")
mkdir -p "$PASS_CONFIG/teams"
echo '{}' >"$PASS_CONFIG/teams/t.json"
printf '#!/usr/bin/env bash\ncat "%s/pass" >"%s/seen"\nexec %s "$@"\n' "$PASS_STATE" "$PASS_STUBS" "$(command -v jq)" >"$PASS_STUBS/jq"
chmod +x "$PASS_STUBS/jq"
PATH="$PASS_STUBS:$PATH" A_TEAM_CONFIG="$PASS_CONFIG" A_TEAM_STATE="$PASS_STATE" bash "$ROOT/scripts/dispatch.sh" &
pass_pid=$!
wait "$pass_pid"
same "pid during the pass" "$pass_pid" "$(cat "$PASS_STUBS/seen" 2>/dev/null)"
[ -e "$PASS_STATE/pass" ] && fail "pass: still marked in progress after it ended"

case_ "a scheduled pass counts the next one down from when it ends, as launchd does"
echo 1800000000 >"$PASS_STATE/next-pass"
printf '#!/usr/bin/env bash\ncat "%s/next-pass" >"%s/seen"\nexec %s "$@"\n' "$PASS_STATE" "$PASS_STUBS" "$(command -v jq)" >"$PASS_STUBS/jq"
PATH="$PASS_STUBS:$PATH" A_TEAM_CONFIG="$PASS_CONFIG" A_TEAM_STATE="$PASS_STATE" bash "$ROOT/scripts/dispatch.sh"
ended=$(date +%s)
same "next-pass during the pass" 1800000000 "$(cat "$PASS_STUBS/seen" 2>/dev/null)"
left=$(($(cat "$PASS_STATE/next-pass") - ended))
[ "$left" -ge 118 ] && [ "$left" -le 120 ] || fail "next-pass after the pass: due in ${left}s, not 120s"

# release.sh against a repo in $REL: repo.json the GraphQL view of its default branch and latest release, runs.json
# its latest release.yml run, and each workflow it was asked to run a line of `dispatched`.
REL=$(mktemp -d "$WORK/release.XXXXXX")
mkdir -p "$REL/bin"
cat >"$REL/bin/gh" <<SH
#!/usr/bin/env bash
case " \$* " in
  *"-X POST"*"/dispatches"*) printf '%s\n' "\$*" >>"$REL/dispatched"; exit 0 ;;
  *graphql*) page="$REL/repo.json" ;;
  *"/actions/workflows/release.yml/runs?"*) page="$REL/runs.json" ;;
  *) echo "gh: not faked: \$*" >&2; exit 1 ;;
esac
filter=
while [ \$# -gt 0 ]; do
  [ "\$1" = --jq ] && { filter=\$2; break; }
  shift
done
if [ -n "\$filter" ]; then jq -r "\$filter" "\$page"; else cat "\$page"; fi
SH
chmod +x "$REL/bin/gh"
# released <head> [<tag commit> <seconds ago>]: main at <head>, and v1.2.0 at <tag commit>, released that long ago.
released() {
  jq -n --arg head "$1" --arg tag "${2:-}" --argjson ago "${3:-0}" '{data: {repository: {
    defaultBranchRef: {name: "main", target: {oid: $head}},
    latestRelease: (if $tag == "" then null
      else {tagName: "v1.2.0", createdAt: (now - $ago | todate), tagCommit: {oid: $tag}} end)}}}' >"$REL/repo.json"
}
# release_run [<status>]: the latest release.yml run, with that status; none without one.
release_run() {
  jq -n --arg status "${1:-}" '{workflow_runs: (if $status == "" then []
    else [{status: $status, html_url: "https://github.com/mentaldesk/demo/actions/runs/9"}] end)}' >"$REL/runs.json"
}
released_by() { : >"$REL/dispatched"; PATH="$REL/bin:$PATH" run release "$@"; }
dispatches() { grep -c . "$REL/dispatched"; }
export A_TEAM_STATE
A_TEAM_STATE=$(mktemp -d "$WORK/state.XXXXXX")

case_ "continuous runs release.yml on main once it has moved past the latest release, and once only"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "release": "continuous" }
JSON
released 1111111aaaa 2222222bbbb 60
release_run completed
released_by demo
same "exit" 0 "$STATUS"
same "said" "ran release.yml on main at 1111111 (latest release: v1.2.0)" "$(cat "$OUT")"
same "dispatched" "api -X POST repos/mentaldesk/demo/actions/workflows/release.yml/dispatches -f ref=main" "$(cat "$REL/dispatched")"
released_by demo
same "again" "ran release.yml for main at 1111111 already: if no release follows, see https://github.com/mentaldesk/demo/actions/workflows/release.yml" "$(cat "$OUT")"
same "dispatched again" 0 "$(dispatches)"

case_ "a main that's at the latest release has nothing to release"
released 3333333cccc 3333333cccc 60
released_by demo
same "said" "nothing to release: main is at v1.2.0" "$(cat "$OUT")"
same "dispatched" 0 "$(dispatches)"

case_ "a repo that has never released is due"
released 4444444dddd
released_by demo
same "said" "ran release.yml on main at 4444444 (latest release: none)" "$(cat "$OUT")"

case_ "a release.yml run still going is left to finish first"
released 5555555eeee 3333333cccc 60
release_run in_progress
released_by demo
same "said" "waiting for the release.yml run going now to finish: https://github.com/mentaldesk/demo/actions/runs/9" "$(cat "$OUT")"
same "dispatched" 0 "$(dispatches)"
release_run completed

case_ "daily waits until the latest release is a day old"
jq '.release = "daily"' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
released 6666666ffff 3333333cccc 3600
released_by demo
same "exit" 0 "$STATUS"
grep -q '^next release due at 20[0-9-]*T[0-9:]*Z: v1.2.0 is under a day old$' "$OUT" || fail "daily: '$(cat "$OUT")'"
same "dispatched" 0 "$(dispatches)"
released 6666666ffff 3333333cccc 90000
released_by demo
same "a day on" "ran release.yml on main at 6666666 (latest release: v1.2.0)" "$(cat "$OUT")"

case_ "--dry-run says what it would run, runs nothing, and leaves the next real pass to run it"
released 7777777aaaa 3333333cccc 90000
released_by --dry-run demo
same "said" "would run release.yml on main at 7777777 (latest release: v1.2.0)" "$(cat "$OUT")"
same "dispatched" 0 "$(dispatches)"
released_by demo
same "for real" 1 "$(dispatches)"

case_ "never, the default, asks GitHub nothing; anything else is refused in one line"
jq 'del(.release)' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
released_by demo
same "exit" 0 "$STATUS"
same "said" "release is never: nothing to do" "$(cat "$OUT")"
jq '.release = "weekly"' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
released_by demo
failed "weekly"
one_line "weekly"
same "refused" "a-team release: release is 'weekly' in demo.json: expected never, daily or continuous" "$(cat "$ERR")"

case_ "a dispatcher pass logs what release says when it changes, paused or not, and nothing for never"
APP=$(mktemp -d "$WORK/dispatch.XXXXXX")
mkdir -p "$APP/bin" "$APP/scripts"
cp "$ROOT"/scripts/*.sh "$APP/scripts/"
cat >"$APP/bin/a-team" <<SH
#!/usr/bin/env bash
[ "\$1" = release ] || exit 1
printf '%s\n' "\$*" >>"$APP/release-args"
cat "$APP/said"
exit "\$(cat "$APP/code" 2>/dev/null || echo 0)"
SH
chmod +x "$APP/bin/a-team"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "release": "continuous", "dispatch": { "enabled": false } }
JSON
echo '{ "repo": "mentaldesk/other", "dispatch": { "enabled": false } }' >"$CONFIG/teams/other.json"
echo "nothing to release: main is at v1.2.0" >"$APP/said"
pass() { A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/dispatch.sh" --dry-run; }
pass
pass
echo "would run release.yml on main at 1111111 (latest release: v1.2.0)" >"$APP/said"
pass
echo "a-team release: can't read mentaldesk/demo: Not Found (HTTP 404)" >"$APP/said"
echo 1 >"$APP/code"
pass
same "logged" "demo release: nothing to release: main is at v1.2.0
demo release: would run release.yml on main at 1111111 (latest release: v1.2.0)
demo release: failed: can't read mentaldesk/demo: Not Found (HTTP 404)" "$(cut -d' ' -f2- "$A_TEAM_STATE/dispatch.log")"
same "asked" "release --dry-run demo" "$(sort -u "$APP/release-args")"

case_ "with the Reviewer on, a green draft Dev PR starts the Reviewer, not the Dev"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 },
  "wip": { "worktrees": 1 }, "roles": { "reviewer": true } }
JSON
gh_items <<'ITEMS'
In_progress 12 A task with its draft PR up
ITEMS
gh_pr 912 true
run board demo triggers reviewer
same "exit" 0 "$STATUS"
same "reasons" '["PR #912 is green and waiting for its review"]' "$(jq -c .reasons "$OUT")"
same "tasks" '[12]' "$(jq -c '[.tasks[].number]' "$OUT")"
same "ready" null "$(jq -c .ready "$OUT")"
run board demo triggers dev
same "dev reasons" '[]' "$(jq -c .reasons "$OUT")"
run board demo pr 12
same "review" '"waiting"' "$(jq -c .review "$OUT")"

case_ "the Reviewer isn't started for a red PR, a ready one, or one that isn't the Dev's"
gh_runs <<'RUNS'
completed failure 2025-09-19T09:00:00Z build
RUNS
run board demo triggers reviewer
same "red" '[]' "$(jq -c .reasons "$OUT")"
gh_runs <<'RUNS'
completed success 2025-09-19T09:00:00Z build
RUNS
gh_pr 912 false
run board demo triggers reviewer
same "ready" '[]' "$(jq -c .reasons "$OUT")"
gh_pr 912 true
edit_item 12 '.labels.nodes = []'
run board demo triggers reviewer
same "not the Dev's" '[]' "$(jq -c .reasons "$OUT")"

case_ "once the review is posted, the Dev acts on it and the Reviewer never starts again"
edit_item 12 '.labels.nodes = [{name: "a-team:dev"}]'
gh_reviewed
run board demo triggers reviewer
same "reviewer reasons" '[]' "$(jq -c .reasons "$OUT")"
run board demo triggers dev
same "dev reasons" '["PR #912 has its review: act on it"]' "$(jq -c .reasons "$OUT")"
run board demo pr 12
same "review" '"posted"' "$(jq -c .review "$OUT")"

case_ "once the Dev answers the review, its next green run is told to mark the PR ready"
# `dev_said <before|after>`: a Dev comment from the team's App, added before or after the review.
dev_said() {
  jq --arg where "$1" '(.. | objects | select(has("isDraft"))).comments.nodes |=
      ([{body: "Done.\n\n<!-- a-team:dev -->", author: {__typename: "Bot", login: "demo-app"}}] as $c
       | if $where == "before" then $c + . else . + $c end)' "$PRS" >"$PRS.new" && mv "$PRS.new" "$PRS"
  gh_reviewing
}
dev_said before
run board demo pr 12
same "a Dev comment before the review" '"posted"' "$(jq -c .review "$OUT")"
dev_said after
run board demo pr 12
same "review" '"answered"' "$(jq -c .review "$OUT")"
run board demo triggers dev
same "dev reasons" '["PR #912 has its review answered and is green: mark it ready"]' "$(jq -c .reasons "$OUT")"

case_ "a review marker pasted by anyone but the team's App doesn't count"
gh_pr 912 true
gh_reviewed someone
run board demo pr 12
same "review" '"waiting"' "$(jq -c .review "$OUT")"

case_ "the Reviewer posts one review on a green draft Dev PR, with its marker"
gh_pr 912 true
printf 'Nothing needs changing.\n' >"$WORK/review"
run board demo review reviewer 912 "$WORK/review"
same "exit" 0 "$STATUS"
same "said" "#912: reviewed" "$(cat "$OUT")"
grep -q '^PR COMMENT pr comment 912 ' "$WRITES" || fail "review: no PR comment in '$(cat "$WRITES")'"
same "first line" "Nothing needs changing." "$(head -1 "$POSTED")"
same "marker" "<!-- a-team:reviewer -->" "$(tail -1 "$POSTED")"

case_ "a second review, a red or ready PR, and anything but the Dev's open task PR are refused"
refused_review() {
  : >"$WRITES"
  run board demo review reviewer 912 "$WORK/review"
  failed "$1"
  one_line "$1"
  grep -qF "$2" "$ERR" || fail "$1: '$(cat "$ERR")'"
  same "$1 writes" "" "$(cat "$WRITES")"
}
gh_reviewed
refused_review "second review" "#912 has its review already: a PR is reviewed once"
gh_pr 912 false
refused_review "ready PR" "#912 is ready for review already"
gh_pr 912 true
gh_reviewing '.labels.nodes = [{name: "pitch"}]'
refused_review "pitch" "reviewer reviews only the Dev's open task PRs"
gh_reviewing '.labels.nodes = [{name: "a-team:customer"}]'
refused_review "docs PR" "reviewer reviews only the Dev's open task PRs"
gh_reviewing '.body = "My own change"'
refused_review "stakeholder's PR" "reviewer reviews only the Dev's open task PRs"
gh_reviewing '.state = "CLOSED"'
refused_review "closed PR" "reviewer reviews only the Dev's open task PRs"
gh_reviewing
gh_runs <<'RUNS'
completed failure 2025-09-19T09:00:00Z build
RUNS
refused_review "red PR" "#912 isn't green"

case_ "the Reviewer can't comment, move, claim or change what a task waits on, and no one else reviews"
gh_runs <<'RUNS'
completed success 2025-09-19T09:00:00Z build
RUNS
: >"$WRITES"
run board demo comment reviewer 12 "$WORK/review"
failed "comment"
grep -qF "reviewer posts only its review" "$ERR" || fail "comment: '$(cat "$ERR")'"
run board demo move reviewer 12 "In review"
failed "move"
run board demo claim reviewer
failed "claim"
run board demo depends reviewer 12 13 "why"
failed "depends"
run board demo review dev 912 "$WORK/review"
failed "dev review"
grep -qF "only reviewer reviews a task's PR" "$ERR" || fail "dev review: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"

case_ "with the Reviewer off, a green draft goes to the Dev as before, and review is refused"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 },
  "wip": { "worktrees": 1 } }
JSON
run board demo triggers dev
same "dev reasons" '["PR #912 is green but still a draft"]' "$(jq -c .reasons "$OUT")"
run board demo triggers reviewer
same "reviewer reasons" '[]' "$(jq -c .reasons "$OUT")"
run board demo pr 12
same "review" '"off"' "$(jq -c .review "$OUT")"
refused_review "reviewer off" "demo has no Reviewer"

case_ "waiting marks a reviewed task, with how many points the review left to consider"
gh_items <<'ITEMS'
In_review 115 Reviewed already
ITEMS
gh_talk <<TALK
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 pr-body ${TODAY}T08:25:00Z demo-app[bot] Closes #115\n<!-- a-team:dev -->
115 pr-comment ${TODAY}T08:40:00Z demo-app[bot] ## Needs changing\n- a.sh:1 off by one.\n\n## Worth considering\n- Rename it.\n- Split it.\n\n<!-- a-team:reviewer -->
TALK
run board demo waiting
same "exit" 0 "$STATUS"
same "reviewed" true "$(jq -c '.[0].reviewed' "$OUT")"
same "consider" 2 "$(jq -c '.[0].consider' "$OUT")"
gh_talk <<TALK
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 pr-body ${TODAY}T08:25:00Z demo-app[bot] Closes #115\n<!-- a-team:dev -->
115 pr-comment ${TODAY}T08:40:00Z demo-app[bot] Nothing needs changing.\n\n<!-- a-team:reviewer -->
TALK
run board demo waiting
same "clean review" '[true,0]' "$(jq -c '[.[0].reviewed, .[0].consider]' "$OUT")"
gh_talk <<TALK
115 body ${TODAY}T08:00:00Z demo-app[bot] The task\n<!-- a-team:lead -->
115 pr-body ${TODAY}T08:25:00Z demo-app[bot] Closes #115\n<!-- a-team:dev -->
TALK
run board demo waiting
same "unreviewed" null "$(jq -c '.[0].reviewed' "$OUT")"

[ "$failures" -eq 0 ] || { echo "$failures failed"; exit 1; }
echo "all passed"
