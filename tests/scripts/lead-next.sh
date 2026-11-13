#!/usr/bin/env bash
# Lead-next, displacing pitches, and blocking.
# shellcheck source=tests/scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
# shellcheck disable=SC2034,SC2154 # helpers read what an area sets, and an area reads what lib.sh sets

case_ "lead-next announces a swap only for pitches that have never been displaced"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "project": { "owner": "mentaldesk", "number": 1 },
  "wip": { "pitched": 2, "exploring": 4, "ideas": 4 } }
JSON
gh_items 21 22 <<'ITEMS'
Pitched 11 Never displaced
Pitched 12 Displaced before
Exploring 21 A higher draft, never in Pitched
Exploring 22 A higher draft, back again
ITEMS
edit_item 12 '.labels.nodes += [{name: "a-team:displaced"}]'
edit_item 22 '.labels.nodes += [{name: "a-team:displaced"}]'
A_TEAM_STATE="$WORK/state" run board demo lead-next
same "exit" 0 "$STATUS"
same "demote" '[[11,true],[12,false]]' "$(jq -c '[.demote[] | [.number, .announce]] | sort' "$OUT")"
same "promote" '[[21,true],[22,false]]' "$(jq -c '[.promote[] | [.number, .announce]] | sort' "$OUT")"

case_ "lead-next: a higher Priority displaces a lower one, both unranked (#81)"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "project": { "owner": "mentaldesk", "number": 1 },
  "wip": { "pitched": 1, "exploring": 4, "ideas": 4 } }
JSON
gh_items 21 <<'ITEMS'
Pitched 11 Low, in front of me
Exploring 21 High, a draft
ITEMS
edit_item 11 '.issueFieldValues.nodes = [{name: "Low", field: {name: "Priority"}}]'
A_TEAM_STATE="$WORK/state" run board demo lead-next
same "exit" 0 "$STATUS"
same "swap" '{"demote":[11],"promote":[21]}' "$(jq -c '{demote: [.demote[].number], promote: [.promote[].number]}' "$OUT")"

case_ "lead-next: a same-Priority draft never displaces a pitch, however the Lead ranked them (#336)"
gh_items 11 21 <<'ITEMS'
Pitched 11 High, recommended Low
Exploring 21 High, recommended High
ITEMS
edit_item 21 '.labels.nodes += [{name: "rank:high"}]'
edit_item 11 '.labels.nodes += [{name: "rank:low"}]'
A_TEAM_STATE="$WORK/state" run board demo lead-next
same "exit" 0 "$STATUS"
same "swap" '{"demote":[],"promote":[]}' "$(jq -c '{demote: [.demote[].number], promote: [.promote[].number]}' "$OUT")"
run board demo move lead 11 Exploring
failed "demote a same-Priority pitch"

case_ "lead-next: with room in Pitched, the higher-recommended of two same-Priority drafts goes first"
gh_items 21 22 <<'ITEMS'
Exploring 21 High, recommended Low
Exploring 22 High, recommended High
ITEMS
edit_item 21 '.labels.nodes += [{name: "rank:low"}]'
edit_item 22 '.labels.nodes += [{name: "rank:high"}]'
A_TEAM_STATE="$WORK/state" run board demo lead-next
same "exit" 0 "$STATUS"
same "swap" '{"demote":[],"promote":[22]}' "$(jq -c '{demote: [.demote[].number], promote: [.promote[].number]}' "$OUT")"

case_ "lead-next: a higher draft displaces the lower-recommended of two same-Priority pitches"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "project": { "owner": "mentaldesk", "number": 1 },
  "wip": { "pitched": 2, "exploring": 4, "ideas": 4 } }
