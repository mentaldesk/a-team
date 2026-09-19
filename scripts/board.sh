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
REVIEWER=$(cfg .reviewer)
[ -n "$NUMBER" ] || die "project.number is not set in $CONFIG"

is_state() {
  local s
  for s in "${STATES[@]}"; do [ "$s" = "$1" ] && return 0; done
  return 1
}

allowed() {
  case "$1:$2>$3" in
    "lead:Idea>Exploring" | "lead:Exploring>Pitched" | "lead:Exploring>Idea" | \
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
                             issueDependenciesSummary { blockedBy } }
              ... on PullRequest { number title url repository { nameWithOwner } labels(first: 20) { nodes { name } } }
            } } } } } }" -F field="$FIELD" --paginate |
    jq -s --arg kind "$KIND" --arg repo "$REPO" \
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

# Reads a JSON array of items on stdin; adds .priority and sorts highest first, unset last.
by_priority() {
  local list ranks values
  list=$(cat)
  if [ "$(jq length <<<"$list")" -eq 0 ]; then echo '[]'; return; fi
  ranks=$(gh api graphql -F owner="$OWNER" -f query='query($owner: String!) {
      organization(login: $owner) { issueFields(first: 50) { nodes {
        ... on IssueFieldSingleSelect { name options { name } } } } } }' |
    jq --arg f "$PRIORITY" '[.data.organization.issueFields.nodes[] | select(.name == $f) | .options[].name]')
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

pr_for() {
  gh api graphql -F owner="${REPO%/*}" -F name="${REPO#*/}" -F n="$1" -f query='
    query($owner: String!, $name: String!, $n: Int!) {
      repository(owner: $owner, name: $name) {
        issue(number: $n) {
          closedByPullRequestsReferences(first: 10, includeClosedPrs: false) {
            nodes { number url isDraft headRefName }
          }
        }
      }
    }' --jq '.data.repository.issue.closedByPullRequestsReferences.nodes | first // "null"'
}

ci() {
  local sha
  sha=$(gh api "repos/$REPO/pulls/$1" --jq .head.sha) || die "could not read PR #$1"
  gh api --paginate "repos/$REPO/commits/$sha/check-runs?per_page=100" --jq '.check_runs[]' |
    jq -s '
      def failed: .conclusion as $c
        | ["failure", "timed_out", "cancelled", "action_required", "startup_failure"] | index($c);
      {
        verdict: (if any(.[]; failed) then "fail"
                  elif length == 0 or any(.[]; .status != "completed") then "pending"
                  else "pass" end),
        failing: map(select(failed) | {name, link: .html_url}),
        pending: map(select(.status != "completed") | .name)
      }'
}

# Conversation and line comments across the whole repo from the last day, as {n, author, at, body}.
recent_comments() {
  local since
  since=$(jq -rn 'now - 86400 | strftime("%Y-%m-%dT%H:%M:%SZ")')
  {
    gh api --paginate "repos/$REPO/issues/comments?since=$since&per_page=100" \
      --jq '.[] | {n: (.issue_url | split("/") | last | tonumber), author: .user.login, at: .created_at, body: (.body // "")}'
    gh api --paginate "repos/$REPO/pulls/comments?since=$since&per_page=100" \
      --jq '.[] | {n: (.pull_request_url | split("/") | last | tonumber), author: .user.login, at: .created_at, body: (.body // "")}'
  } | jq -s .
}

