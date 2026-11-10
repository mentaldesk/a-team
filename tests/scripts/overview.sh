#!/usr/bin/env bash
# Overview, moves, trends, decisions and install.
# shellcheck source=tests/scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
# shellcheck disable=SC2034,SC2154 # helpers read what an area sets, and an area reads what lib.sh sets
app_fakes

case_ "overview returns every card but Done, with when its Status was set, in one call"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "project": { "owner": "mentaldesk", "number": 1 } }
JSON
gh_items 404 <<'ITEMS'
Building 404 Overseer
Ready 414 Every board on one screen
In_review 415 Docs
Idea 395 A seed of mine
Done 99 Already merged
ITEMS
jq '.data.organization.projectV2.items.nodes[].fieldValueByName.updatedAt = "2026-10-01T08:00:00Z"' "$ITEMS" >"$ITEMS.new" &&
  mv "$ITEMS.new" "$ITEMS"
edit_item 414 '.parent = {number: 404}'
edit_item 415 '.labels.nodes = [{name: "a-team:customer"}]'
edit_item 395 '.labels.nodes = [{name: "blocked"}]'
edit_item 414 '.issueDependenciesSummary = {blockedBy: 1}'
run board demo overview
same "exit" 0 "$STATUS"
same "numbers" '[404,414,415,395]' "$(jq -c '[.[].number]' "$OUT")"
same "kinds" '["pitch","task","docs","yours"]' "$(jq -c '[.[].kind]' "$OUT")"
same "fields" '["blocked","kind","lastRun","number","parent","priority","since","status","team","title","url"]' "$(jq -c '.[0] | keys' "$OUT")"
same "since" '"2026-10-01T08:00:00Z"' "$(jq -c '.[0].since' "$OUT")"
same "parent" '404' "$(jq -c '.[1].parent' "$OUT")"
same "blocked" '[false,true,false,true]' "$(jq -c '[.[].blocked]' "$OUT")"
same "priority" '"High"' "$(jq -c '.[0].priority' "$OUT")"
same "status" '"In review"' "$(jq -c '.[2].status' "$OUT")"
same "api calls" 1 "$(grep -c '' <"$CALLS")"

case_ "overview: each card's last run, from the run record, whichever role it was"
rm -f "$A_TEAM_STATE/history.db"
history_db "INSERT INTO runs (id, team, role, pid, log, started) VALUES
    (1, 'demo', 'lead', 1, 'old.jsonl', '2026-10-01T08:00:00Z'), (2, 'demo', 'lead', 2, 'new.jsonl', '2026-10-02T08:00:00Z'),
    (3, 'demo', 'dev', 3, 'dev.jsonl', '2026-10-01T09:00:00Z'), (4, 'other', 'lead', 4, 'other.jsonl', '2026-10-03T08:00:00Z');
  INSERT INTO run_items (run, item) VALUES (1, 404), (2, 404), (3, 414), (4, 415);"
run board demo overview
same "exit" 0 "$STATUS"
same "last runs" '[{"role":"lead","log":"new.jsonl","started":"2026-10-02T08:00:00Z"},{"role":"dev","log":"dev.jsonl","started":"2026-10-01T09:00:00Z"},null,null]' \
  "$(jq -c '[.[].lastRun]' "$OUT")"
rm -f "$A_TEAM_STATE/history.db"

case_ "trend: waiting a week ago, and each item accepted in the last 7 days counted once"
rm -f "$A_TEAM_STATE/history.db"
since=$(ago $((60 * 24 * 9)))
sqlite3 "$A_TEAM_STATE/history.db" "CREATE TABLE events (id INTEGER PRIMARY KEY, team TEXT NOT NULL, item INTEGER NOT NULL,
    at TEXT NOT NULL, who TEXT NOT NULL, what TEXT NOT NULL);
  CREATE TABLE queue (team TEXT NOT NULL, at TEXT NOT NULL, waiting INTEGER NOT NULL);
  INSERT INTO queue VALUES ('demo', '$since', 20), ('demo', '$(ago $((60 * 24 * 7 + 120)))', 12),
    ('demo', '$(ago $((60 * 24 * 6)))', 9), ('other', '$(ago $((60 * 24 * 7 + 60)))', 30);
  INSERT INTO events (team, item, at, who, what) VALUES
    ('demo', 8, '$(ago 50)', 'you', 'accepted · PR #908 merged'), ('demo', 908, '$(ago 50)', 'you', 'accepted · PR #908 merged'),
    ('demo', 7, '$(ago 40)', 'you', 'accepted · closed'), ('demo', 7, '$(ago 30)', 'you', 'accepted · closed'),
    ('demo', 911, '$(ago 20)', 'you', 'accepted · PR #911 merged'),
    ('demo', 9, '$(ago $((60 * 24 * 8)))', 'you', 'accepted · PR #909 merged'),
    ('demo', 10, '$(ago 10)', 'octocat', 'accepted · PR #910 merged'),
    ('demo', 11, '$(ago 10)', 'you', 'Pitched → Approved'),
    ('other', 12, '$(ago 10)', 'you', 'accepted · closed');"
same "trend" "{\"since\":\"$since\",\"weekAgo\":12,\"accepted\":3,\"spentSince\":null,\"spent\":0}" "$(trend)"

case_ "trend leaves a week ago out until the record reaches back a week, and a stale one is no week ago"
sqlite3 "$A_TEAM_STATE/history.db" "DELETE FROM queue WHERE at <= '$(ago $((60 * 24 * 7)))'"
same "partial" null "$(trend | jq .weekAgo)"
sqlite3 "$A_TEAM_STATE/history.db" "INSERT INTO queue VALUES ('demo', '$(ago $((60 * 24 * 8 + 60)))', 20)"
same "stale" null "$(trend | jq .weekAgo)"

