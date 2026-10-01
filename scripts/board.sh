#!/usr/bin/env bash
#
# board.sh — the only way a-team agents read or change a team's project board.
# Commands are documented in process.md.
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
STATES=("Idea" "Exploring" "Pitched" "Approved" "Building" "Ready" "In progress" "In review" "Done")

die() { echo "board.sh: $*" >&2; exit 1; }

DRY_RUN=${A_TEAM_DRY_RUN:-}
SWEEP=
if [ "${1:-}" = --dry-run ]; then
  DRY_RUN=1
  shift
fi

# write <what> <command...>: every change to GitHub goes through here, so --dry-run can skip it.
write() {
  local what=$1
  shift
  if [ -n "$DRY_RUN" ]; then
    echo "dry-run: would $what" >&2
    return 0
  fi
  "$@"
}

say() { echo "${DRY_RUN:+(dry run) }$*"; }

[ $# -ge 2 ] || die "usage: board.sh [--dry-run] <team> <command> [args...] (see process.md)"
TEAM=$1 CMD=$2
shift 2
source "$ROOT/scripts/common.sh"
CONFIG=$(team_config "$TEAM")
[ -f "$CONFIG" ] || die "no config for team '$TEAM' at $CONFIG (start from examples/team.json)"

cfg() { jq -r "$1 // empty" "$CONFIG"; }
REPO=$(cfg .repo)
OWNER=$(cfg .project.owner)
NUMBER=$(cfg .project.number)
FIELD=$(cfg .project.statusField)
FIELD=${FIELD:-Status}
PRIORITY=$(cfg .priorityField)
PRIORITY=${PRIORITY:-Priority}
STAKEHOLDERS=$(jq -c '.stakeholders // [.reviewer // empty]' "$CONFIG")
[ -n "$NUMBER" ] || die "project.number is not set in $CONFIG"

# A stakeholder comment is answered once a run has left a 👀 on it. ACK_FROM is when that started;
# older comments keep the marker-time watermark, so an upgrade doesn't reopen answered history.
# Delete it, and the $ackFrom halves of `said` and `unanswered`, once no open item predates it.
ACK_FROM=2026-09-24T00:00:00Z
# Since APP_FROM the team speaks only as its App, so whose words a comment is comes from its author.
# Before it the team spoke from its stakeholder's account, and the marker alone on the last line said so.
APP_FROM=2026-09-29T00:00:00Z
BOT=$(cfg .app.slug)
BOT=${BOT:+${BOT}[bot]}

# login: an author as REST spells it. GraphQL leaves a bot's "[bot]" off its login.
LOGIN='if . == null then "" elif .__typename == "Bot" then "\(.login)[bot]" else .login end'

# team($m): the team wrote this, as the role whose marker is $m.
TEAM_SAID='def team($m): if $bot != "" and .author == $bot then (.body // "") | contains($m)
    elif .at >= $appFrom then false
    else (.body // "") | gsub("\r"; "") | split("\n") | map(sub("[ \t]+$"; ""))
         | map(select(. != "")) | last // "" | startswith($m) end;'

# said($m): when the role last spoke on this thread. Before ACK_FROM a marker anywhere counted, and
# that history keeps reading as it did.
# unanswered($since): of comments shaped {at, author, body, eyes, kind?}, the ones a stakeholder is
# owed an answer to. $since is the role's own newest comment, which only ACK_FROM's tail needs.
UNANSWERED='def said($m): map(select(team($m) or (.at < $ackFrom and (.body | contains($m))))
    | .at) | max // "";
  def unanswered($since): map(select(
    .kind != "body" and (.author | IN($stakeholders[])) and (team("<!-- a-team:") | not)
    and (.eyes // 0) == 0 and (.at >= $ackFrom or .at > $since)));'

# since($at): " since <when>", the time alone if it was today; nothing for no time.
SINCE='def stamp: fromdateiso8601
      | if strflocaltime("%Y-%m-%d") == (now | strflocaltime("%Y-%m-%d"))
        then strflocaltime("%H:%M") else strflocaltime("%d %b %H:%M") end;
  def since($at): if $at == "" then "" else " since \($at | stamp)" end;'

# closes: the issues a Dev PR's body closes, for when GitHub hasn't linked them.
CLOSES='def closes: if (.body // "") | contains("<!-- a-team:dev -->")
    then [.body | scan("(?i)\\b(?:close[sd]?|fix(?:e[sd])?|resolve[sd]?):?[ \t]+#([0-9]+)\\b") | .[0] | tonumber]
    else [] end;'

is_state() {
  local s
  for s in "${STATES[@]}"; do [ "$s" = "$1" ] && return 0; done
  return 1
}

allowed() {
  # Both moves out of Pitched are gated again in `move`: Idea only with unanswered stakeholder
  # feedback, Exploring only for a pitch `lead-next` names in `demote`.
  case "$1:$2>$3" in
    "lead:Idea>Exploring" | "lead:Exploring>Pitched" | "lead:Exploring>Idea" | \
    "lead:Pitched>Idea" | "lead:Pitched>Exploring" | \
    "lead:Approved>Building" | "lead:Building>In review" | \
    "lead:None>Idea" | "lead:None>Exploring" | "lead:None>Pitched" | "lead:None>Ready" | \
    "dev:Ready>In progress" | "dev:In progress>In review" | "dev:In progress>Ready")
      return 0 ;;
  esac
  return 1
}

check_role() {
  case "$1" in lead | dev) ;; *) die "unknown role '$1' (lead | dev)" ;; esac
}

own_label() {
  case "$1" in lead) echo pitch ;; dev) echo a-team:dev ;; esac
}

KIND=$(cfg .project.ownerType)
KIND=${KIND:-organization}

gql() {
  local query=$1
  shift
  gh api graphql -F owner="$OWNER" -F number="$NUMBER" "$@" -f query="$query"
}

items() {
  gql "query(\$owner: String!, \$number: Int!, \$field: String!, \$endCursor: String) {
      $KIND(login: \$owner) { projectV2(number: \$number) {
        items(first: 100, after: \$endCursor) {
          pageInfo { hasNextPage endCursor }
          nodes {
            id
            fieldValueByName(name: \$field) { ... on ProjectV2ItemFieldSingleSelectValue { name } }
            content {
              __typename
              ... on Issue { number title url repository { nameWithOwner } labels(first: 20) { nodes { name } }
                             issueDependenciesSummary { blockedBy }
                             issueFieldValues(first: 20) { nodes { ... on IssueFieldSingleSelectValue {
                               name field { ... on IssueFieldSingleSelect { name } } } } } }
              ... on PullRequest { number title url repository { nameWithOwner } labels(first: 20) { nodes { name } } }
            } } } } } }" -F field="$FIELD" --paginate |
    jq -s --arg kind "$KIND" --arg repo "$REPO" --arg priority "$PRIORITY" \
      --argjson states "$(printf '%s\n' "${STATES[@]}" | jq -R . | jq -s .)" \
      --slurpfile cfg "$CONFIG" '
      ($cfg[0].project.statusMap // {} | to_entries | map({key: .value, value: .key}) | from_entries) as $rev
      | [.[].data[$kind].projectV2.items.nodes[]
         | select(.content.repository.nameWithOwner == $repo)
         | .fieldValueByName.name as $raw
         | {
             id,
             number: .content.number,
             type: .content.__typename,
             title: .content.title,
             url: .content.url,
             labels: [.content.labels.nodes[].name],
             blockedBy: (.content.issueDependenciesSummary.blockedBy // 0),
             priority: ([.content.issueFieldValues.nodes[]? | select(.field.name == $priority) | .name] | first),
             status: (if $raw == null then "None"
                      elif $rev[$raw] then $rev[$raw]
                      elif ($states | index($raw)) then $raw
                      else "?" + $raw end)
           }]'
}

item() {
  items | jq --argjson n "$1" 'map(select(.number == $n)) | first // empty'
}

project_meta() {
  gql "query(\$owner: String!, \$number: Int!, \$field: String!) {
      $KIND(login: \$owner) { projectV2(number: \$number) { id
        field(name: \$field) { ... on ProjectV2SingleSelectField { id options { id name color description } } } } } }" \
    -F field="$FIELD" --jq ".data.$KIND.projectV2"
}

