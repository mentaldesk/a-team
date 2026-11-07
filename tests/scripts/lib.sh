#!/usr/bin/env bash
#
# tests/scripts/lib.sh — what the shell tests share. An area sources this first, and ends with finish_suite.
#
# shellcheck disable=SC2034,SC2120,SC2154 # a helper reads what its area set and takes what it passes, and an area reads what this sets
set -uo pipefail
# As in CI: a run's own team would otherwise reach every test's gh and git.
unset A_TEAM_RUN_TEAM A_TEAM_RUN_TASK A_TEAM_RUN_STARTED

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
A_TEAM="$ROOT/bin/a-team"
# shellcheck source=tests/leftovers.sh
source "$ROOT/tests/leftovers.sh"
# A case that forgets its fakes stops here, rather than reaching your real GitHub, Keychain or Claude.
GUARD="$WORK/guard"
mkdir "$GUARD"
for tool in gh curl security claude open launchctl; do
  fake "$GUARD/$tool" <<<"echo \"$tool: a test reached the real one\" >&2; exit 99"
done
PATH="$GUARD:$PATH"
# How long a fake waits, so one a killed suite leaves behind still ends.
FAKE_LIMIT=300
OUT="$WORK/out" ERR="$WORK/err"
# Board writes record history in the state folder, which mustn't be the real one.
export A_TEAM_STATE="$WORK/state"
# Every command reads the whole mirror back, so a test's board page is the board as the stakeholder left it.
export A_TEAM_BOARD_READ_EVERY=0
CONFIG=$WORK TEAM='' STATUS=0
# When the stub's GitHub quota resets: always ahead, so a retry stays due later.
QUOTA_RESET=$(($(date +%s) + 86400))

case_() { echo "$*"; }
same() { [ "$2" = "$3" ] || fail "$1: expected '$2', got '$3'"; }
failed() { [ "$STATUS" -ne 0 ] || fail "$1: expected a non-zero exit, got $STATUS"; }
one_line() { same "$1 stderr" 1 "$(grep -c '' <"$ERR")"; }

# A config directory of its own, holding one team called demo, written from stdin.
fixture() {
  CONFIG=$(mktemp -d "$WORK/config.XXXXXX")
  mkdir -p "$CONFIG/teams"
  TEAM="$CONFIG/teams/demo.json"
  cat >"$TEAM"
  if [ -z "${NO_APP:-}" ] && jq -e '.app == null' "$TEAM" >/dev/null 2>&1; then
    jq '.app.id = 7' "$TEAM" >"$TEAM.app" && mv "$TEAM.app" "$TEAM"
  fi
}

# A role's board command needs the App's token, so a test's team has an App with one in its cache, unless NO_TOKEN is set.
with_token() {
  local cache="$A_TEAM_STATE/demo/token.json"
  [ -z "${NO_TOKEN:-}" ] && [ ! -f "$cache" ] || return 0
  mkdir -p "$(dirname "$cache")"
  jq -n '{token: "ghs_app", expires_at: (now + 86400 | floor | todate), permissions: {}}' >"$cache"
}

# Each board command starts from a board moved over afresh from the test's page, unless KEEP_BOARD is set.
run() {
  local db
  case " $* " in
    *" check "*) ;;
    " board "* | " stop "*) with_token ;;
  esac
  for db in history.db dry-run.db; do
    [ -n "${KEEP_BOARD:-}" ] || [ ! -f "${A_TEAM_STATE:-}/$db" ] ||
      sqlite3 "$A_TEAM_STATE/$db" "DELETE FROM board WHERE team = 'demo'; DELETE FROM boards WHERE team = 'demo'; DELETE FROM details WHERE team = 'demo';" 2>/dev/null
  done
  A_TEAM_CONFIG="$CONFIG" "$A_TEAM" "$@" >"$OUT" 2>"$ERR"
  STATUS=$?
}

# waiting: runs `waiting`, keeping its entry saying when the detail was synced apart from the items, in $OUT.synced.
waiting() {
  run board demo waiting
  [ "$STATUS" -eq 0 ] || return 0
  jq -c 'map(select(has("synced"))) | first' "$OUT" >"$OUT.synced"
  jq 'map(select(has("synced") | not))' "$OUT" >"$OUT.items" && mv "$OUT.items" "$OUT"
}

enabled() { jq -c .dispatch.enabled "$TEAM"; }

TODAY=$(jq -rn 'now | strftime("%Y-%m-%d")')
YESTERDAY=$(jq -rn 'now - 86400 | strftime("%Y-%m-%d")')
MONTH=$(date -u +%Y-%m)
export TZ=UTC
mkdir -p "$A_TEAM_STATE"

# The one GraphQL page board.sh's `items` reads, from lines of "<status with _ for space> <n> <title>".
# The issue numbers passed as arguments are the ones with a Priority set. An item's labels follow
# its status, as a real board's do: a pitch carries `pitch` from Exploring on, and a task the Dev
# has claimed is the one In progress or In review.
gh_items() {
  [ -z "${BIN:-}" ] || PATH=${PATH/"$BIN:"/}
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
  jq -n '{data: {organization: {projectV2: {id: "PVT_1", title: "demo", fields: {nodes: [{id: "PVTSSF_status", name: "Status", options: [
    {id: "OPT_exploring", name: "Exploring"}, {id: "OPT_pitched", name: "Pitched"},
    {id: "OPT_approved", name: "Approved"}, {id: "OPT_done", name: "Done"}]}]}}}}}' >"$META"
  gh_thread </dev/null
  gh_recent </dev/null
  RUNS="$BIN/runs.json"
  gh_runs <<'RUNS'
