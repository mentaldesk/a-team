#!/usr/bin/env bash
#
# tests/leftovers.sh — a suite answers for what it started. Source it first: it gives the suite $WORK,
# fail and finish_suite. A process is the suite's if its command names $WORK, so a fake has to.

WORK=$(mktemp -d)
failures=0
LEFT_RUNNING="left running when the suite ended, now stopped:"

fail() { echo "  FAIL $*"; failures=$((failures + 1)); }

# The slash keeps a suite whose folder's name merely starts with ours out of this.
running_from_work() {
  local listing
  listing=$(ps axww -o pid=,command=)
  awk -v folder="$WORK/" '$1 ~ /^[0-9]+$/ && index($0, folder)' <<<"$listing"
}

# Prints the path of each process it stopped. A case's last kill gets a moment to land first.
stop_leftovers() {
  local running _
  for _ in 1 2 3 4 5 6 7 8 9 10; do
    running=$(running_from_work)
    [ -n "$running" ] || return 0
    sleep 0.1
  done
  awk -v folder="$WORK/" '{
    path = substr($0, index($0, folder) + length(folder))
    sub(/[ "\047;].*/, "", path)
    print "       <tmp>/" path
  }' <<<"$running" | sort -u
  for _ in 1 2 3 4 5 6 7 8 9 10; do
    # shellcheck disable=SC2046 # one word per pid
    kill -9 $(awk '{print $1}' <<<"$running") 2>/dev/null
    sleep 0.1
    running=$(running_from_work)
    [ -n "$running" ] || return 0
  done
}

on_suite_exit() {
  local left
  left=$(stop_leftovers)
  [ -z "$left" ] || printf '  %s\n%s\n' "$LEFT_RUNNING" "$left"
  rm -rf "$WORK"
}
trap on_suite_exit EXIT

# Leftovers fail a passing suite. After a failed case they're expected, so they're only listed.
finish_suite() {
  local left
  left=$(stop_leftovers)
  if [ -n "$left" ]; then
    if [ "$failures" -eq 0 ]; then fail "$LEFT_RUNNING"; else echo "  $LEFT_RUNNING"; fi
    echo "$left"
  fi
  [ "$failures" -eq 0 ] || { echo "$failures failed"; exit 1; }
  echo "all passed"
}
