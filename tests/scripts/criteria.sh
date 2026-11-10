#!/usr/bin/env bash
# Criteria, checked and pictures.
# shellcheck source=tests/scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
# shellcheck disable=SC2034,SC2154 # helpers read what an area sets, and an area reads what lib.sh sets

criteria_body() {
  printf '%s\n' '## Context' '' '- [ ] not a criterion' '' '## Acceptance criteria' '' '- [ ] Save is left-aligned' \
    '- [x] Remove is red,' '  in every theme' '  - [ ] and it says so' '* Plain' '' '## Tests' '' '- [ ] nor this' '' '<!-- a-team:lead -->'
}
criteria_issue() {
  jq -n --arg body "$(criteria_body)" '{number: 12, title: "A task", body: $body}' >"$ISSUE"
}
criteria_pull() {
  jq -n --arg body "$1" '{body: $body}' >"$PULL"
}

case_ "criteria lists a task's checklist, numbered, with how the team checked each and its picture"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 } }
JSON
gh_items <<'ITEMS'
In_review 12 A task in front of me
ITEMS
criteria_issue
criteria_pull "$(printf '%s\n' 'Closes #12' '' '## What it looks like' '' 'Save is left-aligned' '' \
  '![Save is left-aligned](https://github.com/x/shot.png)' '' '## Checked by the team' '' '- `screen` Save is left-aligned' \
  '- `test` Remove is red, in every theme' '' 'a-team try demo 912' '' '<!-- a-team:dev -->')"
run board demo criteria 12 912
same "exit" 0 "$STATUS"
same "criteria" '[[1,0,false,"Save is left-aligned","screen","https://github.com/x/shot.png"],[2,0,true,"Remove is red, in every theme","test",null],[3,1,false,"and it says so",null,null],[4,0,false,"Plain",null,null]]' \
  "$(jq -c 'map([.index, .depth, .ticked, .text, .how, .picture.url])' "$OUT")"

case_ "a body with no Acceptance criteria section has none, and a CRLF body reads the same"
jq -n '{body: "## Context\r\n\r\n- [ ] a list, but not criteria"}' >"$ISSUE"
run board demo criteria 12 912
same "none" '[]' "$(jq -c . "$OUT")"
jq -n --arg body "$(criteria_body | sed 's/$/\r/')" '{body: $body}' >"$ISSUE"
run board demo criteria 12 912
same "CRLF" '[false,true,false,false]' "$(jq -c 'map(.ticked)' "$OUT")"
same "CRLF text" '"Remove is red, in every theme"' "$(jq -c '.[1].text' "$OUT")"

case_ "tick changes only the box on that criterion's line, and History says how many are ticked"
criteria_issue
: >"$WRITES"
rm -f "$EDITED"
run board demo tick you 12 1 on "Save is left-aligned"
same "exit" 0 "$STATUS"
same "said" "#12: 2 of 4 ticked" "$(cat "$OUT")"
grep -q "^EDIT issue edit 12 -R mentaldesk/demo --body-file -" "$WRITES" || fail "tick: no edit in '$(cat "$WRITES")'"
same "body" "$(criteria_body | sed 's/^- \[ \] Save/- [x] Save/')" "$(cat "$EDITED")"
run board demo history 12
same "history" '"ticked 2 of 4"' "$(jq -c '.events[0].what' "$OUT")"
same "by you" '"you"' "$(jq -c '.events[0].who' "$OUT")"

case_ "unticking a team's box, or ticking a plain bullet, leaves the rest of the body alone"
: >"$WRITES"
run board demo tick you 12 2 off "Remove is red, in every theme"
same "exit" 0 "$STATUS"
same "unticked" "$(criteria_body | sed 's/^- \[x\] Remove/- [ ] Remove/')" "$(cat "$EDITED")"
run board demo history 12
same "history" '"unticked, 0 of 4 ticked"' "$(jq -c '.events[0].what' "$OUT")"
run board demo tick you 12 4 on "Plain"
same "plain" "$(criteria_body | sed 's/^\* Plain/* [x] Plain/')" "$(cat "$EDITED")"

