#!/usr/bin/env bash
#
# attach.sh [--dry-run] <team> <role> — stops the role's run and holds it (as `a-team stop`), then
# resumes the latest run's conversation with `claude --resume` in the team's workdir. The role
# stays held after you quit, until `a-team resume <team> <role>`.
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

die() { echo "a-team attach: $*" >&2; exit 1; }

DRY_RUN=${A_TEAM_DRY_RUN:-}
if [ "${1:-}" = --dry-run ]; then
  DRY_RUN=1
  shift
fi
[ $# -eq 2 ] || die "usage: a-team attach [--dry-run] <team> <role>"

source "$ROOT/scripts/common.sh"
TEAM=$1
ROLE=$2
case "$ROLE" in
  lead | dev) ;;
  *) die "unknown role '$ROLE': expected lead or dev" ;;
esac
CONFIG=$(team_config "$TEAM")
[ -f "$CONFIG" ] || die "no config for team '$TEAM' at $CONFIG"

LOG="$STATE/$TEAM/$ROLE/latest.jsonl"
SESSION=$(jq -rR 'fromjson? | select(.type == "system" and .subtype == "init") | .session_id // empty' "$LOG" 2>/dev/null |
  head -n 1 || true)
[ -n "$SESSION" ] || die "$TEAM $ROLE has no run with a session to resume"

WORKDIR=$(jq -r '.workdir // empty' "$CONFIG")
WORKDIR=${WORKDIR/#\~/$HOME}
[ -n "$WORKDIR" ] || die "$CONFIG has no workdir to resume in"

if [ -n "$DRY_RUN" ]; then
  bash "$ROOT/scripts/pause.sh" stop --dry-run "$TEAM" "$ROLE"
  echo "(dry run) would run: claude --resume $SESSION, in $WORKDIR"
  exit 0
fi

running() { local stat; stat=$(ps -o stat= -p "$1" 2>/dev/null) && [[ $stat != Z* ]]; }

PID=$(cat "$STATE/$TEAM/$ROLE/pid" 2>/dev/null || true)
bash "$ROOT/scripts/pause.sh" stop "$TEAM" "$ROLE"
# The stopped run may still be writing to the session, so let it exit before resuming it.
for _ in $(seq 100); do
  { [ -n "$PID" ] && running "$PID"; } || break
  sleep 0.1
done
cd "$WORKDIR" || die "can't open $WORKDIR; $TEAM $ROLE is stopped and held"
claude --resume "$SESSION" ||
  die "couldn't resume session $SESSION; $TEAM $ROLE is stopped and held until: a-team resume $TEAM $ROLE"