completed success 2025-09-19T09:00:00Z build
RUNS
  MAIN_RUNS="$BIN/main-runs.json"
  echo '{"check_runs": []}' >"$MAIN_RUNS"
  PRS="$BIN/prs.json" PULL="$BIN/pull.json" REVIEWING="$BIN/reviewing.json"
  gh_pr
  echo '{"head": {"sha": "deadbeefcafe"}}' >"$PULL"
  FILTERS="$BIN/filters"
  : >"$FILTERS"
  CAUGHT="$BIN/caught.json"
  gh_caught </dev/null
  UPDATES="$BIN/updates.json" BUMP_VIEW="$BIN/bump-view.json"
  echo '[]' >"$UPDATES"
  UPSTREAM="$BIN/upstream.json" UPSTREAM_CALLS="$BIN/upstream-calls"
  echo '[]' >"$UPSTREAM"
  : >"$UPSTREAM_CALLS"
  UPSTREAM_LINES="$BIN/upstream-lines.json"
  echo '[]' >"$UPSTREAM_LINES"
  WAITING_ON="$BIN/waiting-on.json" UPSTREAM_ITEMS="$BIN/upstream-items.json" RELEASES="$BIN/releases.json"
  echo '[]' | tee "$WAITING_ON" "$RELEASES" >/dev/null
  echo '{"data": {"repository": {}}}' >"$UPSTREAM_ITEMS"
  REACH="$BIN/reach.json" PREREQUISITES="$BIN/prerequisites.json"
  echo '{"data": {"repository": {}}}' | tee "$REACH" "$PREREQUISITES" >/dev/null
  : >"$BIN/contains"
  VOPEN="$BIN/vision-open.json" VCLOSED="$BIN/vision-closed.json" VCOMMITS="$BIN/vision-commits.json"
  VISION_MD="$BIN/vision.md" THEMES="$BIN/themes.json" FILES="$BIN/files.json" CONTENTS="$BIN/contents.json"
  echo '[]' | tee "$VOPEN" "$VCLOSED" "$VCOMMITS" "$FILES" >/dev/null
  : >"$VISION_MD"
  echo '{"data": {"repository": {}}}' >"$THEMES"
  echo '{}' >"$CONTENTS"
  fake "$BIN/gh" <<SH
#!/usr/bin/env bash
[ ! -e "$BIN/offline" ] || { echo "gh: error connecting to api.github.com" >&2; exit 1; }
# Carrying a-team labels into the record happens once, on upgrade, so it isn't counted with a command's calls.
case " \$* " in
  *HeldSince*) printf '%s\n' "\$*" >>"$BIN/args"; cat "$TALK"; exit 0 ;;
  *"-X DELETE"*"/labels/"*) [ ! -e "$BIN/unlabel-fails" ] || { [ -s "$BIN/unlabel-fails" ] && [[ " \$* " != *"/\$(cat "$BIN/unlabel-fails") "* ]]; } ||
                              { echo "gh: HTTP 502" >&2; exit 1; }
                            echo "\$*" >>"$BIN/unlabelled"; exit 0 ;;