JSON
gh_items 21 <<'ITEMS'
Pitched 11 Low, recommended Low
Pitched 12 Low, recommended High
Exploring 21 High, a draft
ITEMS
edit_item 11 '.issueFieldValues.nodes = [{name: "Low", field: {name: "Priority"}}] | .labels.nodes += [{name: "rank:low"}]'
edit_item 12 '.issueFieldValues.nodes = [{name: "Low", field: {name: "Priority"}}] | .labels.nodes += [{name: "rank:high"}]'
A_TEAM_STATE="$WORK/state" run board demo lead-next
same "exit" 0 "$STATUS"
same "swap" '{"demote":[11],"promote":[21]}' "$(jq -c '{demote: [.demote[].number], promote: [.promote[].number]}' "$OUT")"

case_ "a first demote marks the pitch displaced"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "project": { "owner": "mentaldesk", "number": 1 },
  "wip": { "pitched": 1 } }
JSON
gh_items 21 <<'ITEMS'
Pitched 11 Out-ranked
Exploring 21 A higher draft
ITEMS
run board demo move lead 11 Exploring
same "exit" 0 "$STATUS"
same "displaced" true "$(marks 11 | jq -c .displaced)"
grep -q '/labels' "$WRITES" && fail "labelled: '$(cat "$WRITES")'"
grep -q "option=OPT_exploring" "$WRITES" || fail "status not set: '$(cat "$WRITES")'"

case_ "a repeat demote moves without marking again"
edit_item 11 '.labels.nodes += [{name: "a-team:displaced"}]'
: >"$WRITES"
run board demo move lead 11 Exploring
same "exit" 0 "$STATUS"
same "label" 0 "$(grep -c '/labels' "$WRITES")"
grep -q "option=OPT_exploring" "$WRITES" || fail "status not set: '$(cat "$WRITES")'"

case_ "--dry-run says it would mark a first demote, and marks nothing"
edit_item 11 '.labels.nodes -= [{name: "a-team:displaced"}]'
: >"$WRITES"
run board --dry-run demo move lead 11 Exploring
same "exit" 0 "$STATUS"
same "writes" "" "$(cat "$WRITES")"
grep -q "would mark #11 displaced true" "$ERR" || fail "dry run: nothing about the mark in '$(cat "$ERR")'"

case_ "setup creates no a-team label but pitch"
run board --dry-run demo setup
same "exit" 0 "$STATUS"
same "labels" "(dry run) created label pitch" "$(grep 'created label' "$OUT")"

case_ "setup drops the Todo and In Progress GitHub made, unless an item has one"
cp "$META" "$BIN/meta.saved"
jq '.data.organization.projectV2.fields.nodes[0].options += [{id: "OPT_todo", name: "Todo"}, {id: "OPT_ip", name: "In Progress"}]' \
  "$BIN/meta.saved" >"$META"
jq '.data.organization.projectV2.items.nodes[0].fieldValueByName.name = "In Progress"' "$ITEMS" >"$ITEMS.new" && mv "$ITEMS.new" "$ITEMS"
run board --dry-run demo setup
same "exit" 0 "$STATUS"
grep -q '^  drop  Todo$' "$OUT" || fail "setup: Todo not dropped in '$(cat "$OUT")'"
grep -q '^  keep  In Progress$' "$OUT" || fail "setup: In Progress in use but not kept in '$(cat "$OUT")'"
grep -q '^  keep  Exploring$' "$OUT" || fail "setup: '$(cat "$OUT")'"
mv "$BIN/meta.saved" "$META"

case_ "setup applies exactly what its dry run listed"
run board --dry-run demo setup
listed=$(cat "$OUT")
: >"$WRITES"
run board demo setup
same "exit" 0 "$STATUS"
[ -n "$listed" ] || fail "dry run: nothing listed"
same "options" "$(grep -v '^  add   field ' <<<"$listed" | sed -En 's/^  (keep|add ) {2}//p')" \
  "$(jq -r '.variables.input.singleSelectOptions[].name' "$BIN/mutation.json")"
