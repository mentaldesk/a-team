#!/usr/bin/env bash
#
# credential.sh <team> <get|store|erase> — git's credential helper in a run whose team has a GitHub
# App (see bin/git). It answers github.com only, with the App's installation token, and makes git
# give up at once, saying why, when the App can't reach the repo.
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$ROOT/scripts/common.sh"
source "$ROOT/scripts/github-app.sh"
TEAM=${1:?usage: credential.sh <team> <get|store|erase>}

host='' repo=''
while IFS= read -r line && [ -n "$line" ]; do
  case $line in
    host=*) host=${line#host=} ;;
    path=*) repo=${line#path=} repo=${repo%.git} ;;
  esac
done
[ "${2:-}" = get ] && [ "$host" = github.com ] || exit 0

refuse() {
  echo "a-team: $*" >&2
  echo quit=1
  exit 0
}

token=$("$ROOT/bin/a-team" token "$TEAM" 2>&1) || refuse "${token#a-team token: }"
if [ -n "$repo" ]; then
  slug=$(jq -r .app.slug "$(team_config "$TEAM")")
  repos=$(github GET "installation/repositories?per_page=100" "$token") ||
    refuse "GitHub wouldn't list the repos ${slug}[bot] can reach: $repos"
  jq -e --arg repo "$repo" \
    '.total_count > 100 or any(.repositories[]; (.full_name | ascii_downcase) == ($repo | ascii_downcase))' \
    <<<"$repos" >/dev/null ||
    refuse "${slug}[bot] has no access to $repo: install the App there at https://github.com/apps/$slug/installations/new"
fi
printf 'username=x-access-token\npassword=%s\n' "$token"
