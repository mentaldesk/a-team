#!/usr/bin/env bash
#
# board.sh — the only way a-team agents read or change a team's project board.
# Commands are documented in process.md.
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
STATES=("Idea" "Exploring" "Pitched" "Approved" "Building" "Ready" "In progress" "In review" "Done")

die() { echo "board.sh: $*" >&2; exit 1; }

[ -n "${A_TEAM_CORE:-}" ] || die "run this as a-team board, which starts the board's core"

DRY_RUN=${A_TEAM_DRY_RUN:-}
if [ "${1:-}" = --dry-run ]; then
  DRY_RUN=1
  shift
fi

say() { echo "${DRY_RUN:+(dry run) }$*"; }

[ $# -ge 2 ] || die "usage: board.sh [--dry-run] <team> <command> [args...] (see process.md)"
TEAM=$1 CMD=$2
shift 2
source "$ROOT/scripts/common.sh"
CONFIG=$(team_config "$TEAM")
[ -f "$CONFIG" ] || die "no config for team '$TEAM' at $CONFIG (start from examples/team.json)"
if ! settings=$(jq -er 'if type == "object" then . else error end
  | [.repo, .project.owner, .project.number, .project.statusField, .priorityField,
     (.stakeholders // [.reviewer // empty] | tojson), .app.slug, .project.ownerType, (.mirror != false | tostring), .app.id]
  | map(. // "" | tostring) | join("\u001f")' "$CONFIG" 2>/dev/null); then
  unreadable=$(jq -e 'type == "object"' "$CONFIG" 2>&1 >/dev/null) || true
  unreadable=${unreadable#jq: }
  unreadable="can't read $TEAM.json: ${unreadable#parse error: }"
  [ "$unreadable" != "can't read $TEAM.json: " ] || unreadable="$TEAM.json isn't a JSON object"
  [ "$CMD" = check ] && { printf '%-9s%-10s%s\n' problem config "$unreadable"; exit 1; }
  die "$unreadable"
fi

cfg() { jq -r "$1 // empty" "$CONFIG"; }
IFS=$'\x1f' read -r REPO OWNER NUMBER FIELD PRIORITY _ BOT KIND MIRRORED _ <<<"$settings"
FIELD=${FIELD:-Status}
PRIORITY=${PRIORITY:-Priority}

# The labels setup creates, as "<name>|<colour>|<description>|<what goes wrong without it>".
LABELS="pitch|5319e7|An a-team pitch: Lead shapes it, reviewer approves it|the team can't tell its pitches from tasks"

BOT=${BOT:+${BOT}[bot]}

customer_on() { jq -e '.roles.customer == true' "$CONFIG" >/dev/null 2>&1; }

# `gh api "$@"`, passing on what it prints: 1 only on GitHub's 404, 2 where GitHub didn't answer.
file_there() {
  local trouble
  { trouble=$(gh api "$@" 2>&1 >&3); } 3>&1 && return 0
  [[ $trouble == *"(HTTP 404)"* ]] && return 1
  return 2
}

# Whether the first page of the user docs, the config's `docs`, is on the default branch, as file_there says.
docs_there() { [ -n "$(cfg .docs)" ] && file_there "repos/$REPO/contents/$(cfg .docs)" --silent >/dev/null; }

KIND=${KIND:-organization}

# MIRROR: the GitHub Project a-team copies the board to, as "<owner>/<number>"; empty for none.
MIRROR=
[ -z "$NUMBER" ] || [ "$MIRRORED" = false ] || MIRROR="$OWNER/$NUMBER"

# A dry run works on a copy of the record, so it reads as a real run would and changes nothing. With no record yet, as
# in a try sandbox, it keeps its own between commands, so the board is moved over once and not on every command.
SCRATCH=
if [ -n "$DRY_RUN" ] && [ -f "$STATE/history.db" ]; then
  SCRATCH=$(mktemp "${TMPDIR:-/tmp}/a-team-history.XXXXXX")
  cp "$STATE/history.db" "$SCRATCH"
  HISTORY=$SCRATCH
elif [ -n "$DRY_RUN" ]; then
  mkdir -p "$STATE"
  HISTORY="$STATE/dry-run.db"
fi
trap 'rm -f ${SCRATCH:+"$SCRATCH"}' EXIT

gql() {
  local query=$1
  shift
  gh api graphql -F owner="$OWNER" -F number="$NUMBER" "$@" -f query="$query"
}

# board_pages <query> <args...>: every page of the mirror, read again once if short. Short twice ends the whole
# command while SHORT_ENDS is set, and otherwise fails.
SHORT_ENDS=1
board_pages() {
  local pages short
  for _ in 1 2; do
    pages=$(gql "$@" --paginate) || return 1
    short=$(jq -rs --arg kind "$KIND" '[.[].data[$kind].projectV2.items] | [(map(.nodes | length) | add // 0),
      (map(.totalCount // 0) | max // 0)] | select(.[0] < .[1]) | "\(.[0]) of \(.[1])"' <<<"$pages")
    [ -n "$short" ] || { printf '%s\n' "$pages"; return; }
  done
  [ -n "$SHORT_ENDS" ] || return 1
  echo "board.sh: the board read came back incomplete ($short items), twice. Nothing was changed: end this run, and the next one reads the board again." >&2
  kill -USR1 $$
  exit 1
}
trap 'exit 1' USR1

# What the board keeps of an issue or PR, and where it stands on the mirror.
CONTENT='__typename
  ... on Issue { id number title url state stateReason repository { nameWithOwner } labels(first: 20) { nodes { name } }
                 parent { number }
                 issueDependenciesSummary { blockedBy }
                 issueFieldValues(first: 20) { nodes { ... on IssueFieldSingleSelectValue {
                   name field { ... on IssueFieldSingleSelect { name } } } } } }
  ... on PullRequest { id number title url state repository { nameWithOwner } labels(first: 20) { nodes { name } }
                       author { login } }'
STATUS_VALUE='fieldValueByName(name: $field) { ... on ProjectV2ItemFieldSingleSelectValue { name updatedAt } }'

# mirror_nodes: every item on the mirror, as [{project: {id, status, since}, content}].
mirror_nodes() {
  board_pages "query(\$owner: String!, \$number: Int!, \$field: String!, \$endCursor: String) {
      $KIND(login: \$owner) { projectV2(number: \$number) {
        items(first: 100, after: \$endCursor) {
          totalCount pageInfo { hasNextPage endCursor }
          nodes { id $STATUS_VALUE content { $CONTENT } } } } } }" -F field="$FIELD" |
    jq -s --arg kind "$KIND" '[.[].data[$kind].projectV2.items.nodes[] | select(.content != null)
      | {project: {id, status: .fieldValueByName.name, since: .fieldValueByName.updatedAt}, content}]'
}

# BOARD_FORMAT: the format of the state this a-team keeps. A change that moves or reinterprets stored state bumps it and
# adds a format_<n> step, safe to repeat. A team with no stamp is on format 1: its labels are carried into the record, as adopt_labels does.
BOARD_FORMAT=1

# format_behind: why this a-team can't act on the state, if the state is in a newer format than it knows.
format_behind() {
  local top from here
  [ -f "${HISTORY:-$STATE/history.db}" ] || return 0
  IFS='|' read -r top from < <(history_sql "SELECT format, version FROM formats ORDER BY format DESC LIMIT 1;") || true
  [ "${top:-1}" -gt "$BOARD_FORMAT" ] || return 0
  here=$("$ROOT/bin/a-team" version)
  if [ ! -f "$ROOT/VERSION" ]; then
    echo "the board is in format $top, from a-team $from, and this a-team ($ROOT) is on $here: merge the default branch"
  elif [ -n "${A_TEAM_RUN_TEAM:-}" ]; then
    echo "the board is in format $top, from a-team $from, and this run is on $here: stopping"
  else
    echo "the board is in format $top, from a-team $from, and this is $here: brew upgrade a-team"
  fi
}

# installed_bin: the a-team the dispatcher runs, by its own record; nothing where there's no dispatcher.
installed_bin() { jq -r '.bin // empty' "$STATE/dispatcher.json" 2>/dev/null || true; }

# may_move_state: only the a-team the dispatcher runs, or a run's copy of that release, moves the real state forward.
may_move_state() {
  local bin
  [ -z "$DRY_RUN" ] || return 0
  [ "$STATE" -ef "${XDG_STATE_HOME:-$HOME/.local/state}/a-team" ] || return 0
  bin=$(installed_bin)
  [ -n "$bin" ] || return 0
  [ "$(cd -P "$ROOT" && pwd)" != "$(cd -P "$(dirname "$(readlink -f "$bin")")/.." 2>/dev/null && pwd)" ] || return 0
  [ -f "$ROOT/VERSION" ] && [ "$(cat "$ROOT/VERSION")" = "$("$bin" version 2>/dev/null)" ]
}

# refuse_move <what>: stops this a-team doing <what> to state only the installed one may move.
refuse_move() {
  [ ! -f "$ROOT/VERSION" ] || [ -z "${A_TEAM_RUN_TEAM:-}" ] ||
    die "this run is on $("$ROOT/bin/a-team" version), and only the installed a-team ($("$(installed_bin)" version 2>/dev/null)) may $1: stopping"
  die "this a-team ($ROOT) would $1, and only the installed one ($(installed_bin)) may: try it with --dry-run, or with A_TEAM_STATE set to a copy"
}

# format_move: moves this team's state up to BOARD_FORMAT, one a-team at a time. One that died leaves its steps to repeat.
format_move() {
  local team own step holder waited=0
  team=$(sql "$TEAM")
  until [ "$(history_sql "INSERT OR IGNORE INTO format_moves (team, pid) VALUES ($team, $$); SELECT changes();")" = 1 ]; do
    holder=$(history_sql "SELECT pid FROM format_moves WHERE team = $team;")
    if [ -n "$holder" ] && ! kill -0 "$holder" 2>/dev/null; then
      history_sql "DELETE FROM format_moves WHERE team = $team AND pid = $holder;" >/dev/null
    else
      [ "$((waited++))" -lt 300 ] || die "another a-team (pid $holder) has been moving the board forward for 5 minutes"
      sleep 1
    fi
  done
  own=$(history_sql "SELECT format FROM formats WHERE team = $team;")
  if [ "${own:-1}" -lt "$BOARD_FORMAT" ]; then
    for ((step = ${own:-1} + 1; step <= BOARD_FORMAT; step++)); do
      ! declare -F "format_$step" >/dev/null || "format_$step"
    done
    history_sql "INSERT OR REPLACE INTO formats (team, format, version, moved) VALUES ($team, $BOARD_FORMAT,
      $(sql "$("$ROOT/bin/a-team" version)"), $(sql "$(date -u +%FT%TZ)"));" >/dev/null
  fi
  history_sql "DELETE FROM format_moves WHERE team = $team;" >/dev/null
}

# format_gate: refuses a state this a-team is too old for, stamps one with no stamp, and moves an older one forward.
format_gate() {
  local why own
  why=$(format_behind)
  [ -z "$why" ] || die "$why"
  [ -f "${HISTORY:-$STATE/history.db}" ] || return 0
  own=$(history_sql "SELECT format FROM formats WHERE team = $(sql "$TEAM");")
  if [ "${own:-1}" -lt "$BOARD_FORMAT" ]; then
    may_move_state || refuse_move "move the board from format ${own:-1} to $BOARD_FORMAT"
    format_move
  elif [ -z "$own" ] && may_move_state; then
    history_sql "INSERT OR IGNORE INTO formats (team, format, version) VALUES ($(sql "$TEAM"), $BOARD_FORMAT,
      $(sql "$("$ROOT/bin/a-team" version)"));" >/dev/null
  fi
}

# GitHub fails a whole query that asks for a missing field by name, so this lists them.
project_raw() {
  gql "query(\$owner: String!, \$number: Int!) {
      $KIND(login: \$owner) { projectV2(number: \$number) { id title
        fields(first: 100) { nodes {
          ... on ProjectV2Field { id name dataType }
          ... on ProjectV2IterationField { id name dataType }
          ... on ProjectV2SingleSelectField { id name dataType options { id name color description } } } } } } }"
}

# The mirror's text fields for the Lead's recommended rank and a pitch's theme.
RANK_FIELD="Lead rank" THEME_FIELD=Theme

# project_meta: the project, with its Status field as `field`, its text fields as `rank` and `theme`, each null if
# missing, and as `clash` the names of those two that it has as some other type.
project_meta() {
  local raw
  raw=$(project_raw) || return 1
  jq -ce --arg kind "$KIND" --arg status "$FIELD" --arg rank "$RANK_FIELD" --arg theme "$THEME_FIELD" '
    .data[$kind].projectV2 // empty | .fields.nodes as $fields
    | def named($name): $fields | map(select(.name == $name)) | first;
      def text($name): named($name) | if .dataType == "TEXT" then . else null end;
      {id, title, field: (named($status) | if .options then . else null end), rank: text($rank), theme: text($theme),
       clash: [$rank, $theme | select(. as $name | named($name) != null and text($name) == null)]}' <<<"$raw"
}

# text_field_name <rank|theme>: what that field is called on the mirror.
text_field_name() { [ "$1" = rank ] && echo "$RANK_FIELD" || echo "$THEME_FIELD"; }

# field_clash <name>: what's wrong with a project whose own field has that name.
field_clash() { echo "$OWNER project $NUMBER has a $1 field that isn't text, so a-team can't copy to it: rename that field"; }

# add_text_field <project id> <name>: adds a text field to the project and prints its id.
add_text_field() {
  gh api graphql -F project="$1" -f name="$2" -f query='mutation($project: ID!, $name: String!) {
      createProjectV2Field(input: {projectId: $project, dataType: TEXT, name: $name}) {
        projectV2Field { ... on ProjectV2Field { id } } } }' --jq .data.createProjectV2Field.projectV2Field.id
}

# The organisation's Priority field: its id, and its options, which are the values it takes.
priority_field() {
  gh api graphql -F owner="${OWNER:-${REPO%/*}}" -f query='query($owner: String!) {
      organization(login: $owner) { issueFields(first: 50) { nodes {
        ... on IssueFieldSingleSelect { id name options { id name } } } } } }' |
    jq --arg f "$PRIORITY" '[.data.organization.issueFields.nodes[] | select(.name == $f)] | first // empty'
}

[ "$CMD" = check ] || format_gate

case "$CMD" in
  format)
    [ $# -eq 0 ] || die "usage: board.sh $TEAM format"
    ;;

  check)
    # Exits 1 when the team can't run, and 2 when it can but something it uses is missing.
    fatal=0 minor=0
    problem() { printf '%-9s%-10s%s\n' problem "$1" "$2"; fatal=1; }
    note() { printf '%-9s%-10s%s\n' note "$1" "$2"; minor=1; }
    # `not_there <topic> <file_there's status> <file> <what to say on a 404>`
    not_there() {
      if [ "$2" = 1 ]; then note "$1" "$4"; else note "$1" "GitHub didn't answer for $3: checking again next pass"; fi
    }
    reached='' unreachable=''
    behind=$(format_behind)
    [ -z "$behind" ] || problem board "$behind"
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
    if [ -n "$reached" ] && [ -n "$vision" ]; then
      if drafted=$(file_there -H 'Accept: application/vnd.github.raw' "repos/$REPO/contents/$vision"); then
        ! grep -qF '<!-- a-team:lead -->' <<<"$drafted" || note vision "drafted by the Lead: Write the vision with me replaces it"
      else
        not_there vision $? "$vision" "$vision isn't in $REPO yet: the Lead will draft one and open it as a draft PR"
      fi
    fi
    if customer_on; then
      if [ -z "$(cfg .docs)" ]; then
        note docs "$TEAM.json names no docs page, so the Customer lead has nothing to keep right"
      elif [ -n "$reached" ]; then
        docs_there || not_there docs $? "$(cfg .docs)" "$(cfg .docs) isn't in $REPO yet: the Customer lead will propose the docs as a draft PR"
      fi
    fi
    release=$(cfg .release)
    case "${release:-never}" in
      never) ;;
      daily | continuous)
        [ -z "$reached" ] || file_there "repos/$REPO/actions/workflows/release.yml" --silent >/dev/null ||
          not_there release $? release.yml "release is $release, but $REPO has no release.yml workflow for it to run" ;;
      *) note release "release is '$release': expected never, daily or continuous" ;;
    esac
    read_err=$(mktemp)
    if [ "$(jq -r '.mirror != false' "$CONFIG")" = false ]; then
      echo "mirror: none, so the board is only on this machine"
    elif [ -z "$NUMBER" ]; then
      note project "$TEAM.json names no GitHub Project to copy the board to: pick one, or None, under Mirror in its settings"
    elif ! meta=$(project_meta 2>"$read_err"); then
      reason=$(tail -n 1 "$read_err")
      problem project "can't read $OWNER project $NUMBER: ${reason#gh: }"
      unreachable=1
    elif ! jq -e '.field.id' <<<"$meta" >/dev/null 2>&1; then
      problem project "no single-select field '$FIELD' on $OWNER project $NUMBER"
    else
      while IFS= read -r name; do
        note fields "$(field_clash "$name")"
      done < <(jq -r '.clash[]' <<<"$meta")
      missing=()
      for s in "${STATES[@]}"; do
        option=$(jq -r --arg s "$s" '.project.statusMap[$s] // $s' "$CONFIG")
        jq -e --arg o "$option" '.field.options | map(.name) | index($o)' <<<"$meta" >/dev/null ||
          missing+=("$option")
      done
      if [ ${#missing[@]} -gt 0 ]; then
        problem status "${#missing[@]} of ${#STATES[@]} options missing from '$FIELD': $(printf '%s, ' "${missing[@]}" | sed 's/, $//')"
      else
        unmapped=$(SHORT_ENDS='' mirror_nodes 2>/dev/null | jq -r --argjson states "$(printf '%s\n' "${STATES[@]}" | jq -R . | jq -s .)" \
          --slurpfile cfg "$CONFIG" '($cfg[0].project.statusMap // {} | [.[]]) as $mapped
          | [.[] | select(.project.status != null and (.project.status | IN($states[], $mapped[]) | not))
             | "#\(.content.number) \(.project.status)"] | join(", ")') || unmapped=
        if [ -n "$unmapped" ]; then
          problem items "outside the team's states: $unmapped"
        else
          echo "ok: $OWNER project $NUMBER, field '$FIELD'"
        fi
      fi
    fi
    rm -f "$read_err"
    if [ -n "$MIRROR" ] && [ -z "$unreachable" ] &&
      enabled=$(gql "query(\$owner: String!, \$number: Int!) { $KIND(login: \$owner) { projectV2(number: \$number) {
          workflows(first: 50) { nodes { name enabled } } } } }" \
        --jq ".data.$KIND.projectV2.workflows.nodes[] | select(.enabled) | .name" 2>/dev/null); then
      grep -q '^Auto-add to project' <<<"$enabled" ||
        note workflow "\"Auto-add to project\" is off on $OWNER project $NUMBER, so an issue opened on $REPO, by you or another team, never reaches Triage"
      grep -q '^Item added to project' <<<"$enabled" ||
        note workflow "\"Item added to project\" is off on $OWNER project $NUMBER, so an issue auto-added there gets no Status and never reaches Triage: turn it on, setting Status to Idea"
    fi
    if [ -n "$reached" ]; then
      if ! have=$(gh label list -R "$REPO" --limit 500 --json name --jq '.[].name' 2>&1); then
        note labels "can't list $REPO's labels: ${have#gh: }"
      else
        while IFS='|' read -r name _ _ without; do
          grep -qxF -- "$name" <<<"$have" || note labels "no '$name' label, so $without"
        done <<<"$LABELS"
      fi
    fi
    identity() {
      [ -n "$BOT" ] || { problem app "$TEAM has no GitHub App: run a-team app create $TEAM, then install it"; return; }
      source "$ROOT/scripts/github-app.sh"
      has_app_key "${REPO%/*}" || {
        problem app "$BOT has no key in the login Keychain (service '$KEYCHAIN_SERVICE', account '${REPO%/*}'): $(key_advice "${REPO%/*}")"
        return
      }
      local token line="identity: $BOT"
      token=$("$ROOT/bin/a-team" token "$TEAM" 2>&1) || { problem app "$BOT can't get a token: ${token#a-team token: }"; return; }
      line="$line · token ok"
      # The project's own problem says enough when nobody can read it.
      [ -n "$unreachable" ] || [ -z "$MIRROR" ] || GH_TOKEN=$token gql 'query($owner: String!, $number: Int!) {
          organization(login: $owner) { projectV2(number: $number) { items(first: 1) { totalCount } } } }' \
        --jq '.data.organization.projectV2.items.totalCount' >/dev/null 2>&1 || {
        problem app "$BOT can't read project $NUMBER: grant the organisation's \"Projects: read and write\", and install the App on $REPO"
        return
      }
      granted() { jq -e --arg p "$1" '.permissions[$p] == "write"' "$(token_cache "$TEAM")" >/dev/null 2>&1; }
      [ -n "$unreachable" ] || [ -z "$MIRROR" ] || granted organization_projects ||
        { problem app "$BOT can only read project $NUMBER: grant the organisation's \"Projects: read and write\""; return; }
      [ -n "$unreachable" ] || [ -z "$MIRROR" ] || line="$line · project $NUMBER read+write ok"
      [ -z "$MIRROR" ] || GH_TOKEN=$token priority_field >/dev/null 2>&1 || {
        problem app "$BOT can't read the $PRIORITY field: grant the organisation's \"Issue Fields: read\", or \`triggers\` fails with \"Resource not accessible by integration\""
        return
      }
      [ -z "$MIRROR" ] || line="$line · $PRIORITY readable"
      granted contents || { problem app "$BOT can't push to $REPO: grant the repository's \"Contents: read and write\""; return; }
      echo "$line · push access to $REPO ok"
    }
    identity
    [ "$fatal" = 0 ] || exit 1
    [ "$minor" = 0 ] || exit 2
    ;;

  setup)
    dry_run=false
    { [ "${1:-}" = --dry-run ] || [ -n "$DRY_RUN" ]; } && { dry_run=true; DRY_RUN=1; }
    [ -n "$MIRROR" ] || [ "$(jq -r '.mirror != false' "$CONFIG")" = false ] ||
      die "$TEAM.json names no GitHub Project: pick one, or None, under Mirror in its settings"
    if [ -n "$MIRROR" ]; then
      meta=$(project_meta)
      field=$(jq '.field // empty' <<<"$meta")
      jq -e '.id' <<<"$field" >/dev/null 2>&1 || die "no single-select field '$FIELD' on $OWNER project $NUMBER"
      clash=$(jq -r '.clash[0] // empty' <<<"$meta")
      [ -z "$clash" ] || die "$(field_clash "$clash")"
      pages=$(board_pages "query(\$owner: String!, \$number: Int!, \$field: String!, \$endCursor: String) {
          $KIND(login: \$owner) { projectV2(number: \$number) {
            items(first: 100, after: \$endCursor) { totalCount pageInfo { hasNextPage endCursor }
              nodes { fieldValueByName(name: \$field) { ... on ProjectV2ItemFieldSingleSelectValue { name } } } } } } }" \
        -F field="$FIELD")
      used=$(jq -s --arg kind "$KIND" '[.[].data[$kind].projectV2.items.nodes[].fieldValueByName.name // empty] | unique' <<<"$pages")
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
      for key in rank theme; do
        jq -e --arg k "$key" '.[$k].id' <<<"$meta" >/dev/null && continue
        name=$(text_field_name "$key")
        if $dry_run; then
          echo "  add   field $name"
        else
          add_text_field "$(jq -r .id <<<"$meta")" "$name" >/dev/null
          echo "Field $name added to $OWNER project $NUMBER"
        fi
      done
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
