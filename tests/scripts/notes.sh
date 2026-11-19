#!/usr/bin/env bash
# What each run noticed: kept from its summary, listed for the Lead, and handled by it.
# shellcheck source=tests/scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
# shellcheck disable=SC2034,SC2154 # helpers read what an area sets, and an area reads what lib.sh sets

fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 },
  "wip": { "ideas": 2 } }
JSON
gh_items <<'ITEMS'
Idea 30 An idea
Building 31 A pitch
Ready 40 A task
ITEMS

# ended_run <id> <role> <item|-> <summary|->: a run in the record that has ended, its log ending in that summary, or cut short.
ended_run() {
  local log="$WORK/run$1.jsonl"
  echo '{"type":"system","subtype":"init"}' >"$log"
  [ "$4" = - ] || jq -nc --arg said "$4" '{type: "result", result: $said, is_error: false}' >>"$log"
  history_db "INSERT INTO runs (id, team, role, pid, log, started, ended) VALUES
    ($1, 'demo', '$2', $1, '$log', '2026-11-1$(($1 % 10))T08:00:00Z', '2026-11-1$(($1 % 10))T09:00:00Z');
    $([ "$3" = - ] || echo "INSERT INTO run_items (run, item) VALUES ($1, $3);")"
}

noted() { sqlite3 "$A_TEAM_STATE/history.db" "SELECT id || ' ' || COALESCE(handled, '-') || ' ' || COALESCE(ref, '-') || ' ' || text FROM notes ORDER BY id;"; }

case_ "notes keeps the Noticed list from the end of a run's summary, once, and none from a run cut short or with none"
run board demo wip
ended_run 1 dev 40 $'Draft PR #41 is up.\n\nNoticed:\n- The brief\'s `export` line can\'t carry between calls.\n- The skill names a binary the build no longer makes.'
ended_run 2 dev 40 $'Done.\n\nNoticed: none'
ended_run 3 reviewer 40 -
ended_run 4 lead - $'Pitched #31.\n\n**Noticed:**\n- the brief\'s export line can\'t carry between calls'
for id in 1 2 3 4; do
  run board demo notes "$id"
  same "run $id exit" 0 "$STATUS"
done
run board demo notes 1
same "again" "" "$(cat "$OUT")"
same "kept" "1 - - The brief's \`export\` line can't carry between calls.
2 - - The skill names a binary the build no longer makes.
3 - - the brief's export line can't carry between calls" "$(noted)"
run board demo notes 99
failed "unknown run"
one_line "unknown run"

case_ "the Lead's brief lists the notes no Lead run has handled, grouped, each with who noticed it and how often"
run board demo noticed
same "exit" 0 "$STATUS"
same "heading" "## Noticed since the last Lead run (2 groups, 3 notes)" "$(head -n 1 "$OUT")"
grep -qF "1. Dev ×1, Lead ×1: The brief's \`export\` line can't carry between calls." "$OUT" || fail "group: '$(cat "$OUT")'"
grep -qF "   notes: 1 3 · runs: #40 11 Nov · Lead 14 Nov" "$OUT" || fail "runs: '$(cat "$OUT")'"
grep -qF "   evidence: Noticed by the Dev on #40, and the Lead once" "$OUT" || fail "evidence: '$(cat "$OUT")'"
grep -qF "Room for 2 more Ideas" "$OUT" || fail "room: '$(cat "$OUT")'"

case_ "only the Lead handles notes"
echo "Already fixed." >"$WORK/why"
for role in dev customer reviewer you; do
  run board demo noticed "$role" drop "$WORK/why" 2
  failed "$role"
  one_line "$role"
done
same "untouched" "2 - -" "$(noted | sed -n 2p | cut -d' ' -f1-3)"

