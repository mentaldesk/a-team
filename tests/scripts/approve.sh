#!/usr/bin/env bash
# Approve, decline and shelve.
# shellcheck source=tests/scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
# shellcheck disable=SC2034,SC2154 # helpers read what an area sets, and an area reads what lib.sh sets

case_ "approve moves a Pitched pitch to Approved on the project"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 } }
JSON
gh_items <<'ITEMS'
Pitched 7 A pitch in front of me
Exploring 8 A pitch still being drafted
Idea 9 An Idea of my own
ITEMS
run board demo approve you 7
same "exit" 0 "$STATUS"
same "said" "#7: Pitched -> Approved" "$(cat "$OUT")"
same "writes" 1 "$(grep -c '' <"$WRITES")"
grep -q "item=PVTI_7 " "$WRITES" || fail "approve: not the pitch's item in '$(cat "$WRITES")'"
grep -q "option=OPT_approved " "$WRITES" || fail "approve: not the Approved option in '$(cat "$WRITES")'"

case_ "--dry-run says what it would approve and approves nothing"
: >"$WRITES"
run board --dry-run demo approve you 7
same "exit" 0 "$STATUS"
same "writes" "" "$(cat "$WRITES")"
grep -q "would set #7 to 'Approved'" "$ERR" || fail "approve dry run: '$(cat "$ERR")'"

case_ "no Dev, Customer lead or Reviewer may approve a pitch"
for role in dev customer reviewer; do
  : >"$WRITES"
  run board demo approve "$role" 7
  failed "$role approving"
  one_line "$role approving"
  grep -q "$role may not approve a pitch; approving is the stakeholders' own gate, or the Lead's on their word" "$ERR" ||
    fail "$role approving: '$(cat "$ERR")'"
  same "$role writes" "" "$(cat "$WRITES")"
done

case_ "any other role is unknown"
run board demo approve nobody 7
failed "unknown role"
one_line "unknown role"
grep -q "unknown role 'nobody' (you | lead)" "$ERR" || fail "unknown role: '$(cat "$ERR")'"

case_ "approve is not a general move: only a pitch-labelled issue in Pitched"
for n in 8 9 404; do
  : >"$WRITES"
  run board demo approve you "$n"
  failed "approve #$n"
  one_line "approve #$n"
  same "approve #$n writes" "" "$(cat "$WRITES")"
done
grep -q "#404 is not on the board" "$ERR" || fail "approve off the board: '$(cat "$ERR")'"
run board demo approve you 8
grep -q "is in 'Exploring'" "$ERR" || fail "approve Exploring: '$(cat "$ERR")'"
run board demo approve you 9
grep -q "#9 is not a pitch" "$ERR" || fail "approve Idea: '$(cat "$ERR")'"
edit_item 7 '.labels.nodes = []'
run board demo approve you 7
failed "approve unlabelled"
grep -q "#7 is not a pitch" "$ERR" || fail "approve unlabelled: '$(cat "$ERR")'"
edit_item 7 '.labels.nodes = [{name: "pitch"}] | .__typename = "PullRequest"'
run board demo approve you 7
failed "approve a PR"
grep -q "#7 is not an issue" "$ERR" || fail "approve a PR: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"

case_ "the Lead approves only on a stakeholder's unanswered word, replying with its marker"
gh_items <<'ITEMS'
Pitched 7 A pitch in front of me
Exploring 8 A pitch still being drafted
ITEMS
gh_thread <<THREAD
body $(ago 600) demo-app[bot] 0 The pitch\n\n## Needs your answer\n\nNone.\n\n<!-- a-team:lead -->
comment $(ago 5) reviewer 1 Already answered.
THREAD
printf 'Approved on your word.\n' >"$WORK/reply"
run board demo approve lead 7 "$WORK/reply"
failed "nothing said"
one_line "nothing said"
grep -qF "lead may approve #7 only on a stakeholder's word" "$ERR" || fail "nothing said: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"
same "posted" "" "$(cat "$POSTED")"
SAID=$(ago 5)
gh_thread <<THREAD
body $(ago 600) demo-app[bot] 0 The pitch\n\n## Needs your answer\n\nNone.\n\n<!-- a-team:lead -->
comment $SAID reviewer 0 Let's build it.
THREAD
rm -f "$A_TEAM_STATE/history.db"
run board demo approve lead 7 "$WORK/reply"
same "exit" 0 "$STATUS"
same "said" "#7: Pitched -> Approved" "$(cat "$OUT")"
same "posted" "Approved on your word.

