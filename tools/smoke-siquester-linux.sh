#!/usr/bin/env bash

set -euo pipefail

if [ "$#" -ne 2 ]; then
  echo "Usage: $0 <SIQuester.Desktop executable or DLL> <package.siq>" >&2
  exit 2
fi

if [ -z "${DISPLAY:-}" ] || [ -z "${XDG_CONFIG_HOME:-}" ] || [ -z "${XDG_STATE_HOME:-}" ]; then
  echo "DISPLAY, XDG_CONFIG_HOME and XDG_STATE_HOME must be set." >&2
  exit 2
fi

desktop_application="$1"
package_path="$2"
settings_path="$XDG_CONFIG_HOME/SIQuester/settings.json"
log_path="$XDG_STATE_HOME/SIQuester/logs/siquester.log"

if [[ "$desktop_application" == *.dll ]]; then
  application_command=(dotnet "$desktop_application")
elif [ -x "$desktop_application" ]; then
  application_command=("$desktop_application")
else
  echo "SIQuester application is not an executable or a .NET DLL: $desktop_application" >&2
  exit 2
fi

"${application_command[@]}" "$package_path" &
application_pid=$!

cleanup() {
  if kill -0 "$application_pid" 2>/dev/null; then
    kill "$application_pid" 2>/dev/null || true
    wait "$application_pid" 2>/dev/null || true
  fi
}

trap cleanup EXIT

window_id=""

for _ in $(seq 1 80); do
  window_id="$(xdotool search --onlyvisible --name '^SIQuester$' 2>/dev/null | head -n 1 || true)"

  if [ -n "$window_id" ]; then
    break
  fi

  if ! kill -0 "$application_pid" 2>/dev/null; then
    echo "SIQuester exited before exposing its main window." >&2
    wait "$application_pid"
    exit 1
  fi

  sleep 0.25
done

if [ -z "$window_id" ]; then
  echo "SIQuester did not expose a visible main window within 20 seconds." >&2
  exit 1
fi

xdotool getwindowname "$window_id"
xdotool getwindowgeometry "$window_id"
xdotool key --window "$window_id" ctrl+q
wait "$application_pid"
trap - EXIT

test -s "$settings_path"
test -s "$log_path"
grep -F "Document has been successfully opened." "$log_path"
grep -F "Application settings were committed successfully" "$log_path"
! grep -E "Unhandled exception|FATAL" "$log_path"
sha256sum "$settings_path"
