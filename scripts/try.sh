#!/usr/bin/env bash
#
# try.sh <team> [<pr>] [--clean] — runs a pull request's code, or with no PR the default branch
# as it is on origin, in a scratch worktree, with its own state and nothing it can write to the
# board, and takes the worktree away again afterwards.
# What runs the product is the team config's "try"; without one you get a shell in the worktree.
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

die() { echo "a-team try: $*" >&2; exit 1; }
refuse() { echo "a-team try: $*" >&2; echo "usage: a-team try <team> [<pr>] [--clean]" >&2; exit 2; }
tilde() { echo "${1/#$HOME/\~}"; }

CLEAN=false
ARGS=()
for arg in "$@"; do
  case "$arg" in
    --clean) CLEAN=true ;;
    -*) refuse "unknown option '$arg'" ;;
    *) ARGS+=("$arg") ;;
  esac
done
[ ${#ARGS[@]} -eq 1 ] || [ ${#ARGS[@]} -eq 2 ] || refuse "expected a team and an optional PR number"
TEAM=${ARGS[0]} PR=${ARGS[1]:-}
[ -z "$PR" ] || [[ "$PR" =~ ^[0-9]+$ ]] || refuse "'$PR' is not a PR number"

source "$ROOT/scripts/common.sh"
CONFIG=$(team_config "$TEAM")
[ -f "$CONFIG" ] || refuse "no config for team '$TEAM' at $CONFIG"

cfg() { jq -r "$1 // empty" "$CONFIG"; }
REPO=$(cfg .repo)
COMMAND=$(cfg .try)
WORKDIR=$(cfg .workdir)
WORKDIR=${WORKDIR/#\~/$HOME}
CHECKOUT=$(cfg .checkout)
CHECKOUT=${CHECKOUT/#\~/$HOME}
[ -n "$WORKDIR" ] || die "workdir is not set in $CONFIG"
[ -d "$CHECKOUT" ] || die "no checkout for team '$TEAM' at ${CHECKOUT:-<unset>}"

SLOT=${PR:-main}
TREE="$WORKDIR/.try/$SLOT"
# Beside the worktree, never in it: nothing the run writes can look like a file you changed.
SANDBOX="$WORKDIR/.try/state/$SLOT"
AGAIN="a-team try $TEAM${PR:+ $PR}"

remove() {
  git -C "$CHECKOUT" worktree remove --force "$TREE" 2>/dev/null || rm -rf "$TREE"
  git -C "$CHECKOUT" worktree prune
  rm -rf "$SANDBOX"
}

if $CLEAN; then
  if [ -e "$TREE" ] || [ -e "$SANDBOX" ]; then
    remove
    echo "Removed $(tilde "$TREE")."
  else
    echo "Nothing to remove at $(tilde "$TREE")."
  fi
  exit 0
fi

[ ! -e "$TREE" ] || die "$(tilde "$TREE") is already there. Remove it with: $AGAIN --clean"

if [ -z "$PR" ]; then
  branch=$(git -C "$CHECKOUT" ls-remote --symref origin HEAD 2>/dev/null |
    sed -n 's|^ref: refs/heads/\(.*\)[[:space:]]HEAD$|\1|p')
  [ -n "$branch" ] || die "could not read origin's default branch in $CHECKOUT"
  echo "Fetching $REPO $branch…"
  git -C "$CHECKOUT" fetch --quiet origin "+refs/heads/$branch:refs/remotes/origin/$branch" ||
    die "could not fetch $branch in $CHECKOUT"
  sha=$(git -C "$CHECKOUT" rev-parse "refs/remotes/origin/$branch")
  PROMPT="try:$TEAM@$branch"
  AT="$branch ${sha:0:7}"
  WHAT="$branch's"
  AFTER="Removed the worktree."
else
  pull=$(gh api "repos/$REPO/pulls/$PR" 2>/dev/null) || die "no PR #$PR on $REPO"
  state=$(jq -r '.state // empty' <<<"$pull")
  [ "$state" = open ] || die "PR #$PR on $REPO is $state"
  title=$(jq -r '.title // empty' <<<"$pull")
  sha=$(jq -r '.head.sha // empty' <<<"$pull")
  from=$(jq -r '.head.repo.full_name // empty' <<<"$pull")
  [ -n "$sha" ] || die "could not read the head commit of #$PR on $REPO"

  # try runs the PR's code on purpose, so a branch from somewhere else is the reviewer's call.
  if [ -n "$from" ] && [ "$from" != "$REPO" ]; then
    echo "#$PR is a branch on $from, not $REPO, and try runs its code."
    read -r -p "Go on? [y/N] " answer || answer=
    [[ "$answer" =~ ^[Yy]$ ]] || { echo "Nothing fetched."; exit 1; }
  fi

  echo "Fetching $REPO #$PR \"$title\"…"
  git -C "$CHECKOUT" fetch --quiet origin "pull/$PR/head" || die "could not fetch pull/$PR/head in $CHECKOUT"
  PROMPT="try:$TEAM#$PR"
  AT=${sha:0:7}
  WHAT="this PR's"
  AFTER="Removed the worktree. Merge #$PR if it did what you wanted."
fi

mkdir -p "$WORKDIR/.try"
git -C "$CHECKOUT" worktree add --detach --quiet "$TREE" "$sha" || die "could not make a worktree at $TREE"
mkdir -p "$SANDBOX"
echo "Worktree $(tilde "$TREE") at $AT"
echo
echo "  Sandbox: A_TEAM_STATE=$(tilde "$SANDBOX") · A_TEAM_DRY_RUN=1 (nothing can write to the board)"

export A_TEAM_STATE="$SANDBOX" A_TEAM_DRY_RUN=1
status=0
if [ -n "$COMMAND" ]; then
  echo "  Running: $COMMAND          (quit it to come back)"
  echo
  (cd "$TREE" && export PATH="$TREE/bin:$PATH" && eval "$COMMAND") || status=$?
else
  echo "  No \"try\" command in this team's config, so here's a shell in the worktree. \`a-team\`"
  echo "  here is $WHAT copy. Ctrl+D when done."
  echo
  (cd "$TREE" && PATH="$TREE/bin:$PATH" PS1="$PROMPT \$ " bash --norc --noprofile -i) || true
fi

echo
changed=$(git -C "$TREE" status --porcelain --untracked-files=all | grep -c '' || true)
if [ "$changed" -gt 0 ]; then
  echo "Kept $(tilde "$TREE"): you changed $changed file$([ "$changed" -eq 1 ] || echo s) there."
  echo "Remove it with: $AGAIN --clean"
else
  remove
  echo "$AFTER"
fi
[ "$status" -eq 0 ] || die "$COMMAND exited $status"
