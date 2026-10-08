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

# record <who> <n> <what>: one line of #<n>'s history, after the change it describes has been made.
record() {
  [ -z "$DRY_RUN" ] || return 0
  mkdir -p "$STATE"
  history_sql "INSERT INTO events (team, item, at, who, what) VALUES
    ($(sql "$TEAM"), $(sql "$2"), $(sql "$(date -u +%FT%TZ)"), $(sql "$1"), $(sql "$3"));" >/dev/null ||
    echo "board.sh: couldn't record '$3' in #$2's history" >&2
}

[ $# -ge 2 ] || die "usage: board.sh [--dry-run] <team> <command> [args...] (see process.md)"
TEAM=$1 CMD=$2
shift 2
source "$ROOT/scripts/common.sh"
CONFIG=$(team_config "$TEAM")
[ -f "$CONFIG" ] || die "no config for team '$TEAM' at $CONFIG (start from examples/team.json)"
if ! unreadable=$(jq -e 'type == "object"' "$CONFIG" 2>&1 >/dev/null); then
  unreadable=${unreadable#jq: }
  unreadable="can't read $TEAM.json: ${unreadable#parse error: }"
  [ "$unreadable" != "can't read $TEAM.json: " ] || unreadable="$TEAM.json isn't a JSON object"
  [ "$CMD" = check ] && { printf '%-10s%s\n' config "$unreadable"; exit 1; }
  die "$unreadable"
fi

cfg() { jq -r "$1 // empty" "$CONFIG"; }
REPO=$(cfg .repo)
OWNER=$(cfg .project.owner)
NUMBER=$(cfg .project.number)
FIELD=$(cfg .project.statusField)
FIELD=${FIELD:-Status}
PRIORITY=$(cfg .priorityField)
PRIORITY=${PRIORITY:-Priority}
STAKEHOLDERS=$(jq -c '.stakeholders // [.reviewer // empty]' "$CONFIG")
[ -n "$NUMBER" ] || [ "$CMD" = check ] || die "project.number is not set in $CONFIG"

# The labels setup creates, as "<name>|<colour>|<description>|<what goes wrong without it>".
LABELS="pitch|5319e7|An a-team pitch: Lead shapes it, reviewer approves it|the team can't tell its pitches from tasks
a-team:dev|0e8a16|Claimed by the a-team Dev|the Dev can't claim tasks
a-team:customer|1d76db|The a-team Customer lead's docs PR|the Customer lead can't put its docs PR in front of you
a-team:idea|c5def5|Found by the a-team Lead; give it a Priority to have it pitched|the Lead can't flag the ideas it finds for you
a-team:skipped|d4c5f9|The Lead found nothing to pitch here; comment on it to put it back in the running|the Lead can't pass over an idea, and keeps coming back to it
a-team:displaced|d4c5f9|Displaced from Pitched once already; its later moves go unannounced|the Lead tells you every time it bumps a pitch out of Pitched, not just the first
blocked|fbca04|Waiting on another issue|the Dev can't mark a task that waits on your answer"

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

# fenced: for each line, whether it's inside a ``` or ~~~ code block, its fences included.
# needs_answer: a pitch body's "Needs your answer" section, heading and all, or "" when nothing's in it.
NEEDS='def fenced: reduce .[] as $line ({fence: null, out: []};
      .fence as $open
      | if $open == null then
          ([$line | capture("^ {0,3}(?<f>`{3,}|~{3,})")] | .[0].f) as $f | .fence = $f | .out += [$f != null]
        else
          .out += [true]
          | if $line | test("^ {0,3}\($open[:1]){\($open | length),}[ \t]*$") then .fence = null else . end
        end) | .out;
  def needs_answer: (gsub("\r"; "") | split("\n")) as $lines | ($lines | fenced) as $fenced
    | ([range($lines | length) | select(($fenced[.] | not) and ($lines[.] | test("^##[ \t]+Needs your answer[ \t]*$"; "i")))]
       | first) as $at
    | if $at == null then "" else
        ([range($at + 1; $lines | length)
          | select(($fenced[.] | not) and ($lines[.] | test("^#{1,2}[ \t]|^---+[ \t]*$|<!-- a-team:")))] | first) as $stop
        | ($lines[$at + 1:($stop // ($lines | length))]
           | join("\n") | sub("^\\s+"; "") | sub("\\s+$"; "")) as $text
        | if $text == "" or ($text | test("^none\\.?$"; "i")) then "" else "## Needs your answer\n\n\($text)" end
      end;'

# closes: the issues a Dev PR's body closes, for when GitHub hasn't linked them.
CLOSES='def closes: if (.body // "") | contains("<!-- a-team:dev -->")
    then [.body | scan("(?i)\\b(?:close[sd]?|fix(?:e[sd])?|resolve[sd]?):?[ \t]+#([0-9]+)\\b") | .[0] | tonumber]
    else [] end;'

# checks: the CI verdict on a commit's check runs, shaped as REST gives them.
CHECKS='def checks:
    def failed: .conclusion as $c
      | ["failure", "timed_out", "cancelled", "action_required", "startup_failure"] | index($c);
    {
      verdict: (if any(.[]; failed) then "fail"
                elif length == 0 or any(.[]; .status != "completed") then "pending"
                else "pass" end),
      failing: map(select(failed) | {name, at: .completed_at, link: .html_url}),
      pending: map(select(.status != "completed") | .name)
    };'

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
    "lead:Approved>Building" | \
    "lead:None>Idea" | "lead:None>Exploring" | "lead:None>Pitched" | "lead:None>Ready" | \
    "dev:None>Idea" | "dev:Ready>In progress" | "dev:In progress>In review" | "dev:In progress>Ready" | \
    "customer:None>Idea" | "customer:None>In review")
      return 0 ;;
  esac
  return 1
}

check_role() {
  case "$1" in lead | dev | customer) ;; *) die "unknown role '$1' (lead | dev | customer)" ;; esac
}

own_label() {
  case "$1" in lead) echo pitch ;; dev) echo a-team:dev ;; customer) echo a-team:customer ;; esac
}

customer_on() { jq -e '.roles.customer == true' "$CONFIG" >/dev/null 2>&1; }

# The done pitches the Customer lead has already checked the docs against, one number a line.
COVERED="$STATE/$TEAM/customer/covered"

KIND=$(cfg .project.ownerType)
KIND=${KIND:-organization}

gql() {
  local query=$1
  shift
  gh api graphql -F owner="$OWNER" -F number="$NUMBER" "$@" -f query="$query"
}

# items [<filter>]: the board's items, or only the ones a project filter such as `not_done` matches.
items() {
  gql "query(\$owner: String!, \$number: Int!, \$field: String!, \$endCursor: String, \$filter: String) {
      $KIND(login: \$owner) { projectV2(number: \$number) {
        items(first: 100, after: \$endCursor, query: \$filter) {
          pageInfo { hasNextPage endCursor }
          nodes {
            id
            fieldValueByName(name: \$field) { ... on ProjectV2ItemFieldSingleSelectValue { name } }
            content {
              __typename
              ... on Issue { id number title url stateReason repository { nameWithOwner } labels(first: 20) { nodes { name } }
                             issueDependenciesSummary { blockedBy }
                             issueFieldValues(first: 20) { nodes { ... on IssueFieldSingleSelectValue {
                               name field { ... on IssueFieldSingleSelect { name } } } } } }
              ... on PullRequest { id number title url repository { nameWithOwner } labels(first: 20) { nodes { name } } }
            } } } } } }" -F field="$FIELD" -f filter="${1:-}" --paginate |
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
             node: (.content.id // ""),
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
           } + (if .content.stateReason then {closed: .content.stateReason} else {} end)]'
}