<!-- a-team:lead -->" "$(cat "$POSTED")"
grep -q "item=PVTI_7 .*option=OPT_approved " "$WRITES" || fail "not approved: '$(cat "$WRITES")'"
same "acked" "IC_1" "$(cat "$ACKED")"
when=$(jq -rn --arg at "$SAID" '$at | fromdateiso8601 | "\(strflocaltime("%d %b") | ltrimstr("0")) \(strflocaltime("%H:%M"))"')
same "history" "lead approved on reviewer's word of $when
lead Pitched → Approved" "$(A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board demo history 7 | jq -r '.events[] | "\(.who) \(.what)"')"

case_ "the Lead's approve needs a reply, and only a Pitched pitch that's an issue"
: >"$WRITES"
: >"$POSTED"
: >"$WORK/blank"
run board demo approve lead 7 "$WORK/blank"
failed "empty reply"
grep -qF "the Lead's approve is its reply" "$ERR" || fail "empty reply: '$(cat "$ERR")'"
run board demo approve lead 7
failed "no reply"
grep -qF "usage: board.sh demo approve lead <n> <file>" "$ERR" || fail "no reply: '$(cat "$ERR")'"
run board demo approve you 7 "$WORK/reply"
failed "your approve with a file"
run board demo approve lead 8 "$WORK/reply"
grep -qF "only a Pitched pitch can be approved (#8 is in 'Exploring')" "$ERR" || fail "a draft: '$(cat "$ERR")'"
edit_item 7 '.labels.nodes = [{name: "pitch"}] | .__typename = "PullRequest"'
run board demo approve lead 7 "$WORK/reply"
grep -q "#7 is not an issue" "$ERR" || fail "a document pitch: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"
same "posted" "" "$(cat "$POSTED")"

case_ "the Lead can't approve while Needs your answer still holds a question"
gh_items <<'ITEMS'
Pitched 7 A pitch in front of me
ITEMS
gh_thread <<THREAD
body $(ago 600) demo-app[bot] 0 The pitch\n\n## Needs your answer\n\n1. Which columns?\n\n<!-- a-team:lead -->
comment $(ago 5) reviewer 0 Go ahead.
THREAD
run board demo approve lead 7 "$WORK/reply"
failed "open question"
one_line "open question"
grep -qF "#7 still has questions under Needs your answer" "$ERR" || fail "open question: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"
same "posted" "" "$(cat "$POSTED")"
same "acked" "" "$(cat "$ACKED")"

case_ "--dry-run says what the Lead's approve would post and changes nothing"
gh_thread <<THREAD
body $(ago 600) demo-app[bot] 0 The pitch\n\n<!-- a-team:lead -->
comment $(ago 5) reviewer 0 Let's build it.
THREAD
rm -f "$A_TEAM_STATE/history.db"
run board --dry-run demo approve lead 7 "$WORK/reply"
same "exit" 0 "$STATUS"
same "writes" "" "$(cat "$WRITES")"
same "posted" "" "$(cat "$POSTED")"
grep -q "would set #7 to 'Approved'" "$ERR" || fail "dry run: '$(cat "$ERR")'"
[ ! -e "$A_TEAM_STATE/history.db" ] || fail "dry run: recorded history"

case_ "the Lead's role and the shared rules say when a yes approves"
grep -qF 'approve lead <n> <file>' "$ROOT/roles/lead.md" || fail "role: no approve lead"
grep -qF 'approve lead <n> <file>' "$ROOT/tasks/lead.md" || fail "task prompt: not authorised"
grep -qF 'the Lead approves a pitch when the stakeholder answers it with a' "$ROOT/process.md" || fail "process: no exception"