pr_reviews() {
  gh api --paginate "repos/$REPO/pulls/$1/reviews" \
    --jq '.[] | select(.body != "" or .state == "CHANGES_REQUESTED")
          | {n: '"$1"', author: .user.login, at: .submitted_at, body: (.body // "")}' | jq -s .
}

# awaiting <comments> <role> <n>: the time of the reviewer's newest comment on #n since the role
# last commented, or nothing. It goes into the trigger so new feedback never looks like a retry.
awaiting() {
  jq -r --argjson n "$3" --arg marker "<!-- a-team:$2 -->" --arg reviewer "$REVIEWER" '
    map(select(.n == $n)) | (map(select(.body | contains($marker)) | .at) | max // "") as $since
    | map(select(.author == $reviewer and (.body | contains("<!-- a-team:") | not) and .at > $since) | .at)
    | max // empty' <<<"$1"
}

comments() {
  local n=$1 issue
  issue=$(gh api "repos/$REPO/issues/$n")
  {
    jq '{kind: "body", author: .user.login, at: .created_at, body: (.body // ""), url: .html_url}' <<<"$issue"
    gh api --paginate "repos/$REPO/issues/$n/comments" |
      jq '.[] | {kind: "comment", author: .user.login, at: .created_at, body: (.body // ""), url: .html_url}'
    if jq -e '.pull_request' <<<"$issue" >/dev/null; then
      gh api --paginate "repos/$REPO/pulls/$n/reviews" |
        jq '.[] | select(.body != "" or .state == "CHANGES_REQUESTED")
            | {kind: "review", state, author: .user.login, at: .submitted_at, body: (.body // ""), url: .html_url}'
      gh api --paginate "repos/$REPO/pulls/$n/comments" |
        jq '.[] | {kind: "line", author: .user.login, at: .created_at, body: (.body // ""), url: .html_url, path, line}'
    fi
  } | jq -s 'sort_by(.at)'
}

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
    items | jq '
      def counts: group_by(.status) | map({key: .[0].status, value: length}) | from_entries;
      {pitches: map(select(.labels | index("pitch"))) | counts,
       dev: map(select(.labels | index("a-team:dev"))) | counts,
       reviewer: map(select((.labels | index("pitch") or index("a-team:dev")) | not)) | counts}'
    ;;

  next)
    items | jq 'map(select(.status == "Ready" and .type == "Issue"
                           and (.labels | index("pitch") | not)
                           and (.labels | index("blocked") | not) and .blockedBy == 0))' | by_priority | jq first
    ;;

  lead-next)
    state="$STATE/$TEAM/lead-turn"
    all=$(items)
    count() { jq "[.[] | select($1)] | length" <<<"$all"; }
    pitched=$(count '(.labels | index("pitch")) and .status == "Pitched"')
    exploring=$(count '(.labels | index("pitch")) and .status == "Exploring"')
    found=$(count '(.labels | index("a-team:idea")) and .status == "Idea"')
    ready=$(count '.status == "Ready" and .type == "Issue" and (.labels | index("pitch") | not) and (.labels | index("blocked") | not)')
    room=$(($(cfg '.wip.pitched') - pitched))
    promote=$(jq '[.[] | select((.labels | index("pitch")) and .status == "Exploring")]' <<<"$all" | by_priority |
      jq --argjson room "$((room > 0 ? room : 0))" '.[:$room]')
    exploring=$((exploring - $(jq length <<<"$promote")))
    idea=null
    while IFS= read -r candidate; do
      if [ "$(jq -r .priority <<<"$candidate")" = null ] &&
        { jq -e '.labels | index("a-team:idea")' <<<"$candidate" >/dev/null ||
          agent_written "$(jq -r .number <<<"$candidate")"; }; then
        continue
      fi
      idea=$candidate
      break
    done < <(jq 'map(select(.status == "Idea" and .type == "Issue"))' <<<"$all" | by_priority | jq -c '.[]')
    can_pitch=false can_discover=false
    [ "$exploring" -lt "$(cfg '.wip.exploring')" ] && [ "$idea" != null ] && can_pitch=true
    [ "$found" -lt "$(cfg '.wip.ideas')" ] && can_discover=true
    last=$(cat "$state" 2>/dev/null || echo discover)
    if $can_pitch && { [ "$last" = discover ] || ! $can_discover; }; then
      turn=pitch
    elif $can_discover; then
      turn=discover
    else
      turn=none
    fi
    if [ "$turn" != none ]; then mkdir -p "$(dirname "$state")" && echo "$turn" >"$state"; fi
    jq -n --argjson promote "$promote" --arg turn "$turn" --argjson item "$idea" \
      --argjson room "$(($(cfg '.wip.ideas') - found))" --argjson ready "$ready" \
      --argjson floor "$(cfg '.wip.readyFloor // 0')" '
      {promote: $promote, turn: $turn}
      + (if $turn == "pitch" then {item: $item}
         elif $turn == "discover" then {room: $room}
         else {reason: "Exploring and the discovery queue are both full, or there is no Idea to pitch"} end)
      + {ready: $ready, readyLow: ($ready < $floor)}'
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
    case "$role:$from" in
      "dev:Ready" | "lead:Idea")
        write "label #$n $label" gh issue edit "$n" -R "$REPO" --add-label "$label" >/dev/null ;;
      *)
        jq -e --arg l "$label" '.labels | index($l)' <<<"$it" >/dev/null ||
          die "#$n isn't $role's (no '$label' label); leave it to the reviewer" ;;
    esac
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

  comment)
    [ $# -eq 3 ] || die "usage: board.sh $TEAM comment <role> <n> <file>"
    role=$1 n=$2 file=$3
    check_role "$role"
    [ -f "$file" ] || die "no such file: $file"
    body=$(cat "$file"; printf '\n\n<!-- a-team:%s -->' "$role")
    [ -z "$DRY_RUN" ] || printf '%s\n' "$body" | sed 's/^/  | /' >&2
    printf '%s\n' "$body" | write "comment on #$n" gh issue comment "$n" -R "$REPO" --body-file -
    ;;

  feedback)
    [ $# -eq 2 ] || die "usage: board.sh $TEAM feedback <role> <n>"
    role=$1 n=$2
    check_role "$role"
    comments "$n" | jq --arg marker "<!-- a-team:$role -->" --arg reviewer "$REVIEWER" '
      (map(select(.body | contains($marker)) | .at) | max // "") as $since
      | map(select(.kind != "body" and .author == $reviewer
                   and (.body | contains("<!-- a-team:") | not) and .at > $since))'
    ;;

  link)
    [ $# -eq 2 ] || die "usage: board.sh $TEAM link <parent> <child>"
    child_id=$(gh api "repos/$REPO/issues/$2" --jq .id)
    write "make #$2 a sub-issue of #$1" gh api -X POST "repos/$REPO/issues/$1/sub_issues" -F "sub_issue_id=$child_id" >/dev/null
    say "#$2 is now a sub-issue of #$1"
    ;;

  depends)
    [ $# -eq 2 ] || die "usage: board.sh $TEAM depends <task> <prerequisite>"
    write "block #$1 on #$2" gh api -X POST "repos/$REPO/issues/$1/dependencies/blocked_by" \
      -F "issue_id=$(gh api "repos/$REPO/issues/$2" --jq .id)" >/dev/null
    say "#$1 is now blocked by #$2"
    ;;

  children)
    [ $# -eq 1 ] || die "usage: board.sh $TEAM children <n>"
    gh api --paginate "repos/$REPO/issues/$1/sub_issues" |
      jq -s 'add // [] | map({number, title, state, labels: [.labels[].name]})'
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
    [ $# -eq 1 ] || die "usage: board.sh $TEAM triggers <role>"
    role=$1
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
          verdict=$(ci "$p" | jq -r .verdict)
          [ "$verdict" = fail ] && reasons+=("CI failed on PR #$p at $(gh api "repos/$REPO/pulls/$p" --jq '.head.sha[:7]')")
          [ "$verdict" = pass ] && [ "$(jq -r .isDraft <<<"$pr")" = true ] &&
            reasons+=("PR #$p is green but still a draft")
        elif [ "$status" = "In progress" ]; then
          reasons+=("#$n is In progress but has no PR: an earlier run didn't finish")
        fi
        for x in "${numbers[@]}"; do
          at=$(awaiting "$recent" dev "$x")
          [ -n "$at" ] && reasons+=("reviewer feedback on #$x ($at)")
        done
      done < <(jq -c '.[] | select((.labels | index("a-team:dev")) and (.status == "In progress" or .status == "In review"))' <<<"$all")

      checkout=$(cfg .checkout)
      checkout=${checkout/#\~/$HOME}
      if [ -d "$checkout" ]; then
        while IFS= read -r branch; do
          merged=$(gh api "repos/$REPO/pulls?head=${REPO%/*}:$branch&state=closed&per_page=5" \
            --jq '[.[] | select(.merged_at != null and ((.body // "") | contains("<!-- a-team:dev -->")))][0].number // empty')
          [ -n "$merged" ] && reasons+=("PR #$merged has merged: clean up its worktree")
        done < <(git -C "$checkout" worktree list --porcelain | sed -n 's|^branch refs/heads/||p')
      fi

      used=$(jq '[.[] | select((.labels | index("a-team:dev")) and (.status == "In progress" or .status == "In review"))] | length' <<<"$all")
      if [ "$used" -lt "$(cfg .wip.worktrees)" ]; then
        ready=$(jq -r '[.[] | select(.status == "Ready" and .type == "Issue" and (.labels | index("pitch") | not)
                                     and (.labels | index("blocked") | not) and .blockedBy == 0)][0].number // empty' <<<"$all")
        [ -n "$ready" ] && reasons+=("Ready task available (e.g. #$ready) and a free worktree")
      fi
      creative=false
    else
      while IFS= read -r row; do
        n=$(jq -r .number <<<"$row")
        status=$(jq -r .status <<<"$row")
        at=$(awaiting "$recent" lead "$n")
        [ -n "$at" ] && reasons+=("reviewer feedback on #$n ($at)")
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
      [ "$pitched" -lt "$(cfg .wip.pitched)" ] && [ "$exploring" -gt 0 ] &&
        reasons+=("room in Pitched for a drafted pitch")
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
    ;;

  setup)
    dry_run=false
    { [ "${1:-}" = --dry-run ] || [ -n "$DRY_RUN" ]; } && dry_run=true
    field=$(project_meta | jq '.field // empty')
    jq -e '.id' <<<"$field" >/dev/null 2>&1 || die "no single-select field '$FIELD' on $OWNER project $NUMBER"
    input=$(jq -n --argjson field "$field" --slurpfile cfg "$CONFIG" '
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
      | ($field.options | map(select(.name as $n | $wanted | map(.name) | index($n) | not))) as $extra
      | {fieldId: $field.id, singleSelectOptions: ($wanted + $extra)}')
    if $dry_run; then
      jq -r '.singleSelectOptions[] | "  \(if .id then "keep" else "add " end)  \(.name)"' <<<"$input"
    else
      jq -n --argjson input "$input" '{variables: {input: $input}, query:
        "mutation($input: UpdateProjectV2FieldInput!) { updateProjectV2Field(input: $input) { clientMutationId } }"}' |
        gh api graphql --input - >/dev/null
      echo "Status options set on $OWNER project $NUMBER"
    fi
    existing=$(gh label list -R "$REPO" --limit 500 --json name --jq '.[].name')
    while IFS='|' read -r name color description; do
      if grep -qxF "$name" <<<"$existing"; then continue; fi
      if $dry_run; then
        echo "  add label $name"
      else
        gh label create "$name" -R "$REPO" --color "$color" --description "$description" >/dev/null
        echo "created label $name"
      fi
    done <<<"pitch|5319e7|An a-team pitch: Lead shapes it, reviewer approves it
a-team:dev|0e8a16|Claimed by the a-team Dev
a-team:idea|c5def5|Found by the a-team Lead; give it a Priority to have it pitched
blocked|fbca04|Waiting on another issue"
    ;;

  *)
    die "unknown command '$CMD' (see process.md)"
    ;;
esac
