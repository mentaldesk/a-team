#!/usr/bin/env bash
# A-team attach.
# shellcheck source=tests/scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
# shellcheck disable=SC2034,SC2154 # helpers read what an area sets, and an area reads what lib.sh sets

case_ "attach on a running role stops and holds it while you resume its session, then lets it start again"
WORKDIR=$(mktemp -d "$WORK/workdir.XXXXXX")
fixture <<JSON
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 },
  "workdir": "$WORKDIR", "dispatch": { "enabled": true } }
JSON
gh_items </dev/null
fake_run
session_log
fake_claude
run attach demo dev
wait "$RUN_PID"
same "exit" 0 "$STATUS"
same "resumed" "args: --resume sess-123" "$(sed -n 1p "$RESUMED")"
same "cwd" "cwd: $(cd "$WORKDIR" && pwd -P)" "$(sed -n 2p "$RESUMED")"
same "held first" 'hold: ["dev"]' "$(sed -n 3p "$RESUMED")"
same "stopped first" "signals: 1" "$(sed -n 4p "$RESUMED")"
same "resumed as a team's run, so you commands refuse" "team: demo" "$(sed -n 5p "$RESUMED")"
same "released" '[]' "$(held)"

case_ "attach on a finished role doesn't signal anything, and resumes"
true &
dead=$!
wait "$dead"
echo "$dead" >"$A_TEAM_STATE/demo/dev/pid"
: >"$SIGNALS"
fake_claude
run attach demo dev
same "exit" 0 "$STATUS"
same "resumed" "args: --resume sess-123" "$(sed -n 1p "$RESUMED")"
same "signals" "signals: 0" "$(sed -n 4p "$RESUMED")"
same "released" '[]' "$(held)"

case_ "attach --dry-run names the session and workdir, and neither stops, holds nor resumes"
fake_run
fake_claude
run attach --dry-run demo dev
same "exit" 0 "$STATUS"
grep -q "would stop run $RUN_PID" "$OUT" || fail "dry run: '$(cat "$OUT")'"
grep -q "would run: claude --resume sess-123, in $WORKDIR" "$OUT" || fail "dry run: '$(cat "$OUT")'"
same "hold" '[]' "$(held)"
same "resumed" "" "$(cat "$RESUMED")"
kill -0 "$RUN_PID" 2>/dev/null || fail "dry run: the run was stopped"
kill "$RUN_PID" 2>/dev/null
wait "$RUN_PID" 2>/dev/null

case_ "a session that won't resume says so, and leaves the role stopped and held"
fake_claude 1
run attach demo dev
failed "won't resume"
grep -q "couldn't resume session sess-123" "$ERR" || fail "won't resume: '$(cat "$ERR")'"
same "held" '["dev"]' "$(held)"

case_ "attach with no session to resume is one clear line, and neither holds nor resumes"
run resume demo dev
echo '{"type":"assistant"}' >"$A_TEAM_STATE/demo/dev/latest.jsonl"
fake_claude
run attach demo dev
failed "no session"
one_line "no session"
grep -q "demo dev has no run with a session to resume" "$ERR" || fail "no session: '$(cat "$ERR")'"
same "hold" '[]' "$(held)"
same "resumed" "" "$(cat "$RESUMED")"

case_ "attach <team> dev <task> stops and holds only that run while you resume its session, then lets it go"
run resume demo dev
for n in 21 22; do
  mkdir -p "$A_TEAM_STATE/demo/dev/runs/$n"
  bash -c "trap 'echo $n >>\"$SIGNALS\"; exit' TERM; while [ \$SECONDS -lt $FAKE_LIMIT ]; do sleep 0.1; done" &
  echo $! >"$A_TEAM_STATE/demo/dev/runs/$n/pid"
done
: >"$SIGNALS"
echo '{"type":"system","subtype":"init","session_id":"sess-21"}' >"$A_TEAM_STATE/demo/dev/runs/21/latest.jsonl"
fake_claude
run attach demo dev 21
same "exit" 0 "$STATUS"
same "resumed" "args: --resume sess-21" "$(sed -n 1p "$RESUMED")"
same "role not held" 'hold: []' "$(sed -n 3p "$RESUMED")"
same "stopped" "21" "$(cat "$SIGNALS")"
kill -0 "$(cat "$A_TEAM_STATE/demo/dev/runs/22/pid")" 2>/dev/null || fail "attach by task: #22 was stopped"
[ -f "$A_TEAM_STATE/demo/dev/runs/21/held" ] && fail "attach by task: #21 is still held"
kill "$(cat "$A_TEAM_STATE/demo/dev/runs/22/pid")" 2>/dev/null

case_ "attach is in the usage text"
run help
grep -q '^  attach \[--dry-run\] <team> <role>' "$OUT" || fail "usage: no attach line"
unset A_TEAM_STATE

finish_suite
