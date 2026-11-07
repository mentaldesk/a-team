#!/usr/bin/env bash
# What the suite itself promises.
# shellcheck source=tests/scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
# shellcheck disable=SC2034,SC2154 # helpers read what an area sets, and an area reads what lib.sh sets

# --- the suite answers for what it started ----------------------------------------------------
SUITE=$(mktemp -d "$WORK/suite.XXXXXX")
cat >"$SUITE/fake" <<SH
#!/usr/bin/env bash
trap '' TERM
echo \$\$ >>"\$1"
while [ "\$SECONDS" -lt "$FAKE_LIMIT" ]; do sleep 0.1; done
SH
chmod +x "$SUITE/fake"
# A small suite on the real clean-up: its one argument is what it does before finishing.
cat >"$SUITE/suite" <<SH
#!/usr/bin/env bash
export TMPDIR="$SUITE"
SUITE="$SUITE"
source "$ROOT/tests/leftovers.sh"
leak() { cp "$SUITE/fake" "\$WORK/fake"; nohup "\$WORK/fake" "$SUITE/pids" >/dev/null 2>&1 & }
neighbour() { mkdir "\$WORK.b"; cp "$SUITE/fake" "\$WORK.b/fake"; nohup "\$WORK.b/fake" "$SUITE/neighbours" >/dev/null 2>&1 & }
leak_shared() { fake "\$WORK/shared" <"$SUITE/fake"; nohup "\$WORK/shared" "$SUITE/pids" >/dev/null 2>&1 & }
started() { until [ -s "\$1" ]; do sleep 0.1; done; }
eval "\$1"
finish_suite
SH
small_suite() {
  : >"$SUITE/pids"
  bash "$SUITE/suite" "$1" >"$OUT" 2>"$ERR"
  STATUS=$?
}

case_ "a suite that left nothing running passes"
small_suite ':'
same "exit" 0 "$STATUS"
same "output" "all passed" "$(cat "$OUT")"

case_ "a passing suite that left a fake running stops it, names it and fails"
small_suite 'leak; started "$SUITE/pids"'
same "exit" 1 "$STATUS"
same "output" "  FAIL left running when the suite ended, now stopped:
       <tmp>/fake
1 failed" "$(cat "$OUT")"
stopped "after passing"

case_ "a fake started through the shared launcher is found under its own name"
small_suite 'leak_shared; started "$SUITE/pids"'
same "exit" 1 "$STATUS"
same "output" "  FAIL left running when the suite ended, now stopped:
       <tmp>/shared
1 failed" "$(cat "$OUT")"
stopped "through the launcher"

case_ "a suite whose cases failed lists what it stopped without counting it"
small_suite 'fail a case; leak; started "$SUITE/pids"'
same "exit" 1 "$STATUS"
same "output" "  FAIL a case
  left running when the suite ended, now stopped:
       <tmp>/fake
1 failed" "$(cat "$OUT")"
stopped "after failing"

