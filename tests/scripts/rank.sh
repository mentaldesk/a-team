#!/usr/bin/env bash
# The Lead's rank, themes and Spending.
# shellcheck source=tests/scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
# shellcheck disable=SC2034,SC2154 # helpers read what an area sets, and an area reads what lib.sh sets

case_ "recommend records an Idea's rank, shows it on the mirror, and posts the case as the Lead"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" },
  "project": { "owner": "mentaldesk", "number": 1 }, "wip": { "pitched": 2, "exploring": 4, "ideas": 4 } }
JSON
gh_items <<'ITEMS'
Idea 41 Anything that goes wrong before the dashboard opens dumps a stack trace
Ready 42 A task
ITEMS
edit_item 41 '.labels.nodes = [{name: "rank:low"}]'
echo 'theme 3, small · a stack trace is the only error path there is' >"$WORK/case"
run board demo recommend lead 41 High "$WORK/case"
same "exit" 0 "$STATUS"
same "marks" '{"rank":"High"}' "$(sqlite3 "$A_TEAM_STATE/history.db" "SELECT marks FROM board WHERE team = 'demo' AND item = 41;")"
grep -q 'label' "$WRITES" && fail "labelled: '$(cat "$WRITES")'"
grep -q 'FIELD .*name=Lead rank' "$WRITES" || fail "no Lead rank field made: '$(cat "$WRITES")'"
grep -q 'updateProjectV2ItemFieldValue.*field=PVTF_new.*text=High\|item=PVTI_41 -F field=PVTF_new -f text=High' "$WRITES" ||
  fail "rank not mirrored: '$(cat "$WRITES")'"
same "posted" 'Recommended: **High** · theme 3, small · a stack trace is the only error path there is

<!-- a-team:lead -->' "$(cat "$POSTED")"

case_ "only the Lead recommends, only on an Idea, and only with a one-line case"
edit_item 41 '.labels.nodes = []'
: >"$WRITES"
: >"$POSTED"
for role in you dev customer; do
  run board demo recommend "$role" 41 High "$WORK/case"
  failed "$role"
  grep -qF "$role may not recommend a rank" "$ERR" || fail "$role: '$(cat "$ERR")'"
done
run board demo recommend lead 42 High "$WORK/case"
failed "a task"
grep -qF "recommend is only for Ideas (#42 is in 'Ready')" "$ERR" || fail "a task: '$(cat "$ERR")'"
run board demo recommend lead 41 Soon "$WORK/case"
failed "unknown rank"
printf 'theme 3\nand a second line\n' >"$WORK/long-case"
run board demo recommend lead 41 High "$WORK/long-case"
failed "two lines"
grep -qF "the case for #41 is one line" "$ERR" || fail "two lines: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"
same "posted" "" "$(cat "$POSTED")"

case_ "the Lead's recommendation breaks a tie inside a Priority band, and says it did"
gh_items 41 42 43 <<'ITEMS'
Idea 41 Ranked High, recommended Low
Idea 42 Ranked High, recommended High
Idea 43 Ranked High, not recommended
ITEMS
edit_item 41 '.labels.nodes = [{name: "rank:low"}]'
edit_item 42 '.labels.nodes = [{name: "rank:high"}]'
A_TEAM_STATE="$WORK/state" run board demo lead-next
same "exit" 0 "$STATUS"
same "lead-next" '{"turn":"pitch","item":42,"actedOn":"High"}' "$(jq -c '{turn, item: .item.number, actedOn: .item.actedOn}' "$OUT")"

case_ "a recommendation the next Idea shares decided nothing"
edit_item 41 '.labels.nodes = [{name: "rank:high"}]'
A_TEAM_STATE="$WORK/state" run board demo lead-next
same "exit" 0 "$STATUS"
same "acted on" 'null' "$(jq -c '.item.actedOn' "$OUT")"

case_ "a stakeholder's Priority wins across bands, whatever the Lead recommends"
gh_items 43 <<'ITEMS'
Idea 41 Unranked, recommended Urgent
Idea 43 Ranked High, not recommended
ITEMS
edit_item 41 '.labels.nodes = [{name: "rank:urgent"}]'
echo '{"body": "Mine.", "state": "open"}' >"$ISSUE"
A_TEAM_STATE="$WORK/state" run board demo lead-next
same "exit" 0 "$STATUS"
same "item" 43 "$(jq -c '.item.number' "$OUT")"

