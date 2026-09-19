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
