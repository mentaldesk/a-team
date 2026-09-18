#!/usr/bin/env bash
#
# board.sh — the only way a-team agents read or change a team's project board.
# Commands are documented in process.md.
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
STATES=("Idea" "Exploring" "Pitched" "Approved" "Building" "Ready" "In progress" "In review" "Done")

die() { echo "board.sh: $*" >&2; exit 1; }

[ $# -ge 2 ] || die "usage: board.sh <team> <command> [args...] (see process.md)"
TEAM=$1 CMD=$2
shift 2
CONFIG="$ROOT/teams/$TEAM/team.json"
[ -f "$CONFIG" ] || die "no team config at $CONFIG"

cfg() { jq -r "$1 // empty" "$CONFIG"; }
REPO=$(cfg .repo)
OWNER=$(cfg .project.owner)
NUMBER=$(cfg .project.number)
FIELD=$(cfg .project.statusField)
FIELD=${FIELD:-Status}
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
    "lead:None>Idea" | "lead:None>Exploring" | "lead:None>Ready" | \
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

items() {
  gh project item-list "$NUMBER" --owner "$OWNER" --limit 1000 --format json |
    jq --arg field "$(printf %s "$FIELD" | tr '[:upper:]' '[:lower:]')" --arg repo "$REPO" \
      --argjson states "$(printf '%s\n' "${STATES[@]}" | jq -R . | jq -s .)" \
      --slurpfile cfg "$CONFIG" '
      ($cfg[0].project.statusMap // {} | to_entries | map({key: .value, value: .key}) | from_entries) as $rev
      | [.items[]
         | select((.content.repository // .repository) == $repo and .content.number != null)
         | (.[$field] // null) as $raw
         | {
             id,
             number: .content.number,
             type: .content.type,
             title: .content.title,
             url: .content.url,
             labels: (.labels // []),
             status: (if $raw == null then "None"
                      elif $rev[$raw] then $rev[$raw]
                      elif ($states | index($raw)) then $raw
                      else "?" + $raw end)
           }]'
}

item() {
  items | jq --argjson n "$1" 'map(select(.number == $n)) | first // empty'
}

field_json() {
  gh project field-list "$NUMBER" --owner "$OWNER" --format json |
    jq --arg f "$FIELD" '.fields[] | select(.name == $f)'
}

set_status() {
  local item_id=$1 state=$2 option project_id field option_id
  option=$(jq -r --arg s "$state" '.project.statusMap[$s] // $s' "$CONFIG")
  project_id=$(gh project view "$NUMBER" --owner "$OWNER" --format json | jq -r .id)
  field=$(field_json)
  [ -n "$field" ] || die "no field named '$FIELD' on project $OWNER/$NUMBER"
  option_id=$(jq -r --arg o "$option" '.options[] | select(.name == $o) | .id' <<<"$field")
  [ -n "$option_id" ] || die "field '$FIELD' has no option '$option' (run: board.sh $TEAM check)"
  gh project item-edit --id "$item_id" --project-id "$project_id" \
    --field-id "$(jq -r .id <<<"$field")" --single-select-option-id "$option_id" >/dev/null
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
                           and (.labels | index("blocked") | not))) | first'
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
        gh issue edit "$n" -R "$REPO" --add-label "$label" >/dev/null ;;
      *)
        jq -e --arg l "$label" '.labels | index($l)' <<<"$it" >/dev/null ||
          die "#$n isn't $role's (no '$label' label); leave it to the reviewer" ;;
    esac
    set_status "$(jq -r .id <<<"$it")" "$to"
    echo "#$n: $from -> $to"
    ;;

  add)
    [ $# -eq 3 ] || die "usage: board.sh $TEAM add <role> <n> <status>"
    role=$1 n=$2 to=$3
    check_role "$role"
    is_state "$to" || die "unknown status '$to'"
    [ -z "$(item "$n")" ] || die "#$n is already on the board (use move)"
    allowed "$role" None "$to" || die "$role may not add items as '$to'"
    id=$(gh project item-add "$NUMBER" --owner "$OWNER" \
      --url "https://github.com/$REPO/issues/$n" --format json | jq -r .id)
    set_status "$id" "$to"
    echo "#$n: added as $to"
    ;;

  comment)
    [ $# -eq 3 ] || die "usage: board.sh $TEAM comment <role> <n> <file>"
    role=$1 n=$2 file=$3
    check_role "$role"
    [ -f "$file" ] || die "no such file: $file"
    { cat "$file"; printf '\n\n<!-- a-team:%s -->\n' "$role"; } |
      gh issue comment "$n" -R "$REPO" --body-file -
    ;;

  feedback)
    [ $# -eq 2 ] || die "usage: board.sh $TEAM feedback <role> <n>"
    role=$1 n=$2
    check_role "$role"
    comments "$n" | jq --arg marker "<!-- a-team:$role -->" --arg reviewer "$REVIEWER" '
      (map(select(.body | contains($marker)) | .at) | max // "") as $since
      | map(select(.author == $reviewer and (.body | contains("<!-- a-team:") | not) and .at > $since))'
    ;;

  link)
    [ $# -eq 2 ] || die "usage: board.sh $TEAM link <parent> <child>"
    child_id=$(gh api "repos/$REPO/issues/$2" --jq .id)
    gh api -X POST "repos/$REPO/issues/$1/sub_issues" -F "sub_issue_id=$child_id" >/dev/null
    echo "#$2 is now a sub-issue of #$1"
    ;;

  children)
    [ $# -eq 1 ] || die "usage: board.sh $TEAM children <n>"
    gh api --paginate "repos/$REPO/issues/$1/sub_issues" |
      jq -s 'add // [] | map({number, title, state, labels: [.labels[].name]})'
    ;;

  pr)
    [ $# -eq 1 ] || die "usage: board.sh $TEAM pr <n>"
    gh api graphql -F owner="${REPO%/*}" -F name="${REPO#*/}" -F n="$1" -f query='
      query($owner: String!, $name: String!, $n: Int!) {
        repository(owner: $owner, name: $name) {
          issue(number: $n) {
            closedByPullRequestsReferences(first: 10, includeClosedPrs: false) {
              nodes { number url isDraft headRefName }
            }
          }
        }
      }' --jq '.data.repository.issue.closedByPullRequestsReferences.nodes | first'
    ;;

  checks)
    [ $# -eq 1 ] || die "usage: board.sh $TEAM checks <pr>"
    out=$(gh pr checks "$1" -R "$REPO" --json name,bucket,link 2>/dev/null) || true
    jq -e 'type == "array"' <<<"$out" >/dev/null 2>&1 || die "could not read checks for PR #$1"
    jq '{
          verdict: (if any(.[]; .bucket == "fail" or .bucket == "cancel") then "fail"
                    elif length == 0 or any(.[]; .bucket == "pending") then "pending"
                    else "pass" end),
          failing: map(select(.bucket == "fail" or .bucket == "cancel") | {name, link}),
          pending: map(select(.bucket == "pending") | .name)
        }' <<<"$out"
    ;;

  check)
    field=$(field_json)
    [ -n "$field" ] || die "no field named '$FIELD' on project $OWNER/$NUMBER"
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
    [ "${1:-}" = --dry-run ] && dry_run=true
    lookup='query($owner: String!, $number: Int!, $field: String!) {
      %s(login: $owner) { projectV2(number: $number) { field(name: $field) {
        ... on ProjectV2SingleSelectField { id options { id name color description } } } } } }'
    field=""
    for kind in organization user; do
      # shellcheck disable=SC2059
      field=$(gh api graphql -F owner="$OWNER" -F number="$NUMBER" -F field="$FIELD" \
        -f query="$(printf "$lookup" "$kind")" --jq ".data.$kind.projectV2.field" 2>/dev/null) && break
    done
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
blocked|fbca04|Waiting on another issue"
    ;;

  *)
    die "unknown command '$CMD' (see process.md)"
    ;;
esac