case_ "an interrupted suite stops what it started and removes its folder"
for signal in TERM:143 HUP:129 INT:130; do
  code=${signal#*:} signal=${signal%:*}
  : >"$SUITE/pids"
  # A background job starts with INT ignored, and bash can't undo that. Its sleep survives, as a child can.
  perl -e '$SIG{INT} = "DEFAULT"; exec @ARGV' bash "$SUITE/suite" 'leak; started "$SUITE/pids"; touch "$SUITE/waiting"; sleep 3' >"$OUT" 2>"$ERR" &
  interrupted=$!
  for _ in $(seq 50); do [ -e "$SUITE/waiting" ] && break; sleep 0.1; done
  rm -f "$SUITE/waiting"
  kill -"$signal" "$interrupted"
  wait "$interrupted" 2>/dev/null
  STATUS=$?
  same "$signal exit" "$code" "$STATUS"
  same "$signal output" "  left running when the suite ended, now stopped:
       <tmp>/fake" "$(cat "$OUT")"
  stopped "$signal"
  same "$signal folder" "" "$(find "$SUITE" -name 'tmp.*')"
done

case_ "a suite leaves another suite's process alone, even in a folder whose name starts with its own"
small_suite 'neighbour; started "$SUITE/neighbours"'
same "exit" 0 "$STATUS"
same "output" "all passed" "$(cat "$OUT")"
neighbour=$(cat "$SUITE/neighbours")
kill -0 "$neighbour" 2>/dev/null || fail "stopped the neighbour"
kill -9 "$neighbour" 2>/dev/null

case_ "nothing pipes into write: a dry run reads none of it, so under pipefail the pipe could die of SIGPIPE"
same "pipes" "" "$(grep -nE '[^|]\| *write ' "$ROOT"/scripts/*.sh)"

# --- the driver --------------------------------------------------------------------------------
DRIVER=$(mktemp -d "$WORK/driver.XXXXXX")
mkdir "$DRIVER/scripts"
cp "$ROOT/tests/scripts.sh" "$DRIVER/scripts.sh"
: >"$DRIVER/scripts/lib.sh"
cat >"$DRIVER/scripts/slow.sh" <<'SH'
sleep 0.5
echo "slow case"
echo "all passed"
SH
cat >"$DRIVER/scripts/quick.sh" <<'SH'
echo "quick case"
echo "all passed"
SH
drive() { bash "$DRIVER/scripts.sh" "$@" >"$OUT" 2>"$ERR"; STATUS=$?; }

case_ "every area's cases come out in order, whichever finished first, with one word on the whole"
drive
same "exit" 0 "$STATUS"
same "output" "quick case
slow case
all passed" "$(cat "$OUT")"

case_ "a named area runs alone, and lib isn't one"
drive quick
same "exit" 0 "$STATUS"
same "output" "quick case
all passed" "$(cat "$OUT")"
drive nothing
same "unknown exit" 2 "$STATUS"
grep -qF 'no such area: nothing' "$ERR" || fail "unknown: '$(cat "$ERR")'"
drive lib
same "lib exit" 2 "$STATUS"

case_ "failures add up across areas, and an area that dies counts as one"
cat >"$DRIVER/scripts/broken.sh" <<'SH'
echo "  FAIL one"
echo "  FAIL two"
echo "2 failed"
exit 1
SH
cat >"$DRIVER/scripts/dead.sh" <<'SH'
echo "died here"
exit 3
SH
drive
same "exit" 1 "$STATUS"
same "output" "  FAIL one
  FAIL two
died here
  FAIL dead stopped with exit 3
quick case
slow case
3 failed" "$(cat "$OUT")"
rm "$DRIVER/scripts/broken.sh" "$DRIVER/scripts/dead.sh"

case_ "stopping the driver stops the areas it started, and removes its logs"
cat >"$DRIVER/scripts/waits.sh" <<SH
trap 'echo stopped >>"$DRIVER/stopped"; exit 143' TERM
echo \$\$ >>"$DRIVER/waiting"
while [ "\$SECONDS" -lt "$FAKE_LIMIT" ]; do sleep 0.1; done
SH
cp "$DRIVER/scripts/waits.sh" "$DRIVER/scripts/waits-too.sh"
for signal in TERM:143 HUP:129 INT:130; do
  code=${signal#*:} signal=${signal%:*}
  : >"$DRIVER/waiting"
  : >"$DRIVER/stopped"
  # A background job starts with INT ignored, and bash can't undo that.
  TMPDIR="$DRIVER" perl -e '$SIG{INT} = "DEFAULT"; exec @ARGV' bash "$DRIVER/scripts.sh" waits waits-too >"$OUT" 2>"$ERR" &
  driving=$!
  for _ in $(seq 100); do [ "$(grep -c . <"$DRIVER/waiting")" -eq 2 ] && break; sleep 0.1; done
  kill -"$signal" "$driving"
  wait "$driving" 2>/dev/null
  same "$signal exit" "$code" "$?"
  same "$signal areas told" 2 "$(grep -c . <"$DRIVER/stopped")"
  while read -r pid; do
    kill -0 "$pid" 2>/dev/null && fail "$signal: area $pid still running"
  done <"$DRIVER/waiting"
  same "$signal logs" "" "$(find "$DRIVER" -maxdepth 1 -name 'scripts.*' ! -name scripts.sh)"
done

finish_suite
