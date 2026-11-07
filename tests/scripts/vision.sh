#!/usr/bin/env bash
# A-team vision.
# shellcheck source=tests/scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
# shellcheck disable=SC2034,SC2154 # helpers read what an area sets, and an area reads what lib.sh sets

# --- a-team vision ---------------------------------------------------------------------------
# A claude that records its arguments and where it ran, one per line, for `a-team vision`.
VISION_BIN=$(mktemp -d "$WORK/vision.XXXXXX")
fake "$VISION_BIN/claude" <<SH
#!/usr/bin/env bash
pwd -P >"$VISION_BIN/asked"
printf '%s\n' "\$@" >>"$VISION_BIN/asked"
SH
VISION_WORK=$(mktemp -d "$WORK/visionwork.XXXXXX")
mkdir -p "$VISION_WORK/main"
# vision_with <a-team>: runs that a-team's `vision demo` with the recording claude, from no terminal.
vision_with() {
  A_TEAM_CONFIG="$CONFIG" PATH="$VISION_BIN:$PATH" "$1" vision demo >"$OUT" 2>"$ERR" </dev/null
  STATUS=$?
}

case_ "vision hands the team's checkout to claude, briefed with the team's repo, vision and workdir"
fixture <<JSON
{ "repo": "mentaldesk/demo", "vision": "docs/why.md", "workdir": "$VISION_WORK" }
JSON
vision_with "$A_TEAM"
same "exit" 0 "$STATUS"
same "cwd" "$(cd "$VISION_WORK/main" && pwd -P)" "$(sed -n 1p "$VISION_BIN/asked")"
same "workdir" "--add-dir $VISION_WORK" "$(sed -n 2,3p "$VISION_BIN/asked" | paste -sd ' ' -)"
same "first message" "Write demo's vision with me." "$(tail -n 1 "$VISION_BIN/asked")"
grep -q '^--append-system-prompt$' "$VISION_BIN/asked" || fail "no brief: '$(cat "$VISION_BIN/asked")'"
grep -q 'Interview me, the stakeholder of demo (mentaldesk/demo)' "$VISION_BIN/asked" || fail "brief: no team or repo"
grep -q 'Write the vision to `docs/why.md`' "$VISION_BIN/asked" || fail "brief: no vision path"
grep -q "Run \`$A_TEAM board demo add lead <pr> Pitched\`" "$VISION_BIN/asked" || fail "brief: no board command"
grep -q '{{' "$VISION_BIN/asked" && fail "brief: a placeholder left in '$(grep '{{' "$VISION_BIN/asked")'"

case_ "vision reads its brief from an installed release, laid out as release.yml stages it"
STAGE=$(mktemp -d "$WORK/stage.XXXXXX")
staging=$(grep -E '^ +cp -R .* "\$stage/"$' "$ROOT/.github/workflows/release.yml" | sed -e 's/^ *cp -R //' -e 's| "\$stage/"$||')
[ -n "$staging" ] || fail "release.yml: no cp -R line staging the release"
(cd "$ROOT" && read -ra staged <<<"$staging" && cp -R "${staged[@]}" "$STAGE/")
rm -f "$VISION_BIN/asked"
vision_with "$STAGE/bin/a-team"
same "installed exit" 0 "$STATUS"
grep -q 'Interview me, the stakeholder of demo' "$VISION_BIN/asked" 2>/dev/null || fail "installed: no brief: '$(cat "$ERR")'"

case_ "vision --dry-run says what it would run, and runs nothing"
rm -f "$VISION_BIN/asked"
A_TEAM_CONFIG="$CONFIG" PATH="$VISION_BIN:$PATH" "$A_TEAM" vision --dry-run demo >"$OUT" 2>"$ERR"
same "dry run" "(dry run) would run claude in $VISION_WORK/main, briefed by $ROOT/tasks/vision.md" "$(cat "$OUT")"
[ -f "$VISION_BIN/asked" ] && fail "dry run: claude ran"

case_ "vision without a checkout names the clone command, and runs nothing"
fixture <<JSON
{ "repo": "mentaldesk/demo", "workdir": "$VISION_WORK/elsewhere" }
JSON
vision_with "$A_TEAM"
failed "no checkout"
same "no checkout" "a-team vision: $VISION_WORK/elsewhere/main isn't there: gh repo clone mentaldesk/demo $VISION_WORK/elsewhere/main" "$(cat "$ERR")"
[ -f "$VISION_BIN/asked" ] && fail "no checkout: claude ran"

case_ "vision is in the usage text"
run help
grep -q '^  vision \[--dry-run\] <team>' "$OUT" || fail "usage: no vision line"

finish_suite