case_ "moves: with nothing recorded, none, and no record file"
rm -f "$A_TEAM_STATE/history.db"
run board demo moves 2026-10-11T09:00:00Z
same "exit" 0 "$STATUS"
same "none" '[]' "$(jq -c . "$OUT")"
unrecorded "moves"

case_ "moves: the team's events from a time on, oldest first, and nothing before it"
sqlite3 "$A_TEAM_STATE/history.db" "CREATE TABLE events (id INTEGER PRIMARY KEY, team TEXT NOT NULL, item INTEGER NOT NULL,
    at TEXT NOT NULL, who TEXT NOT NULL, what TEXT NOT NULL);
  INSERT INTO events (team, item, at, who, what) VALUES
    ('demo', 7, '2026-10-11T08:59:59Z', 'lead', 'Exploring → Pitched'),
    ('demo', 8, '2026-10-11T09:05:00Z', 'dev', 'In progress → In review'),
    ('demo', 9, '2026-10-11T09:01:00Z', 'dev', 'In progress → Ready'),
    ('demo', 9, '2026-10-11T09:01:00Z', 'dev', 'handed back with a question'),
    ('other', 10, '2026-10-11T09:02:00Z', 'lead', 'Exploring → Pitched');"
run board demo moves 2026-10-11T09:00:00Z
same "exit" 0 "$STATUS"
same "moves" '[[9,"dev","In progress → Ready"],[9,"dev","handed back with a question"],[8,"dev","In progress → In review"]]' \
  "$(jq -c 'map([.item, .who, .what])' "$OUT")"
same "at" '"2026-10-11T09:01:00Z"' "$(jq -c '.[0].at' "$OUT")"

case_ "moves refuses a time it can't compare"
run board demo moves yesterday
failed "moves"

trends() { A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board demo trends | jq -c .; }

case_ "with nothing recorded, trends has no start, no points and no cost, and makes no record file"
rm -f "$A_TEAM_STATE/history.db"
same "empty" '{"since":null,"queue":[],"accepted":[],"cycles":[],"cost":0,"spentSince":null,"spent":[],"touches":[],"finished":[]}' "$(trends)"
unrecorded "trends"

case_ "trends: the last 15 days of the queue, each item accepted once at its latest, and runs' cost in 7 days"
rm -f "$A_TEAM_STATE/history.db"
since=$(ago $((60 * 24 * 20))) days3=$(ago $((60 * 24 * 3))) m90=$(ago 90) m50=$(ago 50) m30=$(ago 30)
sqlite3 "$A_TEAM_STATE/history.db" "CREATE TABLE events (id INTEGER PRIMARY KEY, team TEXT NOT NULL, item INTEGER NOT NULL,
    at TEXT NOT NULL, who TEXT NOT NULL, what TEXT NOT NULL);
  CREATE TABLE queue (team TEXT NOT NULL, at TEXT NOT NULL, waiting INTEGER NOT NULL);
  CREATE TABLE runs (id INTEGER PRIMARY KEY, team TEXT NOT NULL, role TEXT NOT NULL,
    pid INTEGER NOT NULL, log TEXT NOT NULL, started TEXT NOT NULL, ended TEXT, cost REAL, outcome TEXT);
  INSERT INTO queue VALUES ('demo', '$(ago $((60 * 24 * 16)))', 20), ('demo', '$days3', 12),
    ('demo', '$m90', 9), ('other', '$(ago 60)', 30);
  INSERT INTO events (team, item, at, who, what) VALUES
    ('demo', 8, '$m50', 'you', 'accepted · PR #908 merged'), ('demo', 908, '$m50', 'you', 'accepted · PR #908 merged'),
    ('demo', 7, '$(ago 40)', 'you', 'accepted · closed'), ('demo', 7, '$m30', 'you', 'accepted · closed'),
    ('demo', 9, '$since', 'you', 'accepted · PR #909 merged'),
    ('demo', 10, '$(ago 10)', 'octocat', 'accepted · PR #910 merged'),
    ('other', 12, '$(ago 10)', 'you', 'accepted · closed');
  INSERT INTO runs (team, role, pid, log, started, cost) VALUES ('demo', 'dev', 1, 'a', '$(ago 100)', 1.25),
    ('demo', 'lead', 2, 'b', '$(ago 200)', NULL), ('demo', 'dev', 3, 'c', '$(ago $((60 * 24 * 8)))', 9),
    ('other', 'dev', 4, 'd', '$(ago 100)', 4);"
same "trends" "{\"since\":\"$since\",\"queue\":[{\"at\":\"$days3\",\"waiting\":12},{\"at\":\"$m90\",\"waiting\":9}],\"accepted\":[\"$m50\",\"$m30\"],\"cycles\":[],\"cost\":1.25,\"spentSince\":null,\"spent\":[],\"touches\":[],\"finished\":[]}" "$(trends)"