same "labels" "$(sed -n 's/^(dry run) created label //p' <<<"$listed")" \
  "$(sed -n 's/^label create \([^ ]*\) .*/\1/p' "$WRITES")"

case_ "setup adds whichever of the Lead rank and Theme fields the project lacks, and its dry run lists them"
add_field() { jq --arg name "$1" '.data.organization.projectV2.fields.nodes += [{id: "PVTF_\($name)", name: $name, dataType: "TEXT"}]' "$META" >"$META.new" && mv "$META.new" "$META"; }
listed_fields() { sed -n 's/^  add   field //p' "$OUT" | paste -sd, -; }
added_fields() { sed -n 's/^FIELD .*name=\(.*\) FIELD -f.*/\1/p' "$WRITES" | paste -sd, -; }
run board --dry-run demo setup
same "neither: listed" "Lead rank,Theme" "$(listed_fields)"
: >"$WRITES"
run board demo setup
same "exit" 0 "$STATUS"
same "neither: added" "Lead rank,Theme" "$(added_fields)"
cp "$META" "$BIN/meta.saved"
add_field Theme
run board --dry-run demo setup
same "only Theme: listed" "Lead rank" "$(listed_fields)"
: >"$WRITES"
run board demo setup
same "only Theme: added" "Lead rank" "$(added_fields)"
add_field "Lead rank"
run board --dry-run demo setup
same "both: listed" "" "$(listed_fields)"
: >"$WRITES"
run board demo setup
same "exit" 0 "$STATUS"
same "both: added" "" "$(added_fields)"
mv "$BIN/meta.saved" "$META"

case_ "setup and check name a Lead rank or Theme field of the project's own, before setup changes anything"
cp "$META" "$BIN/meta.saved"
jq '.data.organization.projectV2.fields.nodes += [{id: "PVTSSF_theme", name: "Theme", dataType: "SINGLE_SELECT", options: []},
    {id: "PVTF_rank", name: "Lead rank", dataType: "TEXT"}]' "$BIN/meta.saved" >"$META"
clash="mentaldesk project 1 has a Theme field that isn't text, so a-team can't copy to it: rename that field"
rm -f "$BIN/mutation.json"
: >"$WRITES"
run board demo setup
same "exit" 1 "$STATUS"
grep -qF "$clash" "$ERR" || fail "setup: nothing about the clash in '$(cat "$ERR")'"
[ ! -e "$BIN/mutation.json" ] || fail "setup: Status options changed"
same "writes" "" "$(cat "$WRITES")"
run board --dry-run demo setup
same "dry run: exit" 1 "$STATUS"
grep -qF "$clash" "$ERR" || fail "dry run: nothing about the clash in '$(cat "$ERR")'"
run board demo check
grep -qF "note     fields    $clash" "$OUT" || fail "check: nothing about the clash in '$(cat "$OUT")'"
mv "$BIN/meta.saved" "$META"

case_ "setup names GitHub's reason when it can't read the project"
echo "gh: Could not resolve to a ProjectV2 with the number 1." >"$BIN/project-read-fails"
run board demo setup
failed "setup"
grep -qF "Could not resolve to a ProjectV2 with the number 1." "$ERR" || fail "setup: '$(cat "$ERR")'"
rm "$BIN/project-read-fails"

case_ "either role may block either task, across pitches and at any status"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 } }
JSON
gh_items <<'ITEMS'
Building 10 A pitch
Building 20 Another pitch
Ready 11 A task of the first pitch
In_progress 21 A task of the second pitch
ITEMS
run board demo depends lead 11 21 "both rewrite the same view"
same "exit" 0 "$STATUS"
same "said" "#11 is now blocked by #21, and said why on #11" "$(cat "$OUT")"
grep -q "^POST .*/issues/11/dependencies/blocked_by" "$WRITES" || fail "depends: no POST in '$(cat "$WRITES")'"
grep -q "^Blocked by #21: both rewrite the same view$" "$POSTED" || fail "depends: no reason in '$(cat "$POSTED")'"
grep -q '<!-- a-team:lead -->' "$POSTED" || fail "depends: no lead marker in '$(cat "$POSTED")'"