esac
echo call >>"$CALLS"
printf '%s\n' "\$*" >>"$BIN/args"
case " \$* " in
  *ParentOf*) page="$BIN/parent.json" ;;
  *defaultBranchRef*) page="$TALK" ;;
  *"issues?labels=a-team:upstream"*) page="$WAITING_ON" ;;
  *UpstreamWaits*) page="$UPSTREAM_ITEMS" ;;
  *UpstreamReach*) page="$REACH" ;;
  *Prerequisites*) page="$PREREQUISITES" ;;
  *"/releases?"*) page="$RELEASES" ;;
  *"/compare/"*) all="\$*"; tag=\${all##*/compare/}; tag=\${tag%%...*}
                 jq -n --arg s "\$(sed -n "s/^\$tag //p" "$BIN/contains")" '{status: (if \$s == "" then "ahead" else \$s end)}' >"$BIN/compared.json"
                 page="$BIN/compared.json" ;;
  *"/contents/"*"?ref=a-team/attachments"*vnd.github.raw*) echo "RAW \$*" >>"$WRITES"; echo PNG; exit 0 ;;
  *"/contents/"*"?ref=a-team/attachments"*) [ -e "$BIN/stored" ] || { echo "gh: Not Found (HTTP 404)" >&2; exit 1; }; exit 0 ;;
  *"git/ref/heads/a-team/attachments"*) [ -e "$BIN/branched" ] || { echo "gh: Not Found (HTTP 404)" >&2; exit 1; }; echo HEAD1; exit 0 ;;
  *"git/commits/HEAD1"*) echo TREE1; exit 0 ;;
  *"-X POST"*"/git/"*"--input"*) all="\$*"; kind=\${all##*/git/}; kind=\${kind%% *}; jq -c ". + {kind: \"\$kind\"}" "\${@: -3:1}" >>"$BIN/git.json"
                                  echo "\${kind}-sha"; exit 0 ;;
  *"/git/refs"*"a-team/attachments"*) echo "REF \$*" >>"$WRITES"; exit 0 ;;
  *addReaction*) printf '%s\n' "\$@" | sed -n 's/^subject=//p' >>"$ACKED"; echo '{}'; exit 0 ;;
  *createProjectV2Field*) printf 'FIELD %s ' "\$@" | tr -d '\n' >>"$WRITES"; echo >>"$WRITES"; echo PVTF_new; exit 0 ;;
  *clearProjectV2ItemFieldValue*) printf 'CLEAR %s ' "\$@" | tr -d '\n' >>"$WRITES"; echo >>"$WRITES"; echo '{}'; exit 0 ;;
  *updateIssueFieldValue*) printf '%s ' "\$@" | tr -d '\n' >>"$WRITES"; echo >>"$WRITES"; echo '{}'; exit 0 ;;
  *updateProjectV2ItemFieldValue*)
    [ ! -e "$BIN/mirror-fails" ] || { echo "gh: API rate limit exceeded for installation ID 7 (HTTP 403)" >&2; exit 1; }
    printf '%s ' "\$@" | tr -d '\n' >>"$WRITES"; echo >>"$WRITES"
    # With live, the page shows the move, as the Project would.
    if [ -e "$BIN/live" ]; then
      for arg in "\$@"; do case \$arg in item=*) item=\${arg#item=} ;; option=*) option=\${arg#option=} ;; esac; done
      jq --arg i "\$item" --arg n "\$(jq -r --arg o "\$option" '[.. | objects | select(.id? == \$o) | .name][0]' "$META")" \
        '(.data.organization.projectV2.items.nodes[] | select(.id == \$i)).fieldValueByName = {name: \$n}' "$ITEMS" >"$ITEMS.new" &&
        mv "$ITEMS.new" "$ITEMS"
    fi
    echo '{}'; exit 0 ;;
  *"api rate_limit"*) page="$BIN/rate.json"; jq -n --argjson left "\$(cat "$BIN/remaining" 2>/dev/null || echo null)" \
                         '{resources: {graphql: {reset: $QUOTA_RESET, remaining: \$left}, core: {reset: $((QUOTA_RESET - 1800)), remaining: \$left}}}' >"\$page" ;;
  *"pr list"*"app/dependabot"*) page="$UPDATES" ;;
  *"pr view"*) page="$BUMP_VIEW" ;;
  *"pr list"*"app/demo-app"*) echo "\$*" >>"$UPSTREAM_CALLS"; page="$UPSTREAM" ;;
  *ProjectV2SingleSelectField*) [ ! -e "$BIN/project-read-fails" ] || { cat "$BIN/project-read-fails" >&2; exit 1; }
                                page="$META" ;;
  *BoardRefresh*) jq --argjson off "\$(cat "$BIN/off-mirror" 2>/dev/null || echo '[]')" '{data: {nodes: [.data.organization.projectV2.items.nodes[]
                        | .content + {projectItems: {nodes: (if .content.number | IN(\$off[]) then [] else [
                        {id, project: {number: 1, owner: {login: "mentaldesk"}}, fieldValueByName}] end)}}]}}' "$ITEMS"; exit 0 ;;
  *issueFields*) page="$FIELDS" ;;
  *": issue(number"*) jq '{data: {repository: ([.data.organization.projectV2.items.nodes[].content
                        | {key: "i\(.number)", value: {issueFieldValues}}] | from_entries)}}' "$ITEMS"; exit 0 ;;
  *"issue comment"*) cat >"$POSTED"; exit 0 ;;
  *"-X POST"*"/issues/"*"/comments"*) cat >"$POSTED"; echo IC_posted; exit 0 ;;
  *"pr comment"*) echo "PR COMMENT \$*" >>"$WRITES"; cat >"$POSTED"; exit 0 ;;
  *"pr edit"*) echo "PR EDIT \$*" >>"$WRITES"; cat >"$BIN/pr-body"; exit 0 ;;
  *"issue create"*) [ ! -e "$BIN/create-fails" ] || { echo "gh: Could not resolve to a Repository (HTTP 404)" >&2; exit 1; }
                    echo "CREATE \$*" >>"$WRITES"; cat >"$POSTED"; echo "https://github.com/mentaldesk/demo/issues/77"; exit 0 ;;
  *"pr ready"*) echo "READY \$*" >>"$WRITES"; exit 0 ;;
  *"issue close"*) [ ! -e "$BIN/close-fails" ] || { echo "gh: Resource not accessible by integration (HTTP 403)" >&2; exit 1; }
                   echo "CLOSE \$*" >>"$WRITES"; exit 0 ;;
  *"issue edit"*"--body"*) echo "EDIT \$*" >>"$WRITES"
    if [ "\${@: -1}" = - ]; then cat >"$EDITED"; else printf '%s' "\${@: -1}" >"$EDITED"; fi; exit 0 ;;
  *addProjectV2ItemById*) printf 'ADD %s ' "\$@" | tr -d '\n' >>"$WRITES"; echo >>"$WRITES"; echo PVTI_new; exit 0 ;;
  *"-X POST"*sub_issues*) echo "POST \$*" >>"$WRITES"; echo '{}'; exit 0 ;;
  *"-X DELETE"*sub_issue*) echo "DELETE \$*" >>"$WRITES"; echo '{}'; exit 0 ;;
  *sub_issues*) page="$SUBS" ;;
  *"--remove-label"* | *"--add-label"*) echo "\$*" >>"$WRITES"
    if [ -e "$BIN/live" ]; then
      set -- \$*
      jq --argjson n "\$3" --arg l "\${@: -1}" --arg op "\${@: -2:1}" '(.data.organization.projectV2.items.nodes[].content
        | select(.number == \$n).labels.nodes) |= if \$op == "--add-label" then . + [{name: \$l}] else map(select(.name != \$l)) end' \
        "$ITEMS" >"$ITEMS.new" && mv "$ITEMS.new" "$ITEMS"
    fi
    exit 0 ;;
  *"/labels -f labels[]="*) echo "\$*" >>"$WRITES"; echo '[]'; exit 0 ;;
  *"label list"*) page="$EMPTY" ;;
  *"label create"*) echo "\$*" >>"$WRITES"; exit 0 ;;
  *"-X PUT"*"/contents/"*) [ ! -e "$BIN/put-conflicts" ] || { echo "gh: docs/vision.md does not match 0123abc (HTTP 409)" >&2; exit 1; }
                          echo "PUT \$*" >>"$WRITES"; cat >"$BIN/put.json"; echo '{}'; exit 0 ;;
  *"--input -"*) cat >"$BIN/mutation.json"; echo '{}'; exit 0 ;;
  *"pulls?head="*"state=open"*) page="$VOPEN" ;;
  *"pulls?head="*"state=closed"*) page="$VCLOSED" ;;
  *"commits?path="*) page="$VCOMMITS" ;;
  *VisionThemes*) page="$THEMES" ;;
  *"/files"*) page="$FILES" ;;
  *"/contents/"*"?ref="*) page="$CONTENTS" ;;
  *vnd.github.raw*) [ ! -e "$WORK/docs-down" ] || { echo "gh: Bad Gateway (HTTP 502)" >&2; exit 1; }; [ ! -e "$WORK/no-docs" ] || { echo "gh: Not Found (HTTP 404)" >&2; exit 1; }; cat "$VISION_MD"; exit 0 ;;
  *"commits/HEAD/check-runs"*) page="$MAIN_RUNS" ;;
  *check-runs*) page="$RUNS" ;;
  *"repos/mentaldesk/demo --jq .default_branch"*) echo main; exit 0 ;;
  *IssueEvents*) printf '%s\n' "\$@" >"$BIN/caught-args"; echo >>"$BIN/caught-calls"; page="$CAUGHT" ;;
  *issueOrPullRequest*) page="$TALK" ;;
  *"isDraft body labels"*) page="$REVIEWING" ;;
  *": pullRequest(number"*) page="$UNLINKED" ;;
  *closedByPullRequestsReferences*) page="$PRS" ;;
  *reviews*) page="$REVIEWS" ;;
  *"/issues/comments?since"*) page="$RECENT" ;;
  *"repos/mentaldesk/style/pulls/comments?since"*) page="$UPSTREAM_LINES" ;;
  *"comments?since"*) page="$EMPTY" ;;
  *"-X POST"*dependencies/blocked_by*) [ ! -e "$BIN/link-refused" ] || { echo "gh: Resource not accessible by integration (HTTP 403)" >&2; exit 1; }
                                      echo "POST \$*" >>"$WRITES"; echo '{}'; exit 0 ;;
  *"-X DELETE"*dependencies/blocked_by*) echo "DELETE \$*" >>"$WRITES"; echo '{}'; exit 0 ;;
  *dependencies/blocked_by*) page="$BLOCKED" ;;
  *"/issues/"*"/comments"*) page="$THREAD" ;;
  *"/pulls/"*"/comments"*) page="$LINE" ;;
  *"-X PUT"*"/update-branch"*) [ ! -e "$BIN/update-fails" ] || { echo "gh: merge conflict between base and head (HTTP 422)" >&2; exit 1; }
                              echo "PUT \$*" >>"$WRITES"; echo '{}'; exit 0 ;;
  *"-X PUT"*"/merge"*) [ ! -e "$BIN/merge-fails" ] || { echo "gh: Pull Request is not mergeable (HTTP 405)" >&2; exit 1; }
                      [ ! -e "$BIN/merge-moved" ] || { echo "gh: Head branch was modified (HTTP 409)" >&2; exit 1; }
                      echo "PUT \$*" >>"$WRITES"; echo '{}'; exit 0 ;;
  *"-X DELETE"*"/git/refs/"*) echo "DELETE \$*" >>"$WRITES"; echo '{}'; exit 0 ;;
  *"/issues/404"*) echo "gh: Not Found (HTTP 404)" >&2; exit 1 ;;
  *"/contents/"*) [ ! -e "$WORK/docs-down" ] || { echo "gh: Bad Gateway (HTTP 502)" >&2; exit 1; }; [ ! -e "$WORK/no-docs" ] || { echo "gh: Not Found (HTTP 404)" >&2; exit 1; }; exit 0 ;;
  *"/pulls/"[0-9]*) [ ! -e "$BIN/not-a-pull" ] || { echo "gh: Not Found (HTTP 404)" >&2; exit 1; }
    [ ! -e "$BIN/pull-fails" ] || { echo "gh: API rate limit exceeded (HTTP 403)" >&2; exit 1; }; page="$PULL" ;;
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
# The detail sync asks for the default branch's CI and what blocks each task in the same call.
case " \$* " in
  *defaultBranchRef*)
    jq --slurpfile main "$MAIN_RUNS" --slurpfile blocked "$PREREQUISITES" '.data.repository += (\$blocked[0].data.repository // {})
      | .data.repository.defaultBranchRef = {name: "main", target: {oid: (\$main[0].check_runs[0].head_sha // ""),
          checkSuites: {nodes: [{checkRuns: {nodes: (\$main[0].check_runs | map({name, status: (.status | ascii_upcase),
            conclusion: (.conclusion | if . == null then null else ascii_upcase end),
            completedAt: .completed_at, url: .html_url}))}}]}}}' "\$page" >"\$page.detail"
    page="\$page.detail" ;;
esac
filter=
while [ \$# -gt 0 ]; do
  [ "\$1" = --jq ] && { filter=\$2; break; }
  shift
done
# A board page holds every item unless short-reads says how many reads come back short, and with how many items.
if [ "\$page" = "$ITEMS" ]; then
  [ ! -e "$BIN/read-fails" ] || { echo "gh: HTTP 502" >&2; exit 1; }
  jq '.data.organization.projectV2.items |= (.totalCount //= (.nodes | length))' "\$page" >"\$page.counted"
  if read -r times keep 2>/dev/null <"$BIN/short-reads" && [ "\$times" -gt 0 ]; then
    echo "\$((times - 1)) \$keep" >"$BIN/short-reads"
    jq --argjson keep "\$keep" '.data.organization.projectV2.items.nodes |= .[:\$keep]' "\$page.counted" >"\$page.short"
    mv "\$page.short" "\$page.counted"
  fi
  page="\$page.counted"
fi
if [ -n "\$filter" ]; then jq -r "\$filter" "\$page"; else cat "\$page"; fi
SH
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

# docs_pr_on_board <on|off>: PR #912 on the board as the Customer lead's docs PR, or off it again.
docs_pr_on_board() {
  jq --arg on "$1" '.data.organization.projectV2.items.nodes |= (map(select(.id != "PVTI_912")) + if $on == "on" then [{id: "PVTI_912",
    fieldValueByName: {name: "In review"}, content: {__typename: "PullRequest", id: "PR_912", number: 912, title: "Docs",
      url: "https://github.com/mentaldesk/demo/pull/912", state: "OPEN", repository: {nameWithOwner: "mentaldesk/demo"},
      labels: {nodes: [{name: "a-team:customer"}]}}}] else [] end)' "$ITEMS" >"$ITEMS.new" && mv "$ITEMS.new" "$ITEMS"
}

