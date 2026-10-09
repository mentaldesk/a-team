#!/usr/bin/env bash
#
# attach.sh [--dry-run] <team> <role> — stops the role's run and holds it (as `a-team stop`), then
# resumes the latest run's conversation with `claude --resume` in the team's workdir. Quitting
# lets the role start again; if the session won't resume, it stays held. With a <task>, only that
# Dev run is stopped and held (as `a-team stop <team> dev <task>`), and its own session resumed.
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

die() { echo "a-team attach: $*" >&2; exit 1; }

DRY_RUN=${A_TEAM_DRY_RUN:-}
if [ "${1:-}" = --dry-run ]; then
  DRY_RUN=1
  shift
fi
[ $# -eq 2 ] || [ $# -eq 3 ] || die "usage: a-team attach [--dry-run] <team> <role> [<task>]"

source "$ROOT/scripts/common.sh"
TEAM=$1
ROLE=$2
TASK=${3:-}
case "$ROLE" in
  lead | dev | customer | reviewer) ;;
  *) die "unknown role '$ROLE': expected lead, dev, customer or reviewer" ;;
esac
CONFIG=$(team_config "$TEAM")
[ -f "$CONFIG" ] || die "no config for team '$TEAM' at $CONFIG"
[ "$ROLE" != customer ] || team_roles "$CONFIG" | grep -x customer >/dev/null ||
  die "$TEAM has no Customer lead: turn it on in Settings → Teams"
[ "$ROLE" != reviewer ] || team_roles "$CONFIG" | grep -x reviewer >/dev/null ||
  die "$TEAM has no Reviewer: turn it on in Settings → Teams"

DIR="$STATE/$TEAM/$ROLE"
if [ -n "$TASK" ]; then
  [ "$ROLE" = dev ] || [ "$ROLE" = reviewer ] || die "only a Dev or Reviewer run is for a task"
  [[ $TASK =~ ^[0-9]+$ ]] || die "expected a task number, not '$TASK'"
  DIR="$DIR/runs/$TASK"
fi
LOG="$DIR/latest.jsonl"
SESSION=$(jq -rR 'fromjson? | select(.type == "system" and .subtype == "init") | .session_id // empty' "$LOG" 2>/dev/null |
  head -n 1 || true)
[ -n "$SESSION" ] || die "$TEAM $ROLE${TASK:+ #$TASK} has no run with a session to resume"

WORKDIR=$(jq -r '.workdir // empty' "$CONFIG")
WORKDIR=${WORKDIR/#\~/$HOME}
[ -n "$WORKDIR" ] || die "$CONFIG has no workdir to resume in"

if [ -n "$DRY_RUN" ]; then
  bash "$ROOT/scripts/pause.sh" stop --dry-run "$TEAM" "$ROLE" ${TASK:+"$TASK"}
  echo "(dry run) would run: claude --resume $SESSION, in $WORKDIR"
  exit 0
fi

running() { local stat; stat=$(ps -o stat= -p "$1" 2>/dev/null) && [[ $stat != Z* ]]; }

PID=$(cat "$DIR/pid" 2>/dev/null || true)
bash "$ROOT/scripts/pause.sh" stop "$TEAM" "$ROLE" ${TASK:+"$TASK"}
# The stopped run may still be writing to the session, so let it exit before resuming it.
for _ in $(seq 100); do
  { [ -n "$PID" ] && running "$PID"; } || break
  sleep 0.1
done
WHO="$TEAM $ROLE${TASK:+ $TASK}"
cd "$WORKDIR" || die "can't open $WORKDIR; $WHO is stopped and held"
claude --resume "$SESSION" ||
  die "couldn't resume session $SESSION; $WHO is stopped and held until: a-team resume $WHO"
bash "$ROOT/scripts/pause.sh" resume "$TEAM" "$ROLE" ${TASK:+"$TASK"}
