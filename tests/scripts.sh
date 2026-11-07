#!/usr/bin/env bash
#
# tests/scripts.sh [area...] — the shell side of a-team, an area at a time from tests/scripts/, every one with no
# argument. Areas run side by side, TEST_JOBS at once, each with its own temp folder and state.
#
set -uo pipefail

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/scripts"
areas=()
for area in "$@"; do
  [ -f "$DIR/$area.sh" ] && [ "$area" != lib ] || { echo "no such area: $area (see $DIR)" >&2; exit 2; }
  areas+=("$area")
done
if [ "${#areas[@]}" -eq 0 ]; then
  for file in "$DIR"/*.sh; do
    area=$(basename "$file" .sh)
    [ "$area" = lib ] || areas+=("$area")
  done
fi
# One area needs nothing gathered, and shows its cases as they run.
[ "${#areas[@]}" -gt 1 ] || exec bash "$DIR/${areas[0]}.sh"

LOGS=$(mktemp -d "${TMPDIR:-/tmp}/scripts.XXXXXX")
JOBS=${TEST_JOBS:-$(getconf _NPROCESSORS_ONLN 2>/dev/null || echo 4)}
pids=()
failed=0

# run_area <area>: what's left of it is its status file, not its pid: a whole suite starts enough processes for pids to
# come round again, and then a finished area's pid can't be waited on.
run_area() {
  local area stopping=''
  bash "$DIR/$1.sh" >"$LOGS/$1.log" 2>&1 &
  area=$!
  trap 'stopping=1; kill "$area" 2>/dev/null' TERM
  wait "$area"
  local status=$?
  [ -z "$stopping" ] || { wait "$area"; status=$?; }
  echo "$status" >"$LOGS/$1.status.new"
  mv "$LOGS/$1.status.new" "$LOGS/$1.status"
}

# An area started in the background ignores INT, so whatever stops this stops each area with TERM.
stop() {
  local at
  for at in "${!pids[@]}"; do
    [ -e "$LOGS/${areas[$at]}.status" ] || kill "${pids[$at]}" 2>/dev/null
  done
  wait 2>/dev/null
  exit "$1"
}
trap 'rm -rf "$LOGS"' EXIT
trap 'stop 129' HUP
trap 'stop 130' INT
trap 'stop 143' TERM

# report <area>: its cases, in place of its own last line, and what that line said added to the count.
report() {
  local said status
  sed -E '/^(all passed|[0-9]+ failed)$/d' "$LOGS/$1.log"
  said=$(sed -nE 's/^([0-9]+) failed$/\1/p' "$LOGS/$1.log" | tail -1)
  status=$(cat "$LOGS/$1.status")
  if [ -n "$said" ]; then
    failed=$((failed + said))
  elif [ "$status" -ne 0 ]; then
    echo "  FAIL $1 stopped with exit $status"
    failed=$((failed + 1))
  fi
}

started=0
shown=0
while [ "$shown" -lt "${#areas[@]}" ]; do
  running=$(jobs -rp | wc -l)
  while [ "$started" -lt "${#areas[@]}" ] && [ "$running" -lt "$JOBS" ]; do
    run_area "${areas[$started]}" &
    pids+=("$!")
    started=$((started + 1))
    running=$((running + 1))
  done
  while [ "$shown" -lt "$started" ] && [ -e "$LOGS/${areas[$shown]}.status" ]; do
    report "${areas[$shown]}"
    shown=$((shown + 1))
  done
  [ "$shown" -eq "${#areas[@]}" ] || sleep 0.1
done

[ "$failed" -eq 0 ] || { echo "$failed failed"; exit 1; }
echo "all passed"