item() {
  items | jq --argjson n "$1" 'map(select(.number == $n)) | first // empty'
}

# not_done: a filter leaving Done off the board, or none where the status field's name isn't one word:
# GitHub returns no items at all for a field it doesn't recognise.
not_done() {
  local option
  option=$(jq -r '.project.statusMap.Done // "Done"' "$CONFIG")
  [[ $FIELD =~ ^[A-Za-z0-9]+$ && $option != *'"'* ]] && printf -- '-%s:"%s"' "$FIELD" "$option"
  return 0
}

# The comments, closes, merges and Status changes on an issue or PR that catch_up reads from GitHub.
CATCH_UP='
  fragment IssueEvents on Issue { number timelineItems(since: $since, first: 100,
      itemTypes: [ISSUE_COMMENT, CLOSED_EVENT, PROJECT_V2_ITEM_STATUS_CHANGED_EVENT]) {
    nodes { __typename ...Commented ...Closed ...Moved } } }
  fragment PullEvents on PullRequest { number merged timelineItems(since: $since, first: 100,
      itemTypes: [ISSUE_COMMENT, CLOSED_EVENT, MERGED_EVENT, PROJECT_V2_ITEM_STATUS_CHANGED_EVENT]) {
    nodes { __typename ...Commented ...Closed ...Merged ...Moved } } }
  fragment Who on Actor { __typename login }
  fragment Commented on IssueComment { id createdAt author { ...Who } }
  fragment Closed on ClosedEvent { id createdAt stateReason actor { ...Who }
    closer { __typename ... on PullRequest { number } } }
  fragment Merged on MergedEvent { id createdAt actor { ...Who } }
  fragment Moved on ProjectV2ItemStatusChangedEvent { id createdAt previousStatus status actor { ...Who }
    project { number owner { ... on Organization { login } ... on User { login } } } }'