case_ "tick is yours, on a task In review, and refuses a criterion that's changed"
: >"$WRITES"
run board demo tick dev 12 1 on "Save is left-aligned"
failed "dev tick"
grep -qF "dev may not tick a criterion" "$ERR" || fail "dev tick: '$(cat "$ERR")'"
run board demo tick you 12 1 on "Something else now"
failed "changed"
grep -qF "#12's criteria have changed: open it again" "$ERR" || fail "changed: '$(cat "$ERR")'"
run board demo tick you 12 9 on "Save is left-aligned"
failed "no such"
run board demo tick you 12 1 maybe "Save is left-aligned"
failed "not on or off"
gh_items <<'ITEMS'
In_progress 12 A task still being built
ITEMS
run board demo tick you 12 1 on "Save is left-aligned"
failed "not in review"
grep -qF "only a task In review has criteria to tick (#12 is 'In progress')" "$ERR" || fail "not in review: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"
grep -qF '"Bash(a-team board * tick you *)"' "$ROOT/settings/agents.json" || fail "no tick deny rule"

case_ "tick --dry-run writes nothing and records nothing"
gh_items <<'ITEMS'
In_review 12 A task in front of me
ITEMS
criteria_issue
: >"$WRITES"
run board --dry-run demo tick you 12 1 on "Save is left-aligned"
same "exit" 0 "$STATUS"
same "writes" "" "$(cat "$WRITES")"
grep -q "would tick criterion 1 on #12" "$ERR" || fail "tick dry run: '$(cat "$ERR")'"

case_ "dev checked says how on the PR, above the try line, and ticks those boxes on the issue"
gh_pr 912 true
criteria_issue
SHOT_BODY=$(printf '%s\n' 'Buttons.' '' 'Closes #12' '' '## What it looks like' '' 'Save is left-aligned' '' \
  '![Save is left-aligned](https://github.com/x/shot.png)' '' 'a-team try demo 912' '' '<!-- a-team:dev -->')
gh_reviewing ".body = $(jq -n --arg b "$SHOT_BODY" '$b')"
: >"$WRITES"
rm -f "$BIN/pr-body" "$EDITED"
run board demo checked dev 912 1=screen 3=test
same "exit" 0 "$STATUS"
same "said" "#12: 2 of 4 checked by the team" "$(cat "$OUT")"
same "issue" "$(criteria_body | sed -e 's/^- \[ \] Save/- [x] Save/' -e 's/^  - \[ \] and/  - [x] and/')" "$(cat "$EDITED")"
same "PR" "Buttons.

Closes #12

## What it looks like

Save is left-aligned