# Declining: the stakeholder's no to an Idea or a pitch, with the reason on the issue.
case_ "decline posts the reason as yours, closes the issue as not planned and moves it to Done"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 } }
JSON
gh_items <<'ITEMS'
Idea 9 An Idea I don't want
Pitched 7 A pitch I don't want
Exploring 8 A pitch still being drafted
Ready 10 A task
ITEMS
rm -f "$A_TEAM_STATE/history.db"
printf 'Already covered by #46.\n' >"$WORK/reason"
for n in 9 7; do
  : >"$WRITES"
  : >"$POSTED"
  run board demo decline you "$n" "$WORK/reason"
  same "#$n exit" 0 "$STATUS"
  same "#$n said" "#$n: declined" "$(cat "$OUT")"
  same "#$n posted" "Already covered by #46." "$(cat "$POSTED")"
  grep -q "^CLOSE issue close $n -R mentaldesk/demo --reason not planned$" "$WRITES" ||
    fail "#$n: not closed as not planned in '$(cat "$WRITES")'"
  grep -q "item=PVTI_$n .*option=OPT_done " "$WRITES" || fail "#$n: not moved to Done in '$(cat "$WRITES")'"
  same "#$n history" "you declined
you commented" "$(A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board demo history "$n" | jq -r '.events[] | "\(.who) \(.what)"')"
done

case_ "a decline isn't counted as accepted"
run board demo trend
same "accepted" 0 "$(jq .accepted "$OUT")"

case_ "--dry-run says what it would decline and changes nothing"
: >"$WRITES"
: >"$POSTED"
rm -f "$A_TEAM_STATE/history.db"
run board --dry-run demo decline you 9 "$WORK/reason"
same "exit" 0 "$STATUS"
same "writes" "" "$(cat "$WRITES")"
same "posted" "" "$(cat "$POSTED")"
grep -q "would close #9 as not planned" "$ERR" || fail "decline dry run: '$(cat "$ERR")'"
[ ! -e "$A_TEAM_STATE/history.db" ] || fail "dry run: recorded history"

case_ "no agent may decline: that gate is the stakeholders' own"
for role in lead dev customer reviewer; do
  run board demo decline "$role" 9 "$WORK/reason"
  failed "$role declining"
  one_line "$role declining"
  grep -q "$role may not decline an item; declining is the stakeholders' own gate" "$ERR" ||
    fail "$role declining: '$(cat "$ERR")'"
done
same "writes" "" "$(cat "$WRITES")"

case_ "a decline needs a reason"
printf ' \n\t\n' >"$WORK/blank"
run board demo decline you 9 "$WORK/blank"
failed "no reason"
one_line "no reason"
grep -q "a decline needs a reason" "$ERR" || fail "no reason: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"
same "posted" "" "$(cat "$POSTED")"

case_ "decline is only for an Idea or a Pitched pitch, never a task, a PR or the docs PR"
for n in 8 10 404; do
  run board demo decline you "$n" "$WORK/reason"
  failed "decline #$n"
  one_line "decline #$n"
done
grep -q "#404 is not on the board" "$ERR" || fail "decline off the board: '$(cat "$ERR")'"
run board demo decline you 10 "$WORK/reason"
grep -q "only an Idea or a Pitched pitch can be declined (#10 is in 'Ready')" "$ERR" || fail "decline a task: '$(cat "$ERR")'"
edit_item 7 '.labels.nodes = []'
run board demo decline you 7 "$WORK/reason"
failed "decline unlabelled"
grep -q "#7 is not a pitch" "$ERR" || fail "decline unlabelled: '$(cat "$ERR")'"
edit_item 7 '.labels.nodes = [{name: "pitch"}] | .__typename = "PullRequest"'
run board demo decline you 7 "$WORK/reason"
failed "decline a PR"
grep -q "#7 is not an issue" "$ERR" || fail "decline a PR: '$(cat "$ERR")'"
edit_item 7 '.labels.nodes = [{name: "a-team:customer"}] | .__typename = "PullRequest"'
run board demo decline you 7 "$WORK/reason"
failed "decline the docs PR"
same "writes" "" "$(cat "$WRITES")"
same "posted" "" "$(cat "$POSTED")"

case_ "a close GitHub refuses says why in one line"
touch "$BIN/close-fails"
run board demo decline you 9 "$WORK/reason"
failed "refused close"
grep -q "can't close #9 (gh: Resource not accessible by integration (HTTP 403))" "$ERR" ||
  fail "refused close: '$(cat "$ERR")'"
rm "$BIN/close-fails"

case_ "a Status option whose id is all digits is sent as a string"
jq '.data.organization.projectV2.fields.nodes[0].options |= map(if .name == "Done" then .id = "98236657" else . end)' \
  "$META" >"$META.new" && mv "$META.new" "$META"