case_ "trends: a task's cycle runs from first entering Ready to its acceptance, with every spell In review counted"
base=$(date +%s); before() { jq -rn --argjson t "$base" --argjson m "$1" '$t - $m * 60 | strftime("%Y-%m-%dT%H:%M:%SZ")'; }
m600=$(before 600) m480=$(before 480) m420=$(before 420) m300=$(before 300) m120=$(before 120)
sqlite3 "$A_TEAM_STATE/history.db" "INSERT INTO events (team, item, at, who, what) VALUES
    ('demo', 20, '2026-10-01T00:00:00Z', 'lead', 'Idea → Exploring'),
    ('demo', 20, '$m600', 'lead', 'added as Ready'), ('demo', 20, '$(ago 540)', 'dev', 'Ready → In progress'),
    ('demo', 20, '$m480', 'dev', 'In progress → In review'), ('demo', 20, '$m420', 'you', 'In review → Ready'),
    ('demo', 20, '$(ago 360)', 'dev', 'Ready → In progress'), ('demo', 20, '$m300', 'dev', 'In progress → In review'),
    ('demo', 20, '$m120', 'you', 'accepted · PR #920 merged'), ('demo', 920, '$m120', 'you', 'accepted · PR #920 merged'),
    ('demo', 20, '$(ago 100)', 'you', 'commented'),
    ('demo', 21, '$(ago 300)', 'dev', 'Ready → In progress'), ('demo', 21, '$(ago 240)', 'lead', 'In progress → Ready'),
    ('demo', 21, '$(ago 200)', 'dev', 'Ready → In progress'), ('demo', 21, '$(ago 100)', 'dev', 'In progress → In review'),
    ('demo', 21, '$(ago 60)', 'you', 'accepted · PR #921 merged'),
    ('demo', 22, '$(ago 300)', 'lead', 'Exploring → Pitched'), ('demo', 22, '$(ago 200)', 'you', 'accepted · closed');"
same "cycles" "[{\"ready\":\"$m600\",\"accepted\":\"$m120\",\"withYou\":$((60 * 60 + 180 * 60))}]" "$(trends | jq -c .cycles)"

with_you() { trends | jq -c --argjson n "$1" '[.cycles[] | select(.ready == $n) | .withYou / 60]'; }
jq '.wip.worktrees = 2' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
slots='31 700 dev Ready → In progress
32 650 dev Ready → In progress
30 600 lead added as Ready
31 500 dev In progress → In review
32 450 dev In progress → In review
31 300 you accepted · PR #931 merged
30 200 dev Ready → In progress
30 150 dev In progress → In review
30 100 you accepted · PR #930 merged'

case_ "trends: a Ready task doesn't wait on you while the Dev's slots are full of PRs In review"
events <<<"$slots"
same "slot wait" "[50]" "$(with_you "\"$(before 600)\"")"

case_ "trends: a task handed back with a question waits on you until the answer"
events <<'EVENTS'
40 600 lead added as Ready
40 550 dev Ready → In progress
40 500 dev In progress → Ready
40 500 dev handed back with a question
40 420 dev question answered
40 410 dev Ready → In progress
40 400 dev In progress → In review
40 300 you accepted · PR #940 merged
EVENTS
same "question" "[$((80 + 100))]" "$(with_you "\"$(before 600)\"")"

touches_of() { trends | jq -c '[.touches[].touches]'; }
# boarded <n> <content> [<marks>]: item n on the board, once trends has made the record's tables.
boarded() {
  local marks=${3:+"'$3'"}
  trends >/dev/null
  sqlite3 "$A_TEAM_STATE/history.db" "INSERT INTO board (team, item, node, status, since, content, marks)
    VALUES ('demo', $1, 'N$1', 'Done', '2026-10-01T00:00:00Z', '$2', ${marks:-NULL});"
}
parents() {
  local n
  for n in "$@"; do boarded "${n#*:}" "{\"type\": \"Issue\", \"parent\": ${n%:*}}"; done
}
under_pitch() {
  local n
  for n in "$@"; do parents "99:$n"; done
}

case_ "trends: touches count your comments on a task or its PR from review onwards, and only yours"
events <<'EOF'
1 900 dev In progress → In review
1 800 you accepted · PR #101 merged
101 800 you accepted · PR #101 merged
2 900 dev In progress → In review
2 850 you commented
2 800 you accepted · PR #102 merged
102 800 you accepted · PR #102 merged
3 900 dev In progress → In review
103 850 you commented
3 800 you accepted · PR #103 merged
103 800 you accepted · PR #103 merged
4 900 dev In progress → In review
4 880 you commented
104 870 you commented
4 860 you commented
4 800 you accepted · PR #104 merged
104 800 you accepted · PR #104 merged
5 950 you commented
105 940 you commented
5 900 dev In progress → In review
5 800 you accepted · PR #105 merged
105 800 you accepted · PR #105 merged
6 900 dev In progress → In review
6 850 lead commented
6 845 dev commented
106 840 octocat commented
6 800 you accepted · PR #106 merged
106 800 you accepted · PR #106 merged
EOF
under_pitch 1 2 3 4 5 6
same "none, issue, PR, three, before review, someone else's" "[0,1,1,3,0,0]" "$(touches_of)"

case_ "trends: touches count from the first time it reached review, even after it was sent back"
events <<'EOF'
1 900 dev In progress → In review
1 880 you In review → Ready
1 870 you commented
1 860 dev Ready → In progress
1 850 dev In progress → In review
1 800 you accepted · PR #101 merged
EOF
under_pitch 1
same "the spell out of review counts" "[1]" "$(touches_of)"

case_ "trends: a task accepted over a fortnight ago isn't reported"
events <<EOF
1 $((60 * 24 * 16)) you accepted · PR #101 merged
EOF
under_pitch 1
same "too old" "[]" "$(touches_of)"

