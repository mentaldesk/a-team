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
IFS=$'\x1f' read -r REPO OWNER NUMBER FIELD PRIORITY STAKEHOLDERS BOT KIND MIRRORED APP_ID <<<"$settings"
FIELD=${FIELD:-Status}
PRIORITY=${PRIORITY:-Priority}

# The labels setup creates, as "<name>|<colour>|<description>|<what goes wrong without it>".
LABELS="pitch|5319e7|An a-team pitch: Lead shapes it, reviewer approves it|the team can't tell its pitches from tasks"

RANKS="urgent high medium low"

# Since APP_FROM the team speaks only as its App, so whose words a comment is comes from its author.
# Before it the team spoke from its stakeholder's account, and the marker alone on the last line said so.
APP_FROM=2026-09-29T00:00:00Z
BOT=${BOT:+${BOT}[bot]}

# act_as_app <role>: from here on gh is the team's App, whatever login this shell has, or the command stops before it reaches GitHub.
act_as_app() {
  local why=
  if [ -z "$APP_ID" ]; then
    why="no GitHub App: run a-team app create $TEAM, then install it"
  elif ! GH_TOKEN=$("$ROOT/bin/a-team" token "$TEAM" 2>&1); then
    why=${GH_TOKEN#a-team token: }
  fi
  [ -z "$why" ] || die "$1 acts as ${BOT:-the App}, which can't get a token: $why"
  export GH_TOKEN
}

case "$CMD" in
  attach | audited | comment | covered | edit | new | pictures | priority | recommend | review | skip | \
    spent | theme | vision-review)
    speaker=${1:-}
    case "$speaker" in
      lead | dev | customer | reviewer) act_as_app "$speaker" ;;
      you) [ -z "${A_TEAM_RUN_TEAM:-}" ] || die "only the stakeholder can $CMD as you, and this is a team's run" ;;
    esac
    ;;
esac

# login: an author as REST spells it. GraphQL leaves a bot's "[bot]" off its login.
LOGIN='if . == null then "" elif .__typename == "Bot" then "\(.login)[bot]" else .login end'

# team($m): the team wrote this, as the role whose marker is $m.
TEAM_SAID='def team($m): if $bot != "" and .author == $bot then (.body // "") | contains($m)
    elif .at >= $appFrom then false
    else (.body // "") | gsub("\r"; "") | split("\n") | map(sub("[ \t]+$"; ""))
         | map(select(. != "")) | last // "" | startswith($m) end;'

# closes: the issues a Dev PR's body closes, for when GitHub hasn't linked them.
CLOSES='def closes: if (.body // "") | contains("<!-- a-team:dev -->")
    then [.body | scan("(?i)\\b(?:close[sd]?|fix(?:e[sd])?|resolve[sd]?):?[ \t]+#([0-9]+)\\b") | .[0] | tonumber]
    else [] end;'

# reviewed: the Reviewer has posted its one review on this PR, read off its comments as GraphQL gives them.
# answered: the Dev has commented since that review.
REVIEWED='def said($role): (.author | '"$LOGIN"') == $bot and ((.body // "") | contains("<!-- a-team:\($role) -->"));
  def reviewed: any(.comments.nodes[]?; said("reviewer"));
  def answered: [.comments.nodes[]? | if said("reviewer") then "review" elif said("dev") then "dev" else empty end]
    | index("review") as $at | $at != null and (.[$at:] | index("dev")) != null;'


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

check_role() {
  case "$1" in lead | dev | customer | reviewer) ;; *) die "unknown role '$1' (lead | dev | customer | reviewer)" ;; esac
}

customer_on() { jq -e '.roles.customer == true' "$CONFIG" >/dev/null 2>&1; }

reviewer_on() { jq -e '.roles.reviewer == true' "$CONFIG" >/dev/null 2>&1; }

# `gh api "$@"`, passing on what it prints: 1 only on GitHub's 404, 2 where GitHub didn't answer.
file_there() {
  local trouble
  { trouble=$(gh api "$@" 2>&1 >&3); } 3>&1 && return 0
  [[ $trouble == *"(HTTP 404)"* ]] && return 1
  return 2
}

# Whether the first page of the user docs, the config's `docs`, is on the default branch, as file_there says.
docs_there() { [ -n "$(cfg .docs)" ] && file_there "repos/$REPO/contents/$(cfg .docs)" --silent >/dev/null; }

# A stakeholder asked for a vision review from Commands, and no review PR has opened since.
VISION_ASKED="$STATE/$TEAM/lead/vision-review"

# The done pitches the Customer lead has already checked the docs against, one number a line.
COVERED="$STATE/$TEAM/customer/covered"
# When the Customer lead last finished auditing the docs as a whole, in seconds since the epoch.
AUDITED="$STATE/$TEAM/customer/audited"

KIND=${KIND:-organization}

# MIRROR: the GitHub Project a-team copies the board to, as "<owner>/<number>"; empty for none.
MIRROR=
[ -z "$NUMBER" ] || [ "$MIRRORED" = false ] || MIRROR="$OWNER/$NUMBER"
PRIORITIES='["Urgent", "High", "Medium", "Low"]'

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
# Present once this command has brought the record up to date with GitHub.
SYNCED=$(mktemp -u "${TMPDIR:-/tmp}/a-team-synced.XXXXXX")
trap 'rm -f "$SYNCED" ${SCRATCH:+"$SCRATCH"}' EXIT

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

