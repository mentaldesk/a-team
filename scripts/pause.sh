#!/usr/bin/env bash
#
# pause.sh <pause|resume|stop> [--dry-run] <team> [<role>] — turns a team's dispatch off or on,
# or holds a role until `resume <team> <role>`: `pause` lets its run finish, `stop` ends it. The
# dispatcher reads the config on every pass, so there is nothing to restart either way.
# `stop <team> dev <task>` ends only that task's run and holds only that task, until
# `resume <team> dev <task>`.
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CMD=${1:-}
[ $# -eq 0 ] || shift

die() { echo "a-team ${CMD:-pause}: $*" >&2; exit 1; }

case "$CMD" in
  pause | resume | stop) ;;
  *) die "expected pause, resume or stop, not '$CMD'" ;;
esac

DRY_RUN=${A_TEAM_DRY_RUN:-}
if [ "${1:-}" = --dry-run ]; then
  DRY_RUN=1
  shift
fi
case "$CMD $#" in
  "pause 1" | "pause 2" | "resume 1" | "resume 2" | "resume 3" | "stop 2" | "stop 3") ;;
  stop*) die "usage: a-team stop [--dry-run] <team> <role> [<task>]" ;;
  resume*) die "usage: a-team resume [--dry-run] <team> [<role> [<task>]]" ;;
  *) die "usage: a-team pause [--dry-run] <team> [<role>]" ;;
esac

source "$ROOT/scripts/common.sh"
TEAM=$1
ROLE=${2:-}
TASK=${3:-}
case "$ROLE" in
  '' | lead | dev | customer | reviewer) ;;
  *) die "unknown role '$ROLE': expected lead, dev, customer or reviewer" ;;
esac
CONFIG=$(team_config "$TEAM")
[ -f "$CONFIG" ] || die "no config for team '$TEAM' at $CONFIG"
[ "$ROLE" != customer ] || team_roles "$CONFIG" | grep -qx customer ||
  die "$TEAM has no Customer lead: turn it on in Settings → Teams"
[ "$ROLE" != reviewer ] || team_roles "$CONFIG" | grep -qx reviewer ||
  die "$TEAM has no Reviewer: turn it on in Settings → Teams"

if [ -n "$TASK" ]; then
  [ "$ROLE" = dev ] || [ "$ROLE" = reviewer ] || die "only a Dev or Reviewer run is for a task"
  [[ $TASK =~ ^[0-9]+$ ]] || die "expected a task number, not '$TASK'"
  RUN="$STATE/$TEAM/$ROLE/runs/$TASK"
  if [ "$CMD" = resume ]; then
    if [ -n "$DRY_RUN" ]; then echo "(dry run) would let #$TASK start again"; exit 0; fi
    rm -f "$RUN/held"
    echo "let $TEAM $ROLE start #$TASK again"
    exit 0
  fi
  PID=$(cat "$RUN/pid" 2>/dev/null) && kill -0 "$PID" 2>/dev/null || PID=''
  if [ -n "$DRY_RUN" ]; then
    if [ -n "$PID" ]; then echo "(dry run) would stop run $PID on #$TASK"; else echo "(dry run) no run to stop on #$TASK"; fi
    echo "(dry run) would hold #$TASK"
    exit 0
  fi
  mkdir -p "$RUN"
  date +%s >"$RUN/held"
  if [ -n "$PID" ]; then
    kill "$PID" 2>/dev/null || true
    run_outcome "$TEAM" "$ROLE" "$PID" stopped
    echo "stopped $TEAM $ROLE's run on #$TASK ($PID)"
  else
    echo "no run to stop on #$TASK"
  fi
  echo "#$TASK stays where it is, and no run starts on it until: a-team resume $TEAM $ROLE $TASK"
  exit 0
fi

case "$CMD $ROLE" in
  "pause ") FILTER='.dispatch.enabled = false' CHANGE="set dispatch.enabled to false" ;;
  "resume ") FILTER='.dispatch.enabled = true' CHANGE="set dispatch.enabled to true" ;;
  resume*) FILTER='.dispatch.hold = ((.dispatch.hold // []) - [$role])' CHANGE="remove $ROLE from dispatch.hold" ;;
  *) FILTER='.dispatch.hold = ((.dispatch.hold // []) - [$role] + [$role])' CHANGE="add $ROLE to dispatch.hold" ;;
esac

UPDATED=$(jq -e --arg role "$ROLE" "if type == \"object\" then $FILTER else null end" "$CONFIG" 2>/dev/null) ||
  die "$CONFIG isn't a JSON object"

DIR="$STATE/$TEAM/$ROLE"
PID=''
if [ "$CMD" = stop ]; then
  # A Dev or Reviewer may have several runs going, each under runs/<task>.
  PID=$(for file in "$DIR/pid" "$DIR"/runs/*/pid; do
    pid=$(cat "$file" 2>/dev/null) && kill -0 "$pid" 2>/dev/null && echo "$pid"
  done | sort -un | paste -sd ' ' -) || true
fi

if [ -n "$DRY_RUN" ]; then
  echo "(dry run) would $CHANGE in $CONFIG"
  if [ "$CMD" = stop ]; then
    if [ -n "$PID" ]; then echo "(dry run) would stop run ${PID// /, }"; else echo "(dry run) no run to stop"; fi
  fi
  exit 0
fi

[ -w "$CONFIG" ] || die "can't write $CONFIG"
{ printf '%s\n' "$UPDATED" >"$CONFIG"; } 2>/dev/null || die "can't write $CONFIG"

case "$CMD $ROLE" in
  "pause "?*) echo "held $ROLE: a run already going finishes, and none starts until: a-team resume $TEAM $ROLE" ;;
  pause* | resume*) echo "${CMD}d $TEAM${ROLE:+ $ROLE}" ;;
  stop*)
    mkdir -p "$DIR"
    date +%s >"$DIR/stopped"
    if [ -n "$PID" ]; then
      # shellcheck disable=SC2086 # one pid per word
      kill $PID 2>/dev/null || true
      for pid in $PID; do run_outcome "$TEAM" "$ROLE" "$pid" stopped; done
      echo "stopped $TEAM $ROLE's run (${PID// /, }), and held $ROLE until: a-team resume $TEAM $ROLE"
    else
      echo "no run to stop; held $ROLE until: a-team resume $TEAM $ROLE"
    fi
    if claimed=$("$ROOT/bin/a-team" board "$TEAM" mine "$ROLE" "In progress" 2>/dev/null); then
      left=$(jq -r '.[] | "  #\(.number) \(.title)"' <<<"$claimed")
      if [ -n "$left" ]; then
        echo "It left claimed, In progress:"
        echo "$left"
      else
        echo "It left nothing claimed."
      fi
    else
      echo "Couldn't read the board to say what it left claimed."
    fi
    ;;
esac