case_ "trends: touches count a task or a fix once, and no bump, docs PR, PR of your own or closed issue"
events <<'EOF'
1 800 you accepted · PR #101 merged
101 800 you accepted · PR #101 merged
2 800 you accepted · PR #102 merged
102 800 you accepted · PR #102 merged
3 850 you commented
3 800 you accepted · closed
3 800 you accepted · PR #103 merged
103 840 you commented
103 800 you accepted · PR #103 merged
104 800 you accepted · PR #104 merged
105 800 you accepted · PR #105 merged
106 800 you accepted · PR #106 merged
107 800 you accepted · PR #107 merged
8 800 you accepted · closed
EOF
under_pitch 1 3
boarded 2 '{"type": "Issue"}' '{"dev":true,"fix":true}'
boarded 104 '{"type": "PullRequest", "dependabot": true}'
boarded 105 '{"type": "PullRequest", "dependabot": true}' '{"dev":true}'
boarded 106 '{"type": "PullRequest"}' '{"customer":true}'
boarded 8 '{"type": "Issue"}'
same "task, fix, task accepted both ways" "[0,0,2]" "$(touches_of)"

finished_of() { trends | jq -c '[.finished[].untouched]'; }

case_ "trends: a pitch is untouched with no comment while Pitched and none on its tasks or their PRs"
{
  pitch 1; printf '%s\n' "11 500 you accepted · PR #111 merged" "111 500 you accepted · PR #111 merged"
} | events
parents 1:11
same "none" "[true]" "$(finished_of)"

case_ "trends: a comment while Pitched touches a pitch, in any spell, but not one before or after"
{
  pitch 1; echo "1 850 you commented"
  pitch 2; echo "2 950 you commented"; echo "2 700 you commented"
} | events
same "while Pitched, and outside it" "[false,true]" "$(finished_of)"

case_ "trends: a comment on a task touches its pitch, even from before review, as does one on the task's PR"
{
  pitch 1; echo "11 600 you commented"
  pitch 2; printf '%s\n' "121 600 you commented" "12 500 you accepted · PR #121 merged"
  pitch 3; printf '%s\n' "13 600 lead commented" "13 500 you accepted · PR #131 merged"
} | events
parents 1:11 2:12 3:13
same "task, PR, and the Lead's" "[false,false,true]" "$(finished_of)"

case_ "trends: a pitch built by PRs with no tasks is touched by a comment on one, and one never approved isn't untouched"
{
  printf '%s\n' "1 900 lead Exploring → Pitched" "1 800 you Pitched → Approved" "1 100 lead closed as done · built by #532" "532 500 you commented"
  echo "2 100 lead closed as done"
} | events
same "built by, unapproved" "[false,false]" "$(finished_of)"

case_ "spent records your time on a card, or between cards, for History, trend and trends"
rm -f "$A_TEAM_STATE/history.db"
d9=$(ago $((60 * 24 * 9))) d16=$(ago $((60 * 24 * 16))) m90=$(ago 90) m30=$(ago 30)
for visit in "7 $d16 600 Pitches read" "7 $d9 120 Pitches commented" "8 $m90 372 Review tried,_accepted" "- $m30 300 Other _"; do
  read -r n at seconds gate what <<<"${visit//_/ }"
  run board demo spent you "$n" "$at" "$seconds" "$gate" "${what# }"
  same "exit $n $gate" 0 "$STATUS"
done
cp "$TEAM" "$CONFIG/teams/other.json"
A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board other spent you 8 "$m30" 60 Review read >/dev/null
run board demo history 8
same "history" "[{\"at\":\"$m90\",\"who\":\"you\",\"what\":\"tried, accepted\",\"spent\":372}]" "$(jq -c .events "$OUT")"
same "trend" '{"spentSince":"'"$d16"'","spent":672}' "$(trend | jq -c '{spentSince, spent}')"
same "trends" '{"spentSince":"'"$d16"'","spent":[{"at":"'"$d9"'","seconds":120,"gate":"Pitches"},{"at":"'"$m90"'","seconds":372,"gate":"Review"},{"at":"'"$m30"'","seconds":300,"gate":"Other"}]}' \
  "$(trends | jq -c '{spentSince, spent}')"

case_ "only your own time is recorded, at a gate, and a dry run records none"
rm -f "$A_TEAM_STATE/history.db"
for args in "lead 7 $m30 60 Review read" "you x $m30 60 Review read" "you 7 yesterday 60 Review read" \
  "you 7 $m30 1m Review read" "you 7 $m30 60 Done read"; do
  read -r -a words <<<"$args"
  run board demo spent "${words[@]}"
  failed "spent ${words[*]}"
  one_line "spent ${words[*]}"
done
run board --dry-run demo spent you 7 "$m30" 60 Review read
same "dry run" 0 "$STATUS"
unrecorded "dry run"


case_ "decisions with nothing recorded is empty, with no record file to read"
rm -f "$A_TEAM_STATE/history.db"
run board demo decisions
same "exit" 0 "$STATUS"
same "empty" '[]' "$(cat "$OUT")"
unrecorded "decisions"

case_ "decide records an entry for this team or all teams, newest-confirmed first, and words narrow it"
said "Card commands go in the Cards menu, not the hint bar" "" "The menu and the palette advertise them."
run board demo decide lead you lean all 34 "$WORK/decision" --said 2026-09-26
same "exit" 0 "$STATUS"
same "said" "decision 1 recorded" "$(cat "$OUT")"
said "Ctrl+PgDn switches teams"
run board demo decide lead you rule team https://github.com/mentaldesk/demo/issues/356#issuecomment-9 "$WORK/decision" --said 2026-10-09
same "exit" 0 "$STATUS"
cp "$TEAM" "$CONFIG/teams/other.json"
said "Other's own call"
run board other decide lead lead lean team 7 "$WORK/decision" --said 2026-09-01
same "exit" 0 "$STATUS"
run board demo decisions
same "first" '{"id":2,"team":"demo","scope":"team","decision":"Ctrl+PgDn switches teams","why":"","who":"you","kind":"rule","item":356,"link":"https://github.com/mentaldesk/demo/issues/356#issuecomment-9","confirmed":"2026-10-09T00:00:00Z","confirmedOn":null,"replacedBy":null,"retired":null,"yours":null}' \
  "$(jq -c '.[0] | del(.said, .recorded)' "$OUT")"