# What the record keeps of #<n> beyond Status and Priority.
marks() { sqlite3 "$A_TEAM_STATE/history.db" "SELECT COALESCE(marks, '{}') FROM board WHERE team = 'demo' AND item = $1;" | jq -c .; }

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

# The check runs on the default branch's head commit, from lines as gh_runs reads them; none, by default.
gh_main_runs() {
  local runs=$RUNS
  RUNS=$MAIN_RUNS
  gh_runs
  RUNS=$runs
  jq '.check_runs |= map(. + {head_sha: "b8f5bdd0c0ffee"})' "$MAIN_RUNS" >"$MAIN_RUNS.new" && mv "$MAIN_RUNS.new" "$MAIN_RUNS"
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
    def node($rows; $number; $url):
      ($rows | map(select(.kind == "body")) | first) as $body
      | {number: $number, createdAt: $body.at, body: ($body.body // ""),
         author: who($body.author // ""), reactions: {totalCount: 0},
         timelineItems: {nodes: ($rows | map(select(.kind == "labeled")
           | {createdAt: .at, label: {name: .body}, actor: who(.author)}))},
         comments: {nodes: ($rows | map(select(.kind == "comment")) | to_entries | map(.key as $i | .value
           | seen + {createdAt: .at, body: .body, url: "\($url)#issuecomment-\($i + 1)", author: who(.author)}))},
         reviews: {nodes: ($rows | map(select(.kind == "review" or .kind == "line")
           | if .kind == "review"
             then seen + {createdAt: .at, body: .body, state: "COMMENTED",
                   author: who(.author), comments: {nodes: []}}
             else {createdAt: .at, body: "", state: "COMMENTED", author: who(.author),
                   reactions: {totalCount: 0},
                   comments: {nodes: [seen + {createdAt: .at, body: .body,
                                              author: who(.author)}]}}
             end))}};
    def open_pr($rows; $number): node($rows; $number; "https://github.com/mentaldesk/demo/pull/\($number)")
      + {url: "https://github.com/mentaldesk/demo/pull/\($number)", isDraft: ($draft == "true"),
         mergeable: $mergeable, baseRefName: "main", headRefName: "feat/task-\($number - 900)", headRefOid: "deadbee"};
    split("\n") | map(select(length > 0)) | map(split(" ") as $f
      | {n: ($f[0] | tonumber), kind: ($f[1] | rtrimstr("+seen")), at: $f[2], author: $f[3],
         body: ($f[4:] | join(" ") | split("\\n") | join("\n")),
         seen: ($f[1] | endswith("+seen"))})
    | group_by(.n) | map(
        (map(select(.kind | startswith("pr-") | not))) as $own
        | (map(select(.kind | startswith("pr-")) | .kind |= ltrimstr("pr-"))) as $pr
        | {n: .[0].n, own: node($own; .[0].n; "https://github.com/mentaldesk/demo/issues/\(.[0].n)"),
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

# ago <minutes>: a timestamp that long before now, for the cases that meet board.sh's own clock.
ago() { jq -rn --argjson m "$1" 'now - $m * 60 | strftime("%Y-%m-%dT%H:%M:%SZ")'; }

# `gh_child <n> <parent> <labels> [body]`: the issue `link` and `unlink` read, a sub-issue of
# <parent> ("-" for none) on the record and on GraphQL, carrying the comma-separated <labels> ("-" for none).
# REST never reports the parent, as it doesn't for mentaldesk/website#79.
gh_child() {
  jq -n --argjson n "$1" --arg labels "$3" --arg body "${4:-Spotted while reviewing.}" '
    {id: (9000 + $n), number: $n, title: "The follow-up", body: $body,
     labels: (if $labels == "-" then [] else $labels | split(",") | map({name: .}) end),
     parent_issue_url: null}' >"$ISSUE"
  local parent=null
  [ "$2" = - ] || parent="{\"number\": $2}"
  edit_item "$1" ".parent = $parent"
  jq -n --argjson parent "$parent" '{data: {repository: {issueOrPullRequest: {parent: $parent}}}}' >"$BIN/parent.json"
  : >"$POSTED"
  : >"$WRITES"
  : >"$EDITED"
}

# A project whose Status field has the options a claim moves through.
claimable() {
  jq '.data.organization.projectV2.fields.nodes[0].options += [{id: "OPT_ready", name: "Ready"}, {id: "OPT_progress", name: "In progress"}]' \
    "$META" >"$META.new" && mv "$META.new" "$META"
}

# kept <command...>: a board command that keeps the record as the commands before it left it.
kept() { KEEP_BOARD=1 run "$@"; }

# history_lines <n>: #n's history, oldest first, as "<who> <what>" lines.
history_lines() { A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board demo history "$1" | jq -r '.events | reverse[] | "\(.who) \(.what)"'; }

# drag <n> <status>: someone, or GitHub's own automation, changes #n's card on the mirror.
drag() {
  jq --arg i "PVTI_$1" --arg s "$2" '(.data.organization.projectV2.items.nodes[] | select(.id == $i)).fieldValueByName =
      {name: $s, updatedAt: (now + 1 | todate)}' \
    "$ITEMS" >"$ITEMS.new" && mv "$ITEMS.new" "$ITEMS"
}

# add_card <n> <status|null> <title>: the Project adds an issue to itself, and sets that Status on it if it isn't null.
add_card() {
  jq --argjson n "$1" --arg s "$2" --arg title "$3" '.data.organization.projectV2.items.nodes += [(.data.organization.projectV2.items.nodes[0]
      | .id = "PVTI_\($n)" | .fieldValueByName = (if $s == "null" then null else {name: $s, updatedAt: (now + 1 | todate)} end)
      | .content |= (.id = "I_\($n)" | .number = $n | .title = $title | .url = "https://github.com/mentaldesk/demo/issues/\($n)"
          | .labels.nodes = [] | .issueFieldValues.nodes = []))]' "$ITEMS" >"$ITEMS.new" && mv "$ITEMS.new" "$ITEMS"
}

# statuses: each card's number and Status, as the last command printed them.
statuses() { jq -c 'map([.number, .status])' "$OUT"; }

every_status() {
  jq '.data.organization.projectV2.fields.nodes[0].options = ([["Idea", "idea"], ["Exploring", "exploring"], ["Pitched", "pitched"],
      ["Approved", "approved"], ["Building", "building"], ["Ready", "ready"], ["In progress", "progress"],
      ["In review", "review"], ["Done", "done"]] | map({id: "OPT_\(.[1])", name: .[0]}))' "$META" >"$META.new" && mv "$META.new" "$META"
}

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

# A Dependabot PR: it merges itself once it's green, and one already In review can still be accepted.
# `dependabot_pr <n>`: #<n> on the page gh_items wrote is a PR Dependabot opened.
dependabot_pr() {
  edit_item "$1" '.__typename = "PullRequest" | .author = {login: "dependabot"} | .labels.nodes = []
    | del(.issueDependenciesSummary, .issueFieldValues)'
}

days_ago() { ago $(($1 * 1440)); }

changed() { jq -n --arg at "$(days_ago "$1")" '[{commit: {committer: {date: $at}}}]' >"$VCOMMITS"; }

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
  fake "$TRY_BIN/gh" <<SH
#!/usr/bin/env bash
case " \$* " in
  *"/pulls/7"*) cat "$TRY_BIN/pull.json" ;;
  *" graphql "*)
    [ ! -e "$TRY_BIN/closes.fail" ] || { echo "gh: HTTP 502" >&2; exit 1; }
    cat "$TRY_BIN/closes.json" 2>/dev/null || true ;;
  *) echo "gh: Not Found (HTTP 404)" >&2; exit 1 ;;