: >"$WRITES"
run board demo decline you 9 "$WORK/reason"
same "exit" 0 "$STATUS"
grep -q -- "-f option=98236657 " "$WRITES" || fail "numeric option: not a raw string in '$(cat "$WRITES")'"

case_ "the agents' settings deny decline"
grep -qF '"Bash(a-team board * decline you *)"' "$ROOT/settings/agents.json" || fail "no decline deny rule"

# Not now: a Pitched pitch back to Idea, held back from the Lead until a date.
weeks_on() { jq -rn --argjson w "$1" 'now + $w * 7 * 86400 | strflocaltime("%Y-%m-%d")'; }
back_on() { jq -rn --arg d "$(weeks_on "$1")" '$d | strptime("%Y-%m-%d") | mktime | strftime("%d %b") | ltrimstr("0")'; }

case_ "shelve posts your reason with the date, labels it, moves it to Idea and acks only that, so no run answers it"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 },
  "wip": { "pitched": 2, "exploring": 4, "ideas": 4 } }
JSON
gh_items 7 <<'ITEMS'
Pitched 7 A pitch for later
Exploring 8 A pitch still being drafted
Idea 9 An Idea
Ready 10 A task
ITEMS
jq '.data.organization.projectV2.fields.nodes[0].options += [{id: "OPT_idea", name: "Idea"}]' "$META" >"$META.new" && mv "$META.new" "$META"
gh_thread <<THREAD
comment $(ago 1) reviewer 0 What would this cost to run?
THREAD
rm -f "$A_TEAM_STATE/history.db"
printf "Cost isn't what I'm watching yet.\n" >"$WORK/reason"
run board demo shelve you 7 3 "$WORK/reason"
same "exit" 0 "$STATUS"
same "said" "#7: back on $(back_on 3)" "$(cat "$OUT")"
same "posted" "Cost isn't what I'm watching yet.

Not now: back on $(back_on 3)." "$(cat "$POSTED")"
grep -q "label" "$WRITES" && fail "labelled: '$(cat "$WRITES")'"
same "shelved" "{\"until\":\"$(weeks_on 3)\",\"priority\":\"High\"}" "$(marks 7 | jq -c .shelved)"
grep -q "item=PVTI_7 .*option=OPT_idea " "$WRITES" || fail "not moved to Idea: '$(cat "$WRITES")'"
same "acked" "IC_posted" "$(cat "$ACKED")"
same "history" "you shelved until $(back_on 3)
you Pitched → Idea" "$(A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board demo history 7 | jq -r '.events[] | "\(.who) \(.what)"')"

case_ "a reason is optional: without one the comment is the date alone"
: >"$POSTED"
: >"$WORK/blank"
run board demo shelve you 7 2 "$WORK/blank"
same "exit" 0 "$STATUS"
same "posted" "Not now: back on $(back_on 2)." "$(cat "$POSTED")"

case_ "shelve takes any whole number of weeks from 1, and only a Pitched pitch"
: >"$WRITES"
: >"$POSTED"
for weeks in 0 -1 two 1.5; do
  run board demo shelve you 7 "$weeks" "$WORK/reason"
  failed "$weeks weeks"
  one_line "$weeks weeks"
  grep -qF "weeks is a whole number from 1 ('$weeks' isn't)" "$ERR" || fail "$weeks weeks: '$(cat "$ERR")'"
done
for n in 8 9 10; do
  run board demo shelve you "$n" 2 "$WORK/reason"
  failed "shelve #$n"
done
run board demo shelve you 8 2 "$WORK/reason"
grep -qF "only a Pitched pitch can be shelved (#8 is in 'Exploring')" "$ERR" || fail "a draft: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"
same "posted" "" "$(cat "$POSTED")"

case_ "no Dev, Customer lead or Reviewer may shelve"
for role in dev customer reviewer; do
  run board demo shelve "$role" 7 2 "$WORK/reason"
  failed "$role shelving"
  grep -qF "$role may not shelve a pitch" "$ERR" || fail "$role shelving: '$(cat "$ERR")'"
done
same "writes" "" "$(cat "$WRITES")"