same "why" '"The menu and the palette advertise them."' "$(jq -c '.[1].why' "$OUT")"
same "link" '"https://github.com/mentaldesk/demo/issues/34"' "$(jq -c '.[1].link' "$OUT")"
same "other team sees all-teams" '[1,3]' "$(ids other)"
same "search" '[1]' "$(ids demo hint card)"
same "search by prefix" '[2]' "$(ids demo switch)"
same "no match" '[]' "$(ids demo nothing)"
same "in history" '"lead recorded decision 1"' "$(A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board demo history 34 | jq -c '.events[0] | "\(.who) \(.what)"')"

case_ "decide refuses another role, a bad kind, scope or place, and an empty decision"
for args in "dev you lean team 1" "you you lean team 1" "lead them lean team 1" "lead you law team 1" \
  "lead you lean world 1" "lead you lean team x" "lead you lean team https://github.com/mentaldesk/elsewhere/issues/1"; do
  read -r -a words <<<"$args"
  run board demo decide "${words[@]}" "$WORK/decision"
  failed "decide $args"
  one_line "decide $args"
done
said "" "  "
run board demo decide lead you lean team 1 "$WORK/decision"
failed "empty"
same "unchanged" '[2,1]' "$(ids)"

case_ "the latest word replaces an entry, which only --replaced still lists, and says what it replaced"
said "Card commands show in the hint bar too"
run board demo decide lead you lean all 40 "$WORK/decision" --replaces 1 --said 2026-10-01
same "exit" 0 "$STATUS"
same "said" "decision 4 recorded; it replaces decision 1, what you said on #34 on 26 Sep" "$(cat "$OUT")"
same "live" '[2,4]' "$(ids)"
same "replaced" '[2,4,1]' "$(ids demo --replaced)"
same "linked" 4 "$(A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board demo decisions --replaced | jq '.[] | select(.id == 1) | .replacedBy')"
run board demo decide lead you lean all 41 "$WORK/decision" --replaces 1
failed "replacing a replaced entry"
one_line "replacing a replaced entry"

case_ "replace marks one live entry replaced by a newer one, and reconfirm dates what a pitch cited"
said "Hint bars follow TuiCode"
run board demo decide lead lead lean team 50 "$WORK/decision" --said 2026-10-12
run board demo replace lead 5 2
failed "replacing with an older entry"
one_line "replacing with an older entry"
run board demo replace lead 2 5
same "exit" 0 "$STATUS"
same "live" '[5,4]' "$(ids)"
run board demo replace lead 2 5
failed "already replaced"
run board demo reconfirm lead 60 4
same "exit" 0 "$STATUS"
same "reconfirmed" '[4,5]' "$(ids)"
same "on" 60 "$(A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board demo decisions | jq '.[0].confirmedOn')"
run board demo reconfirm lead 60 4 2
failed "reconfirming a replaced entry"
run board demo reconfirm dev 60 4
failed "dev reconfirming"

case_ "you edit, rule/lean, widen and retire an entry, each one undone the same way"
printf 'Pane hints mirror TuiCode, colours included\n' >"$WORK/wording"
run board demo decision you 5 edit "$WORK/wording"
same "exit" 0 "$STATUS"
same "search finds the new wording" '[5]' "$(ids demo colours)"
same "nor the old" '[]' "$(ids demo follow)"
run board demo decision you 5 kind
same "rule" '"rule"' "$(A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board demo decisions | jq '.[] | select(.id == 5) | .kind')"
run board demo decision you 5 widen
same "other sees it" '[4,5,3]' "$(ids other)"
run board other decision you 5 widen
failed "narrowing another team's entry"
run board demo decision you 5 widen
same "narrowed" '[4,3]' "$(ids other)"
run board demo decision you 5 retire
same "retired" '[4]' "$(ids)"
same "still there" true "$(A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board demo decisions --replaced | jq '.[] | select(.id == 5) | .retired != null')"
run board demo decision you 5 retire
same "back" '[4,5]' "$(ids)"
for args in "lead 5 retire" "dev 5 kind" "you 99 kind" "you 5 delete" "you 5 edit"; do
  read -r -a words <<<"$args"
  run board demo decision "${words[@]}"
  failed "decision $args"
  one_line "decision $args"
done
printf 'one\ntwo\n' >"$WORK/wording"
run board demo decision you 5 edit "$WORK/wording"
failed "two lines"

case_ "an entry you changed refuses the Lead's overwrite, but not your own newer word"
said "Hint bars don't follow TuiCode"
run board demo decide lead lead lean team 70 "$WORK/decision" --replaces 5
failed "lead overwriting yours"
one_line "lead overwriting yours"
run board demo decide lead lead lean team 70 "$WORK/decision"
run board demo replace lead 5 6
failed "lead replacing yours"
same "yours stands" '[6,4,5]' "$(ids)"
run board demo decide lead you lean team 71 "$WORK/decision" --replaces 5
same "your word" 0 "$STATUS"