case_ "the Lead files a group as an Idea whose Evidence says who noticed it, and it leaves the brief"
printf '## Opportunity\n\nEvery run is told to export a variable.\n\n## Evidence\n\n- scripts/dispatch.sh:323\n' >"$WORK/idea"
gh_child 77 - -
run board demo noticed lead file "Every run is told to export a variable the dispatcher already set" "$WORK/idea" 1 3
same "exit" 0 "$STATUS"
same "number" 77 "$(cat "$OUT")"
same "body" "$(printf '## Opportunity\n\nEvery run is told to export a variable.\n\n## Evidence\n\nNoticed by the Dev on #40, and the Lead once.\n\n- scripts/dispatch.sh:323\n\n<!-- a-team:lead -->')" "$(cat "$POSTED")"
same "status" Idea "$(status_of 77)"
same "counts toward the Ideas limit" true "$(marks 77 | jq .idea)"
same "handled" "1 filed 77
3 filed 77" "$(noted | grep -E '^(1|3) ' | cut -d' ' -f1-3)"
KEEP_BOARD=1 run board demo noticed
grep -q "1 group, 1 note" "$OUT" || fail "left: '$(cat "$OUT")'"

case_ "no room for another Idea refuses to file one, and the notes stay in the brief"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 },
  "wip": { "ideas": 1 } }
JSON
edit_item 30 '.labels.nodes = [{name: "a-team:idea"}]'
gh_child 77 - -
run board demo noticed lead file "A title" "$WORK/idea" 2
failed "full"
one_line "full"
grep -qF "no room for another Idea" "$ERR" || fail "full: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"
same "still unhandled" "2 - -" "$(noted | sed -n 2p | cut -d' ' -f1-3)"

case_ "adding a group to an open Idea or pitch comments on it as the Lead, and a task is refused"
echo "More evidence for this pitch." >"$WORK/added"
run board demo noticed lead add 40 "$WORK/added" 2
failed "task"
grep -qF "only to an open Idea or pitch" "$ERR" || fail "task: '$(cat "$ERR")'"
run board demo noticed lead add 31 "$WORK/added" 2
same "exit" 0 "$STATUS"
same "comment" "$(printf 'More evidence for this pitch.\n\nNoticed by the Dev on #40.\n\n<!-- a-team:lead -->')" "$(cat "$POSTED")"
same "handled" "2 added 31" "$(noted | sed -n 2p | cut -d' ' -f1-3)"

case_ "an answer or a drop is one line, kept with the note, and a handled note isn't handled again"
ended_run 5 dev 40 $'Done.\n\nNoticed:\n- Shall I file it as an Idea?\n- Stale skill line'
run board demo notes 5
printf 'Two lines.\nAre too many.\n' >"$WORK/two"
run board demo noticed lead drop "$WORK/two" 5
failed "two lines"
echo "No: the Lead files Ideas from notes now." >"$WORK/answer"
run board demo noticed lead answer "$WORK/answer" 4
same "exit" 0 "$STATUS"
run board demo noticed lead drop "$WORK/why" 5
same "exit" 0 "$STATUS"
run board demo noticed lead drop "$WORK/why" 5
failed "again"
same "again" "board.sh: note 5 is dropped already" "$(cat "$ERR")"
same "why" "No: the Lead files Ideas from notes now.|Already fixed." \
  "$(sqlite3 "$A_TEAM_STATE/history.db" "SELECT group_concat(why, '|') FROM (SELECT why FROM notes WHERE id IN (4, 5) ORDER BY id);")"
run board demo noticed
same "nothing left" "" "$(cat "$OUT")"

case_ "the card's last run carries its notes, and what the Lead did with them, in the overview"
run board demo overview
same "exit" 0 "$STATUS"
same "notes" '[{"text":"Shall I file it as an Idea?","handled":"answered","why":"No: the Lead files Ideas from notes now."},{"text":"Stale skill line","handled":"dropped","why":"Already fixed."}]' \
  "$(jq -c '.[] | select(.number == 40) | .lastRun.notes' "$OUT")"
same "no notes, no key" false "$(jq '.[] | select(.number == 31) | .lastRun // {} | has("notes")' "$OUT")"

case_ "notes alone never wake the Lead"
ended_run 6 dev 40 $'Done.\n\nNoticed:\n- Something new'
run board demo notes 6
run board demo triggers lead
same "exit" 0 "$STATUS"
jq -e '.reasons | map(select(test("notic"; "i"))) | length == 0' "$OUT" >/dev/null || fail "woke: '$(jq -c .reasons "$OUT")'"