case_ "the stakeholder's own unranked Ideas are pitched in the order the Lead recommends"
gh_items <<'ITEMS'
Idea 51 Not recommended
Idea 52 Recommended Low
Idea 53 Recommended Medium
ITEMS
edit_item 52 '.labels.nodes = [{name: "rank:low"}]'
edit_item 53 '.labels.nodes = [{name: "rank:medium"}]'
echo '{"body": "Mine.", "state": "open"}' >"$ISSUE"
A_TEAM_STATE="$WORK/state" run board demo lead-next
same "exit" 0 "$STATUS"
same "lead-next" '{"item":53,"actedOn":"Medium"}' "$(jq -c '{item: .item.number, actedOn: .item.actedOn}' "$OUT")"

case_ "an unranked Idea the team found is never pitched, however high the Lead recommends it"
gh_items <<'ITEMS'
Idea 61 Found by the Lead
Idea 62 The stakeholder's own
ITEMS
edit_item 61 '.labels.nodes = [{name: "a-team:idea"}, {name: "rank:urgent"}]'
echo '{"body": "Mine.", "state": "open"}' >"$ISSUE"
A_TEAM_STATE="$WORK/state" run board demo lead-next
same "exit" 0 "$STATUS"
same "item" 62 "$(jq -c '.item.number' "$OUT")"

case_ "waiting gives a recommended Idea its rank and the Lead's case, in the call it already makes"
gh_items <<'ITEMS'
Pitched 106 Both gates are mine
Idea 6 Recommended
Idea 7 Not recommended
ITEMS
edit_item 6 '.labels.nodes = [{name: "rank:high"}]'
gh_talk <<TALK
106 body ${TODAY}T08:00:00Z demo-app[bot] The pitch\n<!-- a-team:lead -->
6 body ${TODAY}T07:00:00Z reviewer An idea
6 comment ${TODAY}T07:10:00Z demo-app[bot] Recommended: **Low** · theme 5, large · an older case\n\n<!-- a-team:lead -->
6 comment ${TODAY}T07:20:00Z demo-app[bot] Recommended: **High** · theme 2, small · clears the queue\n\n<!-- a-team:lead -->
6 comment ${TODAY}T07:30:00Z reviewer Recommended: **Low** · not the Lead's
TALK
waiting
same "exit" 0 "$STATUS"
same "ideas" '[[6,"High","theme 2, small · clears the queue"],[7,null,"waiting to be ranked"]]' \
  "$(jq -c 'map(select(.status == "Idea") | [.number, .recommendation, .reason])' "$OUT")"
same "api calls" 4 "$(grep -c '' <"$CALLS")"