case_ "the Dev may block its own In progress task, and the comment carries the Dev's marker"
: >"$WRITES"
run board demo depends dev 21 11 "taking #11 first; both are in WaitingView"
same "exit" 0 "$STATUS"
same "said" "#21 is now blocked by #11, and said why on #21" "$(cat "$OUT")"
grep -q "^POST .*/issues/21/dependencies/blocked_by" "$WRITES" || fail "depends: no POST in '$(cat "$WRITES")'"
grep -q '<!-- a-team:dev -->' "$POSTED" || fail "depends: no dev marker in '$(cat "$POSTED")'"

case_ "either role may undo it again, wherever it was drawn"
gh_blocked <<'DEPS'
21
DEPS
: >"$WRITES"
run board demo undepend dev 11 21 "looked again: #11 is in DashboardSettings"
same "exit" 0 "$STATUS"
same "said" "#11 is no longer blocked by #21, and said why on #11" "$(cat "$OUT")"
same "writes" "DELETE" "$(cut -d' ' -f1 <"$WRITES")"
grep -q "/issues/11/dependencies/blocked_by/DEP_21" "$WRITES" || fail "undepend: wrong url in '$(cat "$WRITES")'"
grep -q "^No longer blocked by #21: looked again: #11 is in DashboardSettings$" "$POSTED" ||
  fail "undepend: no reason in '$(cat "$POSTED")'"
grep -q '<!-- a-team:dev -->' "$POSTED" || fail "undepend: no dev marker in '$(cat "$POSTED")'"
run board demo undepend lead 11 21 "the Lead can undo the Dev's block too"
same "exit" 0 "$STATUS"
grep -q '<!-- a-team:lead -->' "$POSTED" || fail "undepend: no lead marker in '$(cat "$POSTED")'"

case_ "an unknown role is refused, in one line, by both verbs"
run board demo depends nobody 11 21 "why"
failed "depends role"
one_line "depends role"
grep -q "unknown role 'nobody' (lead | dev | customer | reviewer)" "$ERR" || fail "depends role: '$(cat "$ERR")'"
run board demo undepend nobody 11 21 "why"
failed "undepend role"
one_line "undepend role"
grep -q "unknown role 'nobody' (lead | dev | customer | reviewer)" "$ERR" || fail "undepend role: '$(cat "$ERR")'"

case_ "undepend on a pair that isn't linked is refused, in one line"
gh_blocked </dev/null
: >"$WRITES"
run board demo undepend lead 11 21 "not needed"
failed "undepend unlinked"
one_line "undepend unlinked"
grep -q "#11 is not blocked by #21" "$ERR" || fail "undepend unlinked: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"

case_ "the wrong number of arguments shows the new signature, in one line"
run board demo depends 11 21
failed "depends usage"
one_line "depends usage"
grep -qF 'usage: board.sh demo depends <role> <task> <prerequisite> "<why>"' "$ERR" ||
  fail "depends usage: '$(cat "$ERR")'"
run board demo undepend lead 11 21
failed "undepend usage"
one_line "undepend usage"
grep -qF 'usage: board.sh demo undepend <role> <task> <prerequisite> "<why>"' "$ERR" ||
  fail "undepend usage: '$(cat "$ERR")'"