case_ "the Lead shelves only when a stakeholder has asked, and its reply carries the date and its marker"
gh_thread <<THREAD
comment $(ago 5) reviewer 1 Already answered.
THREAD
printf 'Shelved for 3 weeks, as you asked.\n' >"$WORK/reply"
run board demo shelve lead 7 3 "$WORK/reply"
failed "nothing asked"
grep -qF "lead may shelve #7 only when a stakeholder has asked" "$ERR" || fail "nothing asked: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"
gh_thread <<THREAD
comment $(ago 5) reviewer 0 Not for a few weeks: shelve it for 3.
THREAD
run board demo shelve lead 7 3 "$WORK/reply"
same "exit" 0 "$STATUS"
same "posted" "Shelved for 3 weeks, as you asked.

Not now: back on $(back_on 3).

<!-- a-team:lead -->" "$(cat "$POSTED")"
run board demo shelve lead 7 3 "$WORK/blank"
failed "empty reply"

case_ "the Lead can't move a pitch out of Pitched to Idea any more: shelve does that"
run board demo move lead 7 Idea
failed "move to Idea"
grep -qF "lead may not move #7 from 'Pitched' to 'Idea'" "$ERR" || fail "move to Idea: '$(cat "$ERR")'"

case_ "--dry-run says what it would shelve and changes nothing"
: >"$WRITES"
: >"$POSTED"
rm -f "$A_TEAM_STATE/history.db"
run board --dry-run demo shelve you 7 2 "$WORK/reason"
same "exit" 0 "$STATUS"
same "writes" "" "$(cat "$WRITES")"
same "posted" "" "$(cat "$POSTED")"
grep -q "would mark #7 shelved" "$ERR" || fail "shelve dry run: '$(cat "$ERR")'"
[ ! -e "$A_TEAM_STATE/history.db" ] || fail "dry run: recorded history"

case_ "lead-next passes over a shelved Idea until its date, even when it's the only one with a Priority"
gh_items 7 <<'ITEMS'
Idea 7 A pitch for later
Idea 9 An unranked Idea the Lead found
ITEMS
edit_item 7 '.labels.nodes = [{name: "pitch"}, {name: "a-team:shelved"}]'
edit_item 9 '.labels.nodes = [{name: "a-team:idea"}]'
gh_thread <<THREAD
comment $(ago 60) reviewer 1 Cost isn't what I'm watching yet.\n\nNot now: back on someday.\n\n<!-- a-team:shelved until=$(weeks_on 2) priority=High -->
THREAD
A_TEAM_STATE="$WORK/state" run board demo lead-next
same "exit" 0 "$STATUS"
same "cooling" '{"turn":"discover","item":null}' "$(jq -c '{turn, item}' "$OUT")"

case_ "once its date has passed it's pitched again at its Priority, saying it was shelved"
gh_thread <<THREAD
comment $(ago 30000) reviewer 1 Not now: back on 1 Oct.\n\n<!-- a-team:shelved until=$YESTERDAY priority=High -->
THREAD
echo discover >"$WORK/state/demo/lead-turn"
A_TEAM_STATE="$WORK/state" run board demo lead-next
same "exit" 0 "$STATUS"
same "back" "{\"turn\":\"pitch\",\"item\":7,\"shelved\":\"$YESTERDAY\"}" "$(jq -c '{turn, item: .item.number, shelved: .item.shelved}' "$OUT")"

case_ "changing its Priority ends the cooldown early"
gh_thread <<THREAD
comment $(ago 60) reviewer 1 Not now.\n\n<!-- a-team:shelved until=$(weeks_on 2) priority=Low -->
THREAD
echo discover >"$WORK/state/demo/lead-turn"
A_TEAM_STATE="$WORK/state" run board demo lead-next
same "reranked" '{"turn":"pitch","item":7}' "$(jq -c '{turn, item: .item.number}' "$OUT")"

case_ "only a stakeholder or the team can shelve: anyone else's marker is ignored"
gh_thread <<THREAD
comment $(ago 60) passer-by 0 <!-- a-team:shelved until=$(weeks_on 2) priority=High -->
THREAD
echo discover >"$WORK/state/demo/lead-turn"
A_TEAM_STATE="$WORK/state" run board demo lead-next
same "ignored" '{"turn":"pitch","item":7}' "$(jq -c '{turn, item: .item.number}' "$OUT")"

