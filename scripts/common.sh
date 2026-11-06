# shellcheck shell=bash
# Sourced by the other scripts: where state and team configs live, and which teams exist.

# shellcheck disable=SC2034 # used by the scripts that source this
STATE="${A_TEAM_STATE:-${XDG_STATE_HOME:-$HOME/.local/state}/a-team}"
CONFIG_DIR="${A_TEAM_CONFIG:-${XDG_CONFIG_HOME:-$HOME/.config}/a-team}"

LABEL=com.a-team.dispatch
# shellcheck disable=SC2034 # used by the scripts that source this
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"

# dispatcher_record <bin> <dry-run> <interval> <installed-at>: dispatcher.json, what's driving the teams.
dispatcher_record() {
  jq -n --arg bin "$1" --arg version "$("$1" version 2>/dev/null || echo unknown)" --argjson dryRun "$2" \
    --argjson interval "$3" --arg log "$STATE/launchd.log" --argjson installedAt "$4" \
    '{$bin, $version, $dryRun, $interval, $log, $installedAt}'
}

# CRITERIA: reading a task's acceptance criteria and the team's record of them, as jq.
# criteria_items: an issue body's checklist. section: the lines under a PR body's heading.
# criteria($pr): the checklist with how the team checked each, and its picture. boxed: the body with those lines ticked.
CRITERIA='
def criteria_items:
  gsub("\r"; "") | split("\n") | to_entries
  | reduce .[] as $e ({on: false, done: false, items: []};
      if .done then .
      elif ($e.value | test("^#+\\s+[Aa]cceptance [Cc]riteria\\s*$")) then .on = true
      elif (.on | not) then .
      elif ($e.value | test("^#+\\s")) then .done = true
      elif ($e.value | test("^\\s*$")) then .
      elif ($e.value | test("^\\s*[-*+]\\s")) then
        ($e.value | capture("^(?<lead>\\s*)[-*+]\\s+(\\[(?<box>[ xX])\\]\\s*)?(?<text>.*)$")) as $m
        | .items += [{line: $e.key, depth: (if $m.lead == "" then 0 else 1 end),
                      ticked: (($m.box // " ") != " "), text: $m.text}]
      elif (.items | length) > 0 then .items[-1].text += " " + ($e.value | sub("^\\s+"; ""))
      else . end)
  | .items;
def unticked: criteria_items | map(select(.ticked | not)) | length;
def section($heading):
  (gsub("\r"; "") | split("\n")) as $lines | ($lines | map(sub("\\s+$"; "")) | index($heading)) as $at
  | if $at == null then [] else $lines[($at + 1):]
    | (map(test("^#{1,2} |^a-team try |^<!-- a-team:")) | index(true)) as $end
    | if $end then .[:$end] else . end end;
def norm: gsub("[\\[\\]]"; "") | gsub("\\s+"; " ") | sub("^ "; "") | sub(" $"; "");
def criteria($pr):
  ([$pr | section("## Checked by the team")[] | capture("^- `(?<how>screen|test)` (?<text>.*)$")
    | {key: (.text | norm), value: .how}] | from_entries) as $how
  | ([$pr | section("## What it looks like")[] | capture("^!\\[(?<alt>[^\\]]*)\\]\\((?<url>\\S+?)\\)\\s*$")
      | {key: (.alt | norm), value: .}] | from_entries) as $shot
  | criteria_items | to_entries
  | map(.value + {index: (.key + 1)} | (.text | norm) as $said | . + {how: $how[$said]}
        | . + {picture: (if .how == "screen" then $shot[$said] else null end)});
def boxed($lines; $mark):
  split("\n") | to_entries
  | map(.key as $k | if ($lines | index($k)) != null
        then .value | sub("^(?<lead>\\s*[-*+]\\s+)(\\[[ xX]\\]\\s?)?"; "\(.lead)[\($mark)] ")
        else .value end)
  | join("\n");
'

# criteria_json <issue body> <PR body>: the issue's checklist, with what the PR records about each criterion.
criteria_json() {
  jq -Rs --arg pr "$2" "$CRITERIA"'criteria($pr)' <<<"$1"
}

team_config() { echo "$CONFIG_DIR/teams/$1.json"; }

# team_roles <config>: the roles a team runs, the Customer lead and the Reviewer only where they're turned on.
team_roles() {
  printf '%s\n' lead dev
  jq -e '.roles.customer == true' "$1" >/dev/null 2>&1 && echo customer
  jq -e '.roles.reviewer == true' "$1" >/dev/null 2>&1 && echo reviewer
  return 0
}

team_names() {
  local config
  for config in "$CONFIG_DIR"/teams/*.json; do
    [ -f "$config" ] && basename "$config" .json
  done
  return 0
}

# iso <epoch seconds>: that instant as ISO-8601 UTC. BSD date spells it -r, GNU date -d @.
iso() { date -u -r "$1" +%FT%TZ 2>/dev/null || date -u -d "@$1" +%FT%TZ; }

# alive <pid> <file>: whether <pid> is running and started before <file> was last written, give or take a minute. A
# process that started later has only been given the same pid since.
alive() {
  local elapsed days=0 seconds=0 part written
  local -a parts
  [[ $1 =~ ^[0-9]+$ ]] || return 1
  elapsed=$(ps -o etime= -p "$1" 2>/dev/null) || return 1
  elapsed=${elapsed//[[:space:]]/}
  [ -n "$elapsed" ] || return 1
  [[ $elapsed == *-* ]] && days=${elapsed%%-*} elapsed=${elapsed#*-}
  IFS=: read -ra parts <<<"$elapsed"
  for part in "${parts[@]}"; do seconds=$((seconds * 60 + 10#$part)); done
  written=$(stat -c %Y "$2" 2>/dev/null || stat -f %m "$2" 2>/dev/null) || return 1
  [ $(($(date +%s) - 10#$days * 86400 - seconds)) -le $((written + 60)) ]
}

# live_pid <file>: the pid in <file>, while that process is still the one it was written for.
live_pid() { local pid; pid=$(cat "$1" 2>/dev/null) && alive "$pid" "$1" && echo "$pid"; }

# stuck_runs <dir>: what <dir>/stuck says of each run, while that run is still the process its pid file was written for.
stuck_runs() {
  local pid file says
  while IFS=$'\t' read -r pid file says; do
    [ "$(live_pid "$1/$file")" = "$pid" ] && printf '%s\n' "$says"
  done < <(jq -r '.[] | [.pid, .file, .says] | @tsv' "$1/stuck" 2>/dev/null)
  return 0
}

sql() { printf "'%s'" "${1//\'/\'\'}"; }

# history_sql [sqlite3 options...] <statement>: the record, created on first use.
history_sql() {
  sqlite3 -cmd '.timeout 5000' "${@:1:$#-1}" "${HISTORY:-$STATE/history.db}" "CREATE TABLE IF NOT EXISTS events (
    id INTEGER PRIMARY KEY, team TEXT NOT NULL, item INTEGER NOT NULL, at TEXT NOT NULL,
    who TEXT NOT NULL, what TEXT NOT NULL);
  CREATE TABLE IF NOT EXISTS runs (id INTEGER PRIMARY KEY, team TEXT NOT NULL, role TEXT NOT NULL,
    pid INTEGER NOT NULL, log TEXT NOT NULL, started TEXT NOT NULL, ended TEXT, cost REAL, outcome TEXT);
  CREATE TABLE IF NOT EXISTS run_items (run INTEGER NOT NULL, item INTEGER NOT NULL);
  CREATE TABLE IF NOT EXISTS caught_up (team TEXT PRIMARY KEY, started TEXT NOT NULL, at TEXT NOT NULL);
  CREATE TABLE IF NOT EXISTS seen (team TEXT NOT NULL, item INTEGER NOT NULL, node TEXT NOT NULL,
    status TEXT NOT NULL, PRIMARY KEY (team, item));
  CREATE TABLE IF NOT EXISTS github (team TEXT NOT NULL, id TEXT NOT NULL, event INTEGER,
    PRIMARY KEY (team, id));
  CREATE TABLE IF NOT EXISTS queue (team TEXT NOT NULL, at TEXT NOT NULL, waiting INTEGER NOT NULL);
  CREATE TABLE IF NOT EXISTS spent (team TEXT NOT NULL, item INTEGER, at TEXT NOT NULL, seconds INTEGER NOT NULL,
    gate TEXT NOT NULL, what TEXT NOT NULL);
  CREATE TABLE IF NOT EXISTS board (team TEXT NOT NULL, item INTEGER NOT NULL, node TEXT NOT NULL, status TEXT NOT NULL,
    priority TEXT, since TEXT NOT NULL, content TEXT NOT NULL DEFAULT '{}', mirror TEXT, mirrored_status TEXT,
    mirrored_priority TEXT, marks TEXT, mirrored_rank TEXT, mirrored_theme TEXT, PRIMARY KEY (team, item));
  CREATE TABLE IF NOT EXISTS boards (team TEXT PRIMARY KEY, project TEXT NOT NULL, read TEXT, failed TEXT, retry TEXT);
  CREATE TABLE IF NOT EXISTS details (team TEXT PRIMARY KEY, detail TEXT, synced TEXT, failed TEXT);
  CREATE TABLE IF NOT EXISTS formats (team TEXT PRIMARY KEY, format INTEGER NOT NULL, version TEXT NOT NULL, moved TEXT);
  CREATE TABLE IF NOT EXISTS format_moves (team TEXT PRIMARY KEY, pid INTEGER NOT NULL);
  ${*: -1}"
}

# run_outcome <team> <role> <pid> <outcome>: why that run, still going, will have ended.
run_outcome() {
  [ -f "$STATE/history.db" ] || return 0
  history_sql "UPDATE runs SET outcome = $(sql "$4") WHERE team = $(sql "$1") AND role = $(sql "$2")
    AND pid = $3 AND ended IS NULL;" >/dev/null || true
}

# budget_today <team> <config> <now>: what the team's runs that ended since local midnight cost, its daily budget
# (0 for none, or for one that isn't a whole number of dollars), and whether that's reached, as JSON.
budget_today() {
  local cost=0
  [ ! -f "$STATE/history.db" ] || cost=$(history_sql "SELECT COALESCE(SUM(cost), 0) FROM runs WHERE team = $(sql "$1")
    AND ended >= strftime('%Y-%m-%dT%H:%M:%SZ', $3, 'unixepoch', 'localtime', 'start of day', 'utc');") || return
  jq -c --argjson cost "$cost" '((.dispatch.budget | numbers | select(. == floor)) // 0) as $budget | ($cost * 100 | round / 100) as $cost
    | {$cost, $budget, reached: ($budget > 0 and $cost >= $budget)}' "$2"
}