case_ "--dry-run gives the same verdicts, and neither the dependency nor the comment is created"
: >"$POSTED"
: >"$WRITES"
run board --dry-run demo depends lead 11 21 "both rewrite the same view"
same "exit" 0 "$STATUS"
same "said" "(dry run) #11 is now blocked by #21, and said why on #11" "$(cat "$OUT")"
grep -q "Blocked by #21: both rewrite the same view" "$ERR" || fail "dry run: no comment in '$(cat "$ERR")'"
gh_blocked <<'DEPS'
21
DEPS
run board --dry-run demo undepend lead 11 21 "looked again"
same "exit" 0 "$STATUS"
same "said" "(dry run) #11 is no longer blocked by #21, and said why on #11" "$(cat "$OUT")"
grep -q "No longer blocked by #21: looked again" "$ERR" || fail "dry run: no comment in '$(cat "$ERR")'"
same "posted" "" "$(cat "$POSTED")"
same "writes" "" "$(cat "$WRITES")"


case_ "lead-next spreads step changes evenly through discovery turns by the team's innovation, 30 where it has none"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "project": { "owner": "mentaldesk", "number": 1 },
  "wip": { "pitched": 2, "exploring": 4, "ideas": 4 } }
JSON
gh_items <<'ITEMS'
ITEMS
turns() {
  local said=""
  rm -f "$WORK/state/demo/lead-innovation"
  for _ in 1 2 3 4 5 6 7 8 9 10; do
    A_TEAM_STATE="$WORK/state" run board demo lead-next
    said+=$(jq -r '.turn[0:1]' "$OUT")
  done
  echo "$said"
}
same "unset" dddsddsdds "$(turns)"
for innovation in 0:dddddddddd 30:dddsddsdds 50:dsdsdsdsds 100:ssssssssss; do
  jq --argjson innovation "${innovation%%:*}" '.innovation = $innovation' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
  same "innovation ${innovation%%:*}" "${innovation#*:}" "$(turns)"
done
A_TEAM_STATE="$WORK/state" run board demo lead-next
same "a step change's room" '{"turn":"step","room":1}' "$(jq -c '{turn, room}' "$OUT")"

case_ "a step change waits for room in Ideas like any discovery"
jq '.innovation = 100 | .wip.ideas = 1' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
gh_items <<'ITEMS'
Idea 41 A discovery waiting for triage
ITEMS
edit_item 41 '.labels.nodes = [{name: "a-team:idea"}]'
rm -f "$WORK/state/demo/lead-innovation"
A_TEAM_STATE="$WORK/state" run board demo lead-next
same "full" '"none"' "$(jq -c .turn "$OUT")"
[ -e "$WORK/state/demo/lead-innovation" ] && fail "full: a turn that wasn't taken counted"

case_ "the one Idea the Lead adds after a step-change turn in the same run is marked a step change, and shows so in Triage"
jq '.innovation = 100 | .wip.ideas = 4' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
gh_items <<'ITEMS'
Idea 41 I can't hand a team a web app
Idea 42 The Assistant forgets the card I had open
ITEMS
edit_item 41 '.labels.nodes = [{name: "a-team:idea"}]'
edit_item 42 '.labels.nodes = [{name: "a-team:idea"}]'
jq '.data.organization.projectV2.fields.nodes[0].options += [{id: "OPT_idea", name: "Idea"}]' "$META" >"$META.new" && mv "$META.new" "$META"
rm -f "$WORK/state/demo/lead/step"
A_TEAM_RUN_STARTED=2026-11-12T01:00:00Z A_TEAM_STATE="$WORK/state" run board demo lead-next
same "turn" '"step"' "$(jq -c .turn "$OUT")"
gh_child 42 - - "<!-- a-team:lead -->"
run board demo add lead 42 Idea
same "another run's" '{"idea":true}' "$(marks 42)"
gh_child 41 - - "<!-- a-team:lead -->"
A_TEAM_RUN_STARTED=2026-11-12T01:00:00Z run board demo add lead 41 Idea
same "exit" 0 "$STATUS"
same "step change" '{"idea":true,"step":true}' "$(marks 41)"
KEEP_BOARD=1 waiting
same "triage" '[[41,true],[42,null]]' "$(jq -c 'map(select(.status == "Idea") | [.number, .stepChange]) | sort' "$OUT")"

finish_suite