case_ "a displaced pitch is no shelved one: it still comes back as soon as a slot frees"
gh_items 11 <<'ITEMS'
Exploring 11 Displaced, not shelved
ITEMS
edit_item 11 '.labels.nodes = [{name: "pitch"}, {name: "a-team:displaced"}]'
A_TEAM_STATE="$WORK/state" run board demo lead-next
same "promoted" '[11]' "$(jq -c '[.promote[].number]' "$OUT")"

case_ "pitching a shelved Idea clears its shelve"
gh_items 7 <<'ITEMS'
Idea 7 A pitch for later
ITEMS
edit_item 7 '.labels.nodes = [{name: "pitch"}, {name: "a-team:shelved"}]'
run board demo move lead 7 Exploring
same "exit" 0 "$STATUS"
same "shelved" null "$(marks 7 | jq -c .shelved)"

case_ "upgrading carries the a-team labels on open items into the record, then takes them off, leaving every other label"
gh_items 7 <<'ITEMS'
Idea 7 A seed
Ready 8 A held task
In_progress 9 Being built
Done 10 Long done
ITEMS
edit_item 7 '.labels.nodes = [{name: "rank:medium"}, {name: "a-team:idea"}, {name: "mine"}]'
edit_item 8 '.labels.nodes = [{name: "blocked"}, {name: "a-team:upstream"}, {name: "bug"}]'
edit_item 10 '.labels.nodes = [{name: "pitch"}, {name: "theme: Good UX"}] | .state = "CLOSED" | .stateReason = "COMPLETED"'
gh_talk <<TALK
8 labeled ${TODAY}T09:00:00Z demo-app[bot] blocked
TALK
gh_thread <<THREAD
comment $(ago 60) demo-app[bot] 0 Blocked by mentaldesk/style#44, until released: needs it.\n\n<!-- a-team:wait repo=mentaldesk/style n=44 until=released -->
THREAD
rm -f "$BIN/unlabelled"
run board demo list
same "exit" 0 "$STATUS"
same "labels" '[["mine"],["bug"],[],["pitch"]]' "$(jq -c '[.[] | select(.number | IN(7, 8, 9, 10)) | .labels]' "$OUT")"
same "seed" '{"rank":"Medium","idea":true}' "$(marks 7)"
same "held" "{\"at\":\"${TODAY}T09:00:00Z\",\"by\":\"team\"}" "$(marks 8 | jq -c .held)"
same "upstream" '["mentaldesk/style#44"]' "$(marks 8 | jq -c .upstream)"
same "claimed" '{"dev":true}' "$(marks 9)"
same "done theme" '"Good UX"' "$(jq -c '.[] | select(.number == 10) | .marks.theme' "$OUT")"
same "unlabelled" "repos/mentaldesk/demo/issues/7/labels/rank%3Amedium
repos/mentaldesk/demo/issues/7/labels/a-team%3Aidea
repos/mentaldesk/demo/issues/8/labels/blocked
repos/mentaldesk/demo/issues/8/labels/a-team%3Aupstream
repos/mentaldesk/demo/issues/9/labels/a-team%3Adev" "$(sed 's/^api -X DELETE //' "$BIN/unlabelled")"
grep -q label "$WRITES" && fail "labelled: '$(cat "$WRITES")'"

case_ "a label GitHub won't take off still leaves its state in the record, and is tried again"
touch "$BIN/unlabel-fails"
KEEP_BOARD='' run board demo list
rm -f "$BIN/unlabel-fails"
same "exit" 0 "$STATUS"
same "seed" '{"rank":"Medium","idea":true}' "$(marks 7)"
same "kept for next time" '["rank:medium","a-team:idea","mine"]' \
  "$(sqlite3 "$A_TEAM_STATE/history.db" "SELECT content FROM board WHERE team = 'demo' AND item = 7;" | jq -c .labels)"

case_ "a label GitHub takes off while another won't come off goes from the record, and only the other is tried again"
echo "a-team%3Aidea" >"$BIN/unlabel-fails"
KEEP_BOARD='' run board demo list
rm -f "$BIN/unlabel-fails"
same "exit" 0 "$STATUS"
same "kept for next time" '["a-team:idea","mine"]' \
  "$(sqlite3 "$A_TEAM_STATE/history.db" "SELECT content FROM board WHERE team = 'demo' AND item = 7;" | jq -c .labels)"