case_ "one team's entry doesn't replace an all-teams one"
said "This team's cards keep their commands in the hint bar"
run board demo decide lead you lean team 72 "$WORK/decision" --replaces 4
failed "team entry replacing all-teams"
one_line "team entry replacing all-teams"
run board demo replace lead 4 6
failed "replace all-teams with team"
one_line "replace all-teams with team"
same "other keeps it" '[4,3]' "$(ids other)"

case_ "a dry run of every decision write changes nothing"
before=$(A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board demo decisions --replaced)
said "Dry"
for args in "decide lead you lean all 80 $WORK/decision --replaces 4" "replace lead 6 7" "reconfirm lead 80 4" \
  "decision you 4 kind" "decision you 4 widen" "decision you 4 retire" "decision you 4 edit $WORK/decision"; do
  read -r -a words <<<"$args"
  run board --dry-run demo "${words[@]}"
  same "dry run $args" 0 "$STATUS"
  grep -q "would" "$ERR" || fail "dry run $args: nothing about the change in '$(cat "$ERR")'"
done
same "unchanged" "$before" "$(A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board demo decisions --replaced)"
rm -f "$A_TEAM_STATE/history.db"
run board --dry-run demo decide lead you lean team 80 "$WORK/decision"
same "dry run with no record" 0 "$STATUS"
unrecorded "dry run decide"

case_ "lead-next says seed until the Lead has seeded the register once"
jq '.wip = {pitched: 3, exploring: 4, ideas: 4}' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
gh_items <<'ITEMS'
Idea 87 An idea
ITEMS
rm -rf "$A_TEAM_STATE/demo/lead/seeded"
run board demo lead-next
same "seed" true "$(jq .seed "$OUT")"
run board --dry-run demo seeded lead
same "dry run" 0 "$STATUS"
run board demo lead-next
same "still seed after a dry run" true "$(jq .seed "$OUT")"
run board demo seeded dev
failed "dev seeding"
run board demo seeded lead
same "exit" 0 "$STATUS"
run board demo lead-next
same "seeded" false "$(jq .seed "$OUT")"
rm -f "$CONFIG/teams/other.json"
unset A_TEAM_STATE

# install.sh against a HOME and state of its own, with launchctl and the tools it checks for stubbed.
install_dispatcher() {
  HOME="$INSTALL_HOME" A_TEAM_STATE="$INSTALL_STATE" A_TEAM_BIN="$INSTALL_STUBS/a-team" PATH="$INSTALL_STUBS:$PATH" \
    bash "$ROOT/scripts/install.sh" "$@" >"$OUT" 2>"$ERR"
  STATUS=$?
}
INSTALL_HOME=$(mktemp -d "$WORK/home.XXXXXX") INSTALL_STATE=$(mktemp -d "$WORK/state.XXXXXX")
INSTALL_STUBS=$(mktemp -d "$WORK/stubs.XXXXXX")
for tool in claude gh; do printf '#!/usr/bin/env bash\n' | fake "$INSTALL_STUBS/$tool"; done
# A job bootout stops stays loaded for $LAUNCHCTL_LINGER more prints, and can't be bootstrapped until it's gone.
fake "$INSTALL_STUBS/launchctl" <<SH
#!/usr/bin/env bash
left=\$(cat "$INSTALL_STUBS/lingering" 2>/dev/null || echo 0)
case \$1 in
  bootout) echo "\${LAUNCHCTL_LINGER:-0}" >"$INSTALL_STUBS/lingering" ;;
  print) [ "\$left" -gt 0 ] && echo \$((left - 1)) >"$INSTALL_STUBS/lingering" ;;
  bootstrap) [ "\$left" -eq 0 ] || { echo "Bootstrap failed: 5: Input/output error" >&2; exit 5; } ;;
esac
SH
printf '#!/usr/bin/env bash\n[ "$1" = version ] && echo 0.1.13-alpha.0.7\n' | fake "$INSTALL_STUBS/a-team"

case_ "install records the binary, its version, the interval and the log beside the state"
install_dispatcher
same "exit" 0 "$STATUS"
same "record" "{\"bin\":\"$INSTALL_STUBS/a-team\",\"version\":\"0.1.13-alpha.0.7\",\"dryRun\":false,\"interval\":120,\"log\":\"$INSTALL_STATE/launchd.log\"}" \
  "$(jq -c 'del(.installedAt)' "$INSTALL_STATE/dispatcher.json")"
[ $(($(date +%s) - $(jq .installedAt "$INSTALL_STATE/dispatcher.json"))) -le 5 ] || fail "installedAt: $(jq .installedAt "$INSTALL_STATE/dispatcher.json")"

case_ "install waits for the dispatcher it replaces to stop before loading it again"
LAUNCHCTL_LINGER=3 install_dispatcher
same "exit" 0 "$STATUS"
same "stderr" "" "$(cat "$ERR")"

case_ "install --dry-run says so in the record"
install_dispatcher --dry-run
same "dry run" true "$(jq .dryRun "$INSTALL_STATE/dispatcher.json")"

case_ "install --uninstall removes the record"
install_dispatcher --uninstall
same "exit" 0 "$STATUS"
[ -e "$INSTALL_STATE/dispatcher.json" ] && fail "uninstall: the record is still there"

case_ "a dry-run pass says when the next one is due, without claiming a live dispatcher's next-pass"
A_TEAM_CONFIG=$(mktemp -d "$WORK/config.XXXXXX") A_TEAM_STATE="$INSTALL_STATE" bash "$ROOT/scripts/dispatch.sh" --dry-run
[ -f "$INSTALL_STATE/dry-next-pass" ] || fail "dry pass: no dry-next-pass"
[ -e "$INSTALL_STATE/next-pass" ] && fail "dry pass: wrote next-pass"