vision_team() {
  fixture <<'JSON'
{ "repo": "mentaldesk/demo", "stakeholders": ["reviewer"], "app": { "id": 7, "slug": "demo-app" },
  "project": { "owner": "mentaldesk", "number": 1 }, "vision": "docs/vision.md", "wip": { "pitched": 3 } }
JSON
  [ -z "${1:-}" ] || { jq --argjson review "$1" '.visionReview = $review' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"; }
  gh_items </dev/null
  mkdir -p "$A_TEAM_STATE/demo/lead" && touch "$A_TEAM_STATE/demo/lead/seeded"
}
lead_reasons() { run board demo triggers lead --sweep; jq -c '[.reasons[] | select(startswith("vision review"))]' "$OUT"; }

case_ "with Manually, the default, a vision review never comes due by itself"
vision_team
changed 400
run board demo triggers lead --sweep
same "exit" 0 "$STATUS"
same "reasons" '[]' "$(jq -c .reasons "$OUT")"
vision_team '{"trigger": "manually", "every": 1}'
changed 400
same "manually" '[]' "$(lead_reasons)"

case_ "with Every N days, a review comes due that long after the vision last changed"
vision_team '{"trigger": "every", "every": 30, "unit": "days"}'
changed 10
same "too soon" '[]' "$(lead_reasons)"
changed 31
same "due" '["vision review due: 30 days since it last changed"]' "$(lead_reasons)"

case_ "weeks and months count in weeks and months, and one of them reads as one"
vision_team '{"trigger": "every", "every": 4, "unit": "weeks"}'
changed 27
same "27 days" '[]' "$(lead_reasons)"
changed 29
same "4 weeks" '["vision review due: 4 weeks since it last changed"]' "$(lead_reasons)"
vision_team '{"trigger": "every", "every": 1, "unit": "months"}'
changed 20
same "20 days" '[]' "$(lead_reasons)"
changed 40
same "1 month" '["vision review due: 1 month since it last changed"]' "$(lead_reasons)"

case_ "a vision that isn't on the default branch yet never comes due: the Lead drafts one instead"
vision_team '{"trigger": "every", "every": 1}'
same "no vision" '[]' "$(lead_reasons)"

case_ "nothing comes due while a review PR is open"
vision_team '{"trigger": "every", "every": 30}'
changed 60
echo '[{"number": 51}]' >"$VOPEN"
same "open" '[]' "$(lead_reasons)"

case_ "a review closed without merging re-arms the interval from the day it was closed"
vision_team '{"trigger": "every", "every": 30}'
changed 60
jq -n --arg at "$(days_ago 5)" --arg old "$(days_ago 90)" '[{number: 51, merged_at: null, closed_at: $at},
  {number: 40, merged_at: null, closed_at: $old}]' >"$VCLOSED"
same "closed 5 days ago" '[]' "$(lead_reasons)"
jq -n --arg at "$(days_ago 35)" '[{number: 51, merged_at: null, closed_at: $at}]' >"$VCLOSED"
same "closed 35 days ago" '["vision review due: 30 days since you closed #51 without merging it"]' "$(lead_reasons)"
jq -n --arg at "$(days_ago 5)" '[{number: 51, merged_at: $at, closed_at: $at}]' >"$VCLOSED"
same "merged, not closed" '["vision review due: 30 days since it last changed"]' "$(lead_reasons)"

# themes_states "<n>:<OPEN|CLOSED>[:<days ago closed>]"...: the issues the Next themes name, as GitHub has them.
themes_states() {
  local entry n state days
  for entry in "$@"; do
    IFS=: read -r n state days <<<"$entry"
    jq -n --arg n "$n" --arg state "$state" --arg at "$([ -n "$days" ] && days_ago "$days")" \
      '{key: "i\($n)", value: {state: $state, closedAt: (if $at == "" then null else $at end)}}'
  done | jq -s '{data: {repository: from_entries}}' >"$THEMES"
}

case_ "with After N%, a review comes due once that share of the issues Next themes names have closed, counting no others"
vision_team '{"trigger": "after", "after": 50}'
changed 3
cat >"$VISION_MD" <<'MD'
# Vision

Inspired by #99, which isn't a theme.

## Next themes

1. **One.** #1 and #2 (see owner/repo#77).
2. **Two.** #3, #4 and [#5](#5).

### Later

3. **Three.** #3 again.

## How to judge a proposal

- Like #98.
MD
themes_states 1:CLOSED:1 2:OPEN 3:OPEN 4:OPEN 5:OPEN
same "1 of 5" '[]' "$(lead_reasons)"
themes_states 1:CLOSED:1 2:CLOSED:2 3:CLOSED:9 4:OPEN 5:OPEN
same "3 of 5" '["vision review due: 3 of the 5 issues its themes name are closed"]' "$(lead_reasons)"
touch "$WORK/docs-down"
same "GitHub didn't answer for the vision" '[]' "$(lead_reasons)"
rm "$WORK/docs-down"
touch "$WORK/no-docs"
same "no vision" '[]' "$(lead_reasons)"
rm "$WORK/no-docs"
same "the answer back" '["vision review due: 3 of the 5 issues its themes name are closed"]' "$(lead_reasons)"

case_ "after closing a review unmerged, only issues closed since count towards the next one"
jq -n --arg at "$(days_ago 3)" '[{number: 52, merged_at: null, closed_at: $at}]' >"$VCLOSED"
changed 30
same "1 since" '[]' "$(lead_reasons)"
themes_states 1:CLOSED:1 2:CLOSED:2 3:CLOSED:1 4:OPEN 5:OPEN
same "3 since" '["vision review due: 3 of the 5 issues its themes name have closed since you closed #52 without merging it"]' \
  "$(lead_reasons)"
