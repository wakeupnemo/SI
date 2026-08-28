#!/usr/bin/env bash

set -euo pipefail

if [ "$#" -ne 4 ]; then
  echo "Usage: $0 <SIQuester.Desktop executable or DLL> <package.siq> <image> <receipt-directory>" >&2
  exit 2
fi

if [ -z "${DISPLAY:-}" ]; then
  echo "DISPLAY must be set to an active X11 display." >&2
  exit 2
fi

for required_command in import python3 rg sha256sum unzip xdotool; do
  if ! command -v "$required_command" >/dev/null 2>&1; then
    echo "Required command is unavailable: $required_command" >&2
    exit 2
  fi
done

desktop_application="$1"
source_package="$2"
source_image="$3"
receipt_directory="$4"
script_directory="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
drag_source="$script_directory/xdnd-file-source.py"
package_path="$receipt_directory/native external drop проверка.siq"
image_name="$(basename -- "$source_image")"
config_directory="$receipt_directory/config"
state_directory="$receipt_directory/state"
cache_directory="$receipt_directory/cache"
data_directory="$receipt_directory/data"
log_path="$state_directory/SIQuester/logs/siquester.log"

if [[ "$desktop_application" == *.dll ]]; then
  application_command=(dotnet "$desktop_application")
elif [ -x "$desktop_application" ]; then
  application_command=("$desktop_application")
else
  echo "SIQuester application is not an executable or a .NET DLL: $desktop_application" >&2
  exit 2
fi

test -f "$source_package"
test -f "$source_image"
test -f "$drag_source"

if [ -e "$receipt_directory" ] && [ ! -d "$receipt_directory" ]; then
  echo "Receipt path is not a directory: $receipt_directory" >&2
  exit 2
fi

if [ -d "$receipt_directory" ] && [ -n "$(find "$receipt_directory" -mindepth 1 -print -quit)" ]; then
  echo "Receipt directory must be empty: $receipt_directory" >&2
  exit 2
fi

mkdir -p "$receipt_directory" "$config_directory" "$state_directory" "$cache_directory" "$data_directory"
cp -- "$source_package" "$package_path"

application_pid=""
drag_source_pid=""

cleanup() {
  for pid in "$drag_source_pid" "$application_pid"; do
    if [ -n "$pid" ] && kill -0 "$pid" 2>/dev/null; then
      kill "$pid" 2>/dev/null || true
      wait "$pid" 2>/dev/null || true
    fi
  done
}

trap cleanup EXIT

start_application() {
  XDG_CONFIG_HOME="$config_directory" \
  XDG_STATE_HOME="$state_directory" \
  XDG_CACHE_HOME="$cache_directory" \
  XDG_DATA_HOME="$data_directory" \
  "${application_command[@]}" "$package_path" &
  application_pid=$!
}

find_window() {
  local title="$1"
  local pid="$2"
  local window_id=""

  for _ in $(seq 1 100); do
    window_id="$(xdotool search --onlyvisible --name "$title" 2>/dev/null | head -n 1 || true)"

    if [ -n "$window_id" ]; then
      printf '%s\n' "$window_id"
      return 0
    fi

    if ! kill -0 "$pid" 2>/dev/null; then
      echo "Process $pid exited before exposing window $title." >&2
      return 1
    fi

    sleep 0.1
  done

  echo "Window did not appear: $title" >&2
  return 1
}

start_application
application_window="$(find_window '^SIQuester Cross-Platform$' "$application_pid")"
python3 "$drag_source" "$source_image" &
drag_source_pid=$!
source_window="$(find_window '^SIQuester Xdnd file source$' "$drag_source_pid")"

sleep 2
xdotool windowmove "$application_window" 10 10 windowsize "$application_window" 1200 760
xdotool windowmove "$source_window" 1230 30 windowsize "$source_window" 300 140

# Select the flat question/list workspace, then expose both question cards by resizing the panes.
xdotool mousemove --window "$application_window" 110 160 click 1
xdotool mousemove --window "$application_window" 375 258 click 1
xdotool mousemove --window "$application_window" 275 294 click 1
xdotool mousemove --sync 411 430 mousedown 1 mousemove --sync 220 430 mouseup 1
xdotool mousemove --sync 819 430 mousedown 1 mousemove --sync 1050 430 mouseup 1
sleep 1
import -window "$application_window" "$receipt_directory/before.png"

# GTK owns the source transfer; the release lands inside the first realized question card.
xdotool mousemove --sync 1380 100 mousedown 1 sleep 0.3 \
  mousemove --sync 1200 180 sleep 0.2 mousemove --sync 900 350 sleep 0.2 \
  mousemove --sync 550 500 sleep 1 mouseup 1
sleep 2
import -window "$application_window" "$receipt_directory/after-drop.png"

test "$(sha256sum "$receipt_directory/before.png" | cut -d' ' -f1)" != \
  "$(sha256sum "$receipt_directory/after-drop.png" | cut -d' ' -f1)"

xdotool key --window "$application_window" ctrl+s
sleep 1
xdotool key --window "$application_window" ctrl+q
wait "$application_pid"
application_pid=""

PACKAGE_PATH="$package_path" SOURCE_IMAGE="$source_image" IMAGE_NAME="$image_name" python3 - <<'PY'
import hashlib
import os
import urllib.parse
import zipfile

package_path = os.environ["PACKAGE_PATH"]
source_image = os.environ["SOURCE_IMAGE"]
image_name = os.environ["IMAGE_NAME"]

with zipfile.ZipFile(package_path) as package:
    entries = {
        urllib.parse.unquote(name): name
        for name in package.namelist()
    }
    expected_entry = f"Images/{image_name}"
    assert expected_entry in entries, f"Missing imported media entry: {expected_entry}"
    imported = package.read(entries[expected_entry])
    content_xml = package.read(entries["content.xml"]).decode("utf-8-sig")

with open(source_image, "rb") as source:
    original = source.read()

assert hashlib.sha256(imported).digest() == hashlib.sha256(original).digest()
assert f">{image_name}</item>" in content_xml
print(f"NATIVE_EXTERNAL_DROP_MEDIA_SHA256={hashlib.sha256(imported).hexdigest()}")
PY

# Reopen the saved package through the production command-line path.
start_application
application_window="$(find_window '^SIQuester Cross-Platform$' "$application_pid")"
sleep 2
import -window "$application_window" "$receipt_directory/reopened.png"
xdotool key --window "$application_window" ctrl+q
wait "$application_pid"
application_pid=""

test -s "$log_path"
test "$(rg -c 'Document has been successfully opened' "$log_path")" -ge 2
! rg -q 'Unhandled exception|FATAL' "$log_path"
sha256sum "$package_path" "$receipt_directory/before.png" \
  "$receipt_directory/after-drop.png" "$receipt_directory/reopened.png" "$log_path"

cleanup
trap - EXIT
