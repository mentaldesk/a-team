#!/usr/bin/env bash
#
# install.sh [--dry-run | --uninstall] — runs dispatch.sh every 2 minutes on this Mac via launchd.
# --dry-run installs it so it only logs what it would start.
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
STATE="${XDG_STATE_HOME:-$HOME/.local/state}/a-team"
LABEL=com.a-team.dispatch
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"
DOMAIN="gui/$(id -u)"
INTERVAL=120

launchctl bootout "$DOMAIN/$LABEL" 2>/dev/null || true
if [ "${1:-}" = --uninstall ]; then
  rm -f "$PLIST"
  echo "Uninstalled $LABEL"
  exit 0
fi

for tool in claude gh jq git; do
  command -v "$tool" >/dev/null || { echo "install.sh: $tool is not on PATH" >&2; exit 1; }
done
path=$(for tool in claude gh jq git dotnet; do command -v "$tool" 2>/dev/null | xargs -I{} dirname {}; done |
  awk '!seen[$0]++' | paste -sd: -):/usr/bin:/bin:/usr/sbin:/sbin

args="<string>/bin/bash</string><string>$ROOT/scripts/dispatch.sh</string>"
[ "${1:-}" = --dry-run ] && args="$args<string>--dry-run</string>"
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

launchctl bootstrap "$DOMAIN" "$PLIST"
echo "Installed $LABEL${1:+ ($1)}: runs every $((INTERVAL / 60)) minutes. Watch it with: bash $ROOT/scripts/status.sh"
