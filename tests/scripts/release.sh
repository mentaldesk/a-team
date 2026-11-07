#!/usr/bin/env bash
# Releasing.
# shellcheck source=tests/scripts/lib.sh
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
# shellcheck disable=SC2034,SC2154 # helpers read what an area sets, and an area reads what lib.sh sets

REL=$(mktemp -d "$WORK/release.XXXXXX")
mkdir -p "$REL/bin"
fake "$REL/bin/gh" <<SH
#!/usr/bin/env bash
case " \$* " in
  *"-X POST"*"/dispatches"*) printf '%s\n' "\$*" >>"$REL/dispatched"; exit 0 ;;
  *graphql*) page="$REL/repo.json" ;;
  *"/actions/workflows/release.yml/runs?"*) page="$REL/runs.json" ;;
  *) echo "gh: not faked: \$*" >&2; exit 1 ;;
esac
filter=
while [ \$# -gt 0 ]; do
  [ "\$1" = --jq ] && { filter=\$2; break; }
  shift
done
if [ -n "\$filter" ]; then jq -r "\$filter" "\$page"; else cat "\$page"; fi
SH
# release_run [<status>]: the latest release.yml run, with that status; none without one.
release_run() {
  jq -n --arg status "${1:-}" '{workflow_runs: (if $status == "" then []
    else [{status: $status, html_url: "https://github.com/mentaldesk/demo/actions/runs/9"}] end)}' >"$REL/runs.json"
}
released_by() { : >"$REL/dispatched"; PATH="$REL/bin:$PATH" run release "$@"; }
dispatches() { grep -c . "$REL/dispatched"; }
export A_TEAM_STATE
A_TEAM_STATE=$(mktemp -d "$WORK/state.XXXXXX")

case_ "continuous runs release.yml on main once it has moved past the latest release, and once only"
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "release": "continuous" }
JSON
released 1111111aaaa 2222222bbbb 60
release_run completed
released_by demo
same "exit" 0 "$STATUS"
same "said" "ran release.yml on main at 1111111 (latest release: v1.2.0)" "$(cat "$OUT")"
same "dispatched" "api -X POST repos/mentaldesk/demo/actions/workflows/release.yml/dispatches -f ref=main" "$(cat "$REL/dispatched")"
released_by demo
same "again" "ran release.yml for main at 1111111 already: if no release follows, see https://github.com/mentaldesk/demo/actions/workflows/release.yml" "$(cat "$OUT")"
same "dispatched again" 0 "$(dispatches)"

case_ "a main that's at the latest release has nothing to release"
released 3333333cccc 3333333cccc 60
released_by demo
same "said" "nothing to release: main is at v1.2.0" "$(cat "$OUT")"
same "dispatched" 0 "$(dispatches)"

case_ "a repo that has never released is due"
released 4444444dddd
released_by demo
same "said" "ran release.yml on main at 4444444 (latest release: none)" "$(cat "$OUT")"

case_ "a release.yml run still going is left to finish first"
released 5555555eeee 3333333cccc 60
release_run in_progress
released_by demo
same "said" "waiting for the release.yml run going now to finish: https://github.com/mentaldesk/demo/actions/runs/9" "$(cat "$OUT")"
same "dispatched" 0 "$(dispatches)"
release_run completed

case_ "daily waits until the latest release is a day old"
jq '.release = "daily"' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
released 6666666ffff 3333333cccc 3600
released_by demo
same "exit" 0 "$STATUS"
grep -q '^next release due at 20[0-9-]*T[0-9:]*Z: v1.2.0 is under a day old$' "$OUT" || fail "daily: '$(cat "$OUT")'"
same "dispatched" 0 "$(dispatches)"
released 6666666ffff 3333333cccc 90000
released_by demo
same "a day on" "ran release.yml on main at 6666666 (latest release: v1.2.0)" "$(cat "$OUT")"

case_ "--dry-run says what it would run, runs nothing, and leaves the next real pass to run it"
released 7777777aaaa 3333333cccc 90000
released_by --dry-run demo
same "said" "would run release.yml on main at 7777777 (latest release: v1.2.0)" "$(cat "$OUT")"
same "dispatched" 0 "$(dispatches)"
released_by demo
same "for real" 1 "$(dispatches)"

case_ "never, the default, asks GitHub nothing; anything else is refused in one line"
jq 'del(.release)' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
released_by demo
same "exit" 0 "$STATUS"
same "said" "release is never: nothing to do" "$(cat "$OUT")"
jq '.release = "weekly"' "$TEAM" >"$TEAM.new" && mv "$TEAM.new" "$TEAM"
released_by demo
failed "weekly"
one_line "weekly"
same "refused" "a-team release: release is 'weekly' in demo.json: expected never, daily or continuous" "$(cat "$ERR")"

case_ "a dispatcher pass logs what release says when it changes, paused or not, and nothing for never"
APP=$(mktemp -d "$WORK/dispatch.XXXXXX")
mkdir -p "$APP/bin" "$APP/scripts"
cp "$ROOT"/scripts/*.sh "$APP/scripts/"
fake "$APP/bin/a-team" <<SH
#!/usr/bin/env bash
[ "\$1" = release ] || exit 1
printf '%s\n' "\$*" >>"$APP/release-args"
cat "$APP/said"
exit "\$(cat "$APP/code" 2>/dev/null || echo 0)"
SH
fixture <<'JSON'
{ "repo": "mentaldesk/demo", "release": "continuous", "dispatch": { "enabled": false } }
JSON
echo '{ "repo": "mentaldesk/other", "dispatch": { "enabled": false } }' >"$CONFIG/teams/other.json"
echo "nothing to release: main is at v1.2.0" >"$APP/said"
pass
pass
echo "would run release.yml on main at 1111111 (latest release: v1.2.0)" >"$APP/said"
pass
echo "a-team release: can't read mentaldesk/demo: Not Found (HTTP 404)" >"$APP/said"
echo 1 >"$APP/code"
pass
same "logged" "demo release: nothing to release: main is at v1.2.0
demo release: would run release.yml on main at 1111111 (latest release: v1.2.0)
demo release: failed: can't read mentaldesk/demo: Not Found (HTTP 404)" "$(cut -d' ' -f2- "$A_TEAM_STATE/dispatch.log")"
same "asked" "release --dry-run demo" "$(sort -u "$APP/release-args")"

finish_suite
