#!/usr/bin/env bash

set -euo pipefail

if [ "$#" -ne 3 ]; then
  echo "Usage: $0 <SIQuester.Desktop executable or DLL> <select-answer package.siq> <receipt-directory>" >&2
  exit 2
fi

if [ -z "${DISPLAY:-}" ] || [ -z "${XDG_CONFIG_HOME:-}" ] || [ -z "${XDG_STATE_HOME:-}" ]; then
  echo "DISPLAY, XDG_CONFIG_HOME and XDG_STATE_HOME must be set." >&2
  exit 2
fi

for required_command in convert import sha256sum xdotool; do
  if ! command -v "$required_command" >/dev/null 2>&1; then
    echo "Required command is unavailable: $required_command" >&2
    exit 1
  fi
done

desktop_application="$1"
package_path="$2"
receipt_directory="$3"
settings_path="$XDG_CONFIG_HOME/SIQuester/settings.json"
log_path="$XDG_STATE_HOME/SIQuester/logs/siquester.log"
screenshot_path="$receipt_directory/question-preview.png"
receipt_path="$receipt_directory/question-preview.receipt.txt"

if [[ "$desktop_application" == *.dll ]]; then
  application_command=(dotnet "$desktop_application")
elif [ -x "$desktop_application" ]; then
  application_command=("$desktop_application")
else
  echo "SIQuester application is not an executable or a .NET DLL: $desktop_application" >&2
  exit 2
fi

mkdir -p "$receipt_directory"
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
for _ in $(seq 1 100); do
  window_id="$(xdotool search --onlyvisible --name '^SIQuester Cross-Platform$' 2>/dev/null | head -n 1 || true)"

  if [ -n "$window_id" ]; then
    break
  fi

  if ! kill -0 "$application_pid" 2>/dev/null; then
    echo "SIQuester exited before exposing its main window." >&2
    wait "$application_pid"
    exit 1
  fi

  sleep 0.2
done

if [ -z "$window_id" ]; then
  echo "SIQuester did not expose a visible main window within 20 seconds." >&2
  exit 1
fi

xdotool windowsize "$window_id" 1200 760
sleep 3

# The compatibility fixture has one expanded round, one theme, and one question.
# These viewport-relative actions exercise selection and application commands;
# semantic UI behavior remains covered independently by headless command tests.
xdotool mousemove --window "$window_id" 75 316 click 1
sleep 1
xdotool mousemove --window "$window_id" 125 348 click 1
sleep 1
xdotool mousemove --window "$window_id" 812 161 click 1
sleep 4

# Advance question, answer request, and right-answer fragments. The terminal
# fragment must expose Replay immediately, without a fourth sentinel click.
xdotool mousemove --window "$window_id" 812 660 click 1
sleep 2
xdotool mousemove --window "$window_id" 812 660 click 1
sleep 2
xdotool mousemove --window "$window_id" 812 660 click 1
sleep 5

import -display "$DISPLAY" -window "$window_id" "$screenshot_path"
test -s "$screenshot_path"

# The empty retained player has virtually no bright pixels in this content area;
# rendered mixed text/media content must cross a conservative deterministic threshold.
bright_fraction="$(convert "$screenshot_path" \
  -crop 540x390+257+237 \
  -colorspace Gray \
  -threshold 75% \
  -format '%[fx:mean]' info:)"
awk -v value="$bright_fraction" 'BEGIN { exit !(value >= 0.01) }'

# Replay in the same dialog, then close and open a second dialog. This checks
# both deterministic replay and complete media-session attach/dispose cycles.
xdotool mousemove --window "$window_id" 812 660 click 1
sleep 2
xdotool mousemove --window "$window_id" 900 660 click 1
sleep 2
xdotool mousemove --window "$window_id" 812 161 click 1
sleep 4
xdotool mousemove --window "$window_id" 812 660 click 1
sleep 3
xdotool mousemove --window "$window_id" 900 660 click 1
sleep 2

xdotool key --window "$window_id" ctrl+q
wait "$application_pid"
trap - EXIT

test -s "$settings_path"
test -s "$log_path"
grep -F "Question preview backend available: WebKitGtk" "$log_path"
grep -F "Question preview package media served: Image" "$log_path"
! grep -E "Question preview host failed|Unhandled exception|FATAL" "$log_path"

created_sessions="$(grep -Fc "Question preview media session created" "$log_path")"
disposed_sessions="$(grep -Fc "Question preview media session disposed" "$log_path")"
replay_count="$(grep -Fc "Question preview replay started" "$log_path")"
media_fetches="$(grep -Fc "Question preview package media served: Image" "$log_path")"

test "$created_sessions" -eq 2
test "$disposed_sessions" -eq 2
test "$replay_count" -eq 1
test "$media_fetches" -ge 2

{
  echo "backend=WebKitGtk"
  echo "package_media=Image"
  echo "created_sessions=$created_sessions"
  echo "disposed_sessions=$disposed_sessions"
  echo "replay_count=$replay_count"
  echo "media_fetches=$media_fetches"
  echo "bright_fraction=$bright_fraction"
  sha256sum "$screenshot_path"
} > "$receipt_path"

cat "$receipt_path"
