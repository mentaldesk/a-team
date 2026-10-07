# shellcheck shell=bash
# Sourced by the other scripts: where state and team configs live, and which teams exist.

# shellcheck disable=SC2034 # used by the scripts that source this
STATE="${A_TEAM_STATE:-${XDG_STATE_HOME:-$HOME/.local/state}/a-team}"
CONFIG_DIR="${A_TEAM_CONFIG:-${XDG_CONFIG_HOME:-$HOME/.config}/a-team}"

team_config() { echo "$CONFIG_DIR/teams/$1.json"; }

# team_roles <config>: the roles a team runs, the Customer lead only where it's turned on.
team_roles() {
  printf '%s\n' lead dev
  jq -e '.roles.customer == true' "$1" >/dev/null 2>&1 && echo customer
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