# catch_up <items>: records what people did on GitHub directly since the last catch-up, in one query.
catch_up() {
  [ -z "$DRY_RUN" ] || return 0
  mkdir -p "$STATE"
  local now state plan found
  now=$(date -u +%FT%TZ)
  state=$(history_sql -json "SELECT (SELECT started FROM caught_up WHERE team = $(sql "$TEAM")) AS started,
      (SELECT at FROM caught_up WHERE team = $(sql "$TEAM")) AS at,
      (SELECT MIN(at) FROM events WHERE team = $(sql "$TEAM")) AS first,
      (SELECT json_group_array(json_object('item', item, 'node', node, 'status', status))
        FROM seen WHERE team = $(sql "$TEAM")) AS seen;") || return 0
  plan=$(jq -n --argjson state "$state" --argjson all "$1" --arg now "$now" '
    $state[0] as $s | ($s.started // $s.first // $now) as $started
    | ([$started, ((($s.at // $started) | fromdateiso8601) - 600 | todateiso8601)] | max) as $since
    | {started: $started, since: $since, query: ($s.at != null or $s.first != null),
       ids: [($s.seen // "[]" | fromjson)[] | . as $was | select(.node != ""
              and ([$all[] | select(.number == $was.item and .status == $was.status)] | length) == 0) | .node]}')
  if [ "$(jq .query <<<"$plan")" = true ]; then
    found=$(gh api graphql -f q="repo:$REPO updated:>=$(jq -r .since <<<"$plan")" -f since="$(jq -r .since <<<"$plan")" \
      -f query="query(\$q: String!, \$since: DateTime!) {
        search(query: \$q, type: ISSUE, first: 100) { nodes { ...IssueEvents ...PullEvents } }
        nodes(ids: $(jq -c .ids <<<"$plan")) { ...IssueEvents ...PullEvents } } $CATCH_UP") ||
      { echo "board.sh: couldn't catch up on what happened on GitHub" >&2; return 0; }
  else
    found='{}'
  fi
  history_sql "BEGIN; $(jq -r -n --argjson found "$found" --argjson plan "$plan" --argjson all "$1" \
      --arg team "$TEAM" --arg now "$now" --arg owner "$OWNER" --arg sq "'" --argjson number "$NUMBER" \
      --argjson stakeholders "$STAKEHOLDERS" --slurpfile cfg "$CONFIG" '
    def q: $sq + (tostring | gsub($sq; $sq + $sq)) + $sq;
    ($cfg[0].project.statusMap // {} | to_entries | map({key: .value, value: .key}) | from_entries) as $rev
    | def status: if . == null or . == "" then "" else $rev[.] // . end;
    [[$found.data.search.nodes[]?, $found.data.nodes[]?] | map(select(.number != null)) | unique_by(.number)[]
     | .number as $n | .merged as $merged | .timelineItems.nodes[]
     | (.author // .actor) as $actor
     | select($actor != null and $actor.__typename != "Bot" and .createdAt >= $plan.started)
     | {id, item: $n, at: .createdAt, who: (if $stakeholders | index($actor.login) then "you" else $actor.login end),
        what: (if .__typename == "IssueComment" then "commented"
               elif .__typename == "MergedEvent" then "accepted · PR #\($n) merged"
               elif .__typename == "ClosedEvent" then
                 if $merged == true then empty
                 elif .closer.__typename == "PullRequest" then "accepted · PR #\(.closer.number) merged"
                 elif .stateReason == "COMPLETED" then "accepted · closed"
                 elif .stateReason == "NOT_PLANNED" then "closed as not planned"
                 elif .stateReason == "DUPLICATE" then "closed as a duplicate"
                 else "closed" end
               elif .project.number == $number and .project.owner.login == $owner then
                 (.previousStatus | status) as $from
                 | if $from == "" then "added as \(.status | status)" else "\($from) → \(.status | status)" end
               else empty end)}]
    | map(
        "INSERT OR IGNORE INTO github (team, id, event) SELECT \($team | q), \(.id | q), (SELECT e.id FROM events e
           WHERE e.team = \($team | q) AND e.item = \(.item) AND e.who = \(.who | q) AND e.what = \(.what | q)
             AND abs(julianday(e.at) - julianday(\(.at | q))) * 1440 <= 5
             AND NOT EXISTS (SELECT 1 FROM github g WHERE g.team = e.team AND g.event = e.id)
           ORDER BY abs(julianday(e.at) - julianday(\(.at | q))) LIMIT 1);
         INSERT INTO events (team, item, at, who, what) SELECT \($team | q), \(.item), \(.at | q), \(.who | q), \(.what | q)
           WHERE EXISTS (SELECT 1 FROM github WHERE team = \($team | q) AND id = \(.id | q) AND event IS NULL);
         UPDATE github SET event = last_insert_rowid() WHERE team = \($team | q) AND id = \(.id | q) AND event IS NULL;")
    + ["DELETE FROM seen WHERE team = \($team | q);"]
    + [$all[] | select(.node != "")
       | "INSERT INTO seen (team, item, node, status) VALUES (\($team | q), \(.number), \(.node | q), \(.status | q));"]
    + ["INSERT INTO caught_up (team, started, at) VALUES (\($team | q), \($plan.started | q), \($now | q))
          ON CONFLICT (team) DO UPDATE SET at = excluded.at;"]
    | join("\n")') COMMIT;" >/dev/null || echo "board.sh: couldn't record what happened on GitHub" >&2
}

# sql_now <modifier>: now, moved by an SQLite date modifier, in the record's own format.
sql_now() { printf "strftime('%%Y-%%m-%%dT%%H:%%M:%%SZ', 'now', '%s')" "$1"; }

# snapshot <count>: how many items are waiting on the stakeholders now, at most once an hour.
snapshot() {
  [ -z "$DRY_RUN" ] || return 0
  mkdir -p "$STATE"
  history_sql "INSERT INTO queue (team, at, waiting) SELECT $(sql "$TEAM"), $(sql "$(date -u +%FT%TZ)"), $1
    WHERE NOT EXISTS (SELECT 1 FROM queue WHERE team = $(sql "$TEAM") AND at > $(sql_now '-1 hour'));" >/dev/null ||
    echo "board.sh: couldn't record how much is waiting" >&2
}

project_raw() {
  gql "query(\$owner: String!, \$number: Int!, \$field: String!) {
      $KIND(login: \$owner) { projectV2(number: \$number) { id
        field(name: \$field) { ... on ProjectV2SingleSelectField { id options { id name color description } } } } } }" \
    -F field="$FIELD" "$@"
}

project_meta() { project_raw --jq ".data.$KIND.projectV2"; }

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
  gh api "repos/$REPO/issues/$1" --jq .body | grep -qE '<!-- a-team:(lead|dev|customer) -->'
}

# The number of the issue #1 is a sub-issue of, or nothing.
parent_of() {
  gh api "repos/$REPO/issues/$1" --jq '.parent_issue_url // empty | split("/") | last'
}

# idea <issue> <status>: the child is an idea rather than a task, by its labels or its board status.
idea() {
  jq -e '.labels | index("pitch")' <<<"$1" >/dev/null && return 0
  case "$2" in Idea | Exploring | Pitched | Approved | Building) return 0 ;; esac
  return 1
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

# ci <pr>: the CI verdict for <pr>'s head commit.
ci() {
  local sha
  sha=$(gh api "repos/$REPO/pulls/$1" --jq .head.sha) || die "could not read PR #$1"
  gh api --paginate "repos/$REPO/commits/$sha/check-runs?per_page=100" --jq '.check_runs[]' |
    jq -s "$CHECKS checks"
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
# on either. `waiting` reads whose turn it is off this page. With `checks`, each PR carries its head
# commit's check runs too.
gated_talk() {
  local said reviewed n pr page unlinked prs query='' runs=''
  local seen='reactions(content: EYES) { totalCount }'
  [ "${2:-}" = checks ] && runs='commits(last: 1) { nodes { commit { checkSuites(first: 50) { nodes {
    checkRuns(first: 100, filterBy: {checkType: LATEST}) { nodes { name status conclusion completedAt url } } } } } } }'
  said="number createdAt body author { __typename login }
        comments(last: 50) { nodes { createdAt body author { __typename login } $seen } }"
  reviewed="$said"" url isDraft mergeable baseRefName $runs
              reviews(last: 50) { nodes { createdAt body state author { __typename login } $seen
              comments(first: 50) { nodes { createdAt body author { __typename login } $seen } } } }"
  for n in $(jq -r '.[].number' <<<"$1"); do
    query+=" x$n: issueOrPullRequest(number: $n) {
      ... on Issue { $said subIssuesSummary { total completed }
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

# gated_prs <talk>: the open PR that closes each of those items, or that is the item, as {n, pr, prUrl,
# draft, conflicting, resolving, base, checks, failedAt, self}, off a page read with `checks`. UNKNOWN
# means GitHub hasn't finished computing it, so it's `resolving` rather than a conflict.
gated_prs() {
  jq "$CHECKS"'[.data.repository | to_entries[].value | select(. != null) | .number as $n
       | (if has("isDraft") then . else .closedByPullRequestsReferences.nodes[]? end)
       | ([.commits.nodes[0].commit.checkSuites.nodes[]?.checkRuns.nodes[]
           | {name, status: (.status | ascii_downcase), conclusion: (.conclusion // "" | ascii_downcase),
              completed_at: .completedAt, html_url: .url}] | checks) as $ci
       | {n: $n, pr: .number, prUrl: .url, draft: .isDraft, self: (.number == $n),
          conflicting: (.mergeable == "CONFLICTING"), resolving: (.mergeable == "UNKNOWN"), base: .baseRefName,
          checks: $ci.verdict, failedAt: ($ci.failing | map(.at // empty) | max // "")}]' <<<"$1"
}

# gated_tasks <talk>: how many tasks each of those items has, and how many are still open, as
# {"<n>": {tasks, openTasks}}.
gated_tasks() {
  jq '[.data.repository | to_entries[].value | select(.subIssuesSummary? != null)
       | {key: (.number | tostring),
          value: {tasks: .subIssuesSummary.total, openTasks: (.subIssuesSummary.total - .subIssuesSummary.completed)}}]
      | from_entries' <<<"$1"
}

# gated_blocked <talk>: when each of those items was last labelled `blocked`, as {"<n>": at}.
gated_blocked() {
  jq '[.data.repository | to_entries[].value | select(. != null)
       | {key: (.number | tostring),
          value: ([.timelineItems.nodes[]? | select(.label.name == "blocked") | .createdAt] | max)}
       | select(.value != null)] | from_entries' <<<"$1"
}

# turns <items> <comments> <prs>: each item with its PR, whose move it is and why. A gate is the
# stakeholders' until one comments; from then it is the role's, the same test `unanswered_feedback`
# makes. A PR that is failing, conflicting, not yet known to merge, still running CI or still a draft
# is the Dev's too, but an unanswered comment outranks them all: the answer is owed before a green
# build means anything.
turns() {
  jq -n --argjson items "$1" --argjson comments "$2" --argjson prs "$3" --argjson stakeholders "$STAKEHOLDERS" \
    --arg ackFrom "$ACK_FROM" --arg appFrom "$APP_FROM" --arg bot "$BOT" "$TEAM_SAID$UNANSWERED$SINCE$NEEDS"'
    $items | map(
      . as $item
      | (.role // if .status == "Pitched" then "lead" else "dev" end) as $role
      | ($comments | map(select(.n == $item.number))) as $theirs
      | ($prs | map(select(.n == $item.number and ((.self | not) or $item.role == "customer"))) | first) as $pr
      | ($theirs | said("<!-- a-team:\($role) -->")) as $said
      | ($theirs | unanswered($said) | map(.at) | max // "") as $asked
      | ($theirs | map(select(.kind == "body") | .at) | max // "") as $opened
      | (if .status == "Pitched" and .pitch
         then $theirs | map(select(.kind == "body")) | first | .body // "" | needs_answer else "" end) as $question
      | (if $pr == null then null
         elif $pr.checks == "fail" then {trouble: "CI failing", at: $pr.failedAt}
         elif $pr.conflicting then {trouble: "conflicts with \($pr.base)", at: ""}
         elif $pr.resolving then {trouble: "resolving mergeable status", at: ""}
         elif $pr.checks == "pending" then {trouble: "CI running", at: ""}
         elif $pr.draft then {trouble: "still a draft", at: ""}
         else null end) as $wrong
      | (if $pr == null then . else . + {pr: $pr.pr, prUrl: $pr.prUrl, checks: $pr.checks,
                                         conflicting: $pr.conflicting, draft: $pr.draft, base: $pr.base,
                                         unready: ($wrong.trouble // "")} end)
      | (if $question == "" then . else . + {question: $question} end)
      | if $asked != ""
        then . + {turn: $role, reason: "answering your feedback\(since($asked))"}
        elif $wrong != null
        then . + {turn: $role, trouble: $wrong.trouble,
                  reason: "\($wrong.trouble)\(since($wrong.at))"}
        else (if $said != "" then $said else $opened end) as $waited
             | (if $question != "" then "asked you"
                elif .status == "Pitched" then "awaiting your approval" else "awaiting your acceptance" end) as $why
             | . + {turn: "you", reason: "\($why)\(since($waited))"}
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

# close_pitch <n> [built]: closes pitch #n once all its tasks are closed; `built` also needs it to have some.
close_pitch() {
  local subs open refused
  subs=$(gh api --paginate "repos/$REPO/issues/$1/sub_issues" | jq -s 'add // []')
  [ "${2:-}" != built ] || [ "$(jq length <<<"$subs")" -gt 0 ] || die "#$1 has no tasks, so nothing was built"
  open=$(jq 'map(select(.state == "open")) | length' <<<"$subs")
  [ "$open" -eq 0 ] || die "#$1 has $open open task$([ "$open" -eq 1 ] || echo s)"
  close() { { gh issue close "$1" -R "$REPO" --reason completed >/dev/null; } 2>&1; }
  refused=$(write "close #$1 as completed" close "$1") || die "can't close #$1 ($(head -1 <<<"$refused"))"
}

# own_task <role> <n>: a Dev run started for one task touches only that task and its PR.
own_task() {
  local task=${A_TEAM_RUN_TASK:-}
  [ "$1" = dev ] && [ -n "$task" ] && [ "$2" != "$task" ] || return 0
  [ "$(pr_for "$task" | jq -r '.number // empty')" = "$2" ] ||
    die "this run is for #$task: leave #$2 to a run of its own"
}

# own_docs <role> <n>: the Customer lead touches only its own docs PR.
own_docs() {
  [ "$1" = customer ] || return 0
  item "$2" | jq -e '.labels | index("a-team:customer")' >/dev/null ||
    die "customer only touches its own docs PR (#$2 isn't it)"
}

# merge <pr>: squash-merges <pr> and deletes its branch.
merge() {
  local head refused ref
  head=$(gh api "repos/$REPO/pulls/$1" --jq '{ref: .head.ref, repo: (.head.repo.full_name // "")}')
  squash() { { gh api -X PUT "repos/$REPO/pulls/$1/merge" -f merge_method=squash >/dev/null; } 2>&1; }
  refused=$(write "squash-merge PR #$1" squash "$1") || die "can't merge PR #$1 ($(head -1 <<<"$refused"))"
  # A repo that deletes merged branches itself has already done it, so that refusal is no failure.
  if [ "$(jq -r .repo <<<"$head")" = "$REPO" ]; then
    ref=$(jq -r .ref <<<"$head")
    write "delete branch $ref" gh api -X DELETE "repos/$REPO/git/refs/heads/$ref" >/dev/null 2>&1 || true
  fi
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
       stakeholders: map(select((.labels | index("pitch") or index("a-team:dev") or index("a-team:customer")) | not))
         | counts}'
    ;;

  next)
    items | jq "map(select($STARTABLE))" | by_priority | jq first
    ;;

  claim)
    [ $# -ge 1 ] && [ $# -le 2 ] || die "usage: board.sh $TEAM claim dev [<n>]"
    [ "$1" = dev ] || die "only dev claims a task"
    all=$(items)
    startable=$(jq "map(select($STARTABLE))" <<<"$all" | by_priority)
    if [ $# -eq 2 ]; then
      it=$(jq --argjson n "$2" 'map(select(.number == $n)) | first // empty' <<<"$startable")
      [ -n "$it" ] || die "#$2 isn't a Ready task the Dev can start: it's claimed, blocked or not Ready"
    else
      it=$(jq 'first // empty' <<<"$startable")
      [ -n "$it" ] || { echo null; exit 0; }
    fi
    used=$(jq "[.[] | select($WORKING)] | length" <<<"$all")
    limit=$(cfg .wip.worktrees)
    [ "$used" -lt "$limit" ] || { [ "$used" -lt $((limit + 1)) ] && [ "$(jq -r .priority <<<"$it")" = Urgent ]; } ||
      die "no free worktree for #$(jq -r .number <<<"$it") ($used in use, wip.worktrees is $limit)"
    n=$(jq -r .number <<<"$it")
    write "label #$n a-team:dev" gh issue edit "$n" -R "$REPO" --add-label a-team:dev >/dev/null
    set_status "$(jq -r .id <<<"$it")" "In progress"
    record dev "$n" "Ready → In progress"
    jq -c '{number, title}' <<<"$it"
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
    own_task "$role" "$n"
    own_docs "$role" "$n"
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
    record "$role" "$n" "$from → $to"
    say "#$n: $from -> $to"
    ;;

  add)
    [ $# -eq 3 ] || die "usage: board.sh $TEAM add <role> <n> <status>"
    role=$1 n=$2 to=$3
    check_role "$role"
    is_state "$to" || die "unknown status '$to'"
    allowed "$role" None "$to" || die "$role may not add items as '$to'"
    if [ "$role:$to" = "customer:In review" ]; then
      customer_on || die "$TEAM has no Customer lead (roles.customer in $TEAM.json)"
      gh api "repos/$REPO/issues/$n" --jq '.pull_request != null and .state == "open"
          and ((.body // "") | contains("<!-- a-team:customer -->"))' | grep -qx true ||
        die "customer may only add its own open docs PR (#$n isn't one)"
      other=$(items | jq -r --argjson n "$n" '[.[] | select((.labels | index("a-team:customer"))
        and .status == "In review" and .number != $n) | .number] | first // empty')
      [ -z "$other" ] || die "customer's docs PR #$other is still open: add to it rather than opening another"
    elif [ "$role:$to" = "customer:Idea" ]; then
      customer_on || die "$TEAM has no Customer lead (roles.customer in $TEAM.json)"
      gh api "repos/$REPO/issues/$n" --jq '.pull_request == null and .state == "open"
          and ((.body // "") | contains("<!-- a-team:customer -->"))' | grep -qx true ||
        die "customer may only add an open issue it opened as an Idea (#$n isn't one)"
      found=$(items | jq --argjson n "$n" '[.[] | select((.labels | index("a-team:idea"))
        and .status == "Idea" and .number != $n)] | length')
      limit=$(cfg '.wip.ideas // 0')
      [ "$found" -lt "$limit" ] ||
        die "$found discovered Ideas are waiting for triage (wip.ideas is $limit): leave #$n off the board"
    fi
    existing=$(item "$n")
    if [ -n "$existing" ]; then
      # The project's auto-add workflow may already have put a new issue on the board.
      case "$(jq -r .status <<<"$existing")" in None | Idea) ;; *) die "#$n is already on the board (use move)" ;; esac
      gh api "repos/$REPO/issues/$n" --jq .body | grep -qF "<!-- a-team:$role -->" ||
        die "#$n is already on the board and isn't $role's (use move)"
    elif [ "$role" = dev ]; then
      gh api "repos/$REPO/issues/$n" --jq .body | grep -qF "<!-- a-team:dev -->" ||
        die "#$n has no dev marker: dev adds only the follow-ups it opened"
    fi
    content=$(gh api "repos/$REPO/issues/$n" --jq 'if .pull_request then "pulls" else "issues" end')
    case "$role:$to" in
      lead:Idea | customer:Idea) write "label #$n a-team:idea" gh api -X POST "repos/$REPO/issues/$n/labels" -f 'labels[]=a-team:idea' >/dev/null ;;
      lead:Exploring | lead:Pitched) write "label #$n pitch" gh api -X POST "repos/$REPO/issues/$n/labels" -f 'labels[]=pitch' >/dev/null ;;
      "customer:In review") write "label #$n a-team:customer" gh api -X POST "repos/$REPO/issues/$n/labels" -f 'labels[]=a-team:customer' >/dev/null ;;
    esac
    if [ -n "$existing" ]; then
      set_status "$(jq -r .id <<<"$existing")" "$to"
      record "$role" "$n" "added as $to"
      say "#$n: added as $to"
      exit 0
    fi
    id=$(write "add #$n to the board" gh api graphql -F project="$(project_meta | jq -r .id)" \
      -F content="$(gh api "repos/$REPO/$content/$n" --jq .node_id)" -f query='
      mutation($project: ID!, $content: ID!) {
        addProjectV2ItemById(input: {projectId: $project, contentId: $content}) { item { id } } }' \
      --jq .data.addProjectV2ItemById.item.id)
    set_status "${id:-new-item}" "$to"
    record "$role" "$n" "added as $to"
    say "#$n: added as $to"
    ;;

  priority)
    [ $# -eq 3 ] || die "usage: board.sh $TEAM priority <role> <n> <value|none>"
    role=$1 n=$2 value=$3
    case "$role" in
      lead | dev | customer) die "$role may not set a $PRIORITY; ranking an item is the stakeholders' own gate" ;;
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
      record "$role" "$n" "$PRIORITY cleared"
      say "#$n: $PRIORITY cleared"
    else
      write "set $PRIORITY on #$n to '$value'" gh api graphql -F issue="$issue" \
        -F field="$(jq -r .id <<<"$field")" -F option="$option" -f query='
        mutation($issue: ID!, $field: ID!, $option: ID!) {
          updateIssueFieldValue(input: {issueId: $issue, issueField: {fieldId: $field,
                                        singleSelectOptionId: $option}}) { clientMutationId } }' >/dev/null
      record "$role" "$n" "$PRIORITY set to $value"
      say "#$n: $PRIORITY set to '$value'"
    fi
    ;;

  approve)
    [ $# -eq 2 ] || die "usage: board.sh $TEAM approve <role> <n>"
    role=$1 n=$2
    case "$role" in
      lead | dev | customer) die "$role may not approve a pitch; approving is the stakeholders' own gate" ;;
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
    record "$role" "$n" "Pitched → Approved"
    say "#$n: Pitched -> Approved"
    ;;

  accept)
    [ $# -eq 2 ] || die "usage: board.sh $TEAM accept <role> <n>"
    role=$1 n=$2
    case "$role" in
      lead | dev | customer) die "$role may not accept a task; accepting is the stakeholders' own gate" ;;
      you) ;;
      *) die "unknown role '$role' (you)" ;;
    esac
    it=$(item "$n")
    [ -n "$it" ] || die "#$n is not on the board"
    status=$(jq -r .status <<<"$it")
    if [ "$(jq -r .type <<<"$it")" = PullRequest ] && jq -e '.labels | index("a-team:customer")' <<<"$it" >/dev/null; then
      [ "$status" = "In review" ] || die "only a docs PR In review can be accepted (#$n is in '$status')"
      merge "$n"
      set_status "$(jq -r .id <<<"$it")" Done
      record "$role" "$n" "accepted · PR #$n merged"
      say "#$n: merged docs PR #$n"
      exit 0
    fi
    [ "$(jq -r .type <<<"$it")" = Issue ] || die "#$n is not an issue, so it is not a task to accept"
    if jq -e '.labels | index("pitch")' <<<"$it" >/dev/null; then
      [ "$status" = "In review" ] || die "only a pitch In review can be accepted (#$n is in '$status')"
      close_pitch "$n"
      record "$role" "$n" "accepted · closed"
      say "#$n: closed as done"
      exit 0
    fi
    [ "$status" = "In review" ] || die "only a task In review can be accepted (#$n is in '$status')"
    pr=$(pr_for "$n" | jq -r '.number // empty')
    [ -n "$pr" ] || die "#$n has no open PR to merge"
    merge "$pr"
    record "$role" "$n" "accepted · PR #$pr merged"
    say "#$n: merged PR #$pr"
    ;;

  finish)
    [ $# -eq 3 ] || die "usage: board.sh $TEAM finish <role> <n> <file>"
    role=$1 n=$2 file=$3
    check_role "$role"
    [ "$role" = lead ] || die "only lead may close a pitch as done"
    [ -f "$file" ] || die "no such file: $file"
    it=$(item "$n")
    [ -n "$it" ] || die "#$n is not on the board"
    jq -e '.labels | index("pitch")' <<<"$it" >/dev/null || die "#$n is not a pitch (no 'pitch' label)"
    status=$(jq -r .status <<<"$it")
    [ "$status" = Building ] || die "only a pitch in Building can be closed as done (#$n is in '$status')"
    close_pitch "$n" built
    body=$(cat "$file"; printf '\n\n<!-- a-team:%s -->' "$role")
    [ -z "$DRY_RUN" ] || printf '%s\n' "$body" | sed 's/^/  | /' >&2
    printf '%s\n' "$body" | write "comment on #$n" gh issue comment "$n" -R "$REPO" --body-file -
    ack "$n"
    record "$role" "$n" "closed as done"
    say "#$n: closed as done"
    ;;

  comment)
    [ $# -eq 3 ] || die "usage: board.sh $TEAM comment <role> <n> <file>"
    role=$1 n=$2 file=$3
    case "$role" in lead | dev | customer | you) ;; *) die "unknown role '$role' (lead | dev | customer | you)" ;; esac
    [ -f "$file" ] || die "no such file: $file"
    own_task "$role" "$n"
    own_docs "$role" "$n"
    # Your comment is feedback the roles still owe an answer: no marker, and no 👀.
    if [ "$role" = you ]; then
      body=$(cat "$file")
    else
      body=$(cat "$file"; printf '\n\n<!-- a-team:%s -->' "$role")
    fi
    [ -z "$DRY_RUN" ] || printf '%s\n' "$body" | sed 's/^/  | /' >&2
    printf '%s\n' "$body" | write "comment on #$n" gh issue comment "$n" -R "$REPO" --body-file -
    [ "$role" = you ] || ack "$n"
    record "$role" "$n" "commented"
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
    record "$role" "$n" "skipped"
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
    child=$(gh api "repos/$REPO/issues/$2" --jq '{id, labels: [.labels[].name]}')
    child_id=$(jq -r .id <<<"$child")
    idea "$child" "$(item "$2" | jq -r '.status // empty')" &&
      die "#$2 is an idea, not a task: say \"Follow-up from #$1\" in its body instead of linking it"
    write "make #$2 a sub-issue of #$1" gh api -X POST "repos/$REPO/issues/$1/sub_issues" -F "sub_issue_id=$child_id" >/dev/null
    # Only the Lead draws breakdowns, and link names no role.
    record lead "$2" "made a sub-issue of #$1"
    say "#$2 is now a sub-issue of #$1"
    ;;

  depends)
    [ $# -eq 4 ] || die "usage: board.sh $TEAM depends <role> <task> <prerequisite> \"<why>\""
    role=$1 task=$2 prereq=$3 why=$4
    check_role "$role"
    [ "$role" != customer ] || die "customer may not change what a task waits on"
    own_task "$role" "$task"
    write "block #$task on #$prereq" gh api -X POST "repos/$REPO/issues/$task/dependencies/blocked_by" \
      -F "issue_id=$(gh api "repos/$REPO/issues/$prereq" --jq .id)" >/dev/null
    depend_note "$role" "$task" "Blocked by #$prereq: $why"
    record "$role" "$task" "blocked by #$prereq"
    say "#$task is now blocked by #$prereq, and said why on #$task"
    ;;

  undepend)
    [ $# -eq 4 ] || die "usage: board.sh $TEAM undepend <role> <task> <prerequisite> \"<why>\""
    role=$1 task=$2 prereq=$3 why=$4
    check_role "$role"
    [ "$role" != customer ] || die "customer may not change what a task waits on"
    own_task "$role" "$task"
    prereq_id=$(gh api "repos/$REPO/issues/$task/dependencies/blocked_by" |
      jq -r --argjson n "$prereq" '[.[] | select(.number == $n) | .id] | first // empty')
    [ -n "$prereq_id" ] || die "#$task is not blocked by #$prereq"
    write "unblock #$task from #$prereq" \
      gh api -X DELETE "repos/$REPO/issues/$task/dependencies/blocked_by/$prereq_id" >/dev/null
    depend_note "$role" "$task" "No longer blocked by #$prereq: $why"
    record "$role" "$task" "no longer blocked by #$prereq"
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
    issue=$(gh api "repos/$REPO/issues/$child" --jq '{id, title, body: (.body // ""), labels: [.labels[].name]}')
    jq -e --argjson n "$parent" 'map(select(.number == $n)) | first | (.labels // []) | index("pitch")' <<<"$all" >/dev/null ||
      die "$role may only take a task off a pitch (#$parent is not one)"
    pitch_status=$(board_status "$parent")
    followup=
    idea "$issue" "$from" && followup=1
    if [ -n "$followup" ]; then
      [ "$pitch_status" != Done ] || die "$role may not change a Done pitch (#$parent)"
    else
      [ -n "$from" ] || die "#$child is not on the board"
      [ "$from" = Ready ] || die "$role may only take a Ready task off a pitch (#$child is '$from')"
      [ "$pitch_status" = Building ] ||
        die "$role may only take a task off a pitch in Building (#$parent is '$pitch_status')"
    fi
    [ "$(parent_of "$child")" = "$parent" ] || die "#$child is not a sub-issue of #$parent"
    write "take #$child off #$parent" gh api -X DELETE "repos/$REPO/issues/$parent/sub_issue" \
      -F "sub_issue_id=$(jq -r .id <<<"$issue")" >/dev/null
    record "$role" "$child" "taken off #$parent"
    if [ -z "$followup" ]; then
      say "#$child is no longer a sub-issue of #$parent"
      exit 0
    fi
    body=$(printf '#%s (%s) grew out of this pitch and is its own item now, so it no longer holds this one up. Its body names #%s as where it came from.\n\n<!-- a-team:%s -->' \
      "$child" "$(jq -r .title <<<"$issue")" "$parent" "$role")
    [ -z "$DRY_RUN" ] || printf '%s\n' "$body" | sed 's/^/  | /' >&2
    printf '%s\n' "$body" | write "comment on #$parent" gh issue comment "$parent" -R "$REPO" --body-file -
    if ! jq -e --arg p "$parent" '.body | test("Follow-up from #" + $p + "\\b")' <<<"$issue" >/dev/null; then
      write "add 'Follow-up from #$parent' to #$child" gh issue edit "$child" -R "$REPO" \
        --body "$(jq -r --arg p "$parent" '"Follow-up from #\($p)\n\n\(.body)"' <<<"$issue")" >/dev/null
    fi
    say "#$child is no longer a sub-issue of #$parent; said so on #$parent, and #$child names #$parent as where it came from"
    ;;

  unblock)
    [ $# -eq 2 ] || die "usage: board.sh $TEAM unblock <role> <n>"
    role=$1 n=$2
    check_role "$role"
    [ "$role" = dev ] || die "only dev may unblock a task, and only one it handed back with a question"
    own_task "$role" "$n"
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

  covered)
    [ $# -eq 2 ] || die "usage: board.sh $TEAM covered <role> <pitch>"
    role=$1 n=$2
    [ "$role" = customer ] || die "only customer checks the docs against a done pitch"
    item "$n" | jq -e '(.labels | index("pitch")) and .status == "Done"' >/dev/null ||
      die "#$n isn't a done pitch"
    if [ -z "$DRY_RUN" ]; then
      mkdir -p "$(dirname "$COVERED")"
      grep -qx "$n" "$COVERED" 2>/dev/null || echo "$n" >>"$COVERED"
    fi
    record "$role" "$n" "docs checked"
    say "#$n: docs checked"
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

  conversation)
    [ $# -eq 1 ] || die "usage: board.sh $TEAM conversation <n>"
    trouble=$(mktemp)
    talk=$(gated_talk "[{\"number\": $1}]" 2>"$trouble") ||
      { reason=$(head -1 "$trouble"); rm -f "$trouble"; die "can't read the conversation on #$1 ($reason)"; }
    rm -f "$trouble"
    jq --argjson stakeholders "$STAKEHOLDERS" --arg appFrom "$APP_FROM" --arg bot "$BOT" \
      "def login: $LOGIN; $TEAM_SAID"'
      def remark($pr): {at: .createdAt, author: (.author | login), body: (.body // ""), pr: $pr};
      def who: if team("<!-- a-team:lead -->") then "lead" elif team("<!-- a-team:dev -->") then "dev"
        elif team("<!-- a-team:customer -->") then "customer"
        elif .author | IN($stakeholders[]) then "you" else .author end;
      [.data.repository | to_entries[].value | select(. != null)
       | (.comments.nodes[]? | remark(null)),
         (.closedByPullRequestsReferences.nodes[]? | .number as $pr
          | (remark($pr) + {description: true}), (.comments.nodes[]? | remark($pr)))]
      | sort_by(.at)
      | map({who: who, at, pr, description: (.description // false),
             body: (.body | gsub("[ \t]*<!-- a-team:(lead|dev|customer) -->[ \t]*"; "") | sub("\\s+$"; ""))})' <<<"$talk"
    ;;

  children)
    [ $# -eq 1 ] || die "usage: board.sh $TEAM children <n>"
    gh api --paginate "repos/$REPO/issues/$1/sub_issues" |
      jq -s --argjson all "$(items)" 'add // [] | map(.number as $n | {number, title, state, labels: [.labels[].name],
        status: ([$all[] | select(.number == $n) | .status] | first)})'
    ;;

  waiting)
    [ $# -eq 0 ] || die "usage: board.sh $TEAM waiting"
    all=$(items "$(not_done)")
    catch_up "$all"
    gated=$(jq --arg team "$TEAM" 'map(select(.status == "Pitched" or .status == "In review")
      | {number, title, status, url, team: $team, priority,
         pitch: (.type == "Issue" and (.labels | index("pitch")) != null)}
        + if .labels | index("a-team:customer") then {role: "customer"} else {} end)' <<<"$all")
    held=$(jq --arg team "$TEAM" "map(select($HELD)
      | {number, title, status, url, team: \$team, priority, pitch: false})" <<<"$all")
    talk=$(gated_talk "$(jq -s add <<<"$gated$held")" checks)
    said=$(gated_comments "$talk")
    waiting=$(turns "$gated" "$said" "$(gated_prs "$talk")" |
      jq --argjson tasks "$(gated_tasks "$talk")" \
        'map(if .pitch and .status == "In review" then . + ($tasks[.number | tostring] // {}) else . end)' |
      jq --argjson asked "$(questions "$held" "$said" "$(gated_blocked "$talk")" | jq 'map(del(.unread))')" \
        --argjson unranked "$(unranked_ideas "$all")" '. + $asked + $unranked')
    snapshot "$(jq length <<<"$waiting")"
    printf '%s\n' "$waiting"
    ;;

  trend)
    [ $# -eq 0 ] || die "usage: board.sh $TEAM trend"
    if [ ! -f "$STATE/history.db" ]; then
      echo '{"since": null, "weekAgo": null, "accepted": 0}'
      exit 0
    fi
    team=$(sql "$TEAM")
    history_sql -json "SELECT
        (SELECT MIN(at) FROM (SELECT at FROM events WHERE team = $team UNION ALL
          SELECT started FROM caught_up WHERE team = $team UNION ALL SELECT at FROM queue WHERE team = $team)) AS since,
        (SELECT waiting FROM queue WHERE team = $team AND at <= $(sql_now '-7 days') AND at > $(sql_now '-8 days')
          ORDER BY at DESC LIMIT 1) AS weekAgo,
        (SELECT COUNT(DISTINCT CASE WHEN what LIKE 'accepted · PR #%' THEN what ELSE item END) FROM events
          WHERE team = $team AND who = 'you' AND what LIKE 'accepted · %' AND at > $(sql_now '-7 days')) AS accepted;" |
      jq '.[0]'
    ;;

  history)
    [ $# -eq 1 ] || die "usage: board.sh $TEAM history <n>"
    [[ $1 =~ ^[0-9]+$ ]] || die "#$1 isn't an item number"
    if [ ! -f "$STATE/history.db" ]; then
      echo '{"since": null, "events": []}'
      exit 0
    fi
    events=$(history_sql -json "SELECT at, who, what, 0 AS run, NULL AS ended, NULL AS cost, NULL AS outcome, id
        FROM events WHERE team = $(sql "$TEAM") AND item = $1
      UNION ALL SELECT started, role, 'run', 1, ended, cost, outcome, runs.id
        FROM runs JOIN run_items ON run_items.run = runs.id WHERE team = $(sql "$TEAM") AND item = $1
      ORDER BY at DESC, run DESC, id DESC;")
    since=$(history_sql "SELECT MIN(at) FROM (SELECT at FROM events WHERE team = $(sql "$TEAM")
      UNION ALL SELECT started FROM runs WHERE team = $(sql "$TEAM"));")
    jq -n --argjson events "${events:-[]}" --arg since "$since" '{
      since: (if $since == "" then null else $since end),
      events: [$events[] | {at, who, what} + if .run == 1 then {run: {ended, cost, outcome}} else {} end]}'
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
    recent='[]'
    [ "$role" = customer ] || recent=$(recent_comments)
    tasks='[]' chores=() ready='' items=()
    # task_reason <n> <title> <reason>: a reason the Dev has to start a run on task #n.
    task_reason() {
      reasons+=("$3")
      tasks=$(jq -c --argjson n "$1" --arg title "$2" --arg why "$3" '
        if any(.[]; .number == $n) then map(if .number == $n then .reasons += [$why] else . end)
        else . + [{number: $n, title: $title, reasons: [$why]}] end' <<<"$tasks")
    }
    if [ "$role" = dev ]; then
      while IFS= read -r row; do
        n=$(jq -r .number <<<"$row")
        title=$(jq -r .title <<<"$row")
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
          [ "$verdict" = fail ] && task_reason "$n" "$title" "CI failed on PR #$p at $(gh api "repos/$REPO/pulls/$p" --jq '.head.sha[:7]')$running"
          [ "$verdict" = pass ] && [ "$(jq -r .isDraft <<<"$pr")" = true ] &&
            task_reason "$n" "$title" "PR #$p is green but still a draft"
          # UNKNOWN means GitHub hasn't finished computing it, so only CONFLICTING fires.
          [ "$(jq -r .mergeable <<<"$pr")" = CONFLICTING ] &&
            task_reason "$n" "$title" "PR #$p conflicts with its base: merge the base branch into it and resolve"
        elif [ "$status" = "In progress" ]; then
          task_reason "$n" "$title" "#$n is In progress but has no PR: an earlier run didn't finish"
        fi
        for x in "${numbers[@]}"; do
          at=$(feedback_at "$recent" dev "$x")
          [ -n "$at" ] && task_reason "$n" "$title" "stakeholder feedback on #$x ($at)"
        done
      done < <(jq -c '.[] | select((.labels | index("a-team:dev")) and (.status == "In progress" or .status == "In review"))' <<<"$all")

      held=$(jq -c "map(select($HELD))" <<<"$all")
      if [ "$held" != "[]" ]; then
        talk=$(gated_talk "$held")
        while IFS=$'\t' read -r n at title; do
          task_reason "$n" "$title" "stakeholder answered the Dev's question on #$n ($at)"
        done < <(questions "$held" "$(gated_comments "$talk")" "$(gated_blocked "$talk")" |
          jq -r '.[] | select(.unread != "") | [.number, .unread, .title] | @tsv')
      fi

      checkout=$(cfg .checkout)
      checkout=${checkout/#\~/$HOME}
      if [ -d "$checkout" ]; then
        while IFS= read -r branch; do
          merged=$(gh api "repos/$REPO/pulls?head=${REPO%/*}:$branch&state=closed&per_page=5" \
            --jq '[.[] | select(.merged_at != null and ((.body // "") | contains("<!-- a-team:dev -->")))][0].number // empty')
          [ -n "$merged" ] && chores+=("PR #$merged has merged: clean up its worktree")
        done < <(git -C "$checkout" worktree list --porcelain | sed -n 's|^branch refs/heads/||p')
      fi

      used=$(jq "[.[] | select($WORKING)] | length" <<<"$all")
      limit=$(cfg .wip.worktrees)
      startable=$(jq "[.[] | select($STARTABLE)]" <<<"$all")
      first=$(jq -r '.[0].number // empty' <<<"$startable")
      if [ -n "$first" ]; then
        if [ "$used" -lt "$limit" ]; then
          ready=$first
          reasons+=("Ready task available (e.g. #$ready) and a free worktree")
        elif [ "$used" -lt $((limit + 1)) ]; then
          ready=$(by_priority <<<"$startable" | jq -r '[.[] | select(.priority == "Urgent")][0].number // empty')
          [ -n "$ready" ] && reasons+=("Ready task available (e.g. #$ready, Urgent) and a fast-track worktree ($used in use, $((limit + 1)) allowed while an Urgent task is Ready)")
        fi
      fi
      reasons+=(${chores[@]+"${chores[@]}"})
      creative=false
    elif [ "$role" = customer ]; then
      if customer_on; then
        accepted=$(jq -r '.[] | select((.labels | index("pitch")) and .status == "Done" and .closed == "COMPLETED")
          | .number' <<<"$all")
        # Turned on, it starts from the pitches done after that, not from every one before.
        if [ ! -f "$COVERED" ]; then
          mkdir -p "$(dirname "$COVERED")"
          printf '%s\n' "$accepted" >"$COVERED"
        else
          for n in $accepted; do
            grep -qx "$n" "$COVERED" || reasons+=("pitch #$n is done: check the docs cover what it shipped")
          done
        fi
      fi
      creative=false
    else
      while IFS= read -r row; do
        n=$(jq -r .number <<<"$row")
        status=$(jq -r .status <<<"$row")
        had=${#reasons[@]}
        at=$(feedback_at "$recent" lead "$n")
        [ -n "$at" ] && reasons+=("stakeholder feedback on #$n ($at)")
        [ "$status" = Approved ] && reasons+=("#$n was approved: break it down")
        if [ "$status" = Building ]; then
          open=$(gh api "repos/$REPO/issues/$n/sub_issues" --jq '[length, (map(select(.state == "open")) | length)] | @tsv')
          [ "${open%%$'\t'*}" -gt 0 ] && [ "${open##*$'\t'}" -eq 0 ] &&
            reasons+=("all of #$n's tasks are closed: check it and close it")
        fi
        [ "${#reasons[@]}" -eq "$had" ] || items+=("$n")
      done < <(jq -c '.[] | select((.labels | index("pitch")) and .status != "Idea" and .status != "Done")' <<<"$all")

      updates=$(gh pr list -R "$REPO" --author app/dependabot --state open --json number,title,comments)
      while IFS=$'\t' read -r n title; do
        reasons+=("Dependabot opened PR #$n ($title): see what the update brings")
        items+=("$n")
      done < <(jq -r --arg bot "$(cfg .app.slug)" '.[]
        | select(any(.comments[]; .author.login == $bot and (.body | contains("<!-- a-team:lead -->"))) | not)
        | [.number, .title] | @tsv' <<<"$updates")

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
    jq -n --argjson creative "$creative" --arg role "$role" --argjson tasks "$tasks" --arg ready "$ready" \
      --argjson chores "$(jq -n '$ARGS.positional' --args ${chores[@]+"${chores[@]}"})" \
      --argjson items "$(jq -n '$ARGS.positional | map(tonumber)' --args ${items[@]+"${items[@]}"})" '
      {reasons: $ARGS.positional, creative: $creative}
      + if $role == "dev"
        then {tasks: $tasks, ready: (if $ready == "" then null else $ready | tonumber end), chores: $chores}
        else {items: $items} end' \
      --args "${reasons[@]+"${reasons[@]}"}"
    ;;

  check)
    # Exits 1 when the team can't run, and 2 when it can but something it uses is missing.
    fatal=0 minor=0
    problem() { printf '%-10s%s\n' "$1" "$2"; fatal=1; }
    note() { printf '%-10s%s\n' "$1" "$2"; minor=1; }
    reached='' unreachable=''
    if [ -z "$REPO" ]; then
      problem repo "$TEAM.json names no repo"
    elif reason=$(gh api "repos/$REPO" --silent 2>&1); then
      reached=1
    else
      problem repo "can't reach $REPO: ${reason#gh: }"
    fi
    workdir=$(cfg .workdir)
    checkout=$(cfg .checkout)
    checkout=${checkout:-${workdir%/}/main}
    if [ -z "$workdir" ]; then
      problem workdir "$TEAM.json names no workdir"
    elif [ ! -d "${workdir/#\~/$HOME}" ]; then
      problem workdir "$workdir isn't there, so the agents would have nothing to work in"
    elif [ ! -d "${checkout/#\~/$HOME}" ]; then
      problem checkout "$checkout isn't there: gh repo clone $REPO $checkout"
    fi
    vision=$(cfg .vision)
    if [ -n "$reached" ] && [ -n "$vision" ] && ! gh api "repos/$REPO/contents/$vision" --silent >/dev/null 2>&1; then
      note vision "$vision isn't in $REPO yet: the Lead will draft one and open it as a draft PR"
    fi
    release=$(cfg .release)
    case "${release:-never}" in
      never) ;;
      daily | continuous)
        [ -z "$reached" ] || gh api "repos/$REPO/actions/workflows/release.yml" --silent >/dev/null 2>&1 ||
          note release "release is $release, but $REPO has no release.yml workflow for it to run" ;;
      *) note release "release is '$release': expected never, daily or continuous" ;;
    esac
    if [ -z "$NUMBER" ]; then
      problem project "$TEAM.json names no project number"
    elif ! meta=$({ project_raw 2>/dev/null || true; } | jq -ce --arg k "$KIND" '.data[$k].projectV2 // empty' 2>/dev/null); then
      reason=$(project_raw 2>&1 >/dev/null | tail -n 1) || true
      problem project "can't read $OWNER project $NUMBER: ${reason#gh: }"
      unreachable=1
    elif ! jq -e '.field.id' <<<"$meta" >/dev/null 2>&1; then
      problem project "no single-select field '$FIELD' on $OWNER project $NUMBER"
    else
      missing=()
      for s in "${STATES[@]}"; do
        option=$(jq -r --arg s "$s" '.project.statusMap[$s] // $s' "$CONFIG")
        jq -e --arg o "$option" '.field.options | map(.name) | index($o)' <<<"$meta" >/dev/null ||
          missing+=("$option")
      done
      if [ ${#missing[@]} -gt 0 ]; then
        problem status "${#missing[@]} of ${#STATES[@]} options missing from '$FIELD': $(printf '%s, ' "${missing[@]}" | sed 's/, $//')"
      else
        unmapped=$(items | jq -r '[.[] | select(.status | startswith("?")) | "#\(.number) \(.status[1:])"] | join(", ")') ||
          unmapped=
        if [ -n "$unmapped" ]; then
          problem items "outside the team's states: $unmapped"
        else
          echo "ok: $OWNER project $NUMBER, field '$FIELD'"
        fi
      fi
    fi
    if [ -n "$reached" ]; then
      if ! have=$(gh label list -R "$REPO" --limit 500 --json name --jq '.[].name' 2>&1); then
        note labels "can't list $REPO's labels: ${have#gh: }"
      else
        while IFS='|' read -r name _ _ without; do
          [ "$name" != a-team:customer ] || customer_on || continue
          grep -qxF -- "$name" <<<"$have" || note labels "no '$name' label, so $without"
        done <<<"$LABELS"
      fi
    fi
    identity() {
      [ -n "$BOT" ] || { problem app "$TEAM has no GitHub App: run a-team app create $TEAM, then install it"; return; }
      source "$ROOT/scripts/github-app.sh"
      has_app_key "${REPO%/*}" || {
        problem app "$BOT has no key in the login Keychain (service '$KEYCHAIN_SERVICE', account '${REPO%/*}'): run a-team app create $TEAM"
        return
      }
      local token line="identity: $BOT"
      token=$("$ROOT/bin/a-team" token "$TEAM" 2>&1) || { problem app "$BOT can't get a token: ${token#a-team token: }"; return; }
      line="$line · token ok"
      # The project's own problem says enough when nobody can read it.
      [ -n "$unreachable" ] || GH_TOKEN=$token gql 'query($owner: String!, $number: Int!) {
          organization(login: $owner) { projectV2(number: $number) { items(first: 1) { totalCount } } } }' \
        --jq '.data.organization.projectV2.items.totalCount' >/dev/null 2>&1 || {
        problem app "$BOT can't read project $NUMBER: grant the organisation's \"Projects: read and write\", and install the App on $REPO"
        return
      }
      granted() { jq -e --arg p "$1" '.permissions[$p] == "write"' "$(token_cache "$TEAM")" >/dev/null 2>&1; }
      [ -n "$unreachable" ] || granted organization_projects ||
        { problem app "$BOT can only read project $NUMBER: grant the organisation's \"Projects: read and write\""; return; }
      [ -n "$unreachable" ] || line="$line · project $NUMBER read+write ok"
      GH_TOKEN=$token priority_field >/dev/null 2>&1 || {
        problem app "$BOT can't read the $PRIORITY field: grant the organisation's \"Issue Fields: read\", or \`triggers\` fails with \"Resource not accessible by integration\""
        return
      }
      line="$line · $PRIORITY readable"
      granted contents || { problem app "$BOT can't push to $REPO: grant the repository's \"Contents: read and write\""; return; }
      echo "$line · push access to $REPO ok"
    }
    [ -z "$NUMBER" ] || identity
    [ "$fatal" = 0 ] || exit 1
    [ "$minor" = 0 ] || exit 2
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
    while IFS='|' read -r name color description _; do
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
    done <<<"$LABELS"
    ;;

  *)
    die "unknown command '$CMD' (see process.md)"
    ;;
esac