echo '[]' >"$VCLOSED"

case_ "a vision whose themes name no issues never comes due on the percentage"
printf '# Vision\n\n## Next themes\n\n1. **Fewer decisions.** No issues yet.\n' >"$VISION_MD"
themes_states
same "none named" '[]' "$(lead_reasons)"

case_ "between sweeps, the trigger's verdict is reused rather than worked out again"
vision_team '{"trigger": "every", "every": 30}'
changed 31
same "sweep" '["vision review due: 30 days since it last changed"]' "$(lead_reasons)"
changed 1
run board demo triggers lead
same "between sweeps" '["vision review due: 30 days since it last changed"]' "$(jq -c '[.reasons[] | select(startswith("vision"))]' "$OUT")"
echo '[{"number": 53}]' >"$VOPEN"
run board demo triggers lead
same "but not once a review is open" '[]' "$(jq -c '[.reasons[] | select(startswith("vision"))]' "$OUT")"

case_ "Review the vision asks for one whatever the trigger, until its PR opens"
vision_team
run board demo vision-review you
same "exit" 0 "$STATUS"
same "said" "vision review asked for: the Lead starts one on the next pass" "$(cat "$OUT")"
same "asked" '["vision review asked for from Commands"]' "$(lead_reasons)"
echo '[{"number": 54}]' >"$VOPEN"
same "opened" '[]' "$(lead_reasons)"
echo '[]' >"$VOPEN"
same "asked once" '[]' "$(lead_reasons)"

case_ "only a stakeholder asks for a review, and a dry run asks for nothing"
run board demo vision-review lead
failed "lead"
run board --dry-run demo vision-review you
same "dry exit" 0 "$STATUS"
same "dry asked" '[]' "$(lead_reasons)"

case_ "the agents' settings deny edit and vision-review"
for rule in "Bash(a-team board * edit you *)" "Bash(a-team board * vision-review you)"; do
  jq -e --arg r "$rule" '.permissions.deny | index($r)' "$ROOT/settings/agents.json" >/dev/null || fail "no deny rule $rule"
done

# Claude Code's rule form: `*` is the only wildcard, and it has to match the whole command as written.
board_denied() {
  local rule
  while IFS= read -r rule; do
    # shellcheck disable=SC2053
    [[ $1 == $rule ]] && return 0
  done < <(jq -r '.permissions.deny[] | select(startswith("Bash(a-team board ")) | .[5:-1]' "$ROOT/settings/agents.json")
  return 1
}

case_ "a word in free text doesn't trip a deny rule, including any rule added later"
words=$(jq -r '.permissions.deny[] | select(startswith("Bash(a-team board ")) | .[5:-1] | split(" ")[3]' "$ROOT/settings/agents.json" | sort -u)
for word in accept release priority decline attach edit decision vision-review $words; do
  for command in "a-team board demo task lead 7 \"A $word in a title\" body.md" \
    "a-team board demo fix lead \"A $word in a title\" body.md" \
    "a-team board demo depends lead 7 8 \"needs the $word first\"" \
    "a-team board demo depends lead 7 owner/repo#8 released pkg \"needs the $word first\"" \
    "a-team board demo undepend lead 7 8 \"no $word now\"" \
    "a-team board demo decisions $word" \
    "a-team board demo decisions --replaced the $word rule" \
    "a-team board demo task lead 7 \"$word your own task\" body.md"; do
    ! board_denied "$command" || fail "denied: $command"
  done
done

case_ "every form of a stakeholder's own command that says you is denied, whatever else is on the line"
for command in "accept you 7" "release you 7" "priority you 7 high" "decline you 7 reason.md" "attach you shot.png" \
  "edit you 7 path sha body.md" "decision you 7 retire" "decision you 7 edit body.md" "vision-review you"; do
  board_denied "a-team board demo $command" || fail "allowed: $command"
  board_denied "a-team board --dry-run demo $command" || fail "allowed with --dry-run: $command"
done