set_status() {
  local item_id=$1 state=$2 option meta option_id
  option=$(jq -r --arg s "$state" '.project.statusMap[$s] // $s' "$CONFIG")
  meta=$(project_meta)
  jq -e '.field.id' <<<"$meta" >/dev/null || die "no single-select field '$FIELD' on $OWNER project $NUMBER"
  option_id=$(jq -r --arg o "$option" '.field.options[] | select(.name == $o) | .id' <<<"$meta")
  [ -n "$option_id" ] || die "field '$FIELD' has no option '$option' (run: board.sh $TEAM check)"
  write "set item $item_id to '$option'" gh api graphql -F project="$(jq -r .id <<<"$meta")" -F item="$item_id" \
    -F field="$(jq -r .field.id <<<"$meta")" -F option="$option_id" -f query='
    mutation($project: ID!, $item: ID!, $field: ID!, $option: String!) {
      updateProjectV2ItemFieldValue(input: {projectId: $project, itemId: $item, fieldId: $field,
                                            value: {singleSelectOptionId: $option}}) { clientMutationId } }' >/dev/null
}

# The organisation's Priority field: its id, and its options, which are the values it takes.
priority_field() {
  gh api graphql -F owner="$OWNER" -f query='query($owner: String!) {
      organization(login: $owner) { issueFields(first: 50) { nodes {
        ... on IssueFieldSingleSelect { id name options { id name } } } } } }' |
    jq --arg f "$PRIORITY" '[.data.organization.issueFields.nodes[] | select(.name == $f)] | first // empty'
}

# Reads a JSON array of items on stdin; adds .priority and sorts highest first, unset last.
by_priority() {
  local list ranks values
  list=$(cat)
  if [ "$(jq length <<<"$list")" -eq 0 ]; then echo '[]'; return; fi
  ranks=$(priority_field | jq -s '[.[0].options[]?.name]')
  values=$(gh api graphql -F owner="${REPO%/*}" -F name="${REPO#*/}" -f query="query(\$owner: String!, \$name: String!) {
      repository(owner: \$owner, name: \$name) {
        $(jq -r '.[] | "i\(.number): issue(number: \(.number)) { issueFieldValues(first: 20) { nodes {
          ... on IssueFieldSingleSelectValue { name field { ... on IssueFieldSingleSelect { name } } } } } }"' <<<"$list")
      } }" | jq --arg f "$PRIORITY" '.data.repository | with_entries(
        .key |= ltrimstr("i") | .value = ([.value.issueFieldValues.nodes[] | select(.field.name == $f) | .name] | first))')
  jq --argjson ranks "$ranks" --argjson values "$values" '
    map(.priority = $values[.number | tostring]) | sort_by(.priority as $p | $ranks | index($p) // length)' <<<"$list"
}

# An issue an agent wrote, from any team, carries its marker in the body.
agent_written() {
  gh api "repos/$REPO/issues/$1" --jq .body | grep -qE '<!-- a-team:(lead|dev) -->'
}

# The number of the issue #1 is a sub-issue of, or nothing.
parent_of() {
  gh api "repos/$REPO/issues/$1" --jq '.parent_issue_url // empty | split("/") | last'
}

# The Idea the Lead should pitch next: a stakeholder's own, or any a stakeholder has prioritised,
# passing over the ones the Lead has skipped and a stakeholder hasn't since commented on.
pitchable_idea() {
  local candidate
  while IFS= read -r candidate; do
    if jq -e '.labels | index("a-team:skipped")' <<<"$candidate" >/dev/null &&
      [ "$(unanswered_feedback lead "$(jq -r .number <<<"$candidate")" | jq length)" -eq 0 ]; then
      continue
    fi
    if [ "$(jq -r .priority <<<"$candidate")" = null ] &&
      { jq -e '.labels | index("a-team:idea")' <<<"$candidate" >/dev/null ||
        agent_written "$(jq -r .number <<<"$candidate")"; }; then
      continue
    fi
    echo "$candidate"
    return
  done < <(jq 'map(select(.status == "Idea" and .type == "Issue"))' <<<"$1" | by_priority | jq -c '.[]')
  echo null
}

# One swap set for Pitched, from all board items: `promote` are the Exploring pitches that
# belong in Pitched, `demote` the Pitched ones they displace, paired in order. Sorting Pitched
# first makes `by_priority`'s stable sort displace only on a strictly higher priority, and
# `demote` never outruns `promote`, so nothing leaves Pitched without a draft taking its slot.
pitch_swap() {
  jq -c '[.[] | select((.labels | index("pitch")) and (.status == "Pitched" or .status == "Exploring"))]
         | sort_by(.status != "Pitched")' <<<"$1" | by_priority |
    jq --argjson limit "$(cfg '.wip.pitched')" '
      ([.[] | select(.status == "Pitched")] | length) as $pitched
      | if $pitched < $limit
        then {promote: [.[] | select(.status == "Exploring")][:$limit - $pitched], demote: []}
        else [.[:$limit][] | select(.status == "Exploring")] as $promote
          | [.[$limit:][] | select(.status == "Pitched")] as $displaced
          | {promote: $promote,
             demote: $displaced[($displaced | length) - ($promote | length):]}
        end'
}

pr_for() {
  gh api graphql -F owner="${REPO%/*}" -F name="${REPO#*/}" -F n="$1" -f query='
    query($owner: String!, $name: String!, $n: Int!) {
      repository(owner: $owner, name: $name) {
        issue(number: $n) {
          closedByPullRequestsReferences(first: 10, includeClosedPrs: false) {
            nodes { number url isDraft headRefName mergeable }
          }
        }
        pullRequests(states: OPEN, first: 100) {
          nodes { number url isDraft headRefName mergeable body }
        }
      }
    }' | jq -c --argjson n "$1" "$CLOSES"'.data.repository
      | .issue.closedByPullRequestsReferences.nodes[0]
        // ((.pullRequests.nodes // []) | map(select(any(closes[]; . == $n))) | first | del(.body))'
}

# ci <pr> [sha]: the CI verdict for <pr>, reading its head commit where the caller already knows it.
ci() {
  local sha=${2:-}
  [ -n "$sha" ] || sha=$(gh api "repos/$REPO/pulls/$1" --jq .head.sha) || die "could not read PR #$1"
  gh api --paginate "repos/$REPO/commits/$sha/check-runs?per_page=100" --jq '.check_runs[]' |
    jq -s '
      def failed: .conclusion as $c
        | ["failure", "timed_out", "cancelled", "action_required", "startup_failure"] | index($c);
      {
        verdict: (if any(.[]; failed) then "fail"
                  elif length == 0 or any(.[]; .status != "completed") then "pending"
                  else "pass" end),
        failing: map(select(failed) | {name, at: .completed_at, link: .html_url}),
        pending: map(select(.status != "completed") | .name)
      }'
}

# Conversation and line comments across the whole repo from the last day, as
# {n, author, at, body, eyes}. Both payloads carry the reaction count inline, so the 2-minute
# trigger path reads a number it is already being handed.
recent_comments() {
  local since
  since=$(jq -rn 'now - 86400 | strftime("%Y-%m-%dT%H:%M:%SZ")')
  {
    gh api --paginate "repos/$REPO/issues/comments?since=$since&per_page=100" \
      --jq '.[] | {n: (.issue_url | split("/") | last | tonumber), author: .user.login, at: .created_at, body: (.body // ""), eyes: .reactions.eyes}'
    gh api --paginate "repos/$REPO/pulls/comments?since=$since&per_page=100" \
      --jq '.[] | {n: (.pull_request_url | split("/") | last | tonumber), author: .user.login, at: .created_at, body: (.body // ""), eyes: .reactions.eyes}'
  } | jq -s .
}