![Save is left-aligned](https://github.com/x/shot.png)

## Checked by the team

- \`screen\` Save is left-aligned
- \`test\` and it says so

a-team try demo 912

<!-- a-team:dev -->" "$(cat "$BIN/pr-body")"
run board demo history 12
same "history" '"checked 2 of 4: 1 on screen, 1 by test"' "$(jq -c '.events[0].what' "$OUT")"

case_ "what checked writes reads back as the team's, and checked again replaces it"
criteria_pull "$(cat "$BIN/pr-body")"
run board demo criteria 12 912
same "read back" '["screen",null,"test",null]' "$(jq -c 'map(.how)' "$OUT")"
gh_reviewing ".body = $(jq -n --arg b "$(cat "$BIN/pr-body")" '$b')"
run board demo checked dev 912 2=test
same "exit" 0 "$STATUS"
same "one section" 1 "$(grep -c '^## Checked by the team' "$BIN/pr-body")"
grep -q 'and it says so' "$BIN/pr-body" && fail "checked again: kept the old record in '$(cat "$BIN/pr-body")'"
grep -qF -- '- `test` Remove is red, in every theme' "$BIN/pr-body" || fail "checked again: '$(cat "$BIN/pr-body")'"

case_ "checked again unticks what it no longer claims, and leaves the stakeholder's own ticks"
jq -n --arg body "$(criteria_body | sed -e 's/^- \[ \] Save/- [x] Save/' -e 's/^  - \[ \] and/  - [x] and/' -e 's/^\* Plain/* [x] Plain/')" \
  '{number: 12, title: "A task", body: $body}' >"$ISSUE"
gh_reviewing ".body = $(jq -n --arg b "$SHOT_BODY" '$b')"
run board demo checked dev 912 1=screen 3=test
same "exit" 0 "$STATUS"
gh_reviewing ".body = $(jq -n --arg b "$(cat "$BIN/pr-body")" '$b')"
run board demo checked dev 912 2=test
same "exit" 0 "$STATUS"
same "issue" "$(criteria_body | sed 's/^\* Plain/* [x] Plain/')" "$(cat "$EDITED")"

case_ "checked refuses a screen criterion with no picture captioned with it, one that doesn't exist, and anyone but the Dev"
: >"$WRITES"
run board demo checked dev 912 2=screen
failed "no picture"
grep -qF "criterion 2 is screen, but no picture on #912 is captioned with it: Remove is red, in every theme" "$ERR" || fail "no picture: '$(cat "$ERR")'"
run board demo checked dev 912 9=test
failed "no such criterion"
grep -qF "#12 has no criterion 9" "$ERR" || fail "no such criterion: '$(cat "$ERR")'"
run board demo checked dev 912 1=seen
failed "not screen or test"
run board demo checked reviewer 912 1=test
failed "reviewer"
grep -qF "reviewer may not say what the team checked" "$ERR" || fail "reviewer: '$(cat "$ERR")'"
same "writes" "" "$(cat "$WRITES")"

case_ "the Dev says what it checked, and is authorised to"
grep -qF 'checked dev <pr> <criterion>=screen|test' "$ROOT/roles/dev.md" || fail "dev.md: doesn't say what it checked"
grep -qF 'checked dev ...' "$ROOT/tasks/dev.md" || fail "task prompt: Dev not authorised to say what it checked"

case_ "the Dev puts what it looked at on its PR, again after a fix on screen, and the Reviewer checks each picture"
step=$(sed -n '/^5\. Build and run the tests/,/^7\./p' "$ROOT/roles/dev.md")
grep -qF 'Keep a snapshot of each screen the acceptance criteria name' <<<"$step" || fail "dev.md: doesn't keep its snapshots"
grep -qF 'pictures dev <pr> <image> "<criterion>"' <<<"$step" || fail "dev.md: doesn't put its snapshots on the PR"
grep -qF 'run `pictures` and `checked`' "$ROOT/roles/dev.md" || fail "dev.md: doesn't replace the pictures after a fix"
grep -qF 'pictures dev ...' "$ROOT/tasks/dev.md" || fail "task prompt: Dev not authorised to put pictures on its PR"
grep -qF 'body <pr>` lists in `images`' "$ROOT/roles/reviewer.md" || fail "reviewer.md: doesn't read the pictures"
grep -qF "each picture against its caption and the contributor docs' UI" "$ROOT/roles/reviewer.md" || fail "reviewer.md: doesn't check them"
grep -qF 'that changes what the user sees with no pictures' "$ROOT/roles/reviewer.md" || fail "reviewer.md: doesn't flag a UI task with none"

pictures_part=$(sed -n '/^### Pictures of a screen/,$p' "$ROOT/roles/lead.md" | sed '/^## Vision/,$d')
grep -qF '"mockups": true' <<<"$pictures_part" || fail "lead.md: pictures aren't only for a team with Mockups on"
grep -qF 'worktree add --detach' <<<"$pictures_part" || fail "lead.md: the scratch worktree isn't detached"
grep -qF 'worktree remove --force' <<<"$pictures_part" || fail "lead.md: the scratch worktree isn't removed"
sed '/worktree add --detach/,$d' <<<"$pictures_part" | grep -qF 'still there from a run cut short' || fail "lead.md: a stale scratch worktree isn't removed before adding one"
grep -qF 'one an earlier run left for that pitch' "$ROOT/tasks/lead.md" || fail "task prompt: Lead not authorised to remove a stale scratch worktree"
grep -qF 'never commit it, push it' <<<"$pictures_part" || fail "lead.md: doesn't say the scratch code is never pushed"
grep -qF 'The code is thrown away' <<<"$pictures_part" || fail "lead.md: doesn't say the scratch code is thrown away"
grep -qF 'No picture: <why>' <<<"$pictures_part" || fail "lead.md: no line for a screen with no picture"
grep -qF 'pictures lead <n>' <<<"$pictures_part" || fail "lead.md: doesn't put the pictures on the pitch"
grep -qF "except the throwaway in *Pictures of a screen*" "$ROOT/roles/lead.md" || fail "lead.md: no exception to not writing product code"
grep -qF 'pictures lead <n>' "$ROOT/tasks/lead.md" || fail "task prompt: Lead not authorised to put pictures on its pitch"
grep -qF 'Never commit or push that code' "$ROOT/tasks/lead.md" || fail "task prompt: Lead may push the scratch code"
grep -qF '"mockups": true' "$ROOT/tasks/lead.md" || fail "task prompt: authorisation isn't only for a team with Mockups on"

grep -qF '## Needs changing' "$ROOT/roles/reviewer.md" || fail "reviewer.md: the board's count reads this heading"
grep -qF '## Worth considering' "$ROOT/roles/reviewer.md" || fail "reviewer.md: the board's count reads this heading"
grep -qF 'Nothing needs changing.' "$ROOT/roles/reviewer.md" || fail "reviewer.md: the board's count reads this opening"
grep -qF 'when X, it does Y' "$ROOT/roles/reviewer.md" || fail "reviewer.md: a wrong result doesn't name its case"
grep -qF 'with a test that fails without the fix' "$ROOT/roles/dev.md" || fail "dev.md: a Needs changing fix has no test"
grep -qF "changing code the PR doesn't otherwise touch" "$ROOT/roles/dev.md" || fail "dev.md: doesn't say what it may leave"
grep -qF 'run only the quick checks for what the' "$ROOT/roles/dev.md" || fail "dev.md: a conflict run isn't told to leave the suite to CI"
grep -qF "the repo's CI doesn't run its" "$ROOT/roles/dev.md" || fail "dev.md: a repo whose CI runs no tests would lose them"
grep -qF 'leaving the full test suite to CI' "$ROOT/tasks/dev.md" || fail "task prompt: Dev not authorised to leave the suite to CI"

case_ "waiting says how many of a task's criteria aren't ticked, and nothing where none is left"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "reviewer": "reviewer", "app": { "id": 7, "slug": "demo-app" }, "project": { "owner": "mentaldesk", "number": 1 } }
JSON
gh_items <<'ITEMS'
In_review 12 A task with some left
In_review 13 A task with all ticked
ITEMS
TODAY=$(date -u +%F)
gh_talk <<TALK
12 body ${TODAY}T08:00:00Z reviewer ## Acceptance criteria\n- [ ] One\n- [x] Two\n- [ ] Three
12 pr-body ${TODAY}T08:10:00Z demo-app[bot] Closes #12
13 body ${TODAY}T08:00:00Z reviewer ## Acceptance criteria\n- [x] One
13 pr-body ${TODAY}T08:10:00Z demo-app[bot] Closes #13
TALK
waiting
same "exit" 0 "$STATUS"
same "unticked" '[[12,2],[13,null]]' "$(jq -c 'map(select(.number)) | sort_by(.number) | map([.number, .unticked])' "$OUT")"

finish_suite
