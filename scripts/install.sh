#!/usr/bin/env bash
#
# install.sh [--dry-run] [--replace] | --uninstall — runs the dispatcher every 2 minutes on this Mac
# via launchd. --dry-run installs it so it only logs what it would start. It asks before replacing
# a dispatcher installed from somewhere else (--replace skips the question).
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$ROOT/scripts/common.sh"
LABEL=com.a-team.dispatch
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"
DOMAIN="gui/$(id -u)"
INTERVAL=120

DRY_RUN=false REPLACE=false
for arg in "$@"; do
  case "$arg" in
    --dry-run) DRY_RUN=true ;;
    --replace) REPLACE=true ;;
    --uninstall)
      launchctl bootout "$DOMAIN/$LABEL" 2>/dev/null || true
      rm -f "$PLIST"
      echo "Uninstalled $LABEL"
      exit 0 ;;
    *) echo "install.sh: unknown option $arg" >&2; exit 2 ;;
  esac
done

for tool in claude gh jq git; do
  command -v "$tool" >/dev/null || { echo "install.sh: $tool is not on PATH" >&2; exit 1; }
done
bin=${A_TEAM_BIN:-$ROOT/bin/a-team}

current=$(/usr/libexec/PlistBuddy -c 'Print :ProgramArguments:0' "$PLIST" 2>/dev/null || true)
if [ -n "$current" ] && [ "$current" != "$bin" ] && ! $REPLACE; then
  echo "The dispatcher is currently installed from $current." >&2
  if [ -t 0 ]; then
    read -r -p "Replace it with $bin? [y/N] " answer
    [[ "$answer" =~ ^[Yy]$ ]] || { echo "Nothing changed."; exit 1; }
  else
    echo "install.sh: not replacing it without --replace." >&2
    exit 1
  fi
fi
path=$(dirname "$bin"):$(for tool in claude gh jq git dotnet; do command -v "$tool" 2>/dev/null | xargs -I{} dirname {}; done |
  awk '!seen[$0]++' | paste -sd: -):/usr/bin:/bin:/usr/sbin:/sbin

args="<string>$bin</string><string>dispatch</string>"
$DRY_RUN && args="$args<string>--dry-run</string>"
mkdir -p "$STATE" "$(dirname "$PLIST")"

cat >"$PLIST" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key><string>$LABEL</string>
  <key>ProgramArguments</key><array>$args</array>
  <key>StartInterval</key><integer>$INTERVAL</integer>
  <key>RunAtLoad</key><true/>
  <key>AbandonProcessGroup</key><true/>
  <key>EnvironmentVariables</key>
  <dict>
    <key>PATH</key><string>$path</string>
    <key>HOME</key><string>$HOME</string>
    <key>A_TEAM_INTERVAL</key><string>$INTERVAL</string>
    ${DOTNET_ROOT:+<key>DOTNET_ROOT</key><string>$DOTNET_ROOT</string>}
  </dict>
  <key>StandardOutPath</key><string>$STATE/launchd.log</string>
  <key>StandardErrorPath</key><string>$STATE/launchd.log</string>
</dict>
</plist>
PLIST

launchctl bootout "$DOMAIN/$LABEL" 2>/dev/null || true
launchctl bootstrap "$DOMAIN" "$PLIST"
echo "Installed $LABEL$($DRY_RUN && echo ' (dry run)'): runs $bin dispatch every $((INTERVAL / 60)) minutes. Watch it with: a-team status"