# reviews <pr>: the PR's review summaries, as {kind, state, author, at, body, url, id, eyes}.
# GraphQL rather than REST, which carries no reaction count for a review.
reviews() {
  gh api graphql -F owner="${REPO%/*}" -F name="${REPO#*/}" -F number="$1" -f query='
    query($owner: String!, $name: String!, $number: Int!) {
      repository(owner: $owner, name: $name) { pullRequest(number: $number) {
        reviews(last: 50) { nodes { id url state body submittedAt author { __typename login }
          reactions(content: EYES) { totalCount } } } } } }' \
    --jq '.data.repository.pullRequest.reviews.nodes[]
          | select((.body // "") != "" or .state == "CHANGES_REQUESTED")
          | {kind: "review", state, author: (.author | '"$LOGIN"'), at: .submittedAt,
             body: (.body // ""), url, id, eyes: .reactions.totalCount}'
}

pr_reviews() { reviews "$1" | jq -s --argjson n "$1" 'map(. + {n: $n})'; }

# awaiting <comments> <role> <n>: the time of a stakeholder's newest unanswered comment on #n, or
# nothing. It goes into the trigger so new feedback never looks like a retry.
awaiting() {
  jq -r --argjson n "$3" --arg marker "<!-- a-team:$2 -->" --argjson stakeholders "$STAKEHOLDERS" \
    --arg ackFrom "$ACK_FROM" --arg appFrom "$APP_FROM" --arg bot "$BOT" "$TEAM_SAID$UNANSWERED"'
    map(select(.n == $n)) | said($marker) as $since
    | unanswered($since) | map(.at) | max // empty' <<<"$1"
}

# gated_talk <items>: one page holding the body, comments and open PR of every item, in one call
# whatever the number of them, plus one more for any PR GitHub hasn't linked to its task. The open
# PR that closes a task comes back nested under the task's own number: a stakeholder answers a task
# on either. `waiting` reads whose turn it is off this page.
gated_talk() {
  local said reviewed n pr page unlinked prs query=''
  local seen='reactions(content: EYES) { totalCount }'
  said="number createdAt body author { __typename login }
        comments(last: 50) { nodes { createdAt body author { __typename login } $seen } }"
  reviewed="$said"" url isDraft mergeable baseRefName headRefOid
              reviews(last: 50) { nodes { createdAt body state author { __typename login } $seen
              comments(first: 50) { nodes { createdAt body author { __typename login } $seen } } } }"
  for n in $(jq -r '.[].number' <<<"$1"); do
    query+=" x$n: issueOrPullRequest(number: $n) {
      ... on Issue { $said
        timelineItems(last: 50, itemTypes: [LABELED_EVENT]) { nodes { ... on LabeledEvent { createdAt label { name } } } }
        closedByPullRequestsReferences(first: 1, includeClosedPrs: false) { nodes { $reviewed } } }
      ... on PullRequest { $reviewed } }"
  done
  [ -n "$query" ] || { echo '{"data": {"repository": {}}}'; return; }
  page=$(gh api graphql -F owner="${REPO%/*}" -F name="${REPO#*/}" \
    -f query="query(\$owner: String!, \$name: String!) { repository(owner: \$owner, name: \$name) {$query
      openPrs: pullRequests(states: OPEN, first: 100) { nodes { number body } } } }") || return
  unlinked=$(jq -c "$CLOSES"'.data.repository | (.openPrs.nodes // []) as $open
    | [to_entries[].value | select(.closedByPullRequestsReferences.nodes? == []) | .number as $n
       | $open[] | select(any(closes[]; . == $n)) | {n: $n, pr: .number}] | unique_by(.n)' <<<"$page")
  query=''
  for pr in $(jq -r '[.[].pr] | unique[]' <<<"$unlinked"); do
    query+=" p$pr: pullRequest(number: $pr) { $reviewed }"
  done
  prs='{"data": {"repository": {}}}'
  if [ -n "$query" ]; then
    prs=$(gh api graphql -F owner="${REPO%/*}" -F name="${REPO#*/}" \
      -f query="query(\$owner: String!, \$name: String!) { repository(owner: \$owner, name: \$name) {$query} }") || return
  fi
  jq --argjson unlinked "$unlinked" --argjson prs "$prs" '
    reduce $unlinked[] as $u (.; .data.repository["x\($u.n)"].closedByPullRequestsReferences.nodes
      = [$prs.data.repository["p\($u.pr)"] // empty])
    | del(.data.repository.openPrs)' <<<"$page"
}

# gated_comments <talk>: everything said on those items, as {n, at, author, body, eyes, kind}.
gated_comments() {
  jq "def login: $LOGIN;"'
      def who: {at: .createdAt, author: (.author | login), body: (.body // ""),
                eyes: (.reactions.totalCount // 0)};
      def talk:
        (who + {kind: "body"}),
        (.comments.nodes[]? | who + {kind: "comment"}),
        (.reviews.nodes[]? |
          (select((.body // "") != "" or .state == "CHANGES_REQUESTED") | who + {kind: "review"}),
          (.comments.nodes[]? | who + {kind: "line"}));
      [.data.repository | to_entries[].value | select(. != null) | .number as $n
       | (talk, (.closedByPullRequestsReferences.nodes[]? | talk))
       | . + {n: $n}]' <<<"$1"
}

# gated_prs <talk>: the open PR that closes each of those items, as {n, pr, prUrl, draft,
# conflicting, base, sha}. UNKNOWN means GitHub hasn't finished computing it, so only CONFLICTING
# counts as a conflict.
gated_prs() {
  jq '[.data.repository | to_entries[].value | select(. != null) | .number as $n
       | .closedByPullRequestsReferences.nodes[]?
       | {n: $n, pr: .number, prUrl: .url, draft: .isDraft,
          conflicting: (.mergeable == "CONFLICTING"), base: .baseRefName, sha: .headRefOid}]' <<<"$1"
}

# gated_blocked <talk>: when each of those items was last labelled `blocked`, as {"<n>": at}.
gated_blocked() {
  jq '[.data.repository | to_entries[].value | select(. != null)
       | {key: (.number | tostring),
          value: ([.timelineItems.nodes[]? | select(.label.name == "blocked") | .createdAt] | max)}
       | select(.value != null)] | from_entries' <<<"$1"
}

# pr_checks <prs>: each of them with the verdict `checks` gives it, and when a failing run finished.
pr_checks() {
  local row
  while IFS= read -r row; do
    jq -c --argjson ci "$(ci "$(jq -r .pr <<<"$row")" "$(jq -r .sha <<<"$row")")" \
      '. + {checks: $ci.verdict, failedAt: ($ci.failing | map(.at // empty) | max // "")}' <<<"$row"
  done < <(jq -c '.[]' <<<"$1") | jq -s .
}

# turns <items> <comments> <prs>: each item with its PR, whose move it is and why. A gate is the
# stakeholders' until one comments; from then it is the role's, the same test `unanswered_feedback`
# makes. A PR that is failing, conflicting, still running CI or still a draft is the Dev's too, but
# an unanswered comment outranks them all: the answer is owed before a green build means anything.
turns() {
  jq -n --argjson items "$1" --argjson comments "$2" --argjson prs "$3" --argjson stakeholders "$STAKEHOLDERS" \
    --arg ackFrom "$ACK_FROM" --arg appFrom "$APP_FROM" --arg bot "$BOT" "$TEAM_SAID$UNANSWERED$SINCE"'
    $items | map(
      . as $item
      | (if .status == "Pitched" then "lead" else "dev" end) as $role
      | ($comments | map(select(.n == $item.number))) as $theirs
      | ($prs | map(select(.n == $item.number)) | first) as $pr
      | ($theirs | said("<!-- a-team:\($role) -->")) as $said
      | ($theirs | unanswered($said) | map(.at) | max // "") as $asked
      | ($theirs | map(select(.kind == "body") | .at) | max // "") as $opened
      | (if $pr == null then null
         elif $pr.checks == "fail" then {trouble: "CI failing", at: $pr.failedAt}
         elif $pr.conflicting then {trouble: "conflicts with \($pr.base)", at: ""}
         elif $pr.checks == "pending" then {trouble: "CI running", at: ""}
         elif $pr.draft then {trouble: "still a draft", at: ""}
         else null end) as $wrong
      | (if $pr == null then . else . + {pr: $pr.pr, prUrl: $pr.prUrl, checks: $pr.checks,
                                         conflicting: $pr.conflicting, draft: $pr.draft} end)
      | if $asked != ""
        then . + {turn: $role, reason: "answering your feedback\(since($asked))"}
        elif $wrong != null
        then . + {turn: $role, trouble: $wrong.trouble,
                  reason: "\($wrong.trouble)\(since($wrong.at))"}
        else (if $said != "" then $said else $opened end) as $waited
             | (if .status == "Pitched" then "approval" else "acceptance" end) as $for
             | . + {turn: "you", reason: "awaiting your \($for)\(since($waited))"}
        end)'
}

# questions <items> <comments> <blocked>: the Ready tasks the Dev handed back with a question, the
# Dev's turn once a stakeholder replies, and `unread` the newest reply no run has left a 👀 on.
# The Dev asks just before labelling `blocked`, hence ASKED_BEFORE.
ASKED_BEFORE=600
questions() {
  jq -n --argjson items "$1" --argjson comments "$2" --argjson blocked "$3" --argjson stakeholders "$STAKEHOLDERS" \
    --argjson before "$ASKED_BEFORE" --arg appFrom "$APP_FROM" --arg bot "$BOT" "$TEAM_SAID$SINCE"'
    $items | map(
      . as $item
      | ($blocked[$item.number | tostring] // null) as $labelled
      | select($labelled != null)
      | ($comments | map(select(.n == $item.number))) as $theirs
      | ($theirs | map(select(.kind == "comment" and team("<!-- a-team:dev -->")
                               and (.at | fromdateiso8601) >= ($labelled | fromdateiso8601) - $before))
         | max_by(.at)) as $asked
      | select($asked != null)
      | ($theirs | map(select(.kind != "body" and (.author | IN($stakeholders[])) and (team("<!-- a-team:") | not)
                               and .at > $asked.at))) as $replies
      | ($replies | map(.at) | max // "") as $replied
      | . + {question: ($asked.body | sub("\\s*<!-- a-team:dev -->\\s*$"; "")),
             unread: ($replies | map(select((.eyes // 0) == 0) | .at) | max // "")}
      | if $replied != ""
        then . + {turn: "dev", reason: "reading your answer\(since($replied))"}
        else . + {turn: "you", reason: "asked you\(since($asked.at))"} end)'
}

# unranked_ideas <items>: the Ideas with no Priority, which never get pitched until a stakeholder
# gives them one. They come off the page `waiting` has already read, so they cost no call of their own.
unranked_ideas() {
  jq --arg team "$TEAM" 'map(select(.status == "Idea" and .type == "Issue" and .priority == null)
    | {number, title, status, url, team: $team, turn: "you", reason: "waiting to be ranked"})' <<<"$1"
}

comments() {
  local n=$1 issue
  issue=$(gh api "repos/$REPO/issues/$n")
  {
    jq '{kind: "body", author: .user.login, at: .created_at, body: (.body // ""), url: .html_url,
         id: .node_id, eyes: .reactions.eyes}' <<<"$issue"
    gh api --paginate "repos/$REPO/issues/$n/comments" |
      jq '.[] | {kind: "comment", author: .user.login, at: .created_at, body: (.body // ""), url: .html_url,
                 id: .node_id, eyes: .reactions.eyes}'
    if jq -e '.pull_request' <<<"$issue" >/dev/null; then
      reviews "$n"
      gh api --paginate "repos/$REPO/pulls/$n/comments" |
        jq '.[] | {kind: "line", author: .user.login, at: .created_at, body: (.body // ""), url: .html_url,
                   path, line, id: .node_id, eyes: .reactions.eyes}'
    fi
  } | jq -s 'sort_by(.at)'
}

# The stakeholders' comments on #<n> that no run has left a 👀 on. What `feedback` returns.
unanswered_feedback() {
  comments "$2" | jq --arg marker "<!-- a-team:$1 -->" --argjson stakeholders "$STAKEHOLDERS" \
    --arg ackFrom "$ACK_FROM" --arg appFrom "$APP_FROM" --arg bot "$BOT" "$TEAM_SAID$UNANSWERED"'
    said($marker) as $since
    | unanswered($since) | map(del(.id, .eyes))'
}

# ack <n>: a 👀 on every stakeholder comment on #n this run could have seen. One that arrived mid-run
# is newer than A_TEAM_RUN_STARTED, so it stays unanswered and gets a run of its own. Unset means
# now, which is right for a person running `comment` by hand, since they have just read the thread.
ack() {
  local id at started=${A_TEAM_RUN_STARTED:-$(iso "$(date +%s)")}
  while IFS=$'\t' read -r id at; do
    write "add 👀 to your comment of $at on #$1" gh api graphql -F subject="$id" -f query='
      mutation($subject: ID!) {
        addReaction(input: {subjectId: $subject, content: EYES}) { reaction { content } } }' >/dev/null
  done < <(comments "$1" | jq -r --argjson stakeholders "$STAKEHOLDERS" --arg started "$started" \
    --arg appFrom "$APP_FROM" --arg bot "$BOT" "$TEAM_SAID"'
    map(select(.at < $started))
    | map(select(.kind != "body" and (.author | IN($stakeholders[]))
                 and (team("<!-- a-team:") | not) and (.eyes // 0) == 0))
    | .[] | [.id, .at] | @tsv')
}

# feedback_at <recent> <role> <n>: when a stakeholder last asked <role> something on #n and got no
# answer. The day window keeps that cheap; a sweep falls back to #n's whole history, however old.
feedback_at() {
  local at
  at=$(awaiting "$1" "$2" "$3")
  [ -n "$at" ] || [ -z "$SWEEP" ] || at=$(unanswered_feedback "$2" "$3" | jq -r 'max_by(.at).at // empty')
  printf '%s' "$at"
}

# depend_note <role> <task> <text>: why a dependency changed, on the blocked task, with the
# role's marker, so a blocked task answers "why?" by itself.
depend_note() {
  local body
  body=$(printf '%s\n\n<!-- a-team:%s -->' "$3" "$1")
  [ -z "$DRY_RUN" ] || printf '%s\n' "$body" | sed 's/^/  | /' >&2
  printf '%s\n' "$body" | write "comment on #$2" gh issue comment "$2" -R "$REPO" --body-file -
}

BLOCKED='((.labels | index("blocked")) or .blockedBy > 0)'
# Ready tasks, and the ones the Dev can start now: `next` picks from STARTABLE, `lead-next` counts it.
READY_TASK='.status == "Ready" and .type == "Issue" and (.labels | index("pitch") | not)'
STARTABLE="$READY_TASK and ($BLOCKED | not)"
UNSTARTABLE="$READY_TASK and $BLOCKED"
# Ready tasks labelled `blocked`: handed back with a question, or held by a stakeholder.
HELD="$READY_TASK and (.labels | index(\"blocked\"))"
# The Dev's tasks that take up one of wip.worktrees.
WORKING='(.labels | index("a-team:dev")) and (.status == "In progress" or .status == "In review")'" and ($BLOCKED | not)"

case "$CMD" in
  list)
    if [ $# -eq 0 ]; then
      items
    else
      items | jq --argjson want "$(printf '%s\n' "$@" | jq -R . | jq -s .)" \
        'map(select(.status as $s | $want | index($s)))'
    fi
    ;;

  mine)
    [ $# -ge 1 ] || die "usage: board.sh $TEAM mine <role> [STATUS...]"
    role=$1
    shift
    check_role "$role"
    items | jq --arg label "$(own_label "$role")" \
      --argjson want "$(if [ $# -gt 0 ]; then printf '%s\n' "$@" | jq -R . | jq -s .; else echo null; fi)" '
      map(select((.labels | index($label)) and ($want == null or (.status as $s | $want | index($s)))))'
    ;;

  wip)
    items | jq "def open_blocked: .status != \"Done\" and $BLOCKED;"'
      def counts: group_by(.status) | map({key: .[0].status, value: length}) | from_entries;
      {pitches: map(select(.labels | index("pitch"))) | counts,
       dev: (map(select(.labels | index("a-team:dev")))
         | (map(select(open_blocked | not)) | counts) + {blocked: map(select(open_blocked)) | length}),
       stakeholders: map(select((.labels | index("pitch") or index("a-team:dev")) | not)) | counts}'
    ;;

  next)
    items | jq "map(select($STARTABLE))" | by_priority | jq first
    ;;

  lead-next)
    state="$STATE/$TEAM/lead-turn"
    all=$(items)
    count() { jq "[.[] | select($1)] | length" <<<"$all"; }
    pitched=$(count '(.labels | index("pitch")) and .status == "Pitched"')
    exploring=$(count '(.labels | index("pitch")) and .status == "Exploring"')
    found=$(count '(.labels | index("a-team:idea")) and .status == "Idea"')
    skipped=$(count '(.labels | index("a-team:skipped")) and .status == "Idea"')
    ready=$(count "$STARTABLE")
    blocked=$(count "$UNSTARTABLE")
    swap=$(pitch_swap "$all" | jq 'map_values(map(. + {announce: (.labels | index("a-team:displaced") | not)}))')
    promote=$(jq .promote <<<"$swap")
    demote=$(jq .demote <<<"$swap")
    idle=false
    [ $((pitched + exploring)) -eq 0 ] && idle=true
    exploring=$((exploring - $(jq length <<<"$promote") + $(jq length <<<"$demote")))
    idea=$(pitchable_idea "$all")
    can_pitch=false can_discover=false
    [ "$exploring" -lt "$(cfg '.wip.exploring')" ] && [ "$idea" != null ] && can_pitch=true
    [ "$found" -lt "$(cfg '.wip.ideas')" ] && can_discover=true
    last=$(cat "$state" 2>/dev/null || echo discover)
    if $can_pitch && { [ "$last" = discover ] || ! $can_discover || $idle; }; then
      turn=pitch
    elif $can_discover; then
      turn=discover
    else
      turn=none
    fi
    if [ "$turn" != none ]; then mkdir -p "$(dirname "$state")" && echo "$turn" >"$state"; fi
    jq -n --argjson promote "$promote" --argjson demote "$demote" --arg turn "$turn" --argjson item "$idea" \
      --argjson room "$(($(cfg '.wip.ideas') - found))" --argjson ready "$ready" --argjson blocked "$blocked" \
      --argjson floor "$(cfg '.wip.readyFloor // 0')" --argjson skipped "$skipped" '
      {promote: $promote, demote: $demote, turn: $turn}
      + (if $turn == "pitch" then {item: $item}
         elif $turn == "discover" then {room: $room}
         else {reason: "Exploring and the discovery queue are both full, or there is no Idea to pitch"} end)
      + {ready: $ready, blocked: $blocked, readyLow: ($ready < $floor), skipped: $skipped}'
    ;;

  move)
    [ $# -eq 3 ] || die "usage: board.sh $TEAM move <role> <n> <status>"
    role=$1 n=$2 to=$3
    check_role "$role"
    is_state "$to" || die "unknown status '$to'"
    it=$(item "$n")
    [ -n "$it" ] || die "#$n is not on the board (use add)"
    from=$(jq -r .status <<<"$it")
    label=$(own_label "$role")
    allowed "$role" "$from" "$to" || die "$role may not move #$n from '$from' to '$to'"
    if [ "$role" = dev ] && jq -e '.labels | index("pitch")' <<<"$it" >/dev/null; then
      die "dev does not move pitches"
    fi
    if [ "$role:$from>$to" = "lead:Pitched>Idea" ] &&
      [ "$(unanswered_feedback lead "$n" | jq length)" -eq 0 ]; then
      die "lead may move #$n out of Pitched only when a stakeholder has asked (no unanswered stakeholder feedback on #$n)"
    fi
    if [ "$role:$from>$to" = "lead:Pitched>Exploring" ] &&
      ! pitch_swap "$(items)" | jq -e --argjson n "$n" 'any(.demote[]; .number == $n)' >/dev/null; then
      die "lead may move #$n out of Pitched only when a higher-priority draft displaces it (nothing in Exploring out-ranks #$n)"
    fi
    case "$role:$from" in
      "dev:Ready" | "lead:Idea")
        write "label #$n $label" gh issue edit "$n" -R "$REPO" --add-label "$label" >/dev/null ;;
      *)
        jq -e --arg l "$label" '.labels | index($l)' <<<"$it" >/dev/null ||
          die "#$n isn't $role's (no '$label' label); leave it to the stakeholders" ;;
    esac
    if [ "$role:$from>$to" = "lead:Pitched>Exploring" ] &&
      ! jq -e '.labels | index("a-team:displaced")' <<<"$it" >/dev/null; then
      write "label #$n a-team:displaced" gh api -X POST "repos/$REPO/issues/$n/labels" -f 'labels[]=a-team:displaced' >/dev/null
    fi
    set_status "$(jq -r .id <<<"$it")" "$to"
    say "#$n: $from -> $to"
    ;;

  add)
    [ $# -eq 3 ] || die "usage: board.sh $TEAM add <role> <n> <status>"
    role=$1 n=$2 to=$3
    check_role "$role"
    is_state "$to" || die "unknown status '$to'"
    allowed "$role" None "$to" || die "$role may not add items as '$to'"
    existing=$(item "$n")
    if [ -n "$existing" ]; then
      # The project's auto-add workflow may already have put a new issue on the board.
      case "$(jq -r .status <<<"$existing")" in None | Idea) ;; *) die "#$n is already on the board (use move)" ;; esac
      gh api "repos/$REPO/issues/$n" --jq .body | grep -qF "<!-- a-team:$role -->" ||
        die "#$n is already on the board and isn't $role's (use move)"
    fi
    content=$(gh api "repos/$REPO/issues/$n" --jq 'if .pull_request then "pulls" else "issues" end')
    if [ "$role" = lead ]; then
      case "$to" in
        Idea) write "label #$n a-team:idea" gh api -X POST "repos/$REPO/issues/$n/labels" -f 'labels[]=a-team:idea' >/dev/null ;;
        Exploring | Pitched) write "label #$n pitch" gh api -X POST "repos/$REPO/issues/$n/labels" -f 'labels[]=pitch' >/dev/null ;;
      esac
    fi
    if [ -n "$existing" ]; then
      set_status "$(jq -r .id <<<"$existing")" "$to"
      say "#$n: added as $to"
      exit 0
    fi
    id=$(write "add #$n to the board" gh api graphql -F project="$(project_meta | jq -r .id)" \
      -F content="$(gh api "repos/$REPO/$content/$n" --jq .node_id)" -f query='
      mutation($project: ID!, $content: ID!) {
        addProjectV2ItemById(input: {projectId: $project, contentId: $content}) { item { id } } }' \
      --jq .data.addProjectV2ItemById.item.id)
    set_status "${id:-new-item}" "$to"
    say "#$n: added as $to"
    ;;

  priority)
    [ $# -eq 3 ] || die "usage: board.sh $TEAM priority <role> <n> <value|none>"
    role=$1 n=$2 value=$3
    case "$role" in
      lead | dev) die "$role may not set a $PRIORITY; ranking an item is the stakeholders' own gate" ;;
      you) ;;
      *) die "unknown role '$role' (you)" ;;
    esac
    field=$(priority_field)
    [ -n "$field" ] || die "no issue field '$PRIORITY' on $OWNER"
    option=
    if [ "$value" != none ]; then
      option=$(jq -r --arg v "$value" '.options[] | select(.name == $v) | .id' <<<"$field")
      [ -n "$option" ] ||
        die "unknown $PRIORITY '$value' ($(jq -r '[.options[].name, "none"] | join(" | ")' <<<"$field"))"
    fi
    # Priority is an organisation-level issue field, so this is not the mutation `move` uses.
    issue=$(gh api "repos/$REPO/issues/$n" --jq .node_id)
    if [ -z "$option" ]; then
      write "clear $PRIORITY on #$n" gh api graphql -F issue="$issue" -F field="$(jq -r .id <<<"$field")" -f query='
        mutation($issue: ID!, $field: ID!) {
          updateIssueFieldValue(input: {issueId: $issue, issueField: {fieldId: $field, delete: true}}) {
            clientMutationId } }' >/dev/null
      say "#$n: $PRIORITY cleared"
    else
      write "set $PRIORITY on #$n to '$value'" gh api graphql -F issue="$issue" \
        -F field="$(jq -r .id <<<"$field")" -F option="$option" -f query='
        mutation($issue: ID!, $field: ID!, $option: ID!) {
          updateIssueFieldValue(input: {issueId: $issue, issueField: {fieldId: $field,
                                        singleSelectOptionId: $option}}) { clientMutationId } }' >/dev/null
      say "#$n: $PRIORITY set to '$value'"
    fi
    ;;

  approve)
    [ $# -eq 2 ] || die "usage: board.sh $TEAM approve <role> <n>"
    role=$1 n=$2
    case "$role" in
      lead | dev) die "$role may not approve a pitch; approving is the stakeholders' own gate" ;;
      you) ;;
      *) die "unknown role '$role' (you)" ;;
    esac
    it=$(item "$n")
    [ -n "$it" ] || die "#$n is not on the board"
    [ "$(jq -r .type <<<"$it")" = Issue ] || die "#$n is not an issue, so it is not a pitch to approve"
    jq -e '.labels | index("pitch")' <<<"$it" >/dev/null || die "#$n is not a pitch (no 'pitch' label)"
    status=$(jq -r .status <<<"$it")
    [ "$status" = Pitched ] || die "only a Pitched pitch can be approved (#$n is in '$status')"
    set_status "$(jq -r .id <<<"$it")" Approved
    say "#$n: Pitched -> Approved"
    ;;

  comment)
    [ $# -eq 3 ] || die "usage: board.sh $TEAM comment <role> <n> <file>"
    role=$1 n=$2 file=$3
    check_role "$role"
    [ -f "$file" ] || die "no such file: $file"
    body=$(cat "$file"; printf '\n\n<!-- a-team:%s -->' "$role")
    [ -z "$DRY_RUN" ] || printf '%s\n' "$body" | sed 's/^/  | /' >&2
    printf '%s\n' "$body" | write "comment on #$n" gh issue comment "$n" -R "$REPO" --body-file -
    ack "$n"
    ;;

  skip)
    [ $# -eq 3 ] || die "usage: board.sh $TEAM skip <role> <n> <file>"
    role=$1 n=$2 file=$3
    check_role "$role"
    [ "$role" = lead ] || die "only lead may skip an Idea"
    [ -f "$file" ] || die "no such file: $file"
    it=$(item "$n")
    [ -n "$it" ] || die "#$n is not on the board, so it is not an Idea to skip"
    status=$(jq -r .status <<<"$it")
    [ "$status" = Idea ] || die "skip is only for Ideas (#$n is in '$status')"
    body=$(cat "$file"; printf '\n\n<!-- a-team:%s -->' "$role")
    [ -z "$DRY_RUN" ] || printf '%s\n' "$body" | sed 's/^/  | /' >&2
    printf '%s\n' "$body" | write "comment on #$n" gh issue comment "$n" -R "$REPO" --body-file -
    ack "$n"
    write "label #$n a-team:skipped" gh issue edit "$n" -R "$REPO" --add-label a-team:skipped >/dev/null
    say "#$n: skipped; a comment there, or removing the 'a-team:skipped' label, puts it back"
    ;;

  feedback)
    [ $# -eq 2 ] || die "usage: board.sh $TEAM feedback <role> <n>"
    role=$1 n=$2
    check_role "$role"
    unanswered_feedback "$role" "$n"
    ;;

  link)
    [ $# -eq 2 ] || die "usage: board.sh $TEAM link <parent> <child>"
    child_id=$(gh api "repos/$REPO/issues/$2" --jq .id)
    write "make #$2 a sub-issue of #$1" gh api -X POST "repos/$REPO/issues/$1/sub_issues" -F "sub_issue_id=$child_id" >/dev/null
    say "#$2 is now a sub-issue of #$1"
    ;;

  depends)
    [ $# -eq 4 ] || die "usage: board.sh $TEAM depends <role> <task> <prerequisite> \"<why>\""
    role=$1 task=$2 prereq=$3 why=$4
    check_role "$role"
    write "block #$task on #$prereq" gh api -X POST "repos/$REPO/issues/$task/dependencies/blocked_by" \
      -F "issue_id=$(gh api "repos/$REPO/issues/$prereq" --jq .id)" >/dev/null
    depend_note "$role" "$task" "Blocked by #$prereq: $why"
    say "#$task is now blocked by #$prereq, and said why on #$task"
    ;;

  undepend)
    [ $# -eq 4 ] || die "usage: board.sh $TEAM undepend <role> <task> <prerequisite> \"<why>\""
    role=$1 task=$2 prereq=$3 why=$4
    check_role "$role"
    prereq_id=$(gh api "repos/$REPO/issues/$task/dependencies/blocked_by" |
      jq -r --argjson n "$prereq" '[.[] | select(.number == $n) | .id] | first // empty')
    [ -n "$prereq_id" ] || die "#$task is not blocked by #$prereq"
    write "unblock #$task from #$prereq" \
      gh api -X DELETE "repos/$REPO/issues/$task/dependencies/blocked_by/$prereq_id" >/dev/null
    depend_note "$role" "$task" "No longer blocked by #$prereq: $why"
    say "#$task is no longer blocked by #$prereq, and said why on #$task"
    ;;

  unlink)
    [ $# -eq 3 ] || die "usage: board.sh $TEAM unlink <role> <parent> <child>"
    role=$1 parent=$2 child=$3
    check_role "$role"
    [ "$role" = lead ] || die "$role may not take a task off a pitch; only lead draws breakdowns"
    all=$(items)
    board_status() { jq -r --argjson n "$1" 'map(select(.number == $n)) | first | .status // empty' <<<"$all"; }
    from=$(board_status "$child")
    [ -n "$from" ] || die "#$child is not on the board"
    [ "$from" = Ready ] || die "$role may only take a Ready task off a pitch (#$child is '$from')"
    jq -e --argjson n "$parent" 'map(select(.number == $n)) | first | (.labels // []) | index("pitch")' <<<"$all" >/dev/null ||
      die "$role may only take a task off a pitch (#$parent is not one)"
    pitch_status=$(board_status "$parent")
    [ "$pitch_status" = Building ] ||
      die "$role may only take a task off a pitch in Building (#$parent is '$pitch_status')"
    [ "$(parent_of "$child")" = "$parent" ] || die "#$child is not a sub-issue of #$parent"
    write "take #$child off #$parent" gh api -X DELETE "repos/$REPO/issues/$parent/sub_issue" \
      -F "sub_issue_id=$(gh api "repos/$REPO/issues/$child" --jq .id)" >/dev/null
    say "#$child is no longer a sub-issue of #$parent"
    ;;

  unblock)
    [ $# -eq 2 ] || die "usage: board.sh $TEAM unblock <role> <n>"
    role=$1 n=$2
    check_role "$role"
    [ "$role" = dev ] || die "only dev may unblock a task, and only one it handed back with a question"
    it=$(item "$n")
    [ -n "$it" ] || die "#$n is not on the board"
    jq -e "$HELD" <<<"$it" >/dev/null || die "#$n isn't a Ready task labelled blocked"
    talk=$(gated_talk "[$it]")
    turn=$(questions "[$it]" "$(gated_comments "$talk")" "$(gated_blocked "$talk")" | jq -r '.[0].turn // empty')
    [ -n "$turn" ] || die "#$n has no question from dev: a stakeholder is holding it, so leave it to them"
    [ "$turn" = dev ] || die "no stakeholder has replied to dev's question on #$n since it was handed back"
    write "remove blocked from #$n" gh issue edit "$n" -R "$REPO" --remove-label blocked >/dev/null
    ack "$n"
    say "#$n: no longer blocked"
    ;;

  body)
    [ $# -eq 1 ] || die "usage: board.sh $TEAM body <n>"
    # gh puts the error's own body on stdout, so its one-line reason is read from stderr alone.
    trouble=$(mktemp)
    issue=$(gh api "repos/$REPO/issues/$1" --jq '{number, title, body: (.body // "")}' 2>"$trouble") ||
      { reason=$(head -1 "$trouble"); rm -f "$trouble"; die "can't read #$1 ($reason)"; }
    rm -f "$trouble"
    printf '%s\n' "$issue"
    ;;

  children)
    [ $# -eq 1 ] || die "usage: board.sh $TEAM children <n>"
    gh api --paginate "repos/$REPO/issues/$1/sub_issues" |
      jq -s 'add // [] | map({number, title, state, labels: [.labels[].name]})'
    ;;

  waiting)
    [ $# -eq 0 ] || die "usage: board.sh $TEAM waiting"
    all=$(items)
    gated=$(jq --arg team "$TEAM" 'map(select(.status == "Pitched" or .status == "In review")
      | {number, title, status, url, team: $team, priority,
         pitch: (.type == "Issue" and (.labels | index("pitch")) != null)})' <<<"$all")
    held=$(jq --arg team "$TEAM" "map(select($HELD)
      | {number, title, status, url, team: \$team, priority, pitch: false})" <<<"$all")
    talk=$(gated_talk "$(jq -s add <<<"$gated$held")")
    said=$(gated_comments "$talk")
    turns "$gated" "$said" "$(pr_checks "$(gated_prs "$talk")")" |
      jq --argjson asked "$(questions "$held" "$said" "$(gated_blocked "$talk")" | jq 'map(del(.unread))')" \
        --argjson unranked "$(unranked_ideas "$all")" '. + $asked + $unranked'
    ;;

  pr)
    [ $# -eq 1 ] || die "usage: board.sh $TEAM pr <n>"
    pr_for "$1"
    ;;

  checks)
    [ $# -eq 1 ] || die "usage: board.sh $TEAM checks <pr>"
    ci "$1"
    ;;

  triggers)
    role=
    while [ $# -gt 0 ]; do
      case $1 in
        --sweep) SWEEP=1 ;;
        *) [ -z "$role" ] || die "usage: board.sh $TEAM triggers <role> [--sweep]"; role=$1 ;;
      esac
      shift
    done
    [ -n "$role" ] || die "usage: board.sh $TEAM triggers <role> [--sweep]"
    check_role "$role"
    all=$(items)
    reasons=()
    recent=$(recent_comments)
    if [ "$role" = dev ]; then
      while IFS= read -r row; do
        n=$(jq -r .number <<<"$row")
        status=$(jq -r .status <<<"$row")
        pr=$(pr_for "$n")
        numbers=("$n")
        if [ -n "$pr" ] && [ "$pr" != null ]; then
          p=$(jq -r .number <<<"$pr")
          numbers+=("$p")
          recent=$(jq -s 'add' <(echo "$recent") <(pr_reviews "$p"))
          checks=$(ci "$p")
          verdict=$(jq -r .verdict <<<"$checks")
          # Changes once the rest settle, so a run starts that can re-run a transient failure.
          running=$(jq -r 'if .pending == [] then "" else ", other checks still running" end' <<<"$checks")
          [ "$verdict" = fail ] && reasons+=("CI failed on PR #$p at $(gh api "repos/$REPO/pulls/$p" --jq '.head.sha[:7]')$running")
          [ "$verdict" = pass ] && [ "$(jq -r .isDraft <<<"$pr")" = true ] &&
            reasons+=("PR #$p is green but still a draft")
          # UNKNOWN means GitHub hasn't finished computing it, so only CONFLICTING fires.
          [ "$(jq -r .mergeable <<<"$pr")" = CONFLICTING ] &&
            reasons+=("PR #$p conflicts with its base: merge the base branch into it and resolve")
        elif [ "$status" = "In progress" ]; then
          reasons+=("#$n is In progress but has no PR: an earlier run didn't finish")
        fi
        for x in "${numbers[@]}"; do
          at=$(feedback_at "$recent" dev "$x")
          [ -n "$at" ] && reasons+=("stakeholder feedback on #$x ($at)")
        done
      done < <(jq -c '.[] | select((.labels | index("a-team:dev")) and (.status == "In progress" or .status == "In review"))' <<<"$all")

      held=$(jq -c "map(select($HELD))" <<<"$all")
      if [ "$held" != "[]" ]; then
        talk=$(gated_talk "$held")
        while IFS=$'\t' read -r n at; do
          reasons+=("stakeholder answered the Dev's question on #$n ($at)")
        done < <(questions "$held" "$(gated_comments "$talk")" "$(gated_blocked "$talk")" |
          jq -r '.[] | select(.unread != "") | [.number, .unread] | @tsv')
      fi

      checkout=$(cfg .checkout)
      checkout=${checkout/#\~/$HOME}
      if [ -d "$checkout" ]; then
        while IFS= read -r branch; do
          merged=$(gh api "repos/$REPO/pulls?head=${REPO%/*}:$branch&state=closed&per_page=5" \
            --jq '[.[] | select(.merged_at != null and ((.body // "") | contains("<!-- a-team:dev -->")))][0].number // empty')
          [ -n "$merged" ] && reasons+=("PR #$merged has merged: clean up its worktree")
        done < <(git -C "$checkout" worktree list --porcelain | sed -n 's|^branch refs/heads/||p')
      fi

      used=$(jq "[.[] | select($WORKING)] | length" <<<"$all")
      limit=$(cfg .wip.worktrees)
      startable=$(jq "[.[] | select($STARTABLE)]" <<<"$all")
      ready=$(jq -r '.[0].number // empty' <<<"$startable")
      if [ -n "$ready" ]; then
        if [ "$used" -lt "$limit" ]; then
          reasons+=("Ready task available (e.g. #$ready) and a free worktree")
        elif [ "$used" -lt $((limit + 1)) ]; then
          urgent=$(by_priority <<<"$startable" | jq -r '[.[] | select(.priority == "Urgent")][0].number // empty')
          [ -n "$urgent" ] && reasons+=("Ready task available (e.g. #$urgent, Urgent) and a fast-track worktree ($used in use, $((limit + 1)) allowed while an Urgent task is Ready)")
        fi
      fi
      creative=false
    else
      while IFS= read -r row; do
        n=$(jq -r .number <<<"$row")
        status=$(jq -r .status <<<"$row")
        at=$(feedback_at "$recent" lead "$n")
        [ -n "$at" ] && reasons+=("stakeholder feedback on #$n ($at)")
        [ "$status" = Approved ] && reasons+=("#$n was approved: break it down")
        if [ "$status" = Building ]; then
          open=$(gh api "repos/$REPO/issues/$n/sub_issues" --jq '[length, (map(select(.state == "open")) | length)] | @tsv')
          [ "${open%%$'\t'*}" -gt 0 ] && [ "${open##*$'\t'}" -eq 0 ] &&
            reasons+=("all of #$n's tasks are closed: validate it")
        fi
      done < <(jq -c '.[] | select((.labels | index("pitch")) and .status != "Idea" and .status != "Done")' <<<"$all")

      pitched=$(jq '[.[] | select((.labels | index("pitch")) and .status == "Pitched")] | length' <<<"$all")
      exploring=$(jq '[.[] | select((.labels | index("pitch")) and .status == "Exploring")] | length' <<<"$all")
      found=$(jq '[.[] | select((.labels | index("a-team:idea")) and .status == "Idea")] | length' <<<"$all")
      ideas=$(jq '[.[] | select(.status == "Idea" and .type == "Issue")] | length' <<<"$all")
      if [ "$exploring" -gt 0 ]; then
        if [ "$pitched" -lt "$(cfg .wip.pitched)" ]; then
          reasons+=("room in Pitched for a drafted pitch")
        elif [ "$(pitch_swap "$all" | jq '.demote | length')" -gt 0 ]; then
          reasons+=("a drafted pitch out-ranks one in Pitched: swap them")
        fi
      fi
      if [ "$pitched" -eq 0 ] && [ "$exploring" -eq 0 ]; then
        idea=$(pitchable_idea "$all" | jq -r '.number // empty')
        [ -n "$idea" ] && reasons+=("nothing pitched or being drafted: pitch an Idea (e.g. #$idea)")
      fi
      creative=false
      { [ "$exploring" -lt "$(cfg .wip.exploring)" ] && [ "$ideas" -gt 0 ]; } ||
        [ "$found" -lt "$(cfg .wip.ideas)" ] && creative=true
    fi
    jq -n --argjson creative "$creative" '{reasons: $ARGS.positional, creative: $creative}' \
      --args "${reasons[@]+"${reasons[@]}"}"
    ;;

  check)
    field=$(project_meta | jq '.field // empty')
    jq -e '.id' <<<"$field" >/dev/null 2>&1 || die "no single-select field '$FIELD' on $OWNER project $NUMBER"
    missing=()
    for s in "${STATES[@]}"; do
      option=$(jq -r --arg s "$s" '.project.statusMap[$s] // $s' "$CONFIG")
      jq -e --arg o "$option" '.options | map(.name) | index($o)' <<<"$field" >/dev/null ||
        missing+=("$option")
    done
    if [ ${#missing[@]} -gt 0 ]; then
      printf "field '%s' is missing options:\n" "$FIELD" >&2
      printf '  %s\n' "${missing[@]}" >&2
      exit 1
    fi
    unmapped=$(items | jq -r 'map(select(.status | startswith("?"))) | .[] | "  #\(.number) \(.status)"')
    [ -z "$unmapped" ] || { echo "items with a status outside the team's states:" >&2; echo "$unmapped" >&2; exit 1; }
    echo "ok: $OWNER project $NUMBER, field '$FIELD'"
    wrong() { printf '%s · %s\n          %s\n' "$line" "$1" "$2" >&2; exit 1; }
    line="identity"
    [ -n "$BOT" ] || wrong "NO APP" "the team can't run without its own GitHub App: run a-team app create $TEAM, then install it"
    source "$ROOT/scripts/github-app.sh"
    line="identity: $BOT"
    has_app_key "${REPO%/*}" ||
      wrong "NO KEY" "nothing in the login Keychain under service '$KEYCHAIN_SERVICE', account '${REPO%/*}': run a-team app create $TEAM"
    token=$("$ROOT/bin/a-team" token "$TEAM" 2>&1) || wrong "NO TOKEN" "${token#a-team token: }"
    line="$line · token ok"
    GH_TOKEN=$token gql 'query($owner: String!, $number: Int!) {
        organization(login: $owner) { projectV2(number: $number) { items(first: 1) { totalCount } } } }' \
      --jq '.data.organization.projectV2.items.totalCount' >/dev/null 2>&1 ||
      wrong "PROJECT $NUMBER UNREADABLE" "grant the organisation's \"Projects: read and write\", and install the App on $REPO"
    granted() { jq -e --arg p "$1" '.permissions[$p] == "write"' "$(token_cache "$TEAM")" >/dev/null 2>&1; }
    granted organization_projects ||
      wrong "PROJECT $NUMBER READ-ONLY" "grant the organisation's \"Projects: read and write\""
    line="$line · project $NUMBER read+write ok"
    GH_TOKEN=$token priority_field >/dev/null 2>&1 ||
      wrong "PRIORITY UNREADABLE" "grant the organisation's \"Issue Fields: read\" — without it \`triggers\` fails with \"Resource not accessible by integration\""
    line="$line · $PRIORITY readable"
    granted contents || wrong "NO PUSH" "grant the repository's \"Contents: read and write\""
    echo "$line · push access to $REPO ok"
    ;;

  setup)
    dry_run=false
    { [ "${1:-}" = --dry-run ] || [ -n "$DRY_RUN" ]; } && { dry_run=true; DRY_RUN=1; }
    field=$(project_meta | jq '.field // empty')
    jq -e '.id' <<<"$field" >/dev/null 2>&1 || die "no single-select field '$FIELD' on $OWNER project $NUMBER"
    used=$(gql "query(\$owner: String!, \$number: Int!, \$field: String!, \$endCursor: String) {
        $KIND(login: \$owner) { projectV2(number: \$number) {
          items(first: 100, after: \$endCursor) { pageInfo { hasNextPage endCursor }
            nodes { fieldValueByName(name: \$field) { ... on ProjectV2ItemFieldSingleSelectValue { name } } } } } } }" \
      -F field="$FIELD" --paginate |
      jq -s --arg kind "$KIND" '[.[].data[$kind].projectV2.items.nodes[].fieldValueByName.name // empty] | unique')
    input=$(jq -n --argjson field "$field" --argjson used "$used" --slurpfile cfg "$CONFIG" '
      [ ["Idea", "GRAY", "A seed worth a look"],
        ["Exploring", "PURPLE", "Lead is researching and writing a pitch"],
        ["Pitched", "PINK", "Waiting on the reviewer: approve or comment"],
        ["Approved", "GREEN", "Reviewer approved; Lead to break it down"],
        ["Building", "BLUE", "Broken into tasks; tasks in flight"],
        ["Ready", "BLUE", "A task Dev can pick up"],
        ["In progress", "YELLOW", "Dev is working on it"],
        ["In review", "ORANGE", "Waiting on the reviewer: merge, accept or comment"],
        ["Done", "GREEN", "Merged or accepted"] ] as $states
      | ($cfg[0].project.statusMap // {}) as $map
      | ($states | map(($map[.[0]] // .[0]) as $name
          | ($field.options | map(select(.name == $name)) | first) as $existing
          | if $existing then $existing
            else {name: $name, color: .[1], description: .[2]} end)) as $wanted
      | ($field.options | map(select(.name as $n | $wanted | map(.name) | index($n) | not))) as $others
      # The defaults GitHub gives a new project, while no item has them.
      | ($others | map(select(.name as $n | ["Todo", "In Progress"] | index($n) and ($used | index($n) | not)))) as $defaults
      | {fieldId: $field.id, singleSelectOptions: ($wanted + ($others - $defaults)), dropped: $defaults}')
    if $dry_run; then
      jq -r '(.singleSelectOptions[] | "  \(if .id then "keep" else "add " end)  \(.name)"),
             (.dropped[] | "  drop  \(.name)")' <<<"$input"
    else
      jq -n --argjson input "$input" '{variables: {input: ($input | del(.dropped))}, query:
        "mutation($input: UpdateProjectV2FieldInput!) { updateProjectV2Field(input: $input) { clientMutationId } }"}' |
        gh api graphql --input - >/dev/null
      echo "Status options set on $OWNER project $NUMBER"
    fi
    existing=$(gh label list -R "$REPO" --limit 500 --json name,color,description)
    while IFS='|' read -r name color description; do
      current=$(jq -c --arg n "$name" 'map(select(.name == $n)) | first' <<<"$existing")
      if [ "$current" = null ]; then
        $dry_run || gh label create "$name" -R "$REPO" --color "$color" --description "$description" >/dev/null
        say "created label $name"
        continue
      fi
      changed=
      [ "$(jq -r '.color' <<<"$current")" = "$color" ] || changed=colour
      [ "$(jq -r '.description' <<<"$current")" = "$description" ] || changed="${changed:+$changed and }description"
      [ -n "$changed" ] || continue
      $dry_run || gh label edit "$name" -R "$REPO" --color "$color" --description "$description" >/dev/null
      say "updated label $name: $changed"
    done <<<"pitch|5319e7|An a-team pitch: Lead shapes it, reviewer approves it
a-team:dev|0e8a16|Claimed by the a-team Dev
a-team:idea|c5def5|Found by the a-team Lead; give it a Priority to have it pitched
a-team:skipped|d4c5f9|The Lead found nothing to pitch here; comment on it to put it back in the running
a-team:displaced|d4c5f9|Displaced from Pitched once already; its later moves go unannounced
blocked|fbca04|Waiting on another issue"
    ;;

  *)
    die "unknown command '$CMD' (see process.md)"
    ;;
esac