case_ "a team role's own forms of those commands reach the board, which refuses them itself"
for role in lead dev customer reviewer; do
  for command in "accept $role 7" "release $role 7" "priority $role 7 high" "decline $role 7 reason.md" \
    "attach $role shot.png" "edit $role 7 path sha body.md" "decision $role 7 retire" "vision-review $role"; do
    ! board_denied "a-team board demo $command" || fail "denied by a rule: $command"
    run board demo $command
    failed "$command"
  done
done

case_ "after a review closed unmerged, the Lead's branching pushes a new one without forcing, and keeps none of the old"
ours="git merge -s ours --no-edit origin/a-team/vision"
grep -qF "$ours" "$ROOT/roles/lead.md" || fail "lead.md no longer says '$ours'"
vdir=$(mktemp -d "$WORK/vision.XXXXXX")
vgit() { git -C "$vdir/$1" -c user.email=test@example.com -c user.name=Test "${@:2}"; }
git -c init.defaultBranch=main init -q --bare "$vdir/origin"
git -c init.defaultBranch=main clone -q "file://$vdir/origin" "$vdir/old" 2>/dev/null
echo "# Vision" >"$vdir/old/vision.md"
vgit old add -A && vgit old commit -qm first && vgit old push -q origin HEAD:main
vgit old switch -qc a-team/vision && echo "closed unmerged" >>"$vdir/old/vision.md"
vgit old commit -qam "the closed review" && vgit old push -q origin a-team/vision
vgit old switch -q main && echo more >"$vdir/old/other" && vgit old add -A && vgit old commit -qm later && vgit old push -q origin main
git clone -q "file://$vdir/origin" "$vdir/lead" 2>/dev/null
vgit lead fetch -q origin
vgit lead worktree add -q -B a-team/vision "$vdir/review" origin/main
# shellcheck disable=SC2086
vgit review ${ours#git } -q
echo "the new review" >>"$vdir/review/vision.md"
vgit review commit -qam "the new review"
vgit review push -q origin a-team/vision 2>"$ERR" || fail "push: '$(cat "$ERR")'"
same "vision" $'# Vision\nthe new review' "$(git -C "$vdir/origin" show a-team/vision:vision.md)"
same "files changed" vision.md "$(git -C "$vdir/origin" diff --name-only main...a-team/vision)"

# A document pitch, the Lead's, in <status>.
document_pitch() {
  gh_items <<ITEMS
${2:-Pitched} $1 Vision review: themes 1 and 5 have run dry
ITEMS
  edit_item "$1" '.__typename = "PullRequest" | .labels.nodes = [{name: "pitch"}] | del(.issueDependenciesSummary, .issueFieldValues)'
  jq '.data.organization.projectV2.fields.nodes[0].options += [{id: "OPT_done", name: "Done"}]' "$META" >"$META.new" && mv "$META.new" "$META"
  echo '{"head": {"sha": "deadbeefcafe", "ref": "a-team/vision", "repo": {"full_name": "mentaldesk/demo"}}, "draft": true}' >"$PULL"
}

case_ "accept merges a document pitch from Pitched, ready first, and moves it to Done"
vision_team
document_pitch 60
run board demo accept you 60
same "exit" 0 "$STATUS"
same "said" "#60: merged document pitch #60" "$(cat "$OUT")"
same "first write" "READY pr ready 60 -R mentaldesk/demo" "$(head -1 "$WRITES")"
grep -q 'PUT api -X PUT repos/mentaldesk/demo/pulls/60/merge -f merge_method=squash' "$WRITES" || fail "merge: '$(cat "$WRITES")'"
grep -q 'DELETE api -X DELETE repos/mentaldesk/demo/git/refs/heads/a-team/vision' "$WRITES" || fail "branch: '$(cat "$WRITES")'"
grep -q 'item=PVTI_60 .*option=OPT_done' "$WRITES" || fail "Done: '$(cat "$WRITES")'"

case_ "accept refuses a document pitch that isn't in Pitched"
document_pitch 61 Approved
run board demo accept you 61
failed "approved"
grep -q "only a document pitch in Pitched can be accepted (#61 is in 'Approved')" "$ERR" || fail "approved: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"

case_ "document reads the one file a document pitch changes, at its branch, with the blob to edit it from"
document_pitch 60
echo '[{"filename": "docs/vision.md"}]' >"$FILES"
jq -n --arg c "$(printf '# Vision\n\nThe tenet — attention.\n' | base64)" '{sha: "0123abc", content: $c}' >"$CONTENTS"
run board demo document 60
same "exit" 0 "$STATUS"
same "document" '{"number":60,"files":["docs/vision.md"],"path":"docs/vision.md","ref":"a-team/vision","sha":"0123abc","text":"# Vision\n\nThe tenet — attention.\n"}' \
  "$(jq -c . "$OUT")"

case_ "document names every file of one that changes more than one, and nothing to edit"
echo '[{"filename": "docs/vision.md"}, {"filename": "README.md"}]' >"$FILES"
run board demo document 60
same "files" '{"number":60,"files":["docs/vision.md","README.md"]}' "$(jq -c . "$OUT")"

case_ "edit commits your file to the pitch's branch, from the blob you started from"
printf '# Vision\n\nEdited.\n' >"$WORK/edited.md"
run board demo edit you 60 docs/vision.md 0123abc "$WORK/edited.md"
same "exit" 0 "$STATUS"
same "said" "#60: committed your edit to docs/vision.md" "$(cat "$OUT")"
grep -q 'PUT api -X PUT repos/mentaldesk/demo/contents/docs/vision.md --input -' "$WRITES" || fail "put: '$(cat "$WRITES")'"
same "commit" '{"message":"Edit docs/vision.md","sha":"0123abc","branch":"a-team/vision","text":"# Vision\n\nEdited.\n"}' \
  "$(jq -c '{message, sha, branch, text: (.content | @base64d)}' "$BIN/put.json")"

case_ "edit says so when the file changed on GitHub meanwhile, and overwrites nothing"
: >"$WRITES"
touch "$BIN/put-conflicts"
run board demo edit you 60 docs/vision.md 0123abc "$WORK/edited.md"
failed "conflict"
same "refused" "board.sh: docs/vision.md changed on GitHub while you were editing it" "$(cat "$ERR")"
same "writes" "" "$(cat "$WRITES")"
rm "$BIN/put-conflicts"

case_ "only a stakeholder edits, and only a document pitch in Pitched"
run board demo edit lead 60 docs/vision.md 0123abc "$WORK/edited.md"
failed "lead"
document_pitch 61 Approved
run board demo edit you 61 docs/vision.md 0123abc "$WORK/edited.md"
failed "approved"
grep -q "#61 isn't a document pitch in Pitched" "$ERR" || fail "approved: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"

case_ "waiting marks a document pitch, and its Needs your answer waits under Blocked"
document_pitch 60
gh_talk <<TALK
60 body ${TODAY}T08:14:00Z demo-app[bot] ## Why now\n\nThemes ran dry.\n\n## Needs your answer\n\n- Move fewer decisions up?\n\n<!-- a-team:lead -->
TALK
waiting
same "exit" 0 "$STATUS"
same "document" '{"number":60,"document":true,"pitch":false,"question":"## Needs your answer\n\n- Move fewer decisions up?"}' \
  "$(jq -c '.[] | select(.number == 60) | {number, document, pitch, question}' "$OUT")"

case_ "waiting gives a document pitch its own branch and base"
document_pitch 60
gh_talk <<TALK
60 body ${TODAY}T08:14:00Z demo-app[bot] Themes ran dry.\n<!-- a-team:lead -->
TALK
jq '.data.repository.x60 += {url: "https://github.com/mentaldesk/demo/pull/60", isDraft: false, mergeable: "MERGEABLE",
      baseRefName: "main", headRefName: "a-team/vision"} | del(.data.repository.x60.closedByPullRequestsReferences)' \
  "$TALK" >"$TALK.new" && mv "$TALK.new" "$TALK"