case_ "a pass run off schedule leaves the scheduled pass's countdown alone"
echo 1800000000 >"$INSTALL_STATE/next-pass"
A_TEAM_UNSCHEDULED=1 A_TEAM_CONFIG=$(mktemp -d "$WORK/config.XXXXXX") A_TEAM_STATE="$INSTALL_STATE" bash "$ROOT/scripts/dispatch.sh"
same "next-pass" 1800000000 "$(cat "$INSTALL_STATE/next-pass")"
rm "$INSTALL_STATE/next-pass"

# A pass as launchd runs the dispatcher install_dispatcher installed, from $PASS_BIN if that's set.
installed_pass() {
  HOME="$INSTALL_HOME" A_TEAM_STATE="$INSTALL_STATE" A_TEAM_BIN="${PASS_BIN:-$INSTALL_STUBS/a-team}" \
    A_TEAM_CONFIG=$(mktemp -d "$WORK/config.XXXXXX") bash "$ROOT/scripts/dispatch.sh" "$@"
}
in_record() { jq -r "$1" "$INSTALL_STATE/dispatcher.json"; }
record_version() { jq ".version = \"$1\" | .installedAt = 1700000000" "$INSTALL_STATE/dispatcher.json" >"$WORK/record" &&
  mv "$WORK/record" "$INSTALL_STATE/dispatcher.json"; }

case_ "launchd's pass writes the record for a dispatcher installed before there was one"
install_dispatcher
rm "$INSTALL_STATE/dispatcher.json"
installed_pass
same "record" "{\"bin\":\"$INSTALL_STUBS/a-team\",\"version\":\"0.1.13-alpha.0.7\",\"dryRun\":false,\"interval\":120,\"log\":\"$INSTALL_STATE/launchd.log\"}" \
  "$(jq -c 'del(.installedAt)' "$INSTALL_STATE/dispatcher.json")"

case_ "launchd's pass brings the record's version up to date, and keeps when it was installed"
record_version 0.1.12
installed_pass
same "version" 0.1.13-alpha.0.7 "$(in_record .version)"
same "installedAt" 1700000000 "$(in_record .installedAt)"