# mark <n> <key> <JSON value|null>: sets, or with null clears, one of #n's marks in the record.
mark() {
  local to
  to="json_set(COALESCE(marks, '{}'), '\$.$2', json($(sql "$3")))"
  [ "$3" != null ] || to="json_remove(COALESCE(marks, '{}'), '\$.$2')"
  write "$([ "$3" = null ] && echo "clear #$1's $2" || echo "mark #$1 $2 $3")" \
    history_sql "UPDATE board SET marks = $to WHERE team = $(sql "$TEAM") AND item = $1;" >/dev/null
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

# board_ready: moves the board over from the mirror on the first run, and copies it all to a mirror newly pointed at.

# core <command> [args...]: the board's state is the core's, on the record this command has open.
core() { A_TEAM_DRY_RUN=$DRY_RUN A_TEAM_HISTORY=${HISTORY:-} "$A_TEAM_CORE" "$TEAM" "$@"; }

# board_sync: once a command, the core brings the record up to date with GitHub, and the mirror with the record.
board_sync() {
  [ ! -e "$SYNCED" ] || return 0
  : >"$SYNCED"
  core __sync || { kill -USR1 $$; exit 1; }
}

# items [open]: the board's items, as the record has them; `open` leaves Done off.
items() {
  board_sync
  core __items ${1:-}
}

mirror_push() { core __mirror-push ${1:-}; }

item() {
  items | jq --argjson n "$1" 'map(select(.number == $n)) | first // empty'
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

# pr_for <n>: the open PR that closes #n, with `review` saying whether the Reviewer is still to review it
# (waiting), has (posted), the Dev has answered that review (answered), or the Reviewer isn't turned on (off).
pr_for() {
  local said='comments(last: 100) { nodes { body author { __typename login } } }'
  gh api graphql -F owner="${REPO%/*}" -F name="${REPO#*/}" -F n="$1" -f query="
    query(\$owner: String!, \$name: String!, \$n: Int!) {
      repository(owner: \$owner, name: \$name) {
        issue(number: \$n) {
          closedByPullRequestsReferences(first: 10, includeClosedPrs: false) {
            nodes { number url isDraft headRefName mergeable mergeStateStatus $said }
          }
        }
        pullRequests(states: OPEN, first: 100) {
          nodes { number url isDraft headRefName mergeable mergeStateStatus body $said }
        }
      }
    }" | jq -c --argjson n "$1" --arg bot "$BOT" --argjson on "$(reviewer_on && echo true || echo false)" \
      "$CLOSES$REVIEWED"'.data.repository
      | (.issue.closedByPullRequestsReferences.nodes[0]
         // ((.pullRequests.nodes // []) | map(select(any(closes[]; . == $n))) | first))
      | if . == null then null
        else . + {review: (if answered then "answered" elif reviewed then "posted" elif $on then "waiting" else "off" end)} | del(.body, .comments) end'
}

# pr_checks <pr> [<sha>]: the CI verdict for <sha>, or <pr>'s head commit.
pr_checks() {
  local sha=${2:-}
  [ -n "$sha" ] || sha=$(gh api "repos/$REPO/pulls/$1" --jq .head.sha) || die "could not read PR #$1"
  gh api --paginate "repos/$REPO/commits/$sha/check-runs?per_page=100" --jq '.check_runs[]' |
    jq -s "$CHECKS checks"
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

# Images attached from the app live on one orphan branch, named by content under a month folder.
ATTACHMENTS=a-team/attachments
IMAGE_TYPES='png|jpe?g|gif|webp'

attachment_link() { printf 'https://github.com/%s/blob/%s/%s?raw=true' "$REPO" "$ATTACHMENTS" "$1"; }

# image_path <file>: where <file> goes on the attachments branch, named by its content, once it's an image of a
# size GitHub takes.
image_path() {
  local file=$1 ext size hash
  [ -f "$file" ] || die "no such file: $file"
  ext=$(tr '[:upper:]' '[:lower:]' <<<"${file##*.}")
  [ "$ext" != jpeg ] || ext=jpg
  grep -qxE "$IMAGE_TYPES" <<<"$ext" || die "$(basename "$file") isn't a .png, .jpg, .gif or .webp"
  size=$(wc -c <"$file" | tr -d ' ')
  [ "$size" -le $((10 * 1024 * 1024)) ] || die "$(basename "$file") is over 10 MB"
  hash=$({ shasum -a 256 || sha256sum; } <"$file" 2>/dev/null | cut -c1-16)
  echo "$(date -u +%Y-%m)/$hash.$ext"
}

# keep_image <file> <path>: stores <file> as <path> on the attachments branch, unless it's there already.
keep_image() {
  gh api "repos/$REPO/contents/$2?ref=$ATTACHMENTS" --silent >/dev/null 2>&1 && return 0
  write "commit $(basename "$1") to $2 on $ATTACHMENTS" store_attachment "$1" "$2" ||
    die "can't store $(basename "$1") on $REPO's $ATTACHMENTS branch"
}

# with_section: the PR body on stdin with the section $SECTION heads swapped for $SECTION, just above the
# `a-team try` line and the marker.
with_section() {
  awk '
    function emit(line) {
      if (seam) { if (out) print "" } else while (blanks-- > 0) print ""
      blanks = 0; seam = 0; out = 1; print line
    }
    BEGIN { heading = ENVIRON["SECTION"]; sub(/\n.*/, "", heading) }
    { line = $0; sub(/[[:space:]]+$/, "", line) }
    line == heading { skipping = 1; seam = 1; next }
    skipping && (/^#{1,2} / || /^a-team try / || /^<!-- a-team:/) { skipping = 0 }
    skipping { next }
    /^$/ { blanks++; next }
    !placed && (/^a-team try / || /^<!-- a-team:dev -->/) { seam = 1; emit(ENVIRON["SECTION"]); seam = 1; placed = 1 }
    { emit($0) }
    END { if (!placed) { seam = 1; emit(ENVIRON["SECTION"]) } }'
}

# task_pr <pr>: PR <pr>'s state, draft, body, labels and comments, and whether it's the docs PR, or nothing when it
# isn't a PR.
task_pr() {
  gh api graphql -F owner="${REPO%/*}" -F name="${REPO#*/}" -F n="$1" -f query='
    query($owner: String!, $name: String!, $n: Int!) { repository(owner: $owner, name: $name) {
      pullRequest(number: $n) { state isDraft body labels(first: 20) { nodes { name } }
        comments(last: 100) { nodes { body author { __typename login } } } } } }' 2>/dev/null |
    jq -c --argjson marks "$(item "$1" | jq -sc '.[0].marks // {}')" \
      '.data.repository.pullRequest // empty | . + {customer: ($marks.customer // false)}' || true
}

DEV_TASK_PR='.state == "OPEN" and ((.body // "") | contains("<!-- a-team:dev -->"))
  and ([.labels.nodes[].name] | index("pitch") | not) and (.customer | not)'

# git_post <path> <jq filter> [jq args...]: POST to the repo's git database, printing the new object's sha.
git_post() {
  local path=$1 filter=$2 input
  shift 2
  input=$(mktemp)
  jq -n "$@" "$filter" >"$input" || { rm -f "$input"; return 1; }
  gh api -X POST "repos/$REPO/git/$path" --input "$input" --jq .sha
  local status=$?
  rm -f "$input"
  return $status
}

# store_attachment <file> <path>: commits <file> as <path> on the attachments branch, starting the
# branch the first time. A ref that moved on under it is read again and tried once more.
store_attachment() {
  local file=$1 path=$2 encoded blob head base tree commit attempt
  encoded=$(mktemp)
  base64 <"$file" | tr -d '\n' >"$encoded"
  blob=$(git_post blobs '{content: $content, encoding: "base64"}' --rawfile content "$encoded")
  rm -f "$encoded"
  [ -n "$blob" ] || return 1
  for attempt in 1 2; do
    head=$(gh api "repos/$REPO/git/ref/heads/$ATTACHMENTS" --jq .object.sha 2>/dev/null) || head=
    base=
    [ -z "$head" ] || base=$(gh api "repos/$REPO/git/commits/$head" --jq .tree.sha) || return 1
    tree=$(git_post trees '{tree: [{path: $path, mode: "100644", type: "blob", sha: $blob}]}
      + if $base == "" then {} else {base_tree: $base} end' \
      --arg path "$path" --arg blob "$blob" --arg base "$base") || return 1
    commit=$(git_post commits '{message: "Attach \($path)", tree: $tree, parents: (if $head == "" then [] else [$head] end)}' \
      --arg path "$path" --arg tree "$tree" --arg head "$head") || return 1
    if [ -z "$head" ]; then
      gh api -X POST "repos/$REPO/git/refs" -f ref="refs/heads/$ATTACHMENTS" -f sha="$commit" --silent && return 0
    else
      gh api -X PATCH "repos/$REPO/git/refs/heads/$ATTACHMENTS" -f sha="$commit" -F force=false --silent && return 0
    fi
    [ "$attempt" = 1 ] || return 1
  done
}

# react <id> <what>: a 👀 on comment <id>.
react() {
  write "add 👀 to $2" gh api graphql -F subject="$1" -f query='
    mutation($subject: ID!) {
      addReaction(input: {subjectId: $subject, content: EYES}) { reaction { content } } }' >/dev/null
}

# ack <n>: a 👀 on every stakeholder comment on #n this run could have seen. One that arrived mid-run
# is newer than A_TEAM_RUN_STARTED, so it stays unanswered and gets a run of its own. Unset means
# now, which is right for a person running `comment` by hand, since they have just read the thread.
ack() {
  local id at started=${A_TEAM_RUN_STARTED:-$(iso "$(date +%s)")}
  while IFS=$'\t' read -r id at; do
    react "$id" "your comment of $at on #$1"
  done < <(comments "$1" | jq -r --argjson stakeholders "$STAKEHOLDERS" --arg started "$started" \
    --arg appFrom "$APP_FROM" --arg bot "$BOT" "$TEAM_SAID"'
    map(select(.at < $started))
    | map(select(.kind != "body" and (.author | IN($stakeholders[]))
                 and (team("<!-- a-team:") | not) and (.eyes // 0) == 0))
    | .[] | [.id, .at] | @tsv')
}

# own_task <role> <n>: a Dev or Reviewer run started for one task touches only that task and its PR.
own_task() {
  local task=${A_TEAM_RUN_TASK:-}
  case "$1" in dev | reviewer) ;; *) return 0 ;; esac
  [ -n "$task" ] && [ "$2" != "$task" ] || return 0
  [ "$(pr_for "$task" | jq -r '.number // empty')" = "$2" ] ||
    die "this run is for #$task: leave #$2 to a run of its own"
}

# own_docs <role> <n>: the Customer lead touches only its own docs PR.
own_docs() {
  [ "$1" = customer ] || return 0
  item "$2" | jq -e '.marks.customer' >/dev/null ||
    die "customer only touches its own docs PR (#$2 isn't it)"
}

# clean_worktrees <role> <clone> <repo>: removes each of the role's worktrees of <clone> that's finished, and says why
# it left the others. The first listed is the clone itself, and one with no branch is nobody's PR.
clean_worktrees() {
  local tree head branch itself
  itself=$(cd "$2" && pwd -P)
  while IFS=$'\t' read -r tree head branch; do
    [ ! -d "$tree" ] || [ "$tree" = "$itself" ] || clean_worktree "$1" "$2" "$3" "$tree" "$head" "$branch"
  done < <(git -C "$2" worktree list --porcelain | awk '
    /^worktree / { n++; tree = substr($0, 10) }
    /^HEAD / { head = $2 }
    /^branch refs\/heads\// { if (n > 1) print tree "\t" head "\t" substr($0, 19) }')
}

# clean_worktree <role> <clone> <repo> <tree> <head> <branch>: a branch name can be reused, so only its newest PR
# speaks for it, and only a worktree at that PR's last commit with no edits is what the PR left behind.
clean_worktree() {
  local role=$1 clone=$2 repo=$3 tree=$4 head=$5 branch=$6 where trouble prs pr number last changed
  where=${tree/#$HOME/\~}
  left() { CLEANED=$((CLEANED + 1)); say "Left $where: $*"; }
  [ "$role" != customer ] || [ "$branch" = docs/customer-lead ] || [ "$branch" = docs/customer-lead-proposal ] || return 0
  trouble=$(mktemp)
  if ! prs=$(gh pr list -R "$repo" --head "$branch" --state all \
    --json number,state,headRefOid,body,author,isCrossRepository 2>"$trouble"); then
    left "couldn't read the PRs on $branch ($(head -1 "$trouble"))."
    rm -f "$trouble"
    return 0
  fi
  rm -f "$trouble"
  pr=$(jq -c 'map(select(.isCrossRepository | not)) | max_by(.number) // empty' <<<"$prs")
  if [ -z "$pr" ]; then
    [ "$role" != customer ] || left "there's no PR on $branch."
    return 0
  fi
  jq -e --arg marker "<!-- a-team:$role -->" --arg role "$role" \
    '((.body // "") | contains($marker)) or ($role == "dev" and .author.login == "app/dependabot")' <<<"$pr" >/dev/null || return 0
  number=$(jq -r .number <<<"$pr")
  last=$(jq -r .headRefOid <<<"$pr")
  case "$(jq -r .state <<<"$pr")" in
    MERGED) ;;
    OPEN) left "#$number is open."; return 0 ;;
    *) left "#$number was closed without merging."; return 0 ;;
  esac
  if [ "$head" != "$last" ]; then
    left "its HEAD ${head:0:7} isn't #$number's last commit (${last:0:7})."
    return 0
  fi
  if ! changed=$(git -C "$tree" status --porcelain --untracked-files=no); then
    left "git couldn't read its changes."
    return 0
  fi
  if [ -n "$changed" ]; then
    left "it has $(grep -c '' <<<"$changed") changed tracked file(s)."
    return 0
  fi
  case "$(pwd -P 2>/dev/null)/" in
    "$tree"/*) left "this shell is in it."; return 0 ;;
  esac
  if ! write "remove the worktree $where" git -C "$clone" worktree remove --force "$tree"; then
    left "git wouldn't remove it."
    return 0
  fi
  CLEANED=$((CLEANED + 1))
  say "Removed $where: #$number has merged."
  write "delete the branch $branch" git -C "$clone" branch -qD "$branch" || say "Couldn't delete its branch $branch."
}

[ "$CMD" = check ] || format_gate

case "$CMD" in
  format)
    [ $# -eq 0 ] || die "usage: board.sh $TEAM format"
    ;;

  new)
    [ $# -eq 2 ] || die "usage: board.sh $TEAM new you <file>"
    role=$1 file=$2
    case "$role" in
      lead | dev | customer | reviewer) die "$role may not open an issue as you; open it yourself and add it" ;;
      you) ;;
      *) die "unknown role '$role' (you)" ;;
    esac
    [ -f "$file" ] || die "no such file: $file"
    title=$(head -n 1 "$file")
    [ -n "${title//[[:space:]]/}" ] || die "an issue needs a title (the file's first line)"
    body=$(tail -n +2 "$file")
    [ -z "$DRY_RUN" ] || printf '%s\n%s\n' "$title" "$body" | sed 's/^/  | /' >&2
    url=$(write <<<"$body" "open an issue titled '$title'" gh issue create -R "$REPO" --title "$title" --body-file -) ||
      die "can't open the issue on $REPO"
    [ -z "$DRY_RUN" ] || url=0
    echo "${url##*/}"
    ;;

  attach)
    [ $# -eq 2 ] || die "usage: board.sh $TEAM attach you <image>"
    role=$1 file=$2
    case "$role" in
      lead | dev | customer | reviewer) die "$role may not attach an image; only a stakeholder does, from the app" ;;
      you) ;;
      *) die "unknown role '$role' (you)" ;;
    esac
    path=$(image_path "$file") || exit 1
    keep_image "$file" "$path"
    attachment_link "$path"
    echo
    ;;

  pictures)
    [ $# -ge 4 ] && [ $(($# % 2)) -eq 0 ] || die "usage: board.sh $TEAM pictures dev <pr> <image> <caption> [<image> <caption>...]"
    role=$1 pr=$2
    case "$role" in
      dev) ;;
      lead | customer | reviewer | you) die "$role may not put pictures on a PR; only dev does, on its own task PR" ;;
      *) die "unknown role '$role' (dev)" ;;
    esac
    shift 2
    shots=() images=() captions=()
    while [ $# -gt 0 ]; do
      grep -q '[^[:space:]]' <<<"$2" || die "$(basename "$1") needs a caption: the acceptance criterion it shows"
      [ "$(wc -l <<<"$2")" -eq 1 ] || die "a caption is one line: '$2'"
      path=$(image_path "$1") || exit 1
      shots+=("$1") images+=("$path") captions+=("$2")
      shift 2
    done
    own_task "$role" "$pr"
    it=$(task_pr "$pr")
    [ -n "$it" ] || die "#$pr isn't a PR"
    jq -e "$DEV_TASK_PR" <<<"$it" >/dev/null || die "dev puts pictures only on its own open task PRs (#$pr isn't one)"
    section="## What it looks like"
    for i in "${!images[@]}"; do
      keep_image "${shots[$i]}" "${images[$i]}"
      section+=$'\n\n'"${captions[$i]}"$'\n\n'"![${captions[$i]//[\[\]]/}]($(attachment_link "${images[$i]}"))"
    done
    body=$(jq -r '.body // ""' <<<"$it" | SECTION=$section with_section)
    [ -z "$DRY_RUN" ] || printf '%s\n' "$body" | sed 's/^/  | /' >&2
    write <<<"$body" "put ${#images[@]} picture(s) on #$pr" gh pr edit "$pr" -R "$REPO" --body-file - >/dev/null
    record "$role" "${A_TEAM_RUN_TASK:-$pr}" "put ${#images[@]} picture(s) on PR #$pr"
    say "#$pr: ${#images[@]} picture(s) under What it looks like"
    ;;

  checked)
    [ $# -ge 3 ] || die "usage: board.sh $TEAM checked dev <pr> <criterion>=screen|test ..."
    role=$1 pr=$2
    case "$role" in
      dev) ;;
      lead | customer | reviewer | you) die "$role may not say what the team checked; only dev does, on its own task PR" ;;
      *) die "unknown role '$role' (dev)" ;;
    esac
    shift 2
    own_task "$role" "$pr"
    it=$(task_pr "$pr")
    [ -n "$it" ] || die "#$pr isn't a PR"
    jq -e "$DEV_TASK_PR" <<<"$it" >/dev/null || die "dev says what it checked only on its own open task PRs (#$pr isn't one)"
    n=$(jq -r "$CLOSES"'closes[0] // empty' <<<"$it")
    [ -n "$n" ] || die "#$pr doesn't close an issue"
    issue=$(gh api "repos/$REPO/issues/$n" --jq '.body // ""') || die "can't read #$n"
    said=$(jq -r '.body // ""' <<<"$it")
    list=$(criteria_json "$issue" "$said")
    pictured=$(jq -Rs "$CRITERIA"'[section("## What it looks like")[] | capture("^!\\[(?<alt>[^\\]]*)\\]") | .alt | norm]' <<<"$said")
    section="## Checked by the team"$'\n' lines='[]' screens=0 tests=0
    for each in "$@"; do
      [[ "$each" =~ ^([1-9][0-9]*)=(screen|test)$ ]] || die "'$each' isn't <criterion>=screen or <criterion>=test"
      index=${BASH_REMATCH[1]} how=${BASH_REMATCH[2]}
      one=$(jq -c --argjson i "$index" '.[$i - 1] // empty' <<<"$list")
      [ -n "$one" ] || die "#$n has no criterion $index"
      if [ "$how" = screen ]; then
        jq -e --arg said "$(jq -r .text <<<"$one")" --argjson pictured "$pictured" "$CRITERIA"'$pictured | index($said | norm)' <<<null >/dev/null ||
          die "criterion $index is screen, but no picture on #$pr is captioned with it: $(jq -r .text <<<"$one")"
        screens=$((screens + 1))
      else
        tests=$((tests + 1))
      fi
      section+=$'\n'"- \`$how\` $(jq -r .text <<<"$one")"
      lines=$(jq -c --argjson line "$(jq .line <<<"$one")" '. + [$line]' <<<"$lines")
    done
    dropped=$(jq -c --argjson lines "$lines" 'map(select(.how != null and (.line as $l | $lines | index($l) | not)) | .line)' <<<"$list")
    ticked=$(jq -Rsr --argjson lines "$lines" --argjson dropped "$dropped" "$CRITERIA"'boxed($lines; "x") | boxed($dropped; " ")' <<<"$issue")
    body=$(SECTION=$section with_section <<<"$said")
    [ -z "$DRY_RUN" ] || printf '%s\n' "$body" | sed 's/^/  | /' >&2
    write "tick what the team checked on #$n" gh issue edit "$n" -R "$REPO" --body-file - <<<"$ticked" >/dev/null
    write "say what the team checked on #$pr" gh pr edit "$pr" -R "$REPO" --body-file - <<<"$body" >/dev/null
    record "$role" "${A_TEAM_RUN_TASK:-$n}" "checked $((screens + tests)) of $(jq length <<<"$list"): $screens on screen, $tests by test"
    say "#$n: $((screens + tests)) of $(jq length <<<"$list") checked by the team"
    ;;

  tick)
    [ $# -eq 5 ] || die "usage: board.sh $TEAM tick you <n> <criterion> <on|off> <its text>"
    role=$1 n=$2 index=$3 state=$4 text=$5
    case "$role" in
      you) ;;
      lead | dev | customer | reviewer) die "$role may not tick a criterion; it's the stakeholders' own check" ;;
      *) die "unknown role '$role' (you)" ;;
    esac
    case "$state" in
      on) mark=x ;;
      off) mark=' ' ;;
      *) die "'$state' isn't on or off" ;;
    esac
    [[ "$index" =~ ^[1-9][0-9]*$ ]] || die "'$index' isn't a criterion's number"
    it=$(item "$n")
    [ -n "$it" ] || die "#$n is not on the board"
    jq -e '.status == "In review" and ((.labels | index("pitch")) | not)' <<<"$it" >/dev/null ||
      die "only a task In review has criteria to tick (#$n is '$(jq -r .status <<<"$it")')"
    issue=$(gh api "repos/$REPO/issues/$n" --jq '.body // ""') || die "can't read #$n"
    line=$(jq -Rsr --argjson i "$index" --arg text "$text" "$CRITERIA"'criteria_items | .[$i - 1] // empty | select(.text == $text) | .line' <<<"$issue")
    [ -n "$line" ] || die "#$n's criteria have changed: open it again"
    ticked=$(jq -Rsr --argjson line "$line" --arg mark "$mark" "$CRITERIA"'boxed([$line]; $mark)' <<<"$issue")
    write "$([ "$state" = on ] && echo tick || echo untick) criterion $index on #$n" \
      gh issue edit "$n" -R "$REPO" --body-file - <<<"$ticked" >/dev/null
    count=$(jq -Rsr "$CRITERIA"'criteria_items | "\(map(select(.ticked)) | length) of \(length)"' <<<"$ticked")
    if [ "$state" = on ]; then
      record "$role" "$n" "ticked $count"
    else
      record "$role" "$n" "unticked, $count ticked"
    fi
    say "#$n: $count ticked"
    ;;

  priority)
    [ $# -eq 3 ] || die "usage: board.sh $TEAM priority <role> <n> <value|none>"
    role=$1 n=$2 value=$3
    case "$role" in
      lead | dev | customer | reviewer) die "$role may not set a $PRIORITY; ranking an item is the stakeholders' own gate" ;;
      you) ;;
      *) die "unknown role '$role' (you)" ;;
    esac
    [ -n "$(item "$n")" ] || die "#$n is not on the board"
    if [ "$value" != none ]; then
      jq -e --arg v "$value" 'index($v)' <<<"$PRIORITIES" >/dev/null ||
        die "unknown $PRIORITY '$value' ($(jq -r '. + ["none"] | join(" | ")' <<<"$PRIORITIES"))"
    fi
    write "set $PRIORITY on #$n to '$value'" history_sql "UPDATE board SET priority = $([ "$value" = none ] && echo NULL || sql "$value")
      WHERE team = $(sql "$TEAM") AND item = $n;" >/dev/null
    mirror_push "$n"
    if [ "$value" = none ]; then
      record "$role" "$n" "$PRIORITY cleared"
      say "#$n: $PRIORITY cleared"
    else
      record "$role" "$n" "$PRIORITY set to $value"
      say "#$n: $PRIORITY set to '$value'"
    fi
    ;;

  comment)
    [ $# -eq 3 ] || die "usage: board.sh $TEAM comment <role> <n> <file>"
    role=$1 n=$2 file=$3
    case "$role" in
      lead | dev | customer | you) ;;
      reviewer) die "reviewer posts only its review, with: board.sh $TEAM review reviewer <pr> <file>" ;;
      *) die "unknown role '$role' (lead | dev | customer | you)" ;;
    esac
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
    write <<<"$body" "comment on #$n" gh issue comment "$n" -R "$REPO" --body-file -
    [ "$role" = you ] || ack "$n"
    record "$role" "$n" "commented"
    ;;

  review)
    [ $# -eq 3 ] || die "usage: board.sh $TEAM review reviewer <pr> <file>"
    role=$1 pr=$2 file=$3
    check_role "$role"
    [ "$role" = reviewer ] || die "only reviewer reviews a task's PR"
    reviewer_on || die "$TEAM has no Reviewer (roles.reviewer in $TEAM.json)"
    [ -f "$file" ] || die "no such file: $file"
    [ -n "$(grep -v '^[[:space:]]*$' "$file" || true)" ] || die "the review is empty: $file"
    own_task "$role" "$pr"
    it=$(task_pr "$pr")
    [ -n "$it" ] || die "#$pr isn't a PR"
    jq -e "$DEV_TASK_PR" <<<"$it" >/dev/null ||
      die "reviewer reviews only the Dev's open task PRs (#$pr isn't one)"
    ! jq -e --arg bot "$BOT" "$REVIEWED reviewed" <<<"$it" >/dev/null ||
      die "#$pr has its review already: a PR is reviewed once"
    jq -e .isDraft <<<"$it" >/dev/null || die "#$pr is ready for review already: it went to the stakeholders unreviewed"
    [ "$(pr_checks "$pr" | jq -r .verdict)" = pass ] || die "#$pr isn't green: it's reviewed once CI passes"
    body=$(cat "$file"; printf '\n\n<!-- a-team:%s -->' "$role")
    [ -z "$DRY_RUN" ] || printf '%s\n' "$body" | sed 's/^/  | /' >&2
    write <<<"$body" "comment on #$pr" gh pr comment "$pr" -R "$REPO" --body-file -
    record "$role" "${A_TEAM_RUN_TASK:-$pr}" "reviewed PR #$pr"
    say "#$pr: reviewed"
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
    write <<<"$body" "comment on #$n" gh issue comment "$n" -R "$REPO" --body-file -
    ack "$n"
    mark "$n" skipped true
    record "$role" "$n" "skipped"
    say "#$n: skipped; a stakeholder's comment there puts it back"
    ;;

  recommend)
    [ $# -eq 4 ] || die "usage: board.sh $TEAM recommend lead <n> <urgent|high|medium|low> <file>"
    role=$1 n=$2 level=$(tr '[:upper:]' '[:lower:]' <<<"$3") file=$4
    case "$role" in
      lead) ;;
      dev | customer | reviewer | you) die "$role may not recommend a rank; recommending is the Lead's, and ranking is the stakeholders'" ;;
      *) die "unknown role '$role' (lead)" ;;
    esac
    [[ " $RANKS " == *" $level "* ]] || die "unknown rank '$3' (urgent | high | medium | low)"
    [ -f "$file" ] || die "no such file: $file"
    case_line=$(grep -v '^[[:space:]]*$' "$file" || true)
    [ -n "$case_line" ] && [ "$(grep -c '' <<<"$case_line")" -eq 1 ] || die "the case for #$n is one line: $file has $(grep -c '' <<<"$case_line")"
    it=$(item "$n")
    [ -n "$it" ] || die "#$n is not on the board, so it is not an Idea to recommend"
    [ "$(jq -r .type <<<"$it")" = Issue ] && [ "$(jq -r .status <<<"$it")" = Idea ] ||
      die "recommend is only for Ideas (#$n is in '$(jq -r .status <<<"$it")')"
    name=$(tr '[:lower:]' '[:upper:]' <<<"${level:0:1}")${level:1}
    mark "$n" rank "$(jq -cn --arg r "$name" '$r')"
    mirror_push "$n"
    body=$(printf 'Recommended: **%s** · %s\n\n<!-- a-team:%s -->' "$name" "$case_line" "$role")
    [ -z "$DRY_RUN" ] || printf '%s\n' "$body" | sed 's/^/  | /' >&2
    write <<<"$body" "comment on #$n" gh issue comment "$n" -R "$REPO" --body-file -
    record "$role" "$n" "recommended $name"
    say "#$n: recommended $name"
    ;;

  theme)
    [ $# -eq 3 ] || die "usage: board.sh $TEAM theme lead <n> <heading|none>"
    role=$1 n=$2 heading=$3
    case "$role" in
      lead) ;;
      dev | customer | reviewer | you) die "$role may not set a pitch's theme; that's the Lead's" ;;
      *) die "unknown role '$role' (lead)" ;;
    esac
    [[ -n $heading && $heading != *,* && $heading != *$'\n'* ]] || die "a theme is its heading from the vision, on one line and with no commas"
    it=$(item "$n")
    [ -n "$it" ] || die "#$n is not on the board"
    jq -e '.type == "Issue" and (.labels | index("pitch"))' <<<"$it" >/dev/null || die "#$n isn't a pitch: only pitches carry a theme"
    if [ "$heading" = none ]; then
      mark "$n" theme null
      record "$role" "$n" "no theme"
      say "#$n: no theme"
    else
      mark "$n" theme "$(jq -cn --arg h "$heading" '$h')"
      record "$role" "$n" "theme: $heading"
      say "#$n: theme: $heading"
    fi
    mirror_push "$n"
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

  audited)
    [ $# -eq 1 ] || die "usage: board.sh $TEAM audited <role>"
    [ "$1" = customer ] || die "only customer audits the docs"
    customer_on || die "$TEAM has no Customer lead (roles.customer in $TEAM.json)"
    if [ -z "$DRY_RUN" ]; then
      mkdir -p "$(dirname "$AUDITED")"
      date +%s >"$AUDITED"
    fi
    say "docs audited: the next audit is in a week"
    ;;

  clean)
    [ $# -eq 1 ] || die "usage: board.sh $TEAM clean <role>"
    role=$1
    case "$role" in dev | customer) ;; *) die "only dev and customer clean up worktrees" ;; esac
    checkout=$(cfg .checkout)
    checkout=${checkout/#\~/$HOME}
    [ -d "$checkout" ] || die "no checkout for $TEAM at ${checkout:-<unset>}"
    CLEANED=0
    clean_worktrees "$role" "$checkout" "$REPO"
    if [ "$role" = dev ]; then
      workdir=$(cfg .workdir)
      workdir=${workdir/#\~/$HOME}
      while IFS= read -r upstream; do
        [ ! -d "$workdir/upstream/${upstream#*/}" ] || clean_worktrees dev "$workdir/upstream/${upstream#*/}" "$upstream"
      done < <(jq -r '.contributesTo // [] | .[]' "$CONFIG")
    fi
    [ "$CLEANED" -gt 0 ] || say "No worktrees of yours to clean up."
    ;;

  document)
    [ $# -eq 1 ] || die "usage: board.sh $TEAM document <n>"
    trouble=$(mktemp)
    ref=$(gh api "repos/$REPO/pulls/$1" --jq .head.ref 2>"$trouble") &&
      files=$(gh api --paginate "repos/$REPO/pulls/$1/files" --jq '.[].filename' 2>"$trouble") ||
      { reason=$(head -1 "$trouble"); rm -f "$trouble"; die "can't read PR #$1 ($reason)"; }
    if [ "$(grep -c . <<<"$files")" -ne 1 ]; then
      rm -f "$trouble"
      jq -n --argjson n "$1" '{number: $n, files: $ARGS.positional}' --args $files
      exit 0
    fi
    file=$(gh api "repos/$REPO/contents/$files?ref=$ref" 2>"$trouble") ||
      { reason=$(head -1 "$trouble"); rm -f "$trouble"; die "can't read $files on $ref ($reason)"; }
    rm -f "$trouble"
    jq --argjson n "$1" --arg path "$files" --arg ref "$ref" \
      '{number: $n, files: [$path], path: $path, ref: $ref, sha, text: (.content | gsub("\\s"; "") | @base64d)}' <<<"$file"
    ;;

  edit)
    [ $# -eq 5 ] || die "usage: board.sh $TEAM edit you <n> <path> <sha> <file>"
    role=$1 n=$2 path=$3 sha=$4 file=$5
    [ "$role" = you ] || die "$role may not edit a document pitch; only the stakeholders edit one from the app"
    [ -f "$file" ] || die "no file $file"
    it=$(item "$n")
    [ -n "$it" ] || die "#$n is not on the board"
    [ "$(jq -r '.type == "PullRequest" and .status == "Pitched"
        and ((.labels | index("pitch")) or .marks.customer)' <<<"$it")" = true ] ||
      die "#$n isn't a document pitch in Pitched"
    ref=$(gh api "repos/$REPO/pulls/$n" --jq .head.ref)
    put() {
      jq -n --arg message "Edit $path" --arg content "$(base64 <"$file" | tr -d '\n')" --arg sha "$sha" --arg branch "$ref" \
        '{message: $message, content: $content, sha: $sha, branch: $branch}' 2>/dev/null |
        { gh api -X PUT "repos/$REPO/contents/$path" --input - >/dev/null; } 2>&1
    }
    if ! refused=$(write "commit $file to $path on $ref" put); then
      grep -qE 'HTTP 409|does not match' <<<"$refused" &&
        die "$path changed on GitHub while you were editing it"
      die "can't commit to $path on $ref ($(head -1 <<<"$refused"))"
    fi
    record "$role" "$n" "edited $path"
    say "#$n: committed your edit to $path"
    ;;

  vision-review)
    [ $# -eq 1 ] || die "usage: board.sh $TEAM vision-review you"
    [ "$1" = you ] || die "$1 may not ask for a vision review; the team's trigger or a stakeholder does"
    [ -n "$(cfg .vision)" ] || die "$TEAM.json names no vision"
    [ -n "$DRY_RUN" ] || { mkdir -p "$(dirname "$VISION_ASKED")" && touch "$VISION_ASKED"; }
    say "vision review asked for: the Lead starts one on the next pass"
    ;;

  spent)
    [ $# -eq 6 ] || die "usage: board.sh $TEAM spent you <n|-> <at> <seconds> <gate> <what>"
    role=$1 n=$2 at=$3 seconds=$4 gate=$5 what=$6
    [ "$role" = you ] || die "only the stakeholders' own time is recorded, not $role's"
    [[ $n =~ ^[0-9]+$ || $n == - ]] || die "#$n isn't an item number"
    [[ $at =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$ ]] || die "'$at' isn't a UTC time"
    [[ $seconds =~ ^[0-9]+$ ]] || die "'$seconds' isn't a number of seconds"
    case $gate in Triage | Pitches | Blocked | Review | Other) ;; *) die "unknown gate '$gate'" ;; esac
    if [ -n "$DRY_RUN" ]; then
      echo "dry-run: would record ${seconds}s of yours on ${n/#-/other}" >&2
      exit 0
    fi
    mkdir -p "$STATE"
    history_sql "INSERT INTO spent (team, item, at, seconds, gate, what) VALUES ($(sql "$TEAM"),
      $([ "$n" = - ] && echo NULL || echo "$n"), $(sql "$at"), $seconds, $(sql "$gate"), $(sql "$what"));" >/dev/null
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