waiting
same "exit" 0 "$STATUS"
same "document" '{"number":60,"pr":null,"branch":"a-team/vision","base":"main"}' \
  "$(jq -c '.[] | select(.number == 60) | {number, pr, branch, base}' "$OUT")"

case_ "a document pitch stays in Pitched whatever the limit: no draft displaces it"
vision_team
jq '.wip.pitched = 1' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
gh_items 73 <<'ITEMS'
Pitched 60 Vision review
Exploring 73 A ranked draft
ITEMS
# The stub answers every Priority query with every item, so this PR keeps the fields an issue has.
edit_item 60 '.__typename = "PullRequest" | .labels.nodes = [{name: "pitch"}]'
run board demo lead-next
same "exit" 0 "$STATUS"
same "demote" '[]' "$(jq -c '[.demote[].number]' "$OUT")"
same "promote" '[73]' "$(jq -c '[.promote[].number]' "$OUT")"


# --- themes and Spending ---------------------------------------------------------------------
case_ "theme records a pitch's vision theme in place of any other, and shows it on the mirror"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "app": { "id": 7, "slug": "demo-app" },
  "project": { "owner": "mentaldesk", "number": 1 }, "wip": { "pitched": 2, "exploring": 4, "ideas": 4 } }
JSON
gh_items <<'ITEMS'
Exploring 81 A pitch
Ready 82 A task
ITEMS
edit_item 81 '.labels.nodes = [{name: "pitch"}, {name: "theme: Think bigger"}]'
rm -f "$A_TEAM_STATE/history.db"
KEEP_BOARD=1 run board demo theme lead 81 "Consistently good UX"
same "exit" 0 "$STATUS"
same "theme" '"Consistently good UX"' "$(sqlite3 "$A_TEAM_STATE/history.db" "SELECT json_extract(marks, '$.theme') FROM board WHERE team = 'demo' AND item = 81;" | jq -R .)"
grep -q 'label' "$WRITES" && fail "labelled: '$(cat "$WRITES")'"
grep -q 'text=Consistently good UX' "$WRITES" || fail "theme not mirrored: '$(cat "$WRITES")'"
grep -q 'labels/theme%3A%20Think%20bigger' "$BIN/unlabelled" || fail "old label kept: '$(cat "$BIN/unlabelled")'"
same "history" "lead theme: Consistently good UX" "$(A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board demo history 81 | jq -r '.events[0] | "\(.who) \(.what)"')"

