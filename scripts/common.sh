# shellcheck shell=bash
# Sourced by the other scripts: where state and team configs live, and which teams exist.

# shellcheck disable=SC2034 # used by the scripts that source this
STATE="${A_TEAM_STATE:-${XDG_STATE_HOME:-$HOME/.local/state}/a-team}"
CONFIG_DIR="${A_TEAM_CONFIG:-${XDG_CONFIG_HOME:-$HOME/.config}/a-team}"

team_config() { echo "$CONFIG_DIR/teams/$1.json"; }

team_names() {
  local config
  for config in "$CONFIG_DIR"/teams/*.json; do
    [ -f "$config" ] && basename "$config" .json
  done
  return 0
}

# iso <epoch seconds>: that instant as ISO-8601 UTC. BSD date spells it -r, GNU date -d @.
iso() { date -u -r "$1" +%FT%TZ 2>/dev/null || date -u -d "@$1" +%FT%TZ; }

sql() { printf "'%s'" "${1//\'/\'\'}"; }

# history_sql [sqlite3 options...] <statement>: the record, created on first use.
history_sql() {
  sqlite3 -cmd '.timeout 5000' "${@:1:$#-1}" "$STATE/history.db" "CREATE TABLE IF NOT EXISTS events (
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
  CREATE TABLE IF NOT EXISTS queue (team TEXT NOT NULL, at TEXT NOT NULL, waiting INTEGER NOT NULL); ${*: -1}"
}

# run_outcome <team> <role> <pid> <outcome>: why that run, still going, will have ended.
run_outcome() {
  [ -f "$STATE/history.db" ] || return 0
  history_sql "UPDATE runs SET outcome = $(sql "$4") WHERE team = $(sql "$1") AND role = $(sql "$2")
    AND pid = $3 AND ended IS NULL;" >/dev/null || true
}
