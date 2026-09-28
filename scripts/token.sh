#!/usr/bin/env bash
#
# token.sh <team> — prints an installation token for the team's GitHub App. The cached one is
# reused while it has five minutes or more left; otherwise a new one is minted and cached.
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TEAM=${1:?usage: a-team token <team>}
source "$ROOT/scripts/common.sh"
source "$ROOT/scripts/github-app.sh"
CONFIG=$(team_config "$TEAM")

die() { echo "a-team token: $*" >&2; exit 1; }

[ -f "$CONFIG" ] || die "no config for team '$TEAM' at $CONFIG"
ID=$(jq -r '.app.id // empty' "$CONFIG")
SLUG=$(jq -r '.app.slug // empty' "$CONFIG")
REPO=$(jq -r .repo "$CONFIG")
[ -n "$ID" ] || die "team '$TEAM' has no \"app\" key, so it posts as you: run a-team app create $TEAM"
CACHE=$(token_cache "$TEAM")

if jq -er --argjson now "$(date +%s)" '
    select((.token | type == "string" and length > 0)
           and ((.expires_at | fromdateiso8601) - $now >= 300)) | .token' "$CACHE" 2>/dev/null; then
  exit 0
fi

jwt=$(app_jwt "$ID" "${REPO%/*}") || exit 1
installation=$(installation "$ID" "${REPO%/*}" "$REPO" "$jwt") || {
  [ $? -eq 2 ] && die "$installation: install it at https://github.com/apps/$SLUG/installations/new"
  die "$installation"
}
installation=$(jq -r .id <<<"$installation")
minted=$(github POST "app/installations/$installation/access_tokens" "$jwt") ||
  die "GitHub wouldn't mint a token for installation $installation: $minted"

mkdir -p "$(dirname "$CACHE")"
tmp=$(mktemp "$CACHE.XXXXXX")
jq '{token, expires_at, permissions}' <<<"$minted" >"$tmp"
mv "$tmp" "$CACHE"
jq -r .token <<<"$minted"