case_ "none takes the theme off, on the mirror too"
jq '.data.organization.projectV2.fields.nodes += [{id: "PVTF_theme", name: "Theme", dataType: "TEXT"}]' "$META" >"$META.new" && mv "$META.new" "$META"
: >"$WRITES"
KEEP_BOARD=1 run board demo theme lead 81 none
same "exit" 0 "$STATUS"
same "theme" "" "$(sqlite3 "$A_TEAM_STATE/history.db" "SELECT json_extract(marks, '$.theme') FROM board WHERE team = 'demo' AND item = 81;")"
grep -q 'CLEAR .*item=PVTI_81' "$WRITES" || fail "theme left on the mirror: '$(cat "$WRITES")'"
same "history" "lead no theme" "$(A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board demo history 81 | jq -r '.events[0] | "\(.who) \(.what)"')"

case_ "only the Lead sets a theme, and only on a pitch"
edit_item 81 '.labels.nodes = [{name: "pitch"}]'
: >"$WRITES"
for refused in "dev 81 Think" "you 81 Think" "lead 82 Think" "lead 81 Think,bigger"; do
  read -r role n heading <<<"$refused"
  run board demo theme "$role" "$n" "$heading"
  failed "theme $role $n"
  one_line "theme $role $n"
done
same "writes" "" "$(cat "$WRITES")"