esac
SH
  fake "$TRY_BIN/record" <<SH
#!/usr/bin/env bash
{ pwd; git rev-parse HEAD; echo "\${A_TEAM_STATE:-}"; echo "\${A_TEAM_DRY_RUN:-}"; } >"$TRY_WORK/ran"
SH
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

clean() { PATH=$CLEAN_PATH run board "$@" clean "$CLEAN_ROLE"; }

branches() { git -C "$CHECKOUT" branch --format='%(refname:short)' | grep -v '^main$'; }

# A run that counts the TERMs it gets in $SIGNALS, as the dev's run in $A_TEAM_STATE.
fake_run() {
  SIGNALS="$A_TEAM_STATE/signals"
  : >"$SIGNALS"
  mkdir -p "$A_TEAM_STATE/demo/dev"
  bash -c "trap 'echo TERM >>\"$SIGNALS\"; exit' TERM; while [ \$SECONDS -lt $FAKE_LIMIT ]; do sleep 0.1; done" &
  RUN_PID=$!
  echo "$RUN_PID" >"$A_TEAM_STATE/demo/dev/pid"
}

# backdate <file>...: as if written an hour ago, before any process the test starts.
backdate() { touch -t "$(jq -rn 'now - 3600 | strflocaltime("%Y%m%d%H%M.%S")')" "$@"; }