case_ "--dry-run carries the labels over in its own copy and takes none off"
rm -f "$BIN/unlabelled"
run board --dry-run demo list
same "exit" 0 "$STATUS"
same "seed" '{"rank":"Medium","idea":true}' "$(jq -c '.[] | select(.number == 7) | .marks' "$OUT")"
[ ! -e "$BIN/unlabelled" ] || fail "dry run took labels off: '$(cat "$BIN/unlabelled")'"
grep -q "would unlabel #7 rank:medium" "$ERR" || fail "dry run: '$(cat "$ERR")'"

case_ "a shelve carried over keeps the date in its hidden comment"
gh_items 7 <<'ITEMS'
Idea 7 A pitch for later
ITEMS
edit_item 7 '.labels.nodes = [{name: "pitch"}, {name: "a-team:shelved"}]'
gh_thread <<THREAD
comment $(ago 60) reviewer 1 Not now: back on someday.\n\n<!-- a-team:shelved until=2026-12-01 priority=High -->
THREAD
run board demo list
same "shelved" '{"until":"2026-12-01","priority":"High"}' "$(marks 7 | jq -c .shelved)"

case_ "waiting says under each card what a-team keeps of it"
gh_items <<'ITEMS'
Idea 41 A seed the Lead found
ITEMS
edit_item 41 '.labels.nodes = [{name: "a-team:idea"}, {name: "rank:medium"}, {name: "a-team:skipped"}]'
waiting
same "exit" 0 "$STATUS"
same "facts" '"Lead recommends Medium · skipped"' "$(jq -c '.[] | select(.number == 41) | .facts' "$OUT")"

case_ "a card says it's shelved only while the shelve holds it back"
gh_items 7 <<'ITEMS'
Idea 7 A pitch for later
ITEMS
edit_item 7 '.labels.nodes = [{name: "pitch"}, {name: "a-team:shelved"}]'
gh_thread <<THREAD
comment $(ago 60) reviewer 1 Not now.\n\n<!-- a-team:shelved until=$(weeks_on 2) priority=High -->
THREAD
run board demo overview
same "exit" 0 "$STATUS"
same "cooling" "\"shelved until $(jq -rn --arg d "$(weeks_on 2)" '$d | strptime("%Y-%m-%d") | mktime | strftime("%d %b") | ltrimstr("0")')\"" \
  "$(jq -c '.[] | select(.number == 7) | .facts' "$OUT")"
sqlite3 "$A_TEAM_STATE/history.db" "UPDATE board SET marks = json_set(marks, '$.shelved.priority', 'Low') WHERE team = 'demo' AND item = 7;"
KEEP_BOARD=1 run board demo overview
same "reranked" null "$(jq -c '.[] | select(.number == 7) | .facts' "$OUT")"

case_ "the Dev holds its own task with its question, as its, and moves it back to Ready"
gh_items <<'ITEMS'
In_progress 9 Being built
In_progress 12 Someone else's
ITEMS
edit_item 12 '.labels.nodes = []'
echo "Which marker should it post?" >"$WORK/ask"
: >"$POSTED"
: >"$WRITES"
KEEP_BOARD='' run board demo hold dev 9 "$WORK/ask"
same "exit" 0 "$STATUS"
same "posted" "Which marker should it post?

<!-- a-team:dev -->" "$(cat "$POSTED")"
same "held by" '"dev"' "$(marks 9 | jq -c .held.by)"
grep -q label "$WRITES" && fail "labelled: '$(cat "$WRITES")'"
run board demo hold dev 12 "$WORK/ask"
failed "not the Dev's"
run board demo hold customer 9 "$WORK/ask"
failed "customer"

case_ "the agents' settings deny shelving as you"
grep -qF '"Bash(a-team board * shelve you *)"' "$ROOT/settings/agents.json" || fail "no shelve deny rule"

case_ "the agents' settings deny holding as you, and releasing, which are yours"
grep -qF '"Bash(a-team board * hold you *)"' "$ROOT/settings/agents.json" || fail "no hold deny rule"
grep -qF '"Bash(a-team board * release you *)"' "$ROOT/settings/agents.json" || fail "no release deny rule"

# Accepting: the app's merge, the gate a task leaves by.

finish_suite