case_ "lead-next lists the pitches with no theme yet: open ones, and done ones with history since runs were recorded"
gh_items <<'ITEMS'
Exploring 81 Unthemed and open
Pitched 82 Themed
Done 83 Done before the record
Done 84 Done since
Building 85 Said to serve none
Done 86 Declined
Idea 87 An idea
ITEMS
edit_item 82 '.labels.nodes = [{name: "pitch"}, {name: "theme: Think bigger"}]'
edit_item 83 '.labels.nodes = [{name: "pitch"}]'
edit_item 84 '.labels.nodes = [{name: "pitch"}]'
edit_item 86 '.labels.nodes = [{name: "pitch"}] | .stateReason = "NOT_PLANNED"'
rm -f "$A_TEAM_STATE/history.db"
sqlite3 "$A_TEAM_STATE/history.db" "CREATE TABLE events (id INTEGER PRIMARY KEY, team TEXT NOT NULL, item INTEGER NOT NULL,
    at TEXT NOT NULL, who TEXT NOT NULL, what TEXT NOT NULL);
  CREATE TABLE runs (id INTEGER PRIMARY KEY, team TEXT NOT NULL, role TEXT NOT NULL,
    pid INTEGER NOT NULL, log TEXT NOT NULL, started TEXT NOT NULL, ended TEXT, cost REAL, outcome TEXT);
  INSERT INTO runs (team, role, pid, log, started) VALUES ('demo', 'lead', 1, 'a', '2026-10-08T00:00:00Z');
  INSERT INTO events (team, item, at, who, what) VALUES ('demo', 83, '2026-10-01T00:00:00Z', 'lead', 'closed as done'),
    ('demo', 84, '2026-10-09T00:00:00Z', 'lead', 'closed as done'),
    ('demo', 85, '2026-10-09T00:00:00Z', 'lead', 'theme: Think bigger'), ('demo', 85, '2026-10-10T00:00:00Z', 'lead', 'no theme'),
    ('demo', 81, '2026-10-10T00:00:00Z', 'lead', 'no theme'), ('demo', 81, '2026-10-11T00:00:00Z', 'lead', 'theme: Think bigger');"
run board --dry-run demo lead-next
same "exit" 0 "$STATUS"
same "unthemed" '[81,84]' "$(jq -c .unthemed "$OUT")"

spending() { A_TEAM_CONFIG="$CONFIG" "$A_TEAM" board demo spending | jq -c .; }

case_ "with no runs recorded, spending is empty, reads nothing from GitHub, and makes no record file"
rm -f "$A_TEAM_STATE/history.db"
: >"$CALLS"
same "empty" '{"since":null,"runs":[],"items":[]}' "$(spending)"
same "api calls" 0 "$(grep -c '' <"$CALLS")"
[ ! -e "$A_TEAM_STATE/history.db" ] || fail "spending: made a record file"

case_ "spending: every run with its cost and items, and the items and the pitches over them, in one read of the board"
gh_items <<'ITEMS'
Done 81 A pitch
Done 517 A task
Idea 40 An idea
Done 90 Untouched
ITEMS
edit_item 81 '.labels.nodes = [{name: "pitch"}, {name: "theme: Think bigger"}]'
edit_item 517 '.parent = {number: 81}'
sqlite3 "$A_TEAM_STATE/history.db" "CREATE TABLE runs (id INTEGER PRIMARY KEY, team TEXT NOT NULL, role TEXT NOT NULL,
    pid INTEGER NOT NULL, log TEXT NOT NULL, started TEXT NOT NULL, ended TEXT, cost REAL, outcome TEXT);
  CREATE TABLE run_items (run INTEGER NOT NULL, item INTEGER NOT NULL);
  INSERT INTO runs (id, team, role, pid, log, started, cost) VALUES (1, 'demo', 'dev', 1, 'a', '2026-10-09T00:00:00Z', 1.25),
    (2, 'demo', 'lead', 2, 'b', '2026-10-08T00:00:00Z', 0.5), (3, 'demo', 'lead', 3, 'c', '2026-10-10T00:00:00Z', NULL),
    (4, 'other', 'dev', 4, 'd', '2026-10-01T00:00:00Z', 9);
  INSERT INTO run_items VALUES (1, 517), (3, 40), (3, 999), (4, 90);"
: >"$CALLS"
spending >"$OUT"
same "since" '"2026-10-08T00:00:00Z"' "$(jq -c .since "$OUT")"
same "runs" '[["lead",0.5,[]],["dev",1.25,[517]],["lead",0,[40,999]]]' "$(jq -c '[.runs[] | [.role, .cost, .items]]' "$OUT")"
same "items" '[[81,true,null,"Think bigger"],[517,false,81,null],[40,false,null,null]]' \
  "$(jq -c '[.items[] | [.number, .pitch, .parent, .theme]]' "$OUT")"
same "api calls" 1 "$(grep -c '' <"$CALLS")"

# --- a-team try -----------------------------------------------------------------------------

finish_suite