held() { jq -c '.dispatch.hold' "$TEAM"; }

history_db() { (source "$ROOT/scripts/common.sh" && history_sql "$@"); }

# runs_recorded: "<role> <item or -> <ended?> <cost or -> <outcome or ->" for each run and item it was for.
runs_recorded() {
  history_db -separator ' ' "SELECT role, COALESCE(item, '-'), ended IS NOT NULL, COALESCE(cost, '-'),
    COALESCE(outcome, '-') FROM runs LEFT JOIN run_items ON run_items.run = runs.id ORDER BY runs.id, item;"
}

# A dispatcher whose a-team answers `triggers dev` with $DEV_TRIGGERS and `triggers reviewer` with
# $REVIEWER_TRIGGERS, records each claim, and claims $CLAIMED. `task-prompt` is one line, and the Lead never has anything to do.
dev_dispatcher() {
  DISPATCH=$(mktemp -d "$WORK/dispatch.XXXXXX")
  mkdir -p "$DISPATCH/bin" "$DISPATCH/scripts"
  cp "$ROOT"/scripts/*.sh "$DISPATCH/scripts/"
  cp -R "$ROOT/settings" "$DISPATCH/"
  echo 0.1.7 >"$DISPATCH/VERSION"
  CLAIMS="$DISPATCH/claims" DEV_TRIGGERS="$DISPATCH/triggers.json" CLAIMED="$DISPATCH/claimed.json"
  CUSTOMER_TRIGGERS="$DISPATCH/customer.json" REVIEWER_TRIGGERS="$DISPATCH/reviewer.json" LEAD_TRIGGERS="$DISPATCH/lead.json"
  TRIGGER_READS="$DISPATCH/trigger-reads"
  : >"$CLAIMS"
  : >"$TRIGGER_READS"
  echo '{"reasons": [], "creative": false}' >"$LEAD_TRIGGERS"
  echo null >"$CLAIMED"
  echo '{"reasons": [], "creative": false}' >"$CUSTOMER_TRIGGERS"
  echo '{"reasons": [], "creative": false, "tasks": [], "ready": null, "chores": []}' >"$REVIEWER_TRIGGERS"
  FREES="$DISPATCH/frees" FREED="$DISPATCH/freed"
  : >"$FREES"
  : >"$FREED"
  BUMPS="$DISPATCH/bumps" BUMPED="$DISPATCH/bumped"
  : >"$BUMPS"
  : >"$BUMPED"
  UPDATES_RUN="$DISPATCH/updates-run" UPDATED_SAID="$DISPATCH/updated-said"
  : >"$UPDATES_RUN"
  : >"$UPDATED_SAID"
  fake "$DISPATCH/bin/a-team" <<SH
#!/usr/bin/env bash
case " \$* " in
  *" bumps "*) echo "\$* as \${A_TEAM_RUN_TEAM:-me} \${PATH%%:*}" >>"$BUMPS"; echo bumps >>"$FREES"; cat "$BUMPED"; [ ! -e "$BUMPED.fails" ] || { cat "$BUMPED.fails" >&2; exit 1; } ;;
  *" free "*) echo "\$* as \${A_TEAM_RUN_TEAM:-me} \${PATH%%:*}" >>"$FREES"; cat "$FREED" ;;
  *" update"*) echo "\$* as \${A_TEAM_RUN_TEAM:-me} \${PATH%%:*}" >>"$UPDATES_RUN"; cat "$UPDATED_SAID" ;;
  *" claim dev "*) echo "\$*" >>"$CLAIMS"; cat "$CLAIMED" ;;
  *" triggers dev"*) echo "\$*" >>"$TRIGGER_READS"; cat "$DEV_TRIGGERS" ;;
  *" triggers lead"*) cat "$LEAD_TRIGGERS" ;;
  *" triggers customer"*) echo "\$*" >>"$CLAIMS"; cat "$CUSTOMER_TRIGGERS" ;;
  *" triggers reviewer"*) echo "\$*" >>"$TRIGGER_READS"; cat "$REVIEWER_TRIGGERS" ;;
  *" task-prompt "*) echo "Run one shift." ;;
  *" version "*) cat "\$(dirname "\$0")/../VERSION" ;;
esac
SH
  A_TEAM_STATE=$(mktemp -d "$WORK/state.XXXXXX")
}

dispatch_dev() { A_TEAM_CONFIG="$CONFIG" bash "$DISPATCH/scripts/dispatch.sh" "$@"; }

devs() { jq --argjson n "$1" '.wip.devs = $n' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"; }

alive() { kill -0 "$(cat "$A_TEAM_STATE/demo/dev/runs/$1/pid")" 2>/dev/null; }

at() { sqlite3 :memory: "SELECT strftime('%Y-%m-%dT%H:%M:%SZ', $((midnight + $1)), 'unixepoch')"; }

today() { A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board demo today; }

seen() { for _ in $(seq 50); do [ -s "$SEEN/$1" ] && break; sleep 0.1; done; cat "$SEEN/$1" 2>/dev/null; }

# A claude that records what it was asked to resume, where, and what the run and the hold were by then.
fake_claude() {
  RESUMED="$BIN/resumed"
  : >"$RESUMED"
  fake "$BIN/claude" <<SH
#!/usr/bin/env bash
{ echo "args: \$*"; echo "cwd: \$(pwd -P)"; echo "hold: \$(jq -c .dispatch.hold "$TEAM")"
  echo "signals: \$(grep -c TERM "$SIGNALS")"; echo "team: \${A_TEAM_RUN_TEAM:-}"; } >>"$RESUMED"
exit ${1:-0}
SH
}

session_log() {
  printf '%s\n' 'not json' \
    '{"type":"system","subtype":"init","cwd":"/elsewhere","session_id":"sess-123"}' \
    '{"type":"assistant","session_id":"sess-123"}' >"$A_TEAM_STATE/demo/dev/latest.jsonl"
}

checked() { PATH="$APP_BIN/board:$PATH" run board demo check; }

# says <exit> <line>: check exited so, and printed exactly that one problem.
says() {
  same "exit" "$1" "$STATUS"
  same "problems" "$2" "$(grep -E '^(problem|note) {2,}' "$OUT")"
}

# History: what `a-team board` did to each item, in $A_TEAM_STATE/history.db.
history_table() {
  A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board demo history "$1" | jq -r '.events[] | "\(.who) \(.what)"'
}

recorded() {
  same "$1 exit" 0 "$STATUS"
  same "$1 history" "$3" "$(history_table "$2")"
  rm -f "$A_TEAM_STATE/history.db"
}

unrecorded() {
  same "$1: recorded" "" "$(sqlite3 "$A_TEAM_STATE/history.db" 'SELECT * FROM events' 2>/dev/null)"
}

trend() { A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board demo trend | jq -c .; }

queue() { sqlite3 "$A_TEAM_STATE/history.db" "SELECT waiting FROM queue WHERE team = 'demo' ORDER BY at"; }

# events <n> <minutes ago> <who> <what>, one per line: a fresh record of demo's events, timed from $base.
events() {
  rm -f "$A_TEAM_STATE/history.db"
  local values=() n m who what
  while read -r n m who what; do values+=("('demo', $n, '$(before "$m")', '$who', '$what')"); done
  sqlite3 "$A_TEAM_STATE/history.db" "CREATE TABLE events (id INTEGER PRIMARY KEY, team TEXT NOT NULL, item INTEGER NOT NULL,
      at TEXT NOT NULL, who TEXT NOT NULL, what TEXT NOT NULL);
    INSERT INTO events (team, item, at, who, what) VALUES $(IFS=,; echo "${values[*]}");"
}

# pitch <n>: pitch n pitched and approved with nothing said, and finished, the rest of the record added on stdin.
pitch() {
  printf '%s\n' "$1 900 lead Exploring → Pitched" "$1 800 you Pitched → Approved" "$1 100 lead closed as done"
}

# A decision file: its first line, then why.
said() { printf '%s\n' "$@" >"$WORK/decision"; }

ids() { A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board "${1:-demo}" decisions "${@:2}" | jq -c '[.[].id]'; }

installed() { jq -nc --argjson dry "$1" --argjson at $(($(date +%s) - $2)) \
  '{bin: "/Users/me/code/a-team/palette/bin/a-team", version: "0.1.13", dryRun: $dry, interval: 120, installedAt: $at}'; }

# dispatcher_line for record $4 and prefix $5 reads $2 and a countdown from $3s, less up to however long it took.
counting_down() {
  local start line s
  start=$(date +%s)
  line=$(dispatcher_line "$4" "$3" "${5:-}")
  for ((s = $3; s >= $3 - ($(date +%s) - start); s--)); do
    [ "$line" = "$2 $((s / 60)):$(printf %02d $((s % 60)))" ] && return
  done
  same "$1" "$2 $(($3 / 60)):$(printf %02d $(($3 % 60)))" "$line"
}

# released <head> [<tag commit> <seconds ago>]: main at <head>, and v1.2.0 at <tag commit>, released that long ago.
released() {
  jq -n --arg head "$1" --arg tag "${2:-}" --argjson ago "${3:-0}" '{data: {repository: {
    defaultBranchRef: {name: "main", target: {oid: $head}},
    latestRelease: (if $tag == "" then null
      else {tagName: "v1.2.0", createdAt: (now - $ago | todate), tagCommit: {oid: $tag}} end)}}}' >"$REL/repo.json"
}

pass() { A_TEAM_CONFIG="$CONFIG" bash "$APP/scripts/dispatch.sh" --dry-run; }

refused_review() {
  : >"$WRITES"
  run board demo review reviewer 912 "$WORK/review"
  failed "$1"
  one_line "$1"
  grep -qF "$2" "$ERR" || fail "$1: '$(cat "$ERR")'"
  same "$1 writes" "" "$(cat "$WRITES")"
}

picture_link() { echo "https://github.com/mentaldesk/demo/blob/a-team/attachments/$MONTH/$(shasum -a 256 <"$1" | cut -c1-16).png?raw=true"; }

# What #11 waits on in other repos, as the team's comments on it record them: lines of "<owner/repo#n> <until> [<package>]".
waits() {
  local ref until package rows=''
  while read -r ref until package; do
    rows+="comment 2026-10-10T09:00:00Z demo-app[bot] 0 Blocked by $ref.\n\n<!-- a-team:wait repo=${ref%#*} n=${ref#*#} until=$until${package:+ package=$package} -->"$'\n'
  done
  printf '%s' "$rows" | gh_thread
}

contains() { printf '%s behind\n' "$@" >"$BIN/contains"; }

stamp() { sqlite3 "$FORMAT_STATE/history.db" "SELECT format, version, moved IS NOT NULL FROM formats WHERE team = 'demo';" 2>/dev/null; }

# live_run <dir under the role> <started seconds ago> [ignores]: a live run on record, in $FAKE; `ignores` makes it ignore SIGTERM.
live_run() {
  local dir="$A_TEAM_STATE/demo/$1"
  mkdir -p "$dir"
  if [ "${3:-}" = ignores ]; then
    (trap '' TERM; exec -a "$WORK/live-run" sleep 300) >/dev/null 2>&1 &
  else
    (exec -a "$WORK/live-run" sleep 300) >/dev/null 2>&1 &
  fi
  FAKE=$!
  echo "$FAKE" >"$dir/pid"
  echo $(($(date +%s) - $2)) >"$dir/last-start"
}

logged() { grep -c "$1" "$A_TEAM_STATE/dispatch.log"; }

stopped() { kill -0 "$(cat "$SUITE/pids")" 2>/dev/null && fail "$1: still running"; }

# A Keychain holding a real test key, and GitHub's App endpoints, stubbed on PATH. `security` has
# no key when NO_KEY is set; `curl` records each mint in $MINTS, keeps the JWT it was sent in
# $APP_BIN/jwt, grants what $APP_BIN/perms.json holds, turns down every JWT when MINT_FAILS is set,
# and finds no installation when NOT_INSTALLED is.
app_fakes() {
  APP_BIN=$(mktemp -d "$WORK/app.XXXXXX")
  MINTS="$APP_BIN/mints" OPENED="$APP_BIN/opened"
  openssl genrsa 2048 2>/dev/null >"$APP_BIN/key.pem"
  openssl rsa -in "$APP_BIN/key.pem" -pubout 2>/dev/null >"$APP_BIN/pub.pem"
  openssl base64 -A <"$APP_BIN/key.pem" >"$APP_BIN/key.b64"
  rm "$APP_BIN/key.pem"
  echo '{"contents": "write", "issues": "write", "organization_projects": "write"}' >"$APP_BIN/perms.json"
  fake "$APP_BIN/security" <<SH
#!/usr/bin/env bash
[ -z "\${NO_KEY:-}" ] || exit 44
case " \$* " in *" -w "*) cat "$APP_BIN/key.b64" ;; esac
SH
  fake "$APP_BIN/curl" <<SH
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
  printf '#!/usr/bin/env bash\necho "$*" >>"%s"\n' "$OPENED" | fake "$APP_BIN/open"
  PATH="$APP_BIN:$PATH"
}