case_ "a pass launchd doesn't run leaves the record alone"
record_version 0.1.12
PASS_BIN=/elsewhere/bin/a-team installed_pass
same "another binary" 0.1.12 "$(in_record .version)"
installed_pass --dry-run
same "another mode" 0.1.12 "$(in_record .version)"
A_TEAM_UNSCHEDULED=1 installed_pass
same "off schedule" 0.1.12 "$(in_record .version)"
install_dispatcher --uninstall
installed_pass
[ -e "$INSTALL_STATE/dispatcher.json" ] && fail "uninstalled: a pass wrote the record"
rm -f "$INSTALL_STATE"/*next-pass

# status.sh's first line, for a state directory holding $1 as dispatcher.json (or none), next-pass $2 seconds from now
# (with prefix $3) and a pass in progress as pid $4.
dispatcher_line() {
  local state
  state=$(mktemp -d "$WORK/state.XXXXXX")
  [ -n "$1" ] && echo "$1" >"$state/dispatcher.json"
  [ -n "${2:-}" ] && echo $(($(date +%s) + $2)) >"$state/${3:-}next-pass"
  [ -n "${4:-}" ] && echo "$4" >"$state/${3:-}pass"
  A_TEAM_CONFIG=$(mktemp -d "$WORK/config.XXXXXX") A_TEAM_STATE="$state" HOME=/Users/me bash "$ROOT/scripts/status.sh" | head -n 1
}

case_ "status opens with what's driving the teams"
counting_down "live" "dispatcher · ~/code/a-team/palette/bin/a-team 0.1.13 · next pass" 72 "$(installed false 600)"
counting_down "dry run" "dispatcher · dry run: nothing will actually start · next pass" 48 "$(installed true 600)" dry-
same "stopped" "dispatcher · stopped 14m ago" "$(dispatcher_line "$(installed false 3600)" $((120 - 14 * 60)))"
same "never installed" "dispatcher · nothing installed · run: a-team install" "$(dispatcher_line '')"
same "unrecorded" "dispatcher" "$(dispatcher_line '' 30)"
same "unrecorded, stale" "dispatcher · nothing installed · run: a-team install" "$(dispatcher_line '' -600)"

case_ "status counts an overdue pass as live while its process is, and stopped once it's gone"
same "busy pass" "dispatcher · ~/code/a-team/palette/bin/a-team 0.1.13 · next pass 0:00" \
  "$(dispatcher_line "$(installed false 3600)" -180 '' $$)"
same "gone" "dispatcher · stopped 5m ago" "$(dispatcher_line "$(installed false 3600)" -180 '' 999999)"
same "unrecorded, busy" "dispatcher" "$(dispatcher_line '' -180 '' $$)"

case_ "a scheduled pass marks itself in progress with its pid, and clears that when it ends"
PASS_STATE=$(mktemp -d "$WORK/state.XXXXXX") PASS_CONFIG=$(mktemp -d "$WORK/config.XXXXXX")
PASS_STUBS=$(mktemp -d "$WORK/stubs.XXXXXX")
mkdir -p "$PASS_CONFIG/teams"
echo '{}' >"$PASS_CONFIG/teams/t.json"
printf '#!/usr/bin/env bash\ncat "%s/pass" >"%s/seen"\nexec %s "$@"\n' "$PASS_STATE" "$PASS_STUBS" "$(command -v jq)" | fake "$PASS_STUBS/jq"
PATH="$PASS_STUBS:$PATH" A_TEAM_CONFIG="$PASS_CONFIG" A_TEAM_STATE="$PASS_STATE" bash "$ROOT/scripts/dispatch.sh" &
pass_pid=$!
wait "$pass_pid"
same "pid during the pass" "$pass_pid" "$(cat "$PASS_STUBS/seen" 2>/dev/null)"
[ -e "$PASS_STATE/pass" ] && fail "pass: still marked in progress after it ended"

case_ "a scheduled pass counts the next one down from when it ends, as launchd does"
echo 1800000000 >"$PASS_STATE/next-pass"
printf '#!/usr/bin/env bash\ncat "%s/next-pass" >"%s/seen"\nexec %s "$@"\n' "$PASS_STATE" "$PASS_STUBS" "$(command -v jq)" | fake "$PASS_STUBS/jq"
PATH="$PASS_STUBS:$PATH" A_TEAM_CONFIG="$PASS_CONFIG" A_TEAM_STATE="$PASS_STATE" bash "$ROOT/scripts/dispatch.sh"
ended=$(date +%s)
same "next-pass during the pass" 1800000000 "$(cat "$PASS_STUBS/seen" 2>/dev/null)"
left=$(($(cat "$PASS_STATE/next-pass") - ended))
[ "$left" -ge 118 ] && [ "$left" -le 120 ] || fail "next-pass after the pass: due in ${left}s, not 120s"

# overlap_first <unscheduled>: starts a pass that stays in its first jq until $OVERLAP/go appears; its pid in OVERLAP_PID.
overlap_first() {
  OVERLAP=$(mktemp -d "$WORK/stubs.XXXXXX")
  printf '#!/usr/bin/env bash\ntouch "%s/started"\nuntil [ -e "%s/go" ]; do [ "$SECONDS" -lt %s ] || exit; sleep 0.1; done\nexec %s "$@"\n' \
    "$OVERLAP" "$OVERLAP" "$FAKE_LIMIT" "$(command -v jq)" | fake "$OVERLAP/jq"
  PATH="$OVERLAP:$PATH" A_TEAM_UNSCHEDULED=$1 A_TEAM_CONFIG="$PASS_CONFIG" A_TEAM_STATE="$PASS_STATE" bash "$ROOT/scripts/dispatch.sh" &
  OVERLAP_PID=$!
  until [ -e "$OVERLAP/started" ]; do sleep 0.1; done
}

# overlap_second <unscheduled>: a second pass, with a jq saying if it ever got as far as a team.
overlap_second() {
  SECOND=$(mktemp -d "$WORK/stubs.XXXXXX")
  printf '#!/usr/bin/env bash\ntouch "%s/reached"\nexec %s "$@"\n' "$SECOND" "$(command -v jq)" | fake "$SECOND/jq"
  PATH="$SECOND:$PATH" A_TEAM_UNSCHEDULED=$1 A_TEAM_CONFIG="$PASS_CONFIG" A_TEAM_STATE="$PASS_STATE" bash "$ROOT/scripts/dispatch.sh" >"$OUT" 2>"$ERR"
  STATUS=$?
}

for first in '' 1; do
  for second in 1 ''; do
    case_ "a pass (unscheduled='$first') makes another (unscheduled='$second') stop before it reaches a team"
    rm -f "$PASS_STATE/next-pass"
    overlap_first "$first"
    overlap_second "$second"
    same "exit" 0 "$STATUS"
    same "said" "${second:+A pass is already running.}" "$(cat "$OUT")"
    [ ! -e "$SECOND/reached" ] || fail "overlap: the second pass reached a team"
    same "still the first's" "$OVERLAP_PID" "$(cat "$PASS_STATE/pass")"
    kill -0 "$OVERLAP_PID" || fail "overlap: the first pass stopped"
    if [ -z "$second" ]; then
      [ -e "$PASS_STATE/next-pass" ] || fail "overlap: launchd's countdown restarted with no next-pass"
    else
      [ ! -e "$PASS_STATE/next-pass" ] || fail "overlap: an off-schedule pass wrote next-pass"
    fi
    touch "$OVERLAP/go"
    wait "$OVERLAP_PID"
    [ ! -e "$PASS_STATE/pass" ] || fail "overlap: the first didn't clear its record"
  done
done

case_ "a killed pass doesn't block the next, in either order"
for next in '' 1; do
  overlap_first ''
  kill -9 "$OVERLAP_PID"
  wait "$OVERLAP_PID" 2>/dev/null
  touch "$OVERLAP/go"
  same "its record" "$OVERLAP_PID" "$(cat "$PASS_STATE/pass")"
  overlap_second "$next"
  same "said" "" "$(cat "$OUT")"
  [ -e "$SECOND/reached" ] || fail "killed: the next pass didn't run"
  [ ! -e "$PASS_STATE/pass" ] || fail "killed: its record is still there"
done

case_ "a pid another process has since been given doesn't block a pass"
echo $$ >"$PASS_STATE/pass"
touch -t 200001010000 "$PASS_STATE/pass"
overlap_second ''
same "said" "" "$(cat "$OUT")"
[ -e "$SECOND/reached" ] || fail "reused pid: the pass didn't run"

# release.sh against a repo in $REL: repo.json the GraphQL view of its default branch and latest release, runs.json
# its latest release.yml run, and each workflow it was asked to run a line of `dispatched`.

finish_suite
